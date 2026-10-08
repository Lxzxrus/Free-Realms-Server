using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Numerics;
using System.Text.Json;
using System.Threading;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

using Sanctuary.Core.Helpers;
using Sanctuary.Core.IO;
using Sanctuary.Database;
using Sanctuary.Database.Entities;
using Sanctuary.Game.Entities;
using Sanctuary.Game.Zones;
using Sanctuary.Packet;
using Sanctuary.Packet.Common;

namespace Sanctuary.Game.Housing;

public sealed class HousingZoneRuntime : IDisposable
{
    private sealed record HouseSurfaceCustomization(
        string FixtureGroup,
        string FixtureType,
        int ItemDefinitionId,
        int TintId);

    private readonly record struct PendingPlacement(
        ulong FixtureGuid,
        Guid PlacementToken,
        int ItemDefinitionId,
        int ItemRecordId,
        int TintId,
        bool HoverActive,
        bool IsCreative)
    {
        public HousingFixtureLedger.Source Source => new(ItemDefinitionId, TintId, ItemRecordId, IsCreative);
    }

    private readonly HousingZone _zone;
    private readonly IResourceManager _resourceManager;
    private readonly IDbContextFactory<DatabaseContext> _dbContextFactory;
    private readonly HousingOptions _options;
    private readonly HousingCreativeCatalog _creativeCatalog;
    private readonly ILogger _logger;
    private readonly object _mutationLock = new();
    private readonly Dictionary<int, Npc> _actors = [];
    private readonly Dictionary<ulong, PendingPlacement> _pendingPlacements = [];
    private readonly HashSet<ulong> _editors = [];
    private readonly HashSet<ulong> _creativeInventoryShown = [];
    private bool _disposed;

    private static long _nextPreviewId = 10_000_000_000;

    public HousingZoneRuntime(HousingZone zone, IServiceProvider serviceProvider)
    {
        _zone = zone;
        _resourceManager = serviceProvider.GetRequiredService<IResourceManager>();
        _dbContextFactory = serviceProvider.GetRequiredService<IDbContextFactory<DatabaseContext>>();
        _options = serviceProvider.GetRequiredService<IOptions<HousingOptions>>().Value;
        _creativeCatalog = serviceProvider.GetRequiredService<HousingCreativeCatalog>();
        _logger = serviceProvider.GetRequiredService<ILoggerFactory>()
            .CreateLogger($"Housing {zone.HouseId}");
    }

    public void Initialize()
    {
        lock (_mutationLock)
        {
            ThrowIfDisposed();

            using var dbContext = _dbContextFactory.CreateDbContext();
            var fixtures = dbContext.HouseFixtures
                .AsNoTracking()
                .Where(fixture => fixture.HouseId == _zone.HouseId)
                .OrderBy(fixture => fixture.Id)
                .ToList();

            foreach (var fixture in fixtures)
                EnsureActor(fixture);
        }
    }

    public void SendInitialData(Player player)
    {
        lock (_mutationLock)
        {
            if (_disposed || !ReferenceEquals(player.Zone, _zone))
                return;

            using var dbContext = _dbContextFactory.CreateDbContext();
            var house = LoadHouse(dbContext);
            if (house is null)
                return;

            var ownerGuid = GuidHelper.GetPlayerGuid(house.CharacterId);
            var ownerName = ResolveOwnerName(house);
            var instanceInfo = BuildInstanceInfo(dbContext, house, player);

            player.SendTunneled(new HousingPacketInstanceList
            {
                PlayerGuid = ownerGuid,
                Instances = [instanceInfo]
            });

            player.SendTunneled(new HousingPacketZoneData
            {
                CanEdit = IsOwner(player),
                HeadSize = 10,
                InstanceInfo = instanceInfo
            });

            player.SendTunneled(new HousingPacketInstanceData
            {
                InstanceData = BuildInstanceData(house, ownerGuid, ownerName)
            });

            if (IsOwner(player))
                SendFixtureItemList(player, house);
            else
                player.SendTunneled(new HousingPacketFixtureItemList());

            SendPersistedFixtureAssets(player, house);
            SendHouseCustomizations(player, house);
            SendHouseInfo(player, house);
        }
    }

    public void OnClientFinishedLoading(Player player)
    {
        player.UpdateCharacterStats(new CharacterStat(CharacterStatId.HeadInflationPercent, 100));

        lock (_mutationLock)
        {
            if (_disposed || !ReferenceEquals(player.Zone, _zone))
                return;

            SendPersistedFixtureTransforms(player);
        }
    }

    public void OnPlayerRemoved(Player player)
    {
        lock (_mutationLock)
        {
            _editors.Remove(player.Guid);
            _pendingPlacements.Remove(player.Guid);
            HideCreativeInventory(player);
        }
    }

    public void Dispose()
    {
        lock (_mutationLock)
        {
            if (_disposed)
                return;

            _disposed = true;
            _editors.Clear();
            _creativeInventoryShown.Clear();
            _pendingPlacements.Clear();
            _actors.Clear();
        }
    }

    public void SetEditMode(Player player, bool inEditMode)
    {
        lock (_mutationLock)
        {
            if (_disposed || !IsOwner(player))
            {
                player.SendTunneled(new HousingPacketUpdateHouseInfo());
                return;
            }

            if (inEditMode)
            {
                _editors.Add(player.Guid);
                ShowCreativeInventory(player);
            }
            else
            {
                _editors.Remove(player.Guid);
                CancelPendingPlacement(player);
                HideCreativeInventory(player);
            }

            using var dbContext = _dbContextFactory.CreateDbContext();
            var house = LoadHouse(dbContext);
            if (house is null)
                return;

            SendHouseInfo(player, house);

            if (inEditMode)
            {
                SendPersistedFixtureUpdates(player, house);
                SendFixtureItemList(player, house);
            }
        }
    }

    public void RequestGrant(Player player)
    {
        lock (_mutationLock)
        {
            if (_disposed || !IsOwner(player))
                return;

            _editors.Add(player.Guid);
            ShowCreativeInventory(player);

            using var dbContext = _dbContextFactory.CreateDbContext();
            var house = LoadHouse(dbContext);
            if (house is null)
                return;

            var ownerGuid = GuidHelper.GetPlayerGuid(house.CharacterId);
            player.SendTunneled(new HousingPacketInstanceData
            {
                InstanceData = BuildInstanceData(house, ownerGuid, ResolveOwnerName(house))
            });
            SendFixtureItemList(player, house);
            SendPersistedFixtureAssets(player, house);
            SendPersistedFixtureTransforms(player);
            SendHouseCustomizations(player, house);
            SendHouseInfo(player, house);
            SendPersistedFixtureUpdates(player, house);
        }
    }

    public void ToggleSetting(Player player, HouseSetting setting)
    {
        lock (_mutationLock)
        {
            if (_disposed || !ReferenceEquals(player.Zone, _zone))
                return;

            using var dbContext = _dbContextFactory.CreateDbContext();

            // Only the owner may change the house. Anyone else gets the real state back,
            // so a checkbox they flipped in their panel flips back.
            if (!IsOwner(player) || _zone.OwnerId is not ulong ownerId)
            {
                var unchangedHouse = LoadHouse(dbContext);
                if (unchangedHouse is not null)
                    SendHouseInfo(player, unchangedHouse);

                return;
            }

            var value = HouseSettings.Toggle(dbContext, _zone.HouseId, ownerId, setting);
            if (value is null)
                return;

            _logger.LogInformation("{Player} set {Setting} to {Value}.", player.Name.FullName, setting, value);

            dbContext.ChangeTracker.Clear();
            var refreshedHouse = LoadHouse(dbContext);
            if (refreshedHouse is not null)
                BroadcastHouseInfo(refreshedHouse);
        }
    }

