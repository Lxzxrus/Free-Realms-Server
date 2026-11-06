using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Numerics;
using System.Threading;
using System.Threading.Tasks;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

using Sanctuary.Database;
using Sanctuary.Database.Entities;
using Sanctuary.Game.Entities;
using Sanctuary.Game.Housing;
using Sanctuary.Game.Resources.Definitions.Zones;
using Sanctuary.Game.Zones;

// A house, and the account that owns it (HouseOwnership.FindHeld).
using HeldHouse = (ulong HouseId, ulong UserId);

namespace Sanctuary.Game;

public class ZoneManager : IZoneManager
{
    private readonly ILogger _logger;
    private readonly IResourceManager _resourceManager;
    private readonly IServiceProvider _serviceProvider;
    private readonly IDbContextFactory<DatabaseContext> _dbContextFactory;
    private static int _uniqueId = 1;

    private int NextID() => Interlocked.Increment(ref _uniqueId) - 1;

    private readonly ConcurrentDictionary<(int, ulong?), IZone> _zones = new();

    public int StartingZoneDefinitionId => 1;

    public WorldZone StartingZone { get; private set; } = null!;

    public IEnumerable<IZone> Zones => _zones.Values;

    private readonly object _playerTransitionLock = new();

    /// <summary>
    /// How long a house stays loaded after its last player leaves. A relog, or a quick trip out and back, finds the
    /// house still there instead of loading it from the database again.
    /// </summary>
    public static readonly TimeSpan EmptyHouseGrace = TimeSpan.FromMinutes(1);

    private readonly EmptyZoneGrace _emptyZoneGrace = new();

    public ZoneManager(
        ILoggerFactory loggerFactory,
        IResourceManager resourceManager,
        IServiceProvider serviceProvider,
        IDbContextFactory<DatabaseContext> dbContextFactory)
    {
        _logger = loggerFactory.CreateLogger<ZoneManager>();

        _resourceManager = resourceManager;
        _serviceProvider = serviceProvider;
        _dbContextFactory = dbContextFactory;
    }

    public bool Load()
    {
        if (!TryCreateStartingZone(StartingZoneDefinitionId, out var startingZone))
            return false;

        StartingZone = startingZone;

        return true;
    }

    public bool TryGetPlayer(ulong guid, [MaybeNullWhen(false)] out Player player)
    {
        player = default;

        foreach (var zone in _zones)
        {
            if (zone.Value.TryGetPlayer(guid, out player))
                return true;
        }

        return false;
    }

    public bool TryGetPlayer(string name, [MaybeNullWhen(false)] out Player player)
    {
        player = default;

        foreach (var zone in _zones)
        {
            foreach (var zonePlayer in zone.Value.Players)
            {
                if (zonePlayer.Name.FullName == name)
                {
                    player = zonePlayer;
                    return true;
                }
            }
        }

        return false;
    }

    private bool TryCreateStartingZone(int definitionId, [MaybeNullWhen(false)] out WorldZone zone)
    {
        zone = default;

        if (!_resourceManager.Zones.TryGetValue(definitionId, out var zoneDefinition))
            return false;

        if (zoneDefinition is not WorldZoneDefinition worldZoneDefinition)
            return false;

        zone = new WorldZone(worldZoneDefinition, _serviceProvider)
        {
            Id = NextID()
        };

        if (!_zones.TryAdd((zone.DefinitionId, null), zone))
            return false;

        zone.OnStart();

        return true;
    }

    public bool TryMovePlayerToZone(int zoneDefinitionId, ulong? ownerId, Player player, Vector4 position, Quaternion rotation, out IZone zone)
    {
        var oldZone = player.Zone;
        bool moved;

        lock (_playerTransitionLock)
        {
            if (!TryGetOrCreateZoneInstance(zoneDefinitionId, ownerId, out var newZone))
            {
                zone = player.Zone;
                return false;
            }

            zone = newZone;

            oldZone.TryRemovePlayer(player.Guid);
            moved = newZone.TryAddPlayer(player);

            if (moved)
                player.OnZoneChanged(newZone, position, rotation);
            else
                oldZone.TryAddPlayer(player);
        }

        EvictIfEmpty(oldZone);

        return moved;
    }


    private bool TryGetOrCreateZoneInstance(int zoneDefinitionId, ulong? ownerId, [MaybeNullWhen(false)] out IZone zone)
    {
        if (!_resourceManager.Zones.TryGetValue(zoneDefinitionId, out var zoneDefinition))
        {
            zone = null;
            return false;
        }

        if (!PlayerCanAccessZone(zoneDefinition, ownerId))
        {
            zone = null;
            return false;
        }

        // Evergrove: a house is looked up once, here. It must exist, it keys its instance (InstanceKey) and it's what
        // the instance is made for.
        HeldHouse? house = null;

        if (zoneDefinition is HousingZoneDefinition && !TryGetHouse(ownerId!.Value, zoneDefinitionId, out house))
        {
            zone = null;
            return false;
        }

        var key = InstanceKey(zoneDefinitionId, ownerId, house?.HouseId);

        zone = _zones.GetOrAdd(key, _ => CreateZoneInstance(zoneDefinition, ownerId, house));

        try
        {
            zone.OnStart();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to start zone '{name}' ({id})", zone.Name, zone.Id);

            _zones.TryRemove(new(key, zone));
            zone.Dispose();

            zone = null;
            return false;
        }

        return true;
    }

