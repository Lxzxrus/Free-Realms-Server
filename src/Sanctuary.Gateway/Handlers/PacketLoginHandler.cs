using System;
using System.Linq;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

using Sanctuary.Core.Configuration;
using Sanctuary.Core.Helpers;
using Sanctuary.Database;
using Sanctuary.Game;
using Sanctuary.Game.Helpers;
using Sanctuary.Packet;
using Sanctuary.Packet.Common.Attributes;

namespace Sanctuary.Gateway.Handlers;

[PacketHandler]
public static class PacketLoginHandler
{
    private static ILogger _logger = null!;
    private static LoginClient _loginClient = null!;
    private static GatewayServer _server = null!;
    private static GatewayServerOptions _options = null!;
    private static IResourceManager _resourceManager = null!;
    private static IDbContextFactory<DatabaseContext> _dbContextFactory = null!;

    public static void ConfigureServices(IServiceProvider serviceProvider)
    {
        var loggerFactory = serviceProvider.GetRequiredService<ILoggerFactory>();
        _logger = loggerFactory.CreateLogger(nameof(PacketLoginHandler));

        _loginClient = serviceProvider.GetRequiredService<LoginClient>();
        _server = serviceProvider.GetRequiredService<GatewayServer>();

        _resourceManager = serviceProvider.GetRequiredService<IResourceManager>();
        _dbContextFactory = serviceProvider.GetRequiredService<IDbContextFactory<DatabaseContext>>();

        var options = serviceProvider.GetRequiredService<IOptionsMonitor<GatewayServerOptions>>();
        _options = options.CurrentValue;
        options.OnChange(o => _options = o);
    }