    public void BeginPlacement(Player player, int itemRecordId)
    {
        lock (_mutationLock)
        {
            if (!CanEdit(player))
                return;

            // In creative mode the editor lists only the catalog, so only a catalog entry can be placed.
            if (_options.CreativeMode)
            {
                if (_creativeCatalog.TryGetByRecordId(itemRecordId, out var entry))
                    StartPendingPlacement(player, CreativeSource(entry));

                return;
            }

            using var dbContext = _dbContextFactory.CreateDbContext();
            var characterId = GuidHelper.GetPlayerId(player.Guid);
            var item = dbContext.Items.AsNoTracking().FirstOrDefault(candidate =>
                candidate.CharacterId == characterId &&
                candidate.Id == itemRecordId &&
                candidate.Count > 0);

            if (item is null || !IsFixtureInventoryItem(item.Definition))
                return;

            StartPendingPlacement(player, InventorySource(item));
        }
    }

    public void PlaceFixtureRequest(
        Player player,
        int itemDefinitionId,
        Vector4 position,
        Quaternion rotation,
        float scale)
    {
        lock (_mutationLock)
        {
            if (!CanEdit(player) || !TryNormalizeTransform(position, rotation, scale, out position, out rotation, out scale))
                return;

            if (_pendingPlacements.TryGetValue(player.Guid, out var pending) &&
                pending.ItemDefinitionId == itemDefinitionId)
            {
                _pendingPlacements.Remove(player.Guid);
                CommitPlacement(player, pending, position, rotation, scale, true);
                return;
            }

            if (_options.CreativeMode)
            {
                if (_creativeCatalog.TryGetByRecordOrDefinitionId(itemDefinitionId, out var entry))
                {
                    var source = CreativeSource(entry);
                    var directCreative = new PendingPlacement(
                        0,
                        Guid.NewGuid(),
                        source.ItemDefinitionId,
                        source.ItemRecordId,
                        source.TintId,
                        true,
                        true);
                    CommitPlacement(player, directCreative, position, rotation, scale, false);
                }

                return;
            }

            using var dbContext = _dbContextFactory.CreateDbContext();
            var characterId = GuidHelper.GetPlayerId(player.Guid);
            var item = dbContext.Items
                .AsNoTracking()
                .Where(candidate =>
                    candidate.CharacterId == characterId &&
                    candidate.Definition == itemDefinitionId &&
                    candidate.Count > 0)
                .OrderBy(candidate => candidate.Id)
                .FirstOrDefault();

            if (item is null || !IsFixtureInventoryItem(item.Definition))
                return;

            var direct = new PendingPlacement(
                0,
                Guid.NewGuid(),
                item.Definition,
                item.Id,
                ResolveItemTintId(item.Definition, item.Tint),
                true,
                false);
            CommitPlacement(player, direct, position, rotation, scale, false);
        }
    }

    public void PlaceFixture(
        Player player,
        int itemDefinitionId,
        Vector4 position,
        Quaternion rotation,
        float scale)
    {
        lock (_mutationLock)
        {
            if (!CanEdit(player) ||
                !_pendingPlacements.TryGetValue(player.Guid, out var pending) ||
                pending.ItemDefinitionId != itemDefinitionId)
            {
                return;
            }

            var isEmptyPosition = IsEmptyPosition(position);
            if (!TryNormalizeTransform(
                    isEmptyPosition ? ResolvePreviewPosition(player, rotation) : position,
                    rotation,
                    scale,
                    out var normalizedPosition,
                    out var normalizedRotation,
                    out var normalizedScale,
                    false))
            {
                return;
            }

            if (!pending.HoverActive)
            {
                pending = pending with { HoverActive = true };
                _pendingPlacements[player.Guid] = pending;
                SendFixtureUpdate(
                    player,
                    pending.FixtureGuid,
                    0,
                    pending.ItemDefinitionId,
                    pending.ItemRecordId,
                    pending.TintId,
                    normalizedPosition,
                    normalizedRotation,
                    normalizedScale,
                    true);
                return;
            }

            if (isEmptyPosition)
                return;

            _pendingPlacements.Remove(player.Guid);
            CommitPlacement(
                player,
                pending,
                normalizedPosition,
                normalizedRotation,
                normalizedScale,
                true);
        }
    }

    public void PickupFixture(Player player, ulong fixtureGuid)
    {
        lock (_mutationLock)
        {
            if (!CanEdit(player))
                return;

            if (_pendingPlacements.TryGetValue(player.Guid, out var pending) &&
                pending.FixtureGuid == fixtureGuid)
            {
                CancelPendingPlacement(player);
                return;
            }

            if (!TryGetFixtureId(fixtureGuid, out var fixtureId))
                return;

            using var dbContext = _dbContextFactory.CreateDbContext();
            var characterId = GuidHelper.GetPlayerId(player.Guid);
            if (!HousingFixtureLedger.TryPickup(dbContext, _zone.HouseId, characterId, fixtureId, out var inventoryItem))
                return;

            if (inventoryItem is not null)
                UpdateReturnedInventoryItem(player, inventoryItem);

            RemoveActor(fixtureId);
            Broadcast(new HousingPacketRemoveFixture
            {
                FixtureGuid = GuidHelper.GetFixtureGuid((ulong)fixtureId)
            });

            dbContext.ChangeTracker.Clear();
            var refreshedHouse = LoadHouse(dbContext);
            if (refreshedHouse is not null)
            {
                SendFixtureItemListUnlessCreative(player, refreshedHouse);
                BroadcastHouseInfo(refreshedHouse);
            }
        }
    }

    public void PickupAllFixtures(Player player)
    {
        lock (_mutationLock)
        {
            if (!CanEdit(player))
                return;

            CancelPendingPlacement(player);

            using var dbContext = _dbContextFactory.CreateDbContext();
            var characterId = GuidHelper.GetPlayerId(player.Guid);
            if (!HousingFixtureLedger.TryPickupAll(
                    dbContext,
                    _zone.HouseId,
                    characterId,
                    out var fixtureIds,
                    out var returnedItems))
            {
                return;
            }

            foreach (var item in returnedItems)
                UpdateReturnedInventoryItem(player, item);

            foreach (var fixtureId in fixtureIds)
            {
                RemoveActor(fixtureId);
                Broadcast(new HousingPacketRemoveFixture
                {
                    FixtureGuid = GuidHelper.GetFixtureGuid((ulong)fixtureId)
                });
            }

            dbContext.ChangeTracker.Clear();
            var refreshedHouse = LoadHouse(dbContext);
            if (refreshedHouse is not null)
            {
                SendFixtureItemListUnlessCreative(player, refreshedHouse);
                BroadcastHouseInfo(refreshedHouse);
            }
        }
    }