    private IZone CreateZoneInstance(BaseZoneDefinition zoneDefinition, ulong? ownerId, HeldHouse? house) => zoneDefinition switch
    {
        WorldZoneDefinition worldZoneDefinition => new WorldZone(worldZoneDefinition, _serviceProvider)
        {
            Id = NextID(),
            OwnerId = ownerId // Note: should always be 'null'.
        },
        HousingZoneDefinition housingZoneDefinition when house is { } held =>
            new HousingZone(housingZoneDefinition, held.HouseId, held.UserId, _serviceProvider)
        {
            Id = NextID(),
            OwnerId = ownerId
        },
        CombatZoneDefinition combatZoneDefinition => new CombatZone(combatZoneDefinition, _serviceProvider)
        {
            Id = NextID(),
            OwnerId = ownerId
        },
        FishingZoneDefinition fishingZoneDefinition => new FishingZone(fishingZoneDefinition, _serviceProvider)
        {
            Id = NextID(),
            OwnerId = ownerId // Always 'null': everyone fishing a hole shares it, and sees each other fish.
        },
        _ => throw new InvalidOperationException($"Unhandled zone definition type: {zoneDefinition.GetType()}")
    };

    public bool TryRemoveZoneInstance(IZone zone)
    {
        return _zones.TryRemove(new(InstanceKey(zone), zone));
    }

    /// <summary>
    /// The key a zone instance is kept under: its definition and its owner. Evergrove: a house's is its lot and the
    /// house itself (<paramref name="houseId"/>), not the character holding it, which can change while the house is
    /// loaded: a deleted character's houses move to another character on the account (<see cref="HouseHandover"/>). So
    /// whoever comes in under the new holder joins the instance already loaded, rather than loading a second copy of
    /// the house beside it. Finding an instance and removing it both use this, so the two can't disagree.
    /// </summary>
    /// <param name="houseId">The house, for a housing zone; <c>null</c> for any other zone.</param>
    internal static (int, ulong?) InstanceKey(int definitionId, ulong? ownerId, ulong? houseId) =>
        houseId is ulong house ? (definitionId, house) : (definitionId, ownerId);

    private static (int, ulong?) InstanceKey(IZone zone) =>
        InstanceKey(zone.DefinitionId, zone.OwnerId, (zone as HousingZone)?.HouseId);

    public void EvictIfEmpty(IZone zone)
    {
        var isStartingZone = zone.DefinitionId == StartingZoneDefinitionId && zone.OwnerId is null;

        if (isStartingZone)
            return;

        if (zone is HousingZone)
        {
            if (!zone.IsEmpty)
                return;

            // Close it once the grace period is over, if nobody has come in (or left again since) by then.
            var ticket = _emptyZoneGrace.Begin(zone);
            _ = Task.Delay(EmptyHouseGrace).ContinueWith(_ =>
            {
                if (_emptyZoneGrace.End(zone, ticket))
                    EvictNowIfEmpty(zone);
            }, TaskScheduler.Default);

            return;
        }

        EvictNowIfEmpty(zone);
    }

    private void EvictNowIfEmpty(IZone zone)
    {
        bool removed;

        lock (_playerTransitionLock)
        {
            // NOTE: This may look a bit awkward, but recall that the first statement
            // will prevent the second statement from being run if it is 'false'!
            removed = zone.IsEmpty && TryRemoveZoneInstance(zone);
        }

        if (!removed)
            return;

        try
        {
            zone.Dispose();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to close zone '{name}' ({id})", zone.Name, zone.Id);
        }
    }


    private bool PlayerCanAccessZone(BaseZoneDefinition definition, ulong? ownerId) => definition switch
    {
        // NOTE: One day, we may need to worry about code that tries to allow a player into a zone
        // they're not aloud to be inside of (e.g., a private(?) house. Not sure if that existed).
        // The assumption is that would be taken care of elsewhere (i.e., a player wouldn't even
        // be able to see an option to get to a zone they shouldn't be able to get into).
        //
        // This is why we don't pass a player instance here directly.
        WorldZoneDefinition => true,
        CombatZoneDefinition => ownerId is not null,
        // Evergrove: and the house must exist, which TryGetOrCreateZoneInstance looks up.
        HousingZoneDefinition => ownerId is not null,
        FishingZoneDefinition => ownerId is null,
        _ => false
    };

    /// <param name="ownerId">The character holding the house.</param>
    /// <param name="house">The house, and the account that owns it.</param>
    private bool TryGetHouse(ulong ownerId, int zoneDefinitionId, [NotNullWhen(true)] out HeldHouse? house)
    {
        using var dbContext = _dbContextFactory.CreateDbContext();

        house = HouseOwnership.FindHeld(dbContext, ownerId, zoneDefinitionId);

        return house is not null;
    }
}