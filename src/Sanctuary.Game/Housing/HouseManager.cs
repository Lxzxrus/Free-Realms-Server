using System;
using System.Collections.Generic;
using System.Linq;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

using Sanctuary.Core.Helpers;
using Sanctuary.Database;
using Sanctuary.Database.Entities;
using Sanctuary.Game.Entities;
using Sanctuary.Game.Helpers;
using Sanctuary.Game.Resources.Definitions.Zones;
using Sanctuary.Game.Zones;
using Sanctuary.Packet;

namespace Sanctuary.Game.Housing;

public sealed class HouseManager : IHouseManager
{
    private readonly IResourceManager _resourceManager;
    private readonly IZoneManager _zoneManager;
    private readonly IDbContextFactory<DatabaseContext> _dbContextFactory;
    private readonly HouseInvites _invites;
    private readonly ILogger<HouseManager> _logger;

    public HouseManager(
        IResourceManager resourceManager,
        IZoneManager zoneManager,
        IDbContextFactory<DatabaseContext> dbContextFactory,
        HouseInvites invites,
        ILogger<HouseManager> logger)
    {
        _resourceManager = resourceManager;
        _zoneManager = zoneManager;
        _dbContextFactory = dbContextFactory;
        _invites = invites;
        _logger = logger;
    }

    public IReadOnlyList<DbHouse> GetOwnedHouses(ulong characterId)
    {
        using var dbContext = _dbContextFactory.CreateDbContext();

        return HouseOwnership.OwnedByAccountOf(dbContext, characterId)
            .AsNoTracking()
            .Include(house => house.Character)
            .ToList()
            // SQLite can't sort by ulong in SQL, so sort after loading.
            .OrderBy(house => house.Id)
            .Where(house => IsAvailableHouse(house.ZoneDefinitionId))
            .ToList();
    }

    public EnterHouseResult EnterOwnedHouse(Player player, int zoneDefinitionId)
    {
        var characterId = GuidHelper.GetPlayerId(player.Guid);

        using var dbContext = _dbContextFactory.CreateDbContext();

        // Before houses were shared, two characters on an account could each buy the same lot. Both are kept, and the
        // player's own comes first.
        var house = HouseOwnership.OwnedByAccountOf(dbContext, characterId)
            .AsNoTracking()
            .Where(entry => entry.ZoneDefinitionId == zoneDefinitionId)
            .ToList()
            // SQLite can't sort by ulong in SQL, so sort after loading.
            .OrderByDescending(entry => entry.CharacterId == characterId)
            .ThenBy(entry => entry.Id)
            .FirstOrDefault();

        return EnterHouse(player, house);
    }

    public EnterHouseResult EnterHouse(Player player, ulong houseGuid)
    {
        ulong houseId;

        try
        {
            houseId = GuidHelper.GetHouseId(houseGuid);
        }
        catch (ArgumentException)
        {
            return EnterHouseResult.HouseNotFound;
        }

        using var dbContext = _dbContextFactory.CreateDbContext();

        var house = dbContext.Houses
            .AsNoTracking()
            .SingleOrDefault(entry => entry.Id == houseId);

        return EnterHouse(player, house);
    }

    public EnterHouseResult VisitHouse(Player player, int zoneDefinitionId, string ownerName)
    {
        using var dbContext = _dbContextFactory.CreateDbContext();

        var ownerId = dbContext.Characters
            .Where(character => character.FullName == ownerName)
            .Select(character => (ulong?)character.Id)
            .FirstOrDefault();

        if (ownerId is null)
            return EnterHouseResult.HouseNotFound;

        var house = dbContext.Houses
            .AsNoTracking()
            .SingleOrDefault(entry =>
                entry.CharacterId == ownerId.Value &&
                entry.ZoneDefinitionId == zoneDefinitionId);

        return EnterHouse(player, house);
    }

    public bool LeaveHouse(Player player)
    {
        if (player.Zone is not HousingZone)
            return false;

        var position = player.StartingZonePosition == default
            ? _zoneManager.StartingZone.SpawnPosition
            : player.StartingZonePosition;
        var rotation = player.StartingZoneRotation == default
            ? _zoneManager.StartingZone.SpawnRotation
            : player.StartingZoneRotation;

        return player.TeleportToZone(
            _zoneManager.StartingZone,
            position,
            rotation);
    }

    public int SendOutLockedOutVisitors(HousingZone zone)
    {
        if (zone.OwnerId is not ulong ownerId)
            return 0;

        // A snapshot: leaving the house takes each visitor out of the zone's player list. Invited guests stay.
        var visitors = zone.Players
            .Where(player =>
                !HouseAccess.MayBeInLockedHouse(player.UserId, player.Friends, zone.OwnerUserId, ownerId) &&
                !zone.IsGuest(player))
            .ToList();

        foreach (var visitor in visitors)
        {
            ChatHelper.SendSystemMessage(visitor, "The owner locked this house, so you've been sent back outside.");

            if (!LeaveHouse(visitor))
                _logger.LogWarning("Failed to send {Player} out of locked house {HouseId}.", visitor.Name.FullName, zone.HouseId);
        }

        return visitors.Count;
    }

