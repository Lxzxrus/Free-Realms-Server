using System;
using System.Collections.Generic;
using System.Linq;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

using Sanctuary.Core.IO;
using Sanctuary.Database;
using Sanctuary.Database.Entities;
using Sanctuary.Game.Entities;
using Sanctuary.Packet;
using Sanctuary.Packet.Common;

namespace Sanctuary.Game.Helpers;

public enum SpecialProfileIds
{
    Referee = 58,
    Enforcer = 20
}

public static class ProfileHelper
{
    /// <summary>
    /// The job every character starts with (Login's <c>DefaultProfileId</c>). A job with no starter kit of its own
    /// (Card Duelist) is worn in the clothes this one wears.
    /// </summary>
    public const int AdventurerProfileId = 1;

    /// <summary>
    /// The level a new job row starts at: 1, so it has a level bar to fill, except the staff jobs (Referee,
    /// Enforcer), which start at the top as upstream has them.
    /// </summary>
    public static int StartingLevel(int profileId) => Enum.IsDefined((SpecialProfileIds)profileId) ? 20 : 1;

    public static void GrantDefaultItems(DbCharacter character, DbProfile dbProfile,
        ClientProfileData profileData, IResourceManager resourceManager)
    {
        foreach (var defaultItemId in profileData.DefaultItems)
        {
            if (!resourceManager.ClientItemDefinitions.TryGetValue(defaultItemId, out var defaultClientItemDefinition))
                continue;

            if (defaultClientItemDefinition.GenderUsage != 0 && defaultClientItemDefinition.GenderUsage != character.Gender)
                continue;

            var dbItem = character.Items.FirstOrDefault(x => x.Definition == defaultItemId);

            if (dbItem is null)
            {
                dbItem = new DbItem
                {
                    Id = character.Items.Count > 0 ? character.Items.Max(x => x.Id) + 1 : 1,
                    CharacterId = character.Id,
                    Definition = defaultClientItemDefinition.Id,
                    Tint = defaultClientItemDefinition.Icon.TintId,
                    Count = 1
                };

                character.Items.Add(dbItem);
            }

            dbProfile.Items.Add(dbItem);
        }
    }

    /// <summary>
    /// Adds a job to the character's rows, not saved: the job's row at <see cref="StartingLevel"/>, holding its
    /// starter kit (<see cref="GrantDefaultItems"/>, which skips the other gender's pieces and reuses items the
    /// character already owns). A job with no kit of its own holds <paramref name="fallbackKit"/> instead. False, with
    /// nothing added, when the character already has the job.
    /// </summary>
    public static bool TryAddProfile(DbCharacter character, ClientProfileData profileData,
        IResourceManager resourceManager, IEnumerable<DbItem> fallbackKit, out DbProfile profile)
    {
        profile = null!;

        if (character.Profiles.Any(existing => existing.Id == profileData.Id))
            return false;

        profile = new DbProfile
        {
            CharacterId = character.Id,
            Id = profileData.Id,
            Level = StartingLevel(profileData.Id)
        };

        if (profileData.DefaultItems.Count > 0)
            GrantDefaultItems(character, profile, profileData, resourceManager);
        else
        {
            foreach (var item in fallbackKit)
                profile.Items.Add(item);
        }

        character.Profiles.Add(profile);
        return true;
    }

    /// <summary>
    /// The client's view of a saved job: its definition, its level bar and the items it wears, one per slot. What
    /// login sends for each job, and what a job granted while playing is sent as.
    /// </summary>
    public static ClientPcProfile CreateClientProfile(ClientProfileData profileData, DbProfile dbProfile,
        IResourceManager resourceManager)
    {
        var clientPcProfile = new ClientPcProfile
        {
            Id = dbProfile.Id,
            NameId = profileData.NameId,
            DescriptionId = profileData.DescriptionId,
            Type = profileData.Type,
            Icon = profileData.Icon,
            AbilityBgImageSet = profileData.AbilityBgImageSet,
            BadgeImageSet = profileData.BadgeImageSet,
            ButtonImageSet = profileData.ButtonImageSet,
            MembersOnly = profileData.MembersOnly,
            ItemClasses = profileData.ItemClasses
        };

        RewardHelper.SetRank(resourceManager.RankLevels, clientPcProfile, dbProfile.Level, dbProfile.LevelXP);

        foreach (var dbItem in dbProfile.Items)
        {
            if (!resourceManager.ClientItemDefinitions.TryGetValue(dbItem.Definition, out var clientItemDefinition))
                continue;

            if (clientPcProfile.Items.TryGetValue(clientItemDefinition.Slot, out var profileItem))
                profileItem.Id = dbItem.Id;
            else
            {
                clientPcProfile.Items.Add(clientItemDefinition.Slot, new ProfileItem
                {
                    Id = dbItem.Id,
                    Slot = clientItemDefinition.Slot
                });
            }
        }

        return clientPcProfile;
    }

