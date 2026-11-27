using System.Data;
using System.Linq;

using Microsoft.EntityFrameworkCore;

using Sanctuary.Database;
using Sanctuary.Database.Entities;
using Sanctuary.Game.Housing;
using Sanctuary.Packet.Common;

namespace Sanctuary.Game;

/// <summary>
/// Deleting a character, as the Login server does when a player deletes one at character select. Evergrove: here rather
/// than in the Login server's handler, so the tests delete a character exactly as a player does. Its houses must move
/// before the character goes (<see cref="HouseHandover"/>), and only a test that runs the real delete would notice if
/// they didn't.
/// </summary>
public static class CharacterDeletion
{
    /// <summary>
    /// Deletes <paramref name="character"/>, which <paramref name="dbContext"/> loaded and tracks, in one serializable
    /// transaction: it leaves its guild, its houses move to another character on its account, and its friend and ignore
    /// entries go, both ways. Run it through the database's execution strategy, which on MariaDB runs it again after a
    /// deadlock.
    /// </summary>
    /// <returns>What happened to its houses, or <c>null</c> if it wasn't deleted, with nothing changed.</returns>
    public static HouseHandoverResult? Delete(DatabaseContext dbContext, DbCharacter character)
    {
        // Evergrove: serializable, as for buying a lot. The houses' new characters are picked by the lots each already
        // has, so two deletes on one account at once mustn't both give one character the same lot.
        using var transaction = dbContext.Database.BeginTransaction(IsolationLevel.Serializable);

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
                return null;

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

        // Evergrove: houses belong to the account, so they move to another of its characters before the delete's
        // cascade can take them (docs/design/evergrove-housing.md, section 8).
        var handover = HouseHandover.MoveOff(dbContext, character.Id);

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
            return null;

        transaction.Commit();
        return handover;
    }
}
