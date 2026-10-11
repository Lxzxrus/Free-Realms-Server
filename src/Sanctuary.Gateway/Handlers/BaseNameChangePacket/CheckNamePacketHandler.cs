using System;
using System.Linq;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

using Sanctuary.Core.Helpers;
using Sanctuary.Database;
using Sanctuary.Game;
using Sanctuary.Game.Helpers;
using Sanctuary.Gateway.Helpers;
using Sanctuary.Packet;
using Sanctuary.Packet.Common;
using Sanctuary.Packet.Common.Attributes;

namespace Sanctuary.Gateway.Handlers;

[PacketHandler]
public static class CheckNamePacketHandler
{
    private static ILogger _logger = null!;
    private static IDbContextFactory<DatabaseContext> _dbContextFactory = null!;
    private static IResourceManager _resourceManager = null!;

    public static void ConfigureServices(IServiceProvider serviceProvider)
    {
        var loggerFactory = serviceProvider.GetRequiredService<ILoggerFactory>();
        _logger = loggerFactory.CreateLogger(nameof(CheckNamePacketHandler));

        _dbContextFactory = serviceProvider.GetRequiredService<IDbContextFactory<DatabaseContext>>();
        _resourceManager = serviceProvider.GetRequiredService<IResourceManager>();
    }

    public static bool HandlePacket(GatewayConnection connection, ReadOnlySpan<byte> data)
    {
        if (!CheckNamePacket.TryDeserialize(data, out var packet))
        {
            _logger.LogError("Failed to deserialize {packet}.", nameof(CheckNamePacket));
            return false;
        }

        _logger.LogTrace("Received {name} packet. ( {packet} )", nameof(CheckNamePacket), packet);

        var checkNameResponsePacket = new CheckNameResponsePacket();

        checkNameResponsePacket.Type = packet.Type;
        checkNameResponsePacket.Guid = packet.Guid;

        // TODO: Check if we have the item that let's us change name.
        if (packet.Token)
        {
            // While the rename cooldown runs, the player has no rename to use, and is told when they will.
            if (CharacterRenameCooldown(connection, packet) is TimeSpan cooldown)
            {
                checkNameResponsePacket.Result = CheckNameResponse.MissingItem;
                ChatHelper.SendSystemMessage(connection.Player, RenameCooldown.Message(cooldown));
            }
            else
            {
                checkNameResponsePacket.Result = CheckNameResponse.FoundItem;
            }

            connection.SendTunneled(checkNameResponsePacket);

            return true;
        }

        checkNameResponsePacket.Name = packet.Name;

        checkNameResponsePacket.Result = packet.Type switch
        {
            NameChangeType.Character => OnCheckCharacterName(connection, packet),
            NameChangeType.Guild => OnCheckGuildName(connection, packet),
            _ => CheckNameResponse.Invalid
        };

        connection.SendTunneled(checkNameResponsePacket);

        return true;
    }

    /// <summary>The time left before the player's character may be renamed, or <c>null</c> if it may be now.</summary>
    private static TimeSpan? CharacterRenameCooldown(GatewayConnection connection, CheckNamePacket packet)
    {
        if (packet.Type != NameChangeType.Character)
            return null;

        using var dbContext = _dbContextFactory.CreateDbContext();

        var characterId = GuidHelper.GetPlayerId(connection.Player.Guid);
        var lastRenamed = dbContext.Characters
            .Where(character => character.Id == characterId)
            .Select(character => character.LastRenamed)
            .FirstOrDefault();

        var remaining = RenameCooldown.Remaining(lastRenamed, DateTimeOffset.UtcNow);
        return remaining > TimeSpan.Zero ? remaining : null;
    }

    private static CheckNameResponse OnCheckCharacterName(GatewayConnection connection, CheckNamePacket packet)
    {
        if (string.IsNullOrWhiteSpace(packet.Name.FirstName)
            || packet.Name.LastName != string.Empty && string.IsNullOrWhiteSpace(packet.Name.LastName))
        {
            return CheckNameResponse.IncorrectLength;
        }

        if (packet.Name.FirstName.Length < 3)
            return CheckNameResponse.FirstNameTooShort;

        if (packet.Name.FirstName.Length > 14)
            return CheckNameResponse.FirstNameTooLong;

        if (packet.Name.LastName != string.Empty)
        {
            if (packet.Name.LastName.Length < 3)
                return CheckNameResponse.LastNameTooShort;

            if (packet.Name.LastName.Length > 14)
                return CheckNameResponse.LastNameTooLong;
        }

        if (CharacterNameHelper.ContainsIllegalCharacters(packet.Name.FullName))
        {
            return CheckNameResponse.IllegalCharacters;
        }

        if (_resourceManager.NameFilter.Any(token =>
            packet.Name.FullName.Contains(token, StringComparison.OrdinalIgnoreCase)))
        {
            return CheckNameResponse.Profane;
        }

        using var dbContext = _dbContextFactory.CreateDbContext();
        var taken = dbContext.Characters.Any(x => x.FirstName == packet.Name.FirstName && x.LastName == packet.Name.LastName);

        if (taken)
            return CheckNameResponse.Taken;

        return CheckNameResponse.Available;
    }

    private static CheckNameResponse OnCheckGuildName(GatewayConnection connection, CheckNamePacket packet)
    {
        var guildName = GuildHelper.NormalizeName(packet.Name.FullName);

        switch (GuildHelper.ValidateName(guildName))
        {
            case GuildNameValidationResult.IncorrectLength:
                return CheckNameResponse.IncorrectLength;
            case GuildNameValidationResult.IllegalCharacters:
                return CheckNameResponse.IllegalCharacters;
        }

        if (GuildHelper.IsProfane(guildName, _resourceManager.NameFilter))
            return CheckNameResponse.Profane;

        using var dbContext = _dbContextFactory.CreateDbContext();

        var currentGuildGuid = connection.Player.GuildData?.Guid ?? 0;

        if (GuildHelper.IsNameTaken(dbContext, currentGuildGuid, guildName))
            return CheckNameResponse.Taken;

        return CheckNameResponse.Available;
    }
}