    public static bool HandlePacket(GatewayConnection connection, Span<byte> data)
    {
        if (!PacketLogin.TryDeserialize(data, out var packet))
        {
            _logger.LogError("Failed to deserialize {packet}.", nameof(PacketLogin));
            return false;
        }

        _logger.LogTrace("Received {name} packet. ( {packet} )", nameof(PacketLogin), packet);

        var packetLoginReply = new PacketLoginReply();

        if (packet.Version != _options.ClientVersion)
        {
            _logger.LogError("{connection} connected with a different client version. ( Guid: {guid}, ClientVersion: \"{version}\" )", connection, packet.Guid, packet.Version);

            connection.Send(packetLoginReply);

            connection.Disconnect();

            return true;
        }

        if (!Guid.TryParse(packet.Ticket, out var ticket))
        {
            _logger.LogError("{connection} connected with an invalid ticket. ( Guid: {guid}, Ticket: \"{ticket}\" )", connection, packet.Guid, packet.Ticket);

            connection.Send(packetLoginReply);

            connection.Disconnect();

            return true;
        }

        ulong characterId;

        try
        {
            characterId = GuidHelper.GetPlayerId(packet.Guid);
        }
        catch (ArgumentException)
        {
            _logger.LogWarning("{connection} connected with a guid that isn't a player's. ( Guid: {guid} )", connection, packet.Guid);

            connection.Send(packetLoginReply);

            connection.Disconnect();

            return true;
        }

        // One player per connection and one connection per character. The Login server refuses a character that is
        // already online, but it only learns of a login from this server after the fact, so it is checked here too.
        var refusal = LoginAdmission.Check(
            connectionHasPlayer: connection.Player is not null,
            characterOnline: _server.OnlineCharacterIds().Contains(characterId));

        if (refusal != LoginRefusal.None)
        {
            _logger.LogWarning("{connection} refused a login of character {id}: {refusal}.", connection, characterId, refusal);

            connection.Send(packetLoginReply);

            connection.Disconnect();

            return true;
        }

        // Use ticket as key.
        connection.InitializeCipher(packet.Ticket);

        using var dbContext = _dbContextFactory.CreateDbContext();

        var character = dbContext.Characters
            .AsNoTrackingWithIdentityResolution()
            .Include(x => x.User)
            .Include(x => x.Items)
            .Include(x => x.Titles)
            .Include(x => x.Mounts)
            .Include(x => x.Friends)
                .ThenInclude(x => x.FriendCharacter)
            .Include(x => x.Ignores)
                .ThenInclude(x => x.IgnoreCharacter)
            .Include(x => x.Profiles)
                .ThenInclude(x => x.Items)
            .Include(x => x.Quests)
            .Include(x => x.CollectionEntries)
            .Include(x => x.GuildMember!)
                .ThenInclude(x => x.Guild)
                    .ThenInclude(x => x.Members)
                        .ThenInclude(x => x.Character)
            .AsSplitQuery()
            .SingleOrDefault(x => x.Id == characterId
#if !DEBUG
                && x.Ticket == ticket
#endif
            );

        if (character is null)
        {
            _logger.LogWarning("{connection} connected with an invalid guid or ticket. ( Guid: {guid}, Ticket: \"{ticket}\" )", connection, packet.Guid, packet.Ticket);

            connection.Send(packetLoginReply);

            connection.Disconnect();

            return true;
        }

        if (character.User.LockedUntil != null)
        {
            DateTimeOffset currentTime = DateTimeOffset.UtcNow;
            DateTimeOffset? lockedUntil = character.User.LockedUntil;
            if (lockedUntil <= currentTime)
            {
                dbContext.Users
                    .Where(x => x.Id == character.User.Id)
                    .ExecuteUpdate(x => x
                        .SetProperty(u => u.LockedUntil, (DateTimeOffset?)null));
            }
            else
            {
                _logger.LogWarning("{connection} connected with a banned account. ( Guid: {guid}, Ticket: \"{ticket}\" )", connection, packet.Guid, packet.Ticket);

                connection.Send(packetLoginReply);

                connection.Disconnect();

                return true;
            }
        }
      
        var orphanedIgnores = character.Ignores
            .Where(x => x.IgnoreCharacter is null)
            .ToList();

        if (orphanedIgnores.Count > 0)
        {
            var orphanedIgnoreIds = orphanedIgnores
                .Select(x => x.IgnoreCharacterId)
                .ToList();

            dbContext.Ignores
                .Where(x => x.CharacterId == character.Id && orphanedIgnoreIds.Contains(x.IgnoreCharacterId))
                .ExecuteDelete();

            foreach (var orphanedIgnore in orphanedIgnores)
            {
                character.Ignores.Remove(orphanedIgnore);
            }
        }

        // Rewards and pickups used to be saved with tint 0 rather than the item's own, which the client draws as a
        // colour of its own (a white shirt came out neon red). Give those rows their own tint back. Housing items keep
        // theirs: housing has its own rule for a tint of 0 (HousingFixtureRules.ResolveItemTintId).
        foreach (var (item, tint) in ItemTints.FindMistinted(character.Items, _resourceManager.ClientItemDefinitions,
            keep: StoreInventoryPurchasePolicy.IsHousingInventoryItem))
        {
            var itemId = item.Id;

            if (dbContext.Items
                .Where(x => x.CharacterId == character.Id && x.Id == itemId && x.Tint == 0)
                .ExecuteUpdate(x => x.SetProperty(i => i.Tint, tint)) > 0)
            {
                item.Tint = tint;
            }
        }

        bool isReferee = character.User.IsMod || character.User.IsAdmin;
        if (isReferee)
        {
            ProfileHelper.AddSpecialProfile(character, dbContext, _resourceManager, _logger, SpecialProfileIds.Referee);
            ProfileHelper.AddSpecialProfile(character, dbContext, _resourceManager, _logger, SpecialProfileIds.Enforcer);
        }
        else
        {
            // if user is no longer a mod, remove referee profile
            ProfileHelper.RemoveSpecialProfile(character, dbContext, _logger, SpecialProfileIds.Referee);
            ProfileHelper.RemoveSpecialProfile(character, dbContext, _logger, SpecialProfileIds.Enforcer);
        }
#if !DEBUG
        // A ticket is used once: clearing it only while it is still the one presented makes two logins racing on the
        // same ticket, or on a newer one issued meanwhile, end with one of them refused.
        var result = dbContext.Characters
            .Where(x => x.Id == character.Id && x.Ticket == ticket)
            .ExecuteUpdate(x => x.SetProperty(x => x.Ticket, (Guid?)null));

        if (result <= 0)
        {
            connection.Send(packetLoginReply);

            connection.Disconnect();

            return true;
        }
#endif

        if (!connection.CreatePlayerFromDatabase(character))
        {
            connection.Send(packetLoginReply);

            connection.Disconnect();

            return true;
        }

        _loginClient.SendCharacterLogin(character.Id);

        packetLoginReply.Success = true;

        connection.Send(packetLoginReply);

        // TODO
        // AchievementObjectiveActivatedPacket - Part 1?
        // EncounterOverworldCombatPacket

        connection.SendInitializationParameters();
        connection.SendZoneDetails();
        connection.ClientGameSettings();
        connection.SendItemDefinitions();

        // TODO
        // AnnoucementDataSendPacket
        // AchievementObjectiveActivatedPacket - Part 2?

        connection.SendSelfToClient();

        _logger.LogInformation("{address} successfully logged in with character {name} ({id}).", connection.EndPoint.Address, character.FullName, character.Id);

        return true;
    }
}
