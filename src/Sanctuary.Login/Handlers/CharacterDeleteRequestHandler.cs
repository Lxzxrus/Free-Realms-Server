using System;
using System.Data;
using System.Data.Common;
using System.Linq;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

using Sanctuary.Core.Helpers;
using Sanctuary.Database;
using Sanctuary.Game.Housing;
using Sanctuary.Packet;
using Sanctuary.Packet.Common;
using Sanctuary.Packet.Common.Attributes;

namespace Sanctuary.Login.Handlers;

[PacketHandler]
public static class CharacterDeleteRequestHandler
{
    private static ILogger _logger = null!;
    private static IDbContextFactory<DatabaseContext> _dbContextFactory = null!;

    public static void ConfigureServices(IServiceProvider serviceProvider)
    {
        var loggerFactory = serviceProvider.GetRequiredService<ILoggerFactory>();
        _logger = loggerFactory.CreateLogger(nameof(CharacterDeleteRequestHandler));

        _dbContextFactory = serviceProvider.GetRequiredService<IDbContextFactory<DatabaseContext>>();
    }

    public static bool HandlePacket(LoginConnection connection, Span<byte> data)
    {
        if (!CharacterDeleteRequest.TryDeserialize(data, out var packet))
        {
            _logger.LogError("Failed to deserialize {packet}.", nameof(CharacterDeleteRequest));
            return false;
        }

        _logger.LogTrace("Received {name} packet. ( {packet} )", nameof(CharacterDeleteRequest), packet);

        var characterDeleteReply = new CharacterDeleteReply();

        if (connection.UserId == 0)
        {
            characterDeleteReply.Status = 2;

            connection.Send(characterDeleteReply);

            return true;
        }

        using var dbContext = _dbContextFactory.CreateDbContext();

        var character = dbContext.Characters.SingleOrDefault(x => x.UserId == connection.UserId && x.Id == GuidHelper.GetPlayerId(packet.EntityKey));

        if (character is null)
        {
            characterDeleteReply.Status = 2;

            connection.Send(characterDeleteReply);

            return true;
        }

        HouseHandoverResult? handover = null;

        var strategy = dbContext.Database.CreateExecutionStrategy();
        var committed = TryExecute(strategy, character.Id, () =>
        {
            // Evergrove: serializable, as for buying a lot. The houses' new characters are picked by the lots each already
            // has, so two deletes on one account at once mustn't both give one character the same lot.
            using var transaction = dbContext.Database.BeginTransaction(IsolationLevel.Serializable);

            try
            {
                var guildMember = dbContext.GuildMembers
                    .AsNoTracking()
                    .SingleOrDefault(x => x.Id == character.Id);

                if (guildMember is not null)
                {
                    character.GuildMemberId = null;

                    var deletedGuildMember = dbContext.GuildMembers
                        .Where(x => x.Id == character.Id)
                        .ExecuteDelete();

                    if (deletedGuildMember <= 0)
                        return false;

                    var hasMembers = dbContext.GuildMembers.Any(x => x.GuildId == guildMember.GuildId);
                    if (!hasMembers)
                    {
                        dbContext.Guilds
                            .Where(x => x.Id == guildMember.GuildId)
                            .ExecuteDelete();
                    }
                    else if (guildMember.Role == GuildRole.Leader.Id
                             && !dbContext.GuildMembers.Any(x => x.GuildId == guildMember.GuildId && x.Role == GuildRole.Leader.Id))
                    {
                        var newLeader = dbContext.GuildMembers
                            .Where(x => x.GuildId == guildMember.GuildId)
                            // SQLite can't sort by DateTimeOffset or ulong in SQL, so sort after loading.
                            .AsEnumerable()
                            .OrderBy(x => x.Role)
                            .ThenBy(x => x.Joined)
                            .ThenBy(x => x.Id)
                            .FirstOrDefault();

                        if (newLeader is not null)
                            newLeader.Role = GuildRole.Leader.Id;
                    }
                }

                // Evergrove: houses belong to the account, so they move to another of its characters before the
                // delete's cascade can take them (docs/design/evergrove-housing.md, section 8).
                handover = HouseHandover.MoveOff(dbContext, character.Id);

                // Delete references first to avoid foreign key constraint violations.
                var characterId = character.Id;

                var friends = dbContext.Friends.Where(f =>
                    f.CharacterId == characterId || f.FriendCharacterId == characterId);

                var ignores = dbContext.Ignores.Where(i =>
                    i.CharacterId == characterId || i.IgnoreCharacterId == characterId);

                dbContext.Friends.RemoveRange(friends);
                dbContext.Ignores.RemoveRange(ignores);
                dbContext.Remove(character);

                if (dbContext.SaveChanges() == 0)
                    return false;
            }
            catch (DbUpdateException ex)
            {
                _logger.LogError(ex, "Failed to delete character {CharacterId}.", character.Id);

                return false;
            }

            transaction.Commit();
            return true;
        });

        if (!committed)
        {
            characterDeleteReply.Status = 2;

            connection.Send(characterDeleteReply);

            return true;
        }

        if (handover is not null)
            LogHandover(character.Id, handover);

        characterDeleteReply.Status = 1;
        characterDeleteReply.EntityKey = packet.EntityKey;

        connection.Send(characterDeleteReply);

        return true;
    }

    /// <summary>
    /// Evergrove: runs the delete through the execution strategy, which on MariaDB tries it again after a deadlock. A
    /// database error that still gets out (from moving the houses, say, or a deadlock it gave up on) refuses the delete
    /// with nothing changed, rather than leaving the client with no answer.
    /// </summary>
    private static bool TryExecute(IExecutionStrategy strategy, ulong characterId, Func<bool> delete)
    {
        try
        {
            return strategy.Execute(delete);
        }
        catch (Exception exception) when (exception is DbException or RetryLimitExceededException)
        {
            _logger.LogError(exception, "Failed to delete character {CharacterId}.", characterId);

            return false;
        }
    }

    private static void LogHandover(ulong characterId, HouseHandoverResult handover)
    {
        foreach (var move in handover.Moved)
        {
            _logger.LogInformation("Deleted character {CharacterId}'s house {HouseId} (lot {ZoneDefinitionId}) moved to character {HolderId}.",
                characterId, move.HouseId, move.ZoneDefinitionId, move.HolderId);
        }

        if (handover.Unmoved.Count == 0)
            return;

        _logger.LogInformation("Deleted character {CharacterId}'s houses {HouseIds} went with it: {Reason}.",
            characterId,
            string.Join(", ", handover.Unmoved),
            handover.OtherCharacters == 0
                ? "its account has no other character"
                : "every other character on its account already has that lot");
    }
}
