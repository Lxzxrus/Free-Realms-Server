using System;
using System.Linq;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

using Sanctuary.Core.IO;
using Sanctuary.Database;
using Sanctuary.Database.Entities;
using Sanctuary.Game.Entities;
using Sanctuary.Game.Resources;
using Sanctuary.Game.Resources.Definitions.Rewards;
using Sanctuary.Packet;
using Sanctuary.Packet.Common;

namespace Sanctuary.Game.Helpers;

/// <summary>
/// Pays items, coins and experience to a player: saves them first, then tells the client. From upstream OSFR PR #120
/// ("Exp granting &amp; level-ups") by ImAlko, which replaced the old <c>RewardManager</c>.
/// </summary>
public static class RewardHelper
{
    public static bool TryGrantReward(IResourceManager resourceManager, DatabaseContext dbContext, ILogger logger,
        Player player, string rewardTableKey, ulong sourceGuid = 0)
    {
        if (!resourceManager.RewardTables.TryRoll(rewardTableKey, out var drop))
        {
            logger.LogError("Unknown reward table {key}.", rewardTableKey);
            return false;
        }

        return TryGrantReward(resourceManager, dbContext, logger, player, drop, sourceGuid);
    }

    public static bool TryGrantReward(IResourceManager resourceManager, DatabaseContext dbContext, ILogger logger,
        Player player, RewardDropDefinition drop, ulong sourceGuid = 0)
    {
        return drop switch
        {
            ItemRewardDropDefinition item => TryGrantItem(resourceManager, dbContext, logger, player,
                item.ItemDefinitionId, item.TintTable?.SelectRandom().TintId, item.Quantity, sourceGuid),
            CurrencyRewardDropDefinition currency => TryGrantCurrency(dbContext, logger, player, currency.CurrencyType, currency.Amount, sourceGuid),
            _ => false
        };
    }

    /// <summary>
    /// Pays <paramref name="quantity"/> of an item at <paramref name="tint"/>, or at the item's own tint
    /// (<see cref="ItemTints.Default"/>) when that's null.
    /// </summary>
    public static bool TryGrantItem(IResourceManager resourceManager, DatabaseContext dbContext, ILogger logger,
        Player player, int itemDefinitionId, int? tint, int quantity, ulong sourceGuid = 0)
    {
        // TODO: I think similar logic lives in the shop + collections stuff. We should eventually extract
        // and generalize that to avoid repeats.
        // https://github.com/Open-Source-Free-Realms/Sanctuary/issues/114
        if (!resourceManager.ClientItemDefinitions.TryGetValue(itemDefinitionId, out var itemDefinition))
        {
            logger.LogError("Cannot grant unknown item definition {itemDefinitionId}.", itemDefinitionId);
            return false;
        }

        var itemTint = tint ?? ItemTints.Default(itemDefinition);

        var characterId = player.CharacterId;

        var dbCharacter = dbContext.Characters
            .Include(character => character.Items)
            .SingleOrDefault(character => character.Id == characterId);

        if (dbCharacter is null)
            return false;

        var dbItem = dbCharacter.Items.SingleOrDefault(item => item.Definition == itemDefinitionId && item.Tint == itemTint);

        if (dbItem is null)
        {
            dbItem = new DbItem
            {
                Id = dbCharacter.Items.Select(item => item.Id).DefaultIfEmpty(0).Max() + 1,
                Definition = itemDefinitionId,
                Count = quantity,
                Tint = itemTint
            };

            dbCharacter.Items.Add(dbItem);
        }
        else
        {
            dbItem.Count += quantity;
        }

        try
        {
            if (dbContext.SaveChanges() <= 0)
                return false;
        }
        catch (DbUpdateException ex)
        {
            logger.LogError(ex, "Failed to save granted item {itemDefinitionId} for character {characterId}.", itemDefinitionId, characterId);
            return false;
        }

        var clientItem = SendItem(player, dbItem, itemDefinition);

        var packet = CreateRewardBundlePacket(player, sourceGuid, iconId: itemDefinition.Icon.Id,
            nameId: itemDefinition.NameId, descriptionId: itemDefinition.DescriptionId);

        packet.RewardBundle.Entries.Add(new RewardBundleEntryItem
        {
            IconId = itemDefinition.Icon.Id,
            NameId = itemDefinition.NameId,
            DefinitionId = clientItem.Definition,
            Tint = clientItem.Tint,
            Quantity = quantity,
            ItemGuid = clientItem.Id
        });

        player.SendTunneled(packet);

        return true;
    }

