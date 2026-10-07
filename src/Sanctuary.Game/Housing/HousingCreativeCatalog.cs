using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Linq;
using System.Threading;

using Sanctuary.Packet.Common;

namespace Sanctuary.Game.Housing;

/// <summary>
/// Every fixture the editor can place, for <see cref="HousingOptions.CreativeMode"/>. Built from the item
/// definitions on first use and rebuilt shortly after they reload.
/// </summary>
/// <remarks>
/// The editor's fixture list identifies each entry by an item record id, and the client sends that id back when the
/// player picks the entry. A creative entry isn't an item the player owns, so it gets an id from
/// <see cref="RecordIdBase"/> up: <c>RecordIdBase + item definition id</c>. Inventory ids count up from 1 per
/// character, so the two never meet, and an id stays the same across reloads and restarts.
/// <para>
/// One entry per item definition. Colour variants that the game ships as separate definitions (blocks, category
/// 147) are separate entries, so every one of those tints is offered. A dye-tintable definition is offered once, in
/// its default tint: the list packet carries no tint, and the server has no list of the tints in a tint group.
/// </para>
/// </remarks>
public sealed class HousingCreativeCatalog : IDisposable
{
    public const int RecordIdBase = 0x4000_0000;

    public sealed record Entry(int ItemDefinitionId, int TintId, FixtureDefinition Definition)
    {
        public int RecordId => RecordIdBase + ItemDefinitionId;
    }

    private sealed record Snapshot(IReadOnlyList<Entry> Entries, IReadOnlyDictionary<int, Entry> ByDefinitionId);

    private readonly IResourceManager _resourceManager;
    private readonly object _buildLock = new();
    private volatile Snapshot? _snapshot;
    private int _version;

    public HousingCreativeCatalog(IResourceManager resourceManager)
    {
        _resourceManager = resourceManager;

        _resourceManager.ClientItemDefinitions.CollectionChanged += OnResourcesChanged;
        _resourceManager.Models.CollectionChanged += OnResourcesChanged;
    }

    /// <summary>
    /// The catalog, ordered by item definition id.
    /// </summary>
    public IReadOnlyList<Entry> Entries => GetSnapshot().Entries;

    public static bool IsCreativeRecordId(int itemRecordId) => itemRecordId >= RecordIdBase;

    public bool TryGetByDefinitionId(int itemDefinitionId, out Entry entry)
    {
        return GetSnapshot().ByDefinitionId.TryGetValue(itemDefinitionId, out entry!);
    }

    public bool TryGetByRecordId(int itemRecordId, out Entry entry)
    {
        if (!IsCreativeRecordId(itemRecordId))
        {
            entry = null!;
            return false;
        }

        return TryGetByDefinitionId(itemRecordId - RecordIdBase, out entry);
    }

    /// <summary>
    /// An id the client sends may be a catalog record id or a plain item definition id; packets differ.
    /// </summary>
    public bool TryGetByRecordOrDefinitionId(int id, out Entry entry)
    {
        return TryGetByRecordId(id, out entry) || TryGetByDefinitionId(id, out entry);
    }

    public void Dispose()
    {
        _resourceManager.ClientItemDefinitions.CollectionChanged -= OnResourcesChanged;
        _resourceManager.Models.CollectionChanged -= OnResourcesChanged;
    }

    private Snapshot GetSnapshot()
    {
        var snapshot = _snapshot;
        if (snapshot is not null)
            return snapshot;

        lock (_buildLock)
        {
            snapshot = _snapshot;
            if (snapshot is not null)
                return snapshot;

            // A reload while building leaves this build stale: use it, but don't keep it.
            var version = Volatile.Read(ref _version);
            snapshot = Build();
            if (Volatile.Read(ref _version) == version)
                _snapshot = snapshot;

            return snapshot;
        }
    }

    private Snapshot Build()
    {
        var entries = new List<Entry>();

        foreach (var itemDefinitionId in _resourceManager.ClientItemDefinitions.Keys.OrderBy(id => id))
        {
            if (itemDefinitionId <= 0 ||
                itemDefinitionId > int.MaxValue - RecordIdBase ||
                !HousingFixtureRules.IsFixtureInventoryItem(_resourceManager, itemDefinitionId))
            {
                continue;
            }

            var definition = HousingFixtureRules.BuildFixtureDefinition(_resourceManager, itemDefinitionId);
            if (definition is null)
                continue;

            entries.Add(new Entry(
                itemDefinitionId,
                HousingFixtureRules.ResolveItemTintId(_resourceManager, itemDefinitionId, 0),
                definition));
        }

        return new Snapshot(entries, entries.ToDictionary(entry => entry.ItemDefinitionId));
    }

    private void OnResourcesChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        Interlocked.Increment(ref _version);
        _snapshot = null;
    }
}
