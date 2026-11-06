using System;
using System.Data.Common;
using System.Linq;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

using Sanctuary.Core.Helpers;
using Sanctuary.Database;
using Sanctuary.Game;
using Sanctuary.Game.Housing;
using Sanctuary.Packet;
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
            try
            {
                // Evergrove: the delete, in its own transaction, is CharacterDeletion's, so the tests run the same one:
                // the character's houses must move to another character on the account before it goes.
                handover = CharacterDeletion.Delete(dbContext, character);

                return handover is not null;
            }
            catch (DbUpdateException ex)
            {
                _logger.LogError(ex, "Failed to delete character {CharacterId}.", character.Id);

                return false;
            }
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