    public void SaveFixture(
        Player player,
        ulong fixtureGuid,
        Vector4 position,
        Quaternion rotation,
        float scale,
        CustomizationDetail customization)
    {
        lock (_mutationLock)
        {
            if (!CanEdit(player) ||
                !TryGetFixtureId(fixtureGuid, out var fixtureId) ||
                !TryNormalizeTransform(position, rotation, scale, out position, out rotation, out scale))
            {
                return;
            }

            using var dbContext = _dbContextFactory.CreateDbContext();
            var characterId = GuidHelper.GetPlayerId(player.Guid);
            var fixture = dbContext.HouseFixtures.FirstOrDefault(candidate =>
                candidate.Id == fixtureId &&
                candidate.HouseId == _zone.HouseId &&
                candidate.House.CharacterId == characterId);

            if (fixture is null)
                return;

            fixture.PositionX = position.X;
            fixture.PositionY = position.Y;
            fixture.PositionZ = position.Z;
            fixture.PositionW = position.W;
            fixture.RotationX = rotation.X;
            fixture.RotationY = rotation.Y;
            fixture.RotationZ = rotation.Z;
            fixture.RotationW = rotation.W;
            fixture.Scale = scale;

            if (customization.TintId > 0)
                fixture.TintId = ResolveItemTintId(fixture.ItemDefinitionId, customization.TintId);

            dbContext.SaveChanges();

            var actorExisted = _actors.ContainsKey(fixture.Id);
            UpdateActor(fixture);
            var excludedPlayerGuid = actorExisted ? player.Guid : 0;
            BroadcastFixtureUpdate(fixture, 0, excludedPlayerGuid);
            Broadcast(new HousingPacketUpdateFixturePosition
            {
                FixtureActorGuid = GetActorGuid(fixture.Id),
                Position = position,
                Rotation = rotation
            }, excludedPlayerGuid);
        }
    }