    /// <summary>
    /// Puts a job in the player's list, and its type in the list of job types the first job of each type fills.
    /// </summary>
    public static void AddClientProfile(Player player, ClientPcProfile profile)
    {
        player.Profiles.Add(profile);

        if (!player.ProfileTypes.Any(entry => entry.Type == profile.Type))
        {
            player.ProfileTypes.Add(new ProfileTypeEntry
            {
                Type = profile.Type,
                ProfileId = profile.Id
            });
        }
    }

    /// <summary>
    /// Switches the player into one of their jobs: the job's outfit with the job swirl, seen by everyone nearby, its
    /// action bar, and its badge for online friends. False when the player doesn't have the job.
    /// </summary>
    public static bool Activate(Player player, int profileId, IZoneManager zoneManager)
    {
        var profile = player.Profiles.FirstOrDefault(x => x.Id == profileId);

        if (profile is null)
            return false;

        player.ActiveProfileId = profileId;

        var clientUpdatePacketActivateProfile = new ClientUpdatePacketActivateProfile();

        using var packetWriter = new PacketWriter();

        profile.Serialize(packetWriter);

        clientUpdatePacketActivateProfile.Payload = packetWriter.Buffer;

        clientUpdatePacketActivateProfile.Attachments = player.GetAttachments();

        clientUpdatePacketActivateProfile.Animation = 3001; // emo_outfit_all
        clientUpdatePacketActivateProfile.CompositeEffect = 4005; // PFX_Job_Swirl

        player.SendTunneled(clientUpdatePacketActivateProfile);

        var playerUpdatePacketEquippedItemsChange = new PlayerUpdatePacketEquippedItemsChange();

        playerUpdatePacketEquippedItemsChange.Guid = player.Guid;

        playerUpdatePacketEquippedItemsChange.Attachments = clientUpdatePacketActivateProfile.Attachments;

        player.SendTunneledToVisible(playerUpdatePacketEquippedItemsChange);

        player.SendToolbar();

        var friendStatusPacket = new FriendStatusPacket
        {
            Guid = player.Guid,
            Status =
            {
                ProfileId = player.ActiveProfile.Id,
                ProfileRank = player.ActiveProfile.Rank,
                ProfileIconId = player.ActiveProfile.Icon,
                ProfileNameId = player.ActiveProfile.NameId,
                ProfileBackgroundImageId = player.ActiveProfile.BadgeImageSet
            }
        };

        foreach (var friend in player.Friends)
        {
            if (!zoneManager.TryGetPlayer(friend.Guid, out var friendPlayer))
                continue;

            friendPlayer.SendTunneled(friendStatusPacket);
        }

        return true;
    }

    public static bool AddSpecialProfile(DbCharacter character, DatabaseContext dbContext,
        IResourceManager resourceManager, ILogger logger, SpecialProfileIds profileId)
    {
        int id = (int)profileId;

        if (!resourceManager.Profiles.TryGetValue(id, out var profileData))
        {
            logger.LogWarning("Profile with ID {profileId} does not exist in the resource manager.", id);
            return false;
        }

        var existingItemIds = character.Items.Select(x => x.Id).ToHashSet();

        if (!TryAddProfile(character, profileData, resourceManager, [], out var newProfile))
        {
            logger.LogDebug("Character {characterId} already has profile {profileId}.", character.Id, id);
            return false;
        }

        // On the character only once it is saved, so a failed save leaves no job behind in the session.
        character.Profiles.Remove(newProfile);

        dbContext.Attach(character);

        foreach (var item in character.Items)
        {
            if (!existingItemIds.Contains(item.Id))
                dbContext.Entry(item).State = EntityState.Added;
        }

        dbContext.Entry(newProfile).State = EntityState.Added;

        try
        {
            if (dbContext.SaveChanges() <= 0)
            {
                logger.LogWarning("Failed to save profile {profileId} for character {characterId}.", id, character.Id);
                return false;
            }
        }
        catch (DbUpdateException ex)
        {
            logger.LogError(ex, "Failed to save profile {profileId} for character {characterId}.", id, character.Id);
            return false;
        }
        character.Profiles.Add(newProfile);
        return true;
    }

    public static bool RemoveSpecialProfile(DbCharacter character, DatabaseContext dbContext, ILogger logger, SpecialProfileIds profileId)
    {
        int id = (int)profileId;

        if (!character.Profiles.Any(p => p.Id == id))
            return true;

        try
        {
            if (dbContext.Profiles.Where(p => p.CharacterId == character.Id && p.Id == id).ExecuteDelete() <= 0)
            {
                logger.LogWarning("Failed to remove profile {profileId} for character {characterId}.", id, character.Id);
                return false;
            }
        }
        catch (DbUpdateException ex)
        {
            logger.LogError(ex, "Failed to remove profile {profileId} for character {characterId}.", id, character.Id);
            return false;
        }

        var profileToRemove = character.Profiles.FirstOrDefault(p => p.Id == id);
        if (profileToRemove != null)
        {
            character.Profiles.Remove(profileToRemove);
        }

        return true;
    }
}
