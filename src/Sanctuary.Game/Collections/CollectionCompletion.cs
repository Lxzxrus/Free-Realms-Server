using System.Linq;

using Sanctuary.Database;
using Sanctuary.Database.Entities;
using Sanctuary.Game.Helpers;
using Sanctuary.Game.Resources;
using Sanctuary.Game.Resources.Definitions;

namespace Sanctuary.Game.Collections;

/// <summary>
/// A finished collection pays once per character. An item-based collection counts what the character holds, so
/// selling or trading an entry item away and picking it up again finishes it again (threat model S6); the
/// <see cref="DbCharacterCompletedCollection"/> row is what stops that paying twice.
/// </summary>
public static class CollectionCompletion
{
    /// <summary>
    /// Records the first time a character finishes <paramref name="definition"/>, and stages its stars on the job it
    /// names, in <paramref name="dbContext"/> without saving; the caller saves them with the reward items. False when
    /// the character has finished it before, so nothing is paid again. <paramref name="experience"/> is null when the
    /// collection pays no stars, the character doesn't have that job, or the job is at the top level.
    /// </summary>
    public static bool TryRecord(DatabaseContext dbContext, RankLevelDefinitionCollection rankLevels, ulong characterId,
        CollectionDefinition definition, out ExperienceGain? experience)
    {
        experience = null;

        var completed = dbContext.CharacterCompletedCollections;

        if (completed.Local.Any(row => row.CharacterId == characterId && row.CollectionId == definition.Id) ||
            completed.Any(row => row.CharacterId == characterId && row.CollectionId == definition.Id))
        {
            return false;
        }

        completed.Add(new DbCharacterCompletedCollection
        {
            CharacterId = characterId,
            CollectionId = definition.Id
        });

        if (RewardHelper.TryAddExperience(rankLevels, dbContext, characterId, definition.RewardProfileId,
                definition.RewardExperience, out var gain))
        {
            experience = gain;
        }

        return true;
    }
}