    public void ApplyCustomization(
        Player player,
        int itemRecordOrDefinitionId,
        string fixtureGroup,
        string fixtureType)
    {
        lock (_mutationLock)
        {
            if (!CanEdit(player))
                return;

            fixtureGroup = NormalizeSelector(fixtureGroup);
            fixtureType = NormalizeSelector(fixtureType);
            if (fixtureGroup.Length == 0 || fixtureType.Length == 0)
                return;

            using var dbContext = _dbContextFactory.CreateDbContext();
            var characterId = GuidHelper.GetPlayerId(player.Guid);

            // In creative mode a wallpaper or floor comes from the catalog and uses nothing up.
            DbItem? item = null;
            int itemDefinitionId;
            int tintId;
            if (_options.CreativeMode)
            {
                if (!_creativeCatalog.TryGetByRecordOrDefinitionId(itemRecordOrDefinitionId, out var entry))
                    return;

                itemDefinitionId = entry.ItemDefinitionId;
                tintId = entry.TintId;
            }
            else
            {
                item = dbContext.Items.FirstOrDefault(candidate =>
                    candidate.CharacterId == characterId &&
                    candidate.Id == itemRecordOrDefinitionId &&
                    candidate.Count > 0);
                item ??= dbContext.Items
                    .Where(candidate =>
                        candidate.CharacterId == characterId &&
                        candidate.Definition == itemRecordOrDefinitionId &&
                        candidate.Count > 0)
                    .OrderBy(candidate => candidate.Id)
                    .FirstOrDefault();

                if (item is null)
                    return;

                itemDefinitionId = item.Definition;
                tintId = item.Tint;
            }

            if (!_resourceManager.ClientItemDefinitions.TryGetValue(itemDefinitionId, out var definition) ||
                !HousingPlacementCatalog.IsFixtureCustomization(definition) ||
                HousingSurfaceCatalog.GetTargetModelIds(_zone.Name, fixtureType).Count == 0 ||
                HousingSurfaceCatalog.GetTextureOverride(itemDefinitionId).Length == 0)
            {
                return;
            }

            var house = dbContext.Houses.FirstOrDefault(candidate =>
                candidate.Id == _zone.HouseId &&
                candidate.CharacterId == characterId);
            if (house is null)
                return;

            var customizations = DeserializeCustomizations(house.CustomizationData);
            customizations.RemoveAll(candidate =>
                string.Equals(candidate.FixtureGroup, fixtureGroup, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(candidate.FixtureType, fixtureType, StringComparison.OrdinalIgnoreCase));
            customizations.Add(new HouseSurfaceCustomization(
                fixtureGroup,
                fixtureType,
                itemDefinitionId,
                tintId));
            house.CustomizationData = JsonSerializer.Serialize(customizations);

            var sourceItemId = item?.Id ?? 0;
            var sourceItemCount = item is null ? 0 : item.Count - 1;
            if (item is not null)
            {
                if (sourceItemCount == 0)
                    dbContext.Items.Remove(item);
                else
                    item.Count = sourceItemCount;
            }

            dbContext.SaveChanges();

            if (item is not null)
                UpdateConsumedInventoryItem(player, sourceItemId, sourceItemCount);

            foreach (var recipient in _zone.Players)
                SendHouseCustomization(recipient, fixtureGroup, fixtureType, itemDefinitionId, tintId);

            dbContext.ChangeTracker.Clear();
            var refreshedHouse = LoadHouse(dbContext);
            if (refreshedHouse is not null)
                SendFixtureItemListUnlessCreative(player, refreshedHouse);
        }
    }

    public void RemoveCustomization(Player player, string fixtureGroup, string fixtureType)
    {
        lock (_mutationLock)
        {
            if (!CanEdit(player))
                return;

            fixtureGroup = NormalizeSelector(fixtureGroup);
            fixtureType = NormalizeSelector(fixtureType);
            if (fixtureGroup.Length == 0 || fixtureType.Length == 0)
                return;

            using var dbContext = _dbContextFactory.CreateDbContext();
            var characterId = GuidHelper.GetPlayerId(player.Guid);
            var house = dbContext.Houses.FirstOrDefault(candidate =>
                candidate.Id == _zone.HouseId &&
                candidate.CharacterId == characterId);
            if (house is null)
                return;

            var customizations = DeserializeCustomizations(house.CustomizationData);
            var removedCustomizations = customizations.Where(candidate =>
                string.Equals(candidate.FixtureGroup, fixtureGroup, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(candidate.FixtureType, fixtureType, StringComparison.OrdinalIgnoreCase)).ToList();
            var removed = customizations.RemoveAll(candidate =>
                string.Equals(candidate.FixtureGroup, fixtureGroup, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(candidate.FixtureType, fixtureType, StringComparison.OrdinalIgnoreCase));
            if (removed == 0)
                return;

            house.CustomizationData = customizations.Count == 0
                ? null
                : JsonSerializer.Serialize(customizations);
            dbContext.SaveChanges();

            foreach (var customization in removedCustomizations)
            {
                foreach (var recipient in _zone.Players)
                {
                    SendHouseCustomizationReset(
                        recipient,
                        customization.FixtureGroup,
                        customization.FixtureType,
                        customization.ItemDefinitionId,
                        customization.TintId);
                }
            }
        }
    }

    public void RefreshFixtureItemList(Player player)
    {
        lock (_mutationLock)
        {
            if (_disposed || !IsOwner(player) || !ReferenceEquals(player.Zone, _zone))
                return;

            using var dbContext = _dbContextFactory.CreateDbContext();
            var house = LoadHouse(dbContext);
            if (house is not null)
                SendFixtureItemListUnlessCreative(player, house);
        }
    }

    private void CommitPlacement(
        Player player,
        PendingPlacement pending,
        Vector4 position,
        Quaternion rotation,
        float scale,
        bool removePreview)
    {
        if (!IsInsideBuildArea(position))
        {
            if (removePreview)
                RemovePreview(player, pending.FixtureGuid);
            return;
        }

        DbHouseFixture? savedFixture;
        var sourceItemCount = 0;
        using var strategyContext = _dbContextFactory.CreateDbContext();
        var strategy = strategyContext.Database.CreateExecutionStrategy();
        try
        {
            strategy.ExecuteInTransaction(
                () =>
                {
                    strategyContext.ChangeTracker.Clear();
                    if (strategyContext.HouseFixtures.AsNoTracking().Any(fixture =>
                            fixture.HouseId == _zone.HouseId &&
                            fixture.PlacementToken == pending.PlacementToken))
                    {
                        return;
                    }

                    if (!pending.IsCreative && !IsFixtureInventoryItem(pending.ItemDefinitionId))
                        return;

                    HousingFixtureLedger.Place(
                        strategyContext,
                        _zone.HouseId,
                        GuidHelper.GetPlayerId(player.Guid),
                        pending.PlacementToken,
                        pending.Source,
                        position,
                        rotation,
                        scale);
                },
                () =>
                {
                    using var verificationContext = _dbContextFactory.CreateDbContext();
                    return verificationContext.HouseFixtures.AsNoTracking().Any(fixture =>
                        fixture.HouseId == _zone.HouseId &&
                        fixture.PlacementToken == pending.PlacementToken);
                },
                IsolationLevel.Serializable);

            using var resultContext = _dbContextFactory.CreateDbContext();
            savedFixture = resultContext.HouseFixtures.AsNoTracking().SingleOrDefault(fixture =>
                fixture.HouseId == _zone.HouseId &&
                fixture.PlacementToken == pending.PlacementToken);
            sourceItemCount = pending.IsCreative
                ? 0
                : resultContext.Items.AsNoTracking()
                    .Where(item =>
                        item.CharacterId == GuidHelper.GetPlayerId(player.Guid) &&
                        item.Id == pending.ItemRecordId)
                    .Select(item => (int?)item.Count)
                    .SingleOrDefault() ?? 0;
        }
        catch (Exception exception) when (exception is DbUpdateException or RetryLimitExceededException)
        {
            _logger.LogWarning(exception, "Unable to place fixture {ItemDefinitionId} in house {HouseId}.", pending.ItemDefinitionId, _zone.HouseId);
            savedFixture = null;
        }

        if (savedFixture is null)
        {
            if (removePreview)
                RemovePreview(player, pending.FixtureGuid);
            return;
        }

        if (removePreview)
            RemovePreview(player, pending.FixtureGuid);

        if (!pending.IsCreative)
            UpdateConsumedInventoryItem(player, pending.ItemRecordId, sourceItemCount);

        EnsureActor(savedFixture);
        BroadcastFixtureUpdate(savedFixture, 0);
        var actorGuid = GetActorGuid(savedFixture.Id);
        if (actorGuid != 0)
        {
            Broadcast(new HousingPacketUpdateFixturePosition
            {
                FixtureActorGuid = actorGuid,
                Position = GetPosition(savedFixture),
                Rotation = GetHousingRotation(savedFixture)
            });
        }

        using var refreshContext = _dbContextFactory.CreateDbContext();
        var refreshedHouse = LoadHouse(refreshContext);
        if (refreshedHouse is null)
            return;

        // A creative entry is a stack that never runs out: the next piece is ready to place, as it is after placing
        // one from a stack of several.
        if (pending.IsCreative)
        {
            BroadcastHouseInfo(refreshedHouse);

            if (refreshedHouse.Fixtures.Count < refreshedHouse.MaxFixtureCount)
                StartPendingPlacement(player, pending.Source);

            return;
        }

        if (sourceItemCount == 0)
            SendFixtureItemList(player, refreshedHouse);
        BroadcastHouseInfo(refreshedHouse);

        if (sourceItemCount > 0 && refreshedHouse.Fixtures.Count < refreshedHouse.MaxFixtureCount)
        {
            var sourceItem = refreshContext.Items.AsNoTracking().FirstOrDefault(candidate =>
                candidate.CharacterId == GuidHelper.GetPlayerId(player.Guid) &&
                candidate.Id == pending.ItemRecordId &&
                candidate.Count > 0);
            if (sourceItem is not null)
                StartPendingPlacement(player, InventorySource(sourceItem));
        }
    }

    private void StartPendingPlacement(Player player, HousingFixtureLedger.Source source)
    {
        CancelPendingPlacement(player);

        var fixtureGuid = GuidHelper.GetFixtureGuid(
            unchecked((ulong)Interlocked.Increment(ref _nextPreviewId)));
        var pending = new PendingPlacement(
            fixtureGuid,
            Guid.NewGuid(),
            source.ItemDefinitionId,
            source.ItemRecordId,
            source.TintId,
            false,
            source.IsCreative);
        _pendingPlacements[player.Guid] = pending;

        SendFixtureAsset(player, source.ItemDefinitionId, pending.TintId, true);
    }

    private HousingFixtureLedger.Source InventorySource(DbItem item)
    {
        return new HousingFixtureLedger.Source(
            item.Definition,
            ResolveItemTintId(item.Definition, item.Tint),
            item.Id,
            false);
    }

    private static HousingFixtureLedger.Source CreativeSource(HousingCreativeCatalog.Entry entry)
    {
        return new HousingFixtureLedger.Source(entry.ItemDefinitionId, entry.TintId, entry.RecordId, true);
    }

    private void CancelPendingPlacement(Player player)
    {
        if (!_pendingPlacements.Remove(player.Guid, out var pending))
            return;

        RemovePreview(player, pending.FixtureGuid);
    }

    private static void RemovePreview(Player player, ulong fixtureGuid)
    {
        if (fixtureGuid == 0)
            return;

        player.SendTunneled(new HousingPacketRemoveFixture
        {
            FixtureGuid = fixtureGuid
        });
    }

    private DbHouse? LoadHouse(DatabaseContext dbContext)
    {
        return dbContext.Houses
            .AsNoTracking()
            .Include(house => house.Character)
            .Include(house => house.Fixtures)
            .FirstOrDefault(house =>
                house.Id == _zone.HouseId &&
                house.CharacterId == _zone.OwnerId &&
                house.ZoneDefinitionId == _zone.DefinitionId);
    }

    private PlayerHousingInstanceInfo BuildInstanceInfo(
        DatabaseContext dbContext,
        DbHouse house,
        Player viewer)
    {
        var viewerId = GuidHelper.GetPlayerId(viewer.Guid);
        var hasVote = dbContext.HouseVotes.AsNoTracking().Any(vote =>
            vote.HouseId == house.Id && vote.CharacterId == viewerId);

        return new PlayerHousingInstanceInfo
        {
            OwnerGuid = GuidHelper.GetPlayerGuid(house.CharacterId),
            InstanceGuid = GuidHelper.GetHouseGuid(house.Id),
            NameId = _zone.HousingDefinition.NameId,
            OwnerName = ResolveOwnerName(house),
            HouseName = string.IsNullOrWhiteSpace(house.Name)
                ? _zone.HousingDefinition.DisplayName
                : house.Name,
            IconId = _zone.HousingDefinition.IconId,
            FixtureCount = house.Fixtures.Count,
            FurnitureScore = house.FurnitureScore,
            LastVisited = house.LastVisited,
            IsLocked = house.IsLocked,
            IsMembersOnly = house.IsMembersOnly,
            IsFloraAllowed = house.IsFloraAllowed,
            Description = house.Description,
            KeywordList = house.KeywordList,
            Unknown21 = _zone.HousingDefinition.DirectorySnapshot,
            Rating = house.Rating,
            Votes = house.Votes,
            HasRating = house.IsPublished,
            CanVote = house.IsPublished && viewerId != house.CharacterId && !hasVote,
            FactoryPlotId = 0,
            WhenCreated = house.Created
        };
    }

    private PlayerHousingInstanceData BuildInstanceData(
        DbHouse house,
        ulong ownerGuid,
        string ownerName)
    {
        var fixtures = new Dictionary<uint, FixtureInstance>();
        foreach (var fixture in house.Fixtures.OrderBy(candidate => candidate.Id))
        {
            fixtures[unchecked((uint)fixture.Id)] = BuildFixtureInstance(
                fixture,
                GetActorGuid(fixture.Id));
        }

        return new PlayerHousingInstanceData
        {
            HouseGuid = GuidHelper.GetHouseGuid(house.Id),
            OwnerGuid = ownerGuid,
            OwnerName = ownerName,
            NameId = _zone.HousingDefinition.NameId,
            Name = string.IsNullOrWhiteSpace(house.Name)
                ? _zone.HousingDefinition.DisplayName
                : house.Name,
            IsLocked = house.IsLocked,
            IsFloraAllowed = house.IsFloraAllowed,
            MaxFixtureCount = house.MaxFixtureCount,
            MaxLandmarkCount = house.MaxLandmarkCount,
            Preview = false,
            CurFixtureCount = house.Fixtures.Count,
            CurLandmarkCount = 0,
            IconId = _zone.HousingDefinition.IconId,
            FurnitureScore = house.FurnitureScore,
            IsMembersOnly = house.IsMembersOnly,
            Unknown22 = string.Empty,
            Unknown23 = string.Empty,
            Fixtures = fixtures,
            Permissions = new Dictionary<int, InstancePermission>
            {
                [0] = new()
                {
                    Guid = ownerGuid,
                    Level = 3
                }
            },
            BuildAreas = _zone.HousingDefinition.BuildAreas
        };
    }

    /// <summary>
    /// For when the player's inventory has changed. In creative mode the list is the catalog, which inventory doesn't
    /// change, so it isn't sent again.
    /// </summary>
    private void SendFixtureItemListUnlessCreative(Player player, DbHouse house)
    {
        if (!_options.CreativeMode)
            SendFixtureItemList(player, house);
    }

    private void SendFixtureItemList(Player player, DbHouse house)
    {
        var packet = new HousingPacketFixtureItemList();
        var definitionIds = new HashSet<int>();

        foreach (var fixture in house.Fixtures.OrderBy(candidate => candidate.Id))
            TryAddFixtureDefinition(packet, definitionIds, fixture.ItemDefinitionId);

        if (_options.CreativeMode)
        {
            AddCreativeCatalog(packet, definitionIds);
            player.SendTunneled(packet);
            return;
        }

        foreach (var item in player.Items
            .Where(item => item.Count > 0 && IsFixtureInventoryItem(item.Definition))
            .OrderBy(item => item.Definition)
            .ThenBy(item => item.Id))
        {
            if (!TryAddFixtureDefinition(packet, definitionIds, item.Definition) ||
                !_resourceManager.ClientItemDefinitions.TryGetValue(item.Definition, out var definition))
            {
                continue;
            }

            packet.Infos.Add(new FixtureInstanceInfo
            {
                FixtureGuid = unchecked((ulong)item.Id),
                ItemDefinitionId = item.Definition,
                CouplingDisplay =
                {
                    Id = item.Id,
                    CompositeEffect = definition.CompositeEffectId,
                    EffectType = 0
                }
            });

            if (definition.CompositeEffectId != 0 && !packet.Effects.Contains(definition.CompositeEffectId))
                packet.Effects.Add(definition.CompositeEffectId);
        }

        player.SendTunneled(packet);
    }

    // The editor's tray lists only what is in the player's inventory (Housing.lua PopulateItems reads
    // BaseClient.InventoryItemsData for the selected category); it ignores HousingPacketFixtureItemList. So while the owner
    // decorates, the catalog is shown as client-side inventory items whose ids are catalog record ids. They are never added
    // to player.Items or the database, so nothing outside the house editor can use, trade or sell them.
    private void ShowCreativeInventory(Player player)
    {
        if (!_options.CreativeMode || !_creativeInventoryShown.Add(player.Guid))
            return;

        foreach (var entry in CreativeEntries())
        {
            if (!_resourceManager.ClientItemDefinitions.TryGetValue(entry.ItemDefinitionId, out var definition))
                continue;

            var item = new ClientItem
            {
                Id = entry.RecordId,
                Definition = entry.ItemDefinitionId,
                Tint = entry.TintId,
                Count = 1
            };

            using var writer = new PacketWriter();
            item.Serialize(writer);
            definition.Serialize(writer);
            player.SendTunneled(new ClientUpdatePacketItemAdd
            {
                Payload = writer.Buffer
            });
        }
    }

    private void HideCreativeInventory(Player player)
    {
        if (!_creativeInventoryShown.Remove(player.Guid))
            return;

        foreach (var entry in CreativeEntries())
        {
            player.SendTunneled(new ClientUpdatePacketItemDelete
            {
                ItemGuid = entry.RecordId
            });
        }
    }

    private IEnumerable<HousingCreativeCatalog.Entry> CreativeEntries()
    {
        return _options.CreativeCatalogLimit > 0
            ? _creativeCatalog.Entries.Take(_options.CreativeCatalogLimit)
            : _creativeCatalog.Entries;
    }

    private void AddCreativeCatalog(HousingPacketFixtureItemList packet, HashSet<int> definitionIds)
    {
        var effects = new HashSet<int>();
        foreach (var entry in CreativeEntries())
        {
            if (definitionIds.Add(entry.ItemDefinitionId))
                packet.Definitions.Add(entry.Definition);

            packet.Infos.Add(new FixtureInstanceInfo
            {
                FixtureGuid = unchecked((ulong)entry.RecordId),
                ItemDefinitionId = entry.ItemDefinitionId,
                CouplingDisplay =
                {
                    Id = entry.RecordId,
                    CompositeEffect = entry.Definition.CompositeEffectId,
                    EffectType = 0
                }
            });

            if (entry.Definition.CompositeEffectId != 0 && effects.Add(entry.Definition.CompositeEffectId))
                packet.Effects.Add(entry.Definition.CompositeEffectId);
        }
    }

    private bool TryAddFixtureDefinition(
        HousingPacketFixtureItemList packet,
        HashSet<int> definitionIds,
        int itemDefinitionId)
    {
        if (!definitionIds.Add(itemDefinitionId))
            return true;

        var definition = BuildFixtureDefinition(itemDefinitionId);
        if (definition is null)
        {
            definitionIds.Remove(itemDefinitionId);
            return false;
        }

        packet.Definitions.Add(definition);
        return true;
    }

    private FixtureDefinition? BuildFixtureDefinition(int itemDefinitionId)
    {
        return HousingFixtureRules.BuildFixtureDefinition(_resourceManager, itemDefinitionId);
    }

    private FixtureInstance BuildFixtureInstance(DbHouseFixture fixture, ulong npcGuid)
    {
        _resourceManager.ClientItemDefinitions.TryGetValue(
            fixture.ItemDefinitionId,
            out var itemDefinition);

        return new FixtureInstance
        {
            Guid = GuidHelper.GetFixtureGuid((ulong)fixture.Id),
            HouseGuid = GuidHelper.GetHouseGuid(fixture.HouseId),
            FixtureDefinitionId = fixture.ItemDefinitionId,
            Position = GetPosition(fixture),
            Rotation = GetHousingRotation(fixture),
            Tilt = Quaternion.Identity,
            NpcGuid = npcGuid,
            TintId = ResolveItemTintId(fixture.ItemDefinitionId, fixture.TintId),
            Customization = new CustomizationDetail
            {
                Type = 1,
                TextureAlias = itemDefinition?.TextureAlias ?? string.Empty,
                TintAlias = itemDefinition?.TintAlias ?? string.Empty,
                TintId = ResolveItemTintId(fixture.ItemDefinitionId, fixture.TintId),
                TextureOverride = string.Empty
            },
            Unknown11 = string.Empty,
            Unknown12 = string.Empty,
            XmlData = fixture.CustomizationData ?? string.Empty,
            Scale = NormalizeScale(fixture.Scale)
        };
    }

    private void SendFixtureUpdate(
        Player player,
        ulong fixtureGuid,
        ulong npcGuid,
        int itemDefinitionId,
        int itemRecordId,
        int tintId,
        Vector4 position,
        Quaternion rotation,
        float scale,
        bool includeAsset)
    {
        var definition = BuildFixtureDefinition(itemDefinitionId);
        if (definition is null ||
            !_resourceManager.ClientItemDefinitions.TryGetValue(itemDefinitionId, out var itemDefinition))
        {
            return;
        }

        tintId = ResolveItemTintId(itemDefinitionId, tintId);
        var instance = new FixtureInstance
        {
            Guid = fixtureGuid,
            HouseGuid = GuidHelper.GetHouseGuid(_zone.HouseId),
            FixtureDefinitionId = itemDefinitionId,
            Position = position,
            Rotation = rotation,
            Tilt = Quaternion.Identity,
            NpcGuid = npcGuid,
            TintId = tintId,
            Customization = new CustomizationDetail
            {
                Type = 1,
                TextureAlias = itemDefinition.TextureAlias ?? string.Empty,
                TintAlias = itemDefinition.TintAlias ?? string.Empty,
                TintId = tintId,
                TextureOverride = string.Empty
            },
            Unknown11 = string.Empty,
            Unknown12 = string.Empty,
            XmlData = string.Empty,
            Scale = NormalizeScale(scale)
        };

        player.SendTunneled(new HousingPacketFixtureUpdate
        {
            Instance = instance,
            Info = new FixtureInstanceInfo
            {
                FixtureGuid = fixtureGuid,
                ItemDefinitionId = itemDefinitionId,
                CouplingDisplay =
                {
                    Id = itemRecordId,
                    CompositeEffect = itemDefinition.CompositeEffectId
                }
            },
            Definition = definition,
            Unknown1 = unchecked((int)GetFixtureRuntimeKey(fixtureGuid))
        });

        if (includeAsset)
            SendFixtureAsset(player, itemDefinitionId, tintId, false);
    }

    private void BroadcastFixtureUpdate(DbHouseFixture fixture, int itemRecordId, ulong excludedPlayerGuid = 0)
    {
        var fixtureGuid = GuidHelper.GetFixtureGuid((ulong)fixture.Id);
        var actorGuid = GetActorGuid(fixture.Id);
        foreach (var player in _zone.Players)
        {
            if (player.Guid == excludedPlayerGuid)
                continue;

            SendFixtureUpdate(
                player,
                fixtureGuid,
                actorGuid,
                fixture.ItemDefinitionId,
                itemRecordId,
                fixture.TintId,
                GetPosition(fixture),
                GetHousingRotation(fixture),
                fixture.Scale,
                true);
        }
    }

    private void SendPersistedFixtureUpdates(Player player, DbHouse house)
    {
        foreach (var fixture in house.Fixtures.OrderBy(candidate => candidate.Id))
        {
            SendFixtureUpdate(
                player,
                GuidHelper.GetFixtureGuid((ulong)fixture.Id),
                GetActorGuid(fixture.Id),
                fixture.ItemDefinitionId,
                0,
                fixture.TintId,
                GetPosition(fixture),
                GetHousingRotation(fixture),
                fixture.Scale,
                false);
        }
    }

    private void SendFixtureAsset(Player player, int itemDefinitionId, int tintId, bool isPreview)
    {
        var definition = BuildFixtureDefinition(itemDefinitionId);
        if (definition is null ||
            !_resourceManager.ClientItemDefinitions.TryGetValue(itemDefinitionId, out var itemDefinition))
        {
            return;
        }

        tintId = ResolveItemTintId(itemDefinitionId, tintId);
        player.SendTunneled(new HousingPacketFixtureAsset
        {
            ModelDefinitionId = definition.ModelId,
            ItemDefinitionId = itemDefinitionId,
            Definition = definition,
            TextureAlias = itemDefinition.TextureAlias ?? string.Empty,
            TintAlias = itemDefinition.TintAlias ?? string.Empty,
            TintId = tintId,
            PreviewTintId = tintId,
            IsPreview = isPreview
        });
    }

    private void SendPersistedFixtureAssets(Player player, DbHouse house)
    {
        foreach (var appearance in house.Fixtures
            .Select(fixture => new
            {
                fixture.ItemDefinitionId,
                TintId = ResolveItemTintId(fixture.ItemDefinitionId, fixture.TintId)
            })
            .Distinct())
        {
            SendFixtureAsset(player, appearance.ItemDefinitionId, appearance.TintId, false);
        }
    }

    private void EnsureActor(DbHouseFixture fixture)
    {
        if (_actors.TryGetValue(fixture.Id, out var existing))
        {
            ApplyActorState(existing, fixture);
            return;
        }

        var modelId = ResolveFixtureActorModelId(fixture.ItemDefinitionId);
        if (modelId == 0 ||
            !_resourceManager.ClientItemDefinitions.TryGetValue(fixture.ItemDefinitionId, out var itemDefinition) ||
            !_zone.TryCreateNpc(GuidHelper.GetFixtureGuid((ulong)fixture.Id), out var actor))
        {
            return;
        }

        actor.Name = string.Empty;
        actor.ModelId = modelId;
        actor.TextureAlias = itemDefinition.TextureAlias ?? string.Empty;
        actor.TintAlias = itemDefinition.TintAlias ?? string.Empty;
        actor.HideNamePlate = true;
        // The client marks an actor as a selectable house fixture only when it is interactable with an interact range
        // of 0 (FreeRealms.exe 0x92f84b-0x92f88a sets entity flag 4). Otherwise the editor can't pick it up, move or
        // rotate it, and a click becomes an ordinary NPC interaction.
        actor.IsInteractable = true;
        actor.InteractRange = 0;
        actor.Visible = true;
        actor.MovementType = 0;
        actor.Speed = 0f;
        actor.CollisionEnabled = true;
        _actors[fixture.Id] = actor;
        ApplyActorState(actor, fixture);
    }

    private void UpdateActor(DbHouseFixture fixture)
    {
        if (_actors.TryGetValue(fixture.Id, out var actor))
            ApplyActorState(actor, fixture);
        else
            EnsureActor(fixture);
    }

    private void ApplyActorState(Npc actor, DbHouseFixture fixture)
    {
        actor.TintId = ResolveItemTintId(fixture.ItemDefinitionId, fixture.TintId);
        actor.Scale = NormalizeScale(fixture.Scale);
        actor.UpdatePositionWithoutBroadcast(GetPosition(fixture), ToActorRotation(GetHousingRotation(fixture)));
    }

    private void RemoveActor(int fixtureId)
    {
        if (!_actors.Remove(fixtureId, out var actor))
            return;

        _zone.UpdateEntityZoneTile(actor, actor.ZoneTile, ZoneTile.Empty);
        _zone.TryRemoveNpc(actor.Guid);
    }

    private ulong GetActorGuid(int fixtureId)
    {
        return _actors.TryGetValue(fixtureId, out var actor) ? actor.Guid : 0;
    }

    private void SendPersistedFixtureTransforms(Player player)
    {
        using var dbContext = _dbContextFactory.CreateDbContext();
        var fixtures = dbContext.HouseFixtures
            .AsNoTracking()
            .Where(fixture => fixture.HouseId == _zone.HouseId)
            .OrderBy(fixture => fixture.Id)
            .ToList();

        foreach (var fixture in fixtures)
        {
            var actorGuid = GetActorGuid(fixture.Id);
            if (actorGuid == 0)
                continue;

            player.SendTunneled(new HousingPacketUpdateFixturePosition
            {
                FixtureActorGuid = actorGuid,
                Position = GetPosition(fixture),
                Rotation = GetHousingRotation(fixture)
            });
        }
    }

    private void SendHouseCustomizations(Player player, DbHouse house)
    {
        foreach (var customization in DeserializeCustomizations(house.CustomizationData))
        {
            SendHouseCustomization(
                player,
                customization.FixtureGroup,
                customization.FixtureType,
                customization.ItemDefinitionId,
                customization.TintId);
        }
    }

    private void SendHouseCustomization(
        Player player,
        string fixtureGroup,
        string fixtureType,
        int itemDefinitionId,
        int tintId)
    {
        var definition = BuildFixtureDefinition(itemDefinitionId);
        if (definition is null ||
            !_resourceManager.ClientItemDefinitions.TryGetValue(itemDefinitionId, out var itemDefinition) ||
            !HousingPlacementCatalog.IsFixtureCustomization(itemDefinition))
        {
            return;
        }

        var modelIds = HousingSurfaceCatalog.GetTargetModelIds(_zone.Name, fixtureType);
        var textureOverride = HousingSurfaceCatalog.GetTextureOverride(itemDefinitionId);
        if (modelIds.Count == 0 || textureOverride.Length == 0)
            return;

        definition.Category = fixtureGroup;
        definition.LuaCall = fixtureType;
        foreach (var modelId in modelIds)
        {
            if (!_resourceManager.Models.ContainsKey(modelId))
                continue;

            definition.ModelId = modelId;
            player.SendTunneled(new HousingPacketFixtureAsset
            {
                ModelDefinitionId = modelId,
                ItemDefinitionId = itemDefinitionId,
                Definition = definition,
                TextureAlias = itemDefinition.TextureAlias ?? "customization",
                TintAlias = itemDefinition.TintAlias ?? "dyetint",
                TintId = tintId,
                PreviewTintId = tintId,
                TextureOverride = textureOverride,
                IsPreview = false
            });
        }
    }

    private void SendHouseCustomizationReset(
        Player player,
        string fixtureGroup,
        string fixtureType,
        int itemDefinitionId,
        int tintId)
    {
        var definition = BuildFixtureDefinition(itemDefinitionId);
        if (definition is null ||
            !_resourceManager.ClientItemDefinitions.TryGetValue(itemDefinitionId, out var itemDefinition))
        {
            return;
        }

        definition.Category = fixtureGroup;
        definition.LuaCall = fixtureType;
        foreach (var modelId in HousingSurfaceCatalog.GetTargetModelIds(_zone.Name, fixtureType))
        {
            if (!_resourceManager.Models.ContainsKey(modelId))
                continue;

            definition.ModelId = modelId;
            player.SendTunneled(new HousingPacketFixtureAsset
            {
                ModelDefinitionId = modelId,
                ItemDefinitionId = itemDefinitionId,
                Definition = definition,
                TextureAlias = itemDefinition.TextureAlias ?? "customization",
                TintAlias = itemDefinition.TintAlias ?? "dyetint",
                TintId = tintId,
                PreviewTintId = tintId,
                TextureOverride = string.Empty,
                IsPreview = false
            });
        }
    }

    private void SendHouseInfo(Player player, DbHouse house)
    {
        player.SendTunneled(new HousingPacketUpdateHouseInfo
        {
            InEditMode = _editors.Contains(player.Guid),
            IsLocked = house.IsLocked,
            IsFloraAllowed = house.IsFloraAllowed,
            PetAutospawn = house.PetAutospawn,
            CurFixtureCount = house.Fixtures.Count,
            CurLandmarkCount = 0,
            FurnitureScore = house.FurnitureScore
        });
    }

    private void BroadcastHouseInfo(DbHouse house)
    {
        foreach (var player in _zone.Players)
            SendHouseInfo(player, house);
    }

    private void Broadcast(ISerializablePacket packet, ulong excludedPlayerGuid = 0)
    {
        foreach (var player in _zone.Players)
        {
            if (player.Guid == excludedPlayerGuid)
                continue;

            player.SendTunneled(packet);
        }
    }

    private void UpdateConsumedInventoryItem(Player player, int itemRecordId, int count)
    {
        var item = player.Items.SingleOrDefault(candidate => candidate.Id == itemRecordId);
        if (count == 0)
        {
            if (item is not null)
                player.Items.Remove(item);

            player.SendTunneled(new ClientUpdatePacketItemDelete
            {
                ItemGuid = itemRecordId
            });
            return;
        }

        if (item is not null)
            item.Count = count;

        player.SendTunneled(new ClientUpdatePacketItemUpdate
        {
            ItemGuid = itemRecordId,
            Count = count
        });
    }

    private void UpdateReturnedInventoryItem(Player player, DbItem dbItem)
    {
        var item = player.Items.SingleOrDefault(candidate =>
            candidate.Definition == dbItem.Definition &&
            candidate.Tint == dbItem.Tint);
        if (item is not null)
        {
            item.Count = dbItem.Count;
            player.SendTunneled(new ClientUpdatePacketItemUpdate
            {
                ItemGuid = item.Id,
                Count = item.Count
            });
            return;
        }

        item = new ClientItem
        {
            Id = dbItem.Id,
            Definition = dbItem.Definition,
            Tint = dbItem.Tint,
            Count = dbItem.Count
        };
        player.Items.Add(item);

        if (!_resourceManager.ClientItemDefinitions.TryGetValue(item.Definition, out var definition))
            return;

        using var writer = new PacketWriter();
        item.Serialize(writer);
        definition.Serialize(writer);
        player.SendTunneled(new ClientUpdatePacketItemAdd
        {
            Payload = writer.Buffer
        });
    }

    private bool CanEdit(Player player)
    {
        return !_disposed &&
            ReferenceEquals(player.Zone, _zone) &&
            IsOwner(player) &&
            _editors.Contains(player.Guid);
    }

    private bool IsOwner(Player player)
    {
        return _zone.OwnerId == GuidHelper.GetPlayerId(player.Guid);
    }

    private bool IsFixtureInventoryItem(int itemDefinitionId)
    {
        return HousingFixtureRules.IsFixtureInventoryItem(_resourceManager, itemDefinitionId);
    }

    private int ResolveItemTintId(int itemDefinitionId, int requestedTintId)
    {
        return HousingFixtureRules.ResolveItemTintId(_resourceManager, itemDefinitionId, requestedTintId);
    }

    private int ResolveFixtureActorModelId(int itemDefinitionId)
    {
        if (!_resourceManager.ClientItemDefinitions.TryGetValue(itemDefinitionId, out var definition))
            return 0;

        if (!HousingPlacementCatalog.IsFixtureCustomization(definition) && definition.Param1 > 0)
            return definition.Param1;

        var modelName = HousingPlacementCatalog.TryGet(itemDefinitionId, out var placement)
            ? placement.AssetName
            : definition.ModelName;
        if (string.IsNullOrWhiteSpace(modelName))
            return 0;

        if (modelName.EndsWith(".agr", StringComparison.OrdinalIgnoreCase))
        {
            var actorName = modelName[..^4];
            if (actorName.EndsWith("_complete", StringComparison.OrdinalIgnoreCase))
                actorName = actorName[..^9];

            var actorModelId = ResolveModelId(actorName + ".adr");
            if (actorModelId != 0)
                return actorModelId;
        }

        return ResolveModelId(modelName);
    }

    private int ResolveModelId(string? modelName)
    {
        return HousingFixtureRules.ResolveModelId(_resourceManager, modelName);
    }

    private bool TryNormalizeTransform(
        Vector4 position,
        Quaternion rotation,
        float scale,
        out Vector4 normalizedPosition,
        out Quaternion normalizedRotation,
        out float normalizedScale,
        bool requireBuildArea = true)
    {
        normalizedPosition = position.W == 0f
            ? new Vector4(position.X, position.Y, position.Z, 1f)
            : position;
        normalizedRotation = ToHousingRotation(rotation);
        normalizedScale = NormalizeScale(scale);

        return IsFinite(normalizedPosition) &&
            IsFinite(normalizedRotation) &&
            float.IsFinite(scale) &&
            normalizedScale is >= 0.05f and <= 20f &&
            (!requireBuildArea || IsInsideBuildArea(normalizedPosition));
    }

    private bool IsInsideBuildArea(Vector4 position)
    {
        return _zone.HousingDefinition.BuildAreas.Any(area =>
            position.X >= MathF.Min(area.Min.X, area.Max.X) &&
            position.X <= MathF.Max(area.Min.X, area.Max.X) &&
            position.Y >= MathF.Min(area.Min.Y, area.Max.Y) &&
            position.Y <= MathF.Max(area.Min.Y, area.Max.Y) &&
            position.Z >= MathF.Min(area.Min.Z, area.Max.Z) &&
            position.Z <= MathF.Max(area.Min.Z, area.Max.Z));
    }

    private static bool IsFinite(Vector4 value)
    {
        return float.IsFinite(value.X) &&
            float.IsFinite(value.Y) &&
            float.IsFinite(value.Z) &&
            float.IsFinite(value.W);
    }

    private static bool IsFinite(Quaternion value)
    {
        return float.IsFinite(value.X) &&
            float.IsFinite(value.Y) &&
            float.IsFinite(value.Z) &&
            float.IsFinite(value.W);
    }

    private static float NormalizeScale(float scale)
    {
        return scale <= 0f ? 1f : scale;
    }

    private static Quaternion ToHousingRotation(Quaternion rotation)
    {
        if (!IsFinite(rotation))
            return default;

        if (MathF.Abs(rotation.X) <= 0.0001f &&
            MathF.Abs(rotation.Y) <= 0.0001f &&
            MathF.Abs(rotation.Z) <= 0.0001f)
        {
            return default;
        }

        var planarLengthSquared = rotation.X * rotation.X + rotation.Z * rotation.Z;
        if (MathF.Abs(rotation.Y) <= 0.0001f &&
            MathF.Abs(rotation.W) <= 0.0001f &&
            MathF.Abs(planarLengthSquared - 1f) <= 0.01f)
        {
            return new Quaternion(MathF.Atan2(rotation.X, rotation.Z), 0f, 0f, 0f);
        }

        return new Quaternion(rotation.X, rotation.Y, rotation.Z, 0f);
    }

    private static Quaternion ToActorRotation(Quaternion rotation)
    {
        var housingRotation = ToHousingRotation(rotation);
        return new Quaternion(
            MathF.Sin(housingRotation.X),
            0f,
            MathF.Cos(housingRotation.X),
            0f);
    }

    private static Vector4 ResolvePreviewPosition(Player player, Quaternion rotation)
    {
        var normalizedRotation = ToHousingRotation(rotation);
        var forward = new Vector3(
            MathF.Sin(normalizedRotation.X),
            0f,
            MathF.Cos(normalizedRotation.X)) * 2.5f;
        return new Vector4(
            player.Position.X + forward.X,
            player.Position.Y,
            player.Position.Z + forward.Z,
            1f);
    }

    private static bool IsEmptyPosition(Vector4 position)
    {
        return MathF.Abs(position.X) <= 0.001f &&
            MathF.Abs(position.Y) <= 0.001f &&
            MathF.Abs(position.Z) <= 0.001f;
    }

    private static bool TryGetFixtureId(ulong fixtureGuid, out int fixtureId)
    {
        fixtureId = 0;
        ulong rawId;
        try
        {
            rawId = GuidHelper.GetFixtureId(fixtureGuid);
        }
        catch (ArgumentException)
        {
            rawId = fixtureGuid;
        }

        if (rawId == 0 || rawId > int.MaxValue)
            return false;

        fixtureId = (int)rawId;
        return true;
    }

    private static uint GetFixtureRuntimeKey(ulong fixtureGuid)
    {
        try
        {
            var id = GuidHelper.GetFixtureId(fixtureGuid);
            if (id is > 0 and <= uint.MaxValue)
                return (uint)id;
        }
        catch (ArgumentException)
        {
        }

        var low = unchecked((uint)fixtureGuid);
        if (low != 0)
            return low;

        var high = unchecked((uint)(fixtureGuid >> 32));
        return high == 0 ? 1u : high;
    }

    private static Vector4 GetPosition(DbHouseFixture fixture)
    {
        return new Vector4(
            fixture.PositionX,
            fixture.PositionY,
            fixture.PositionZ,
            fixture.PositionW);
    }

    private static Quaternion GetHousingRotation(DbHouseFixture fixture)
    {
        return ToHousingRotation(new Quaternion(
            fixture.RotationX,
            fixture.RotationY,
            fixture.RotationZ,
            fixture.RotationW));
    }

    private static string ResolveOwnerName(DbHouse house)
    {
        if (!string.IsNullOrWhiteSpace(house.Character?.FullName))
            return house.Character.FullName;

        var name = $"{house.Character?.FirstName} {house.Character?.LastName}".Trim();
        return name.Length == 0 ? "Unknown" : name;
    }

    private static string NormalizeSelector(string value)
    {
        value = value.Trim();
        return value.Length <= 128 ? value : value[..128];
    }

    private static List<HouseSurfaceCustomization> DeserializeCustomizations(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return [];

        try
        {
            return JsonSerializer.Deserialize<List<HouseSurfaceCustomization>>(value) ?? [];
        }
        catch (JsonException)
        {
            return [];
        }
    }

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
    }
}
