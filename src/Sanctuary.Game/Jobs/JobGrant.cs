using System;
using System.Linq;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

using Sanctuary.Database;
using Sanctuary.Database.Entities;
using Sanctuary.Game.Helpers;

namespace Sanctuary.Game.Jobs;

public enum JobGrantResult
{
    Granted,
    /// <summary>The character already has the job; nothing changed.</summary>
    AlreadyHad,
    /// <summary>No job with that id in <c>Profiles.json</c>.</summary>
    UnknownJob,
    /// <summary>Referee and Enforcer: login gives and takes them with the account's staff flags, nothing else does.</summary>
    StaffJob,
    CharacterNotFound,
    SaveFailed
}

/// <summary>
/// Gives a character a job: the job's row at level 1 with its starter kit, saved. What character creation and
/// <c>UnlockAllProfiles</c> write for a new character (<see cref="ProfileHelper.TryAddProfile"/>), for one that
/// already exists. <see cref="JobManager"/> then tells a player who is online.
/// </summary>
public static class JobGrant
{
    /// <summary>
    /// Saves the job on the character. Granting a job the character has is <see cref="JobGrantResult.AlreadyHad"/>
    /// and changes nothing, so a second grant, or a second click on the trainer, is harmless. <paramref name="profile"/>
    /// is the saved row with its kit when the result is <see cref="JobGrantResult.Granted"/>.
    /// </summary>
    public static JobGrantResult TrySave(DatabaseContext dbContext, IResourceManager resourceManager, ILogger logger,
        ulong characterId, int profileId, out DbProfile profile)
    {
        profile = null!;

        if (Enum.IsDefined((SpecialProfileIds)profileId))
            return JobGrantResult.StaffJob;

        if (!resourceManager.Profiles.TryGetValue(profileId, out var profileData))
            return JobGrantResult.UnknownJob;

        var character = dbContext.Characters
            .Include(character => character.Items)
            .Include(character => character.Profiles)
                .ThenInclude(profile => profile.Items)
            .SingleOrDefault(character => character.Id == characterId);

        if (character is null)
            return JobGrantResult.CharacterNotFound;

        // A job with no kit of its own is worn in the Adventurer's clothes, as UnlockAllProfiles does at creation.
        var fallbackKit = character.Profiles
            .Where(existing => existing.Id == ProfileHelper.AdventurerProfileId)
            .SelectMany(existing => existing.Items)
            .ToList();

        if (!ProfileHelper.TryAddProfile(character, profileData, resourceManager, fallbackKit, out profile))
            return JobGrantResult.AlreadyHad;

        try
        {
            if (dbContext.SaveChanges() <= 0)
                return JobGrantResult.SaveFailed;
        }
        catch (DbUpdateException ex)
        {
            logger.LogError(ex, "Failed to save job {profileId} for character {characterId}.", profileId, characterId);
            return JobGrantResult.SaveFailed;
        }

        return JobGrantResult.Granted;
    }
}