    /// <summary>
    /// Mirrors a saved item row into the player's bags and tells the client: a row the player doesn't have yet is
    /// added, one they have gets its new count.
    /// </summary>
    public static ClientItem SendItem(Player player, DbItem dbItem, ClientItemDefinition itemDefinition)
    {
        var clientItem = player.Items.SingleOrDefault(item => item.Id == dbItem.Id);

        if (clientItem is null)
        {
            clientItem = new ClientItem
            {
                Id = dbItem.Id,
                Definition = dbItem.Definition,
                Count = dbItem.Count,
                Tint = dbItem.Tint
            };

            player.Items.Add(clientItem);

            using var writer = new PacketWriter();
            clientItem.Serialize(writer);
            itemDefinition.Serialize(writer);

            player.SendTunneled(new ClientUpdatePacketItemAdd { Payload = writer.Buffer });
        }
        else
        {
            clientItem.Count = dbItem.Count;
            player.SendTunneled(new ClientUpdatePacketItemUpdate
            {
                ItemGuid = clientItem.Id,
                Count = clientItem.Count
            });
        }

        return clientItem;
    }

    public static bool TryGrantCurrency(DatabaseContext dbContext, ILogger logger, Player player, CurrencyType currencyType,
        int amount, ulong sourceGuid = 0)
    {
        if (amount <= 0)
            return false;

        if (currencyType == CurrencyType.StationCash)
        {
            logger.LogWarning("Station Cash grants are not yet implemented.");
            return false;
        }

        var characterId = player.CharacterId;

        var dbCharacter = dbContext.Characters.SingleOrDefault(character => character.Id == characterId);

        if (dbCharacter is null)
            return false;

        dbCharacter.Coins += amount;

        try
        {
            if (dbContext.SaveChanges() <= 0)
                return false;
        }
        catch (DbUpdateException ex)
        {
            logger.LogError(ex, "Failed to save granted currency for character {characterId}.", characterId);
            return false;
        }

        player.Coins = dbCharacter.Coins;

        player.SendTunneled(new ClientUpdatePacketCoinCount { Coins = player.Coins });
        player.SendTunneled(CreateRewardBundlePacket(player, sourceGuid, coins: amount));

        return true;
    }

    /// <summary>
    /// Pays stars to one of the player's jobs, saves them, and moves the client's level bar, with the level-up notice
    /// when they finish a level. <paramref name="showReward"/> adds a reward toast for the stars; leave it off when the
    /// caller already shows them in a reward bundle of its own (a quest turn-in, a finished collection). False, with
    /// nothing changed, when the character doesn't have that job, the job is at the top level, or the save fails.
    /// </summary>
    public static bool TryGrantExperience(IResourceManager resourceManager, DatabaseContext dbContext, ILogger logger,
        Player player, int profileId, int amount, ulong sourceGuid = 0, bool showReward = true)
    {
        if (!TryAddExperience(resourceManager.RankLevels, dbContext, player.CharacterId, profileId, amount, out var gain))
            return false;

        try
        {
            if (dbContext.SaveChanges() <= 0)
                return false;
        }
        catch (DbUpdateException ex)
        {
            logger.LogError(ex, "Failed to save granted experience for profile {profileId}, character {characterId}.",
                profileId, player.CharacterId);
            return false;
        }

        SendExperience(resourceManager.RankLevels, logger, player, gain);

        if (showReward)
            player.SendTunneled(CreateRewardBundlePacket(player, sourceGuid, experience: amount));

        return true;
    }

