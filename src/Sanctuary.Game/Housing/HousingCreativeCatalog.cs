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

    // A record id is RecordIdBase | dye << 20 | item definition id. Item definition ids fit in 20 bits (the
    // largest is about 900,000) and dye tint ids in 9. Every entry carries the owner's palette, dyeable or not, so the
    // Decorate panel's colour bar can read it back from any tray item, and a dyed variant gets its own id without any
    // stored state.
    private const int DyeShift = 20;
    private const int MaxDefinitionId = (1 << DyeShift) - 1;
    private const int MaxDyeTintId = (1 << 9) - 1;

    /// <param name="DefaultTintId">The definition's own tint.</param>
    /// <param name="IsDyeable">Whether a dye changes its colour (<see cref="HousingDyeTints.IsDyeable"/>).</param>
    public sealed record Entry(int ItemDefinitionId, int DefaultTintId, FixtureDefinition Definition, bool IsDyeable = false)
    {
        /// <summary>The owner's dye when the entry was shown, 0 for none. It colours the entry only if it is dyeable.</summary>
        public int Dye { get; init; }

        public int TintId => IsDyeable && Dye > 0 ? Dye : DefaultTintId;

        public int RecordId => RecordIdBase | Dye << DyeShift | ItemDefinitionId;

        /// <summary>This entry as shown to an owner with <paramref name="palette"/>.</summary>
        public Entry WithPalette(HousingPalette palette)
        {
            return this with { Dye = palette.Dye is > 0 and <= MaxDyeTintId ? palette.Dye : 0 };
        }
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

        var definitionId = itemRecordId & MaxDefinitionId;
        var dye = (itemRecordId - RecordIdBase) >> DyeShift;
        if (dye > MaxDyeTintId || !TryGetByDefinitionId(definitionId, out entry))
        {
            entry = null!;
            return false;
        }

        entry = entry.WithPalette(new HousingPalette(dye));
        return true;
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
                itemDefinitionId > MaxDefinitionId ||
                !HousingFixtureRules.IsFixtureInventoryItem(_resourceManager, itemDefinitionId))
            {
                continue;
            }

            var definition = HousingFixtureRules.BuildFixtureDefinition(_resourceManager, itemDefinitionId);
            if (definition is null ||
                !_resourceManager.ClientItemDefinitions.TryGetValue(itemDefinitionId, out var itemDefinition))
            {
                continue;
            }

            entries.Add(new Entry(
                itemDefinitionId,
                HousingFixtureRules.ResolveItemTintId(_resourceManager, itemDefinitionId, 0),
                definition,
                HousingDyeTints.IsDyeable(itemDefinition)));
        }

        return new Snapshot(entries, entries.ToDictionary(entry => entry.ItemDefinitionId));
    }

    private void OnResourcesChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        Interlocked.Increment(ref _version);
        _snapshot = null;
    }
}