    public HouseInviteResult InviteToHouse(Player inviter, ulong inviteeGuid, out Player? invitee)
    {
        invitee = null;

        if (inviter.Zone is not HousingZone zone || zone.OwnerId is not ulong ownerId)
            return HouseInviteResult.NotInHouse;

        if (!zone.IsOwner(inviter))
            return HouseInviteResult.NotOwner;

        if (!_zoneManager.TryGetPlayer(inviteeGuid, out invitee))
            return HouseInviteResult.NotOnline;

        if (zone.IsOwner(invitee))
            return HouseInviteResult.NoInviteNeeded;

        if (ReferenceEquals(invitee.Zone, zone))
            return HouseInviteResult.AlreadyThere;

        if (invitee.Ignores.Any(ignored => ignored.Guid == inviter.Guid))
            return HouseInviteResult.Ignored;

        _invites.Add(GuidHelper.GetPlayerId(invitee.Guid), zone.HouseId, GuidHelper.GetPlayerId(inviter.Guid), DateTimeOffset.UtcNow);

        invitee.SendTunneled(new HousingPacketInviteNotification
        {
            InviterGuid = inviter.Guid,
            InviteeGuid = invitee.Guid,
            HouseGuid = GuidHelper.GetHouseGuid(zone.HouseId),
            OwnerGuid = GuidHelper.GetPlayerGuid(ownerId),
            InviterName = inviter.Name,
            InviteeName = invitee.Name
        });

        _logger.LogInformation("{Inviter} invited {Invitee} into house {HouseId}.", inviter.Name.FullName, invitee.Name.FullName, zone.HouseId);
        return HouseInviteResult.Sent;
    }

    public bool DeclineInvite(Player invitee, ulong houseGuid)
    {
        ulong houseId;

        try
        {
            houseId = GuidHelper.GetHouseId(houseGuid);
        }
        catch (ArgumentException)
        {
            return false;
        }

        if (!_invites.TryRemove(GuidHelper.GetPlayerId(invitee.Guid), houseId, DateTimeOffset.UtcNow, out var inviterId))
            return false;

        if (_zoneManager.TryGetPlayer(GuidHelper.GetPlayerGuid(inviterId), out var inviter))
        {
            inviter.SendTunneled(new HousingPacketDeclineNotification
            {
                PlayerGuid = invitee.Guid,
                HouseGuid = houseGuid,
                Name = invitee.Name
            });
        }

        return true;
    }

    private EnterHouseResult EnterHouse(Player player, DbHouse? house)
    {
        if (house is null || !IsAvailableHouse(house.ZoneDefinitionId))
            return EnterHouseResult.HouseNotFound;

        if (player.Zone is not WorldZone and not HousingZone)
            return EnterHouseResult.UnsupportedSourceZone;

        if (!HouseAccess.MayEnter(player.UserId, player.Friends, player.MembershipStatus != 0, house, FindOwnerUserId(house)) &&
            !_invites.IsInvited(GuidHelper.GetPlayerId(player.Guid), house.Id, DateTimeOffset.UtcNow))
        {
            return EnterHouseResult.NotAuthorized;
        }

        if (!_resourceManager.Zones.TryGetValue(house.ZoneDefinitionId, out var definition) ||
            definition is not HousingZoneDefinition zoneDefinition)
        {
            return EnterHouseResult.ZoneUnavailable;
        }

        // The zone manager creates the house instance, if needed, while it moves the player in.
        if (!player.TeleportToZone(
                house.ZoneDefinitionId,
                house.CharacterId,
                zoneDefinition.GetArrivalPosition(),
                zoneDefinition.SpawnRotation))
        {
            return EnterHouseResult.TransferFailed;
        }

        UpdateLastVisited(house.Id);
        return EnterHouseResult.Success;
    }

    /// <summary>The account that owns the house: that of the character who bought it.</summary>
    private ulong FindOwnerUserId(DbHouse house)
    {
        using var dbContext = _dbContextFactory.CreateDbContext();

        return HousePlots.FindUserId(dbContext, house.CharacterId) ?? 0;
    }

    private void UpdateLastVisited(ulong houseId)
    {
        try
        {
            // A captured value, not DateTimeOffset.UtcNow inside the expression: SQLite can't translate that.
            var now = DateTimeOffset.UtcNow;

            using var dbContext = _dbContextFactory.CreateDbContext();
            dbContext.Houses
                .Where(house => house.Id == houseId)
                .ExecuteUpdate(setters => setters.SetProperty(house => house.LastVisited, now));
        }
        catch (Exception exception)
        {
            _logger.LogWarning(exception, "Failed to update the last visit for house {HouseId}.", houseId);
        }
    }

    private bool IsAvailableHouse(int zoneDefinitionId)
    {
        return _resourceManager.Zones.TryGetValue(zoneDefinitionId, out var definition) &&
            definition is HousingZoneDefinition;
    }
}