    /// <summary>
    /// Adds stars to a character's job in <paramref name="dbContext"/> without saving, so the caller saves them along
    /// with its own changes, then calls <see cref="SendExperience"/>. False, with nothing changed, when there are no
    /// stars to add, the character doesn't have that job, or the job is already at the top level.
    /// </summary>
    public static bool TryAddExperience(RankLevelDefinitionCollection rankLevels, DatabaseContext dbContext,
        ulong characterId, int profileId, int amount, out ExperienceGain gain)
    {
        gain = default;

        if (amount <= 0)
            return false;

        // Re-read the row it changes, like every other writer, rather than trusting what the player holds.
        var dbProfile = dbContext.Profiles
            .SingleOrDefault(profile => profile.CharacterId == characterId && profile.Id == profileId);

        if (dbProfile is null || dbProfile.Level >= rankLevels.MaxLevel)
            return false;

        var oldLevel = dbProfile.Level;

        (dbProfile.Level, dbProfile.LevelXP) = rankLevels.Accrue(dbProfile.Level, dbProfile.LevelXP, amount);

        gain = new ExperienceGain(profileId, amount, oldLevel, dbProfile.Level, dbProfile.LevelXP);

        return true;
    }

    /// <summary>
    /// Shows a saved <see cref="ExperienceGain"/> to the player: the level-up notice if a level was finished, then the
    /// job's new level and stars.
    /// </summary>
    public static void SendExperience(RankLevelDefinitionCollection rankLevels, ILogger logger, Player player, ExperienceGain gain)
    {
        var profile = player.Profiles.SingleOrDefault(profile => profile.Id == gain.ProfileId);

        // Saved but not shown; the next login loads it.
        if (profile is null)
        {
            logger.LogWarning("Granted experience to profile {profileId} for {guid}, but no matching client profile is loaded.",
                gain.ProfileId, player.Guid);
            return;
        }

        SetRank(rankLevels, profile, gain.Level, gain.LevelXp);

        if (gain.LeveledUp)
            player.SendTunneled(CreateRankPacket(profile));

        player.SendTunneled(CreateExperiencePacket(profile));

        // Reaching the guild rank shows the client's "create guild" now, not at the next zone change.
        if (gain.OldLevel < Player.GuildCreationRank && gain.Level >= Player.GuildCreationRank)
            player.SendTunneled(new GuildCanCreateGuildPacket { CanCreateGuild = player.CanCreateGuild });
    }

    /// <summary>
    /// A job's saved level and stars, in the fields the client's profile carries them in. Login and every gain both
    /// set them through here, so a relog shows what the last gain did.
    /// </summary>
    public static void SetRank(RankLevelDefinitionCollection rankLevels, ClientPcProfile profile, int level, int levelXp)
    {
        profile.Rank = level;
        profile.RankPercent = rankLevels.GetRankPercent(level, levelXp);
        profile.StarsEarned = levelXp;
    }

    internal static ClientUpdatePacketUpdateProfileExperience CreateExperiencePacket(ClientPcProfile profile)
    {
        return new ClientUpdatePacketUpdateProfileExperience
        {
            ProfileId = profile.Id,
            Rank = profile.Rank,
            RankPercent = profile.RankPercent,
            StarsAvailable = 0,
            StarsEarned = profile.StarsEarned,
            Unknown6 = 0
        };
    }

    internal static ClientUpdatePacketUpdateProfileRank CreateRankPacket(ClientPcProfile profile)
    {
        return new ClientUpdatePacketUpdateProfileRank
        {
            ProfileId = profile.Id,
            Rank = profile.Rank
        };
    }

    private static RewardBundlePacket CreateRewardBundlePacket(Player player, ulong sourceGuid, int coins = 0,
        int experience = 0, int iconId = 0, int nameId = 0, int descriptionId = 0)
    {
        var notificationId = Environment.TickCount & int.MaxValue;

        var packet = new RewardBundlePacket();
        packet.RewardBundle.DescriptionId = descriptionId;
        packet.RewardBundle.SourceGuid = sourceGuid ^ (uint)notificationId;
        packet.RewardBundle.PlayerGuid = player.Guid;
        packet.RewardBundle.IconId = iconId;
        packet.RewardBundle.NameId = nameId;
        packet.RewardBundle.Coins = coins;
        packet.RewardBundle.Experience = experience;

        return packet;
    }
}
