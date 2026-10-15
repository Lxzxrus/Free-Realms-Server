using System;
using System.Collections.Generic;
using System.Numerics;

using Microsoft.Extensions.Logging;

using Sanctuary.Game.Resources.Definitions.Zones;
using Sanctuary.Packet;
using Sanctuary.Packet.Common;

namespace Sanctuary.Game.Minigames.Fishing;

public enum FishingPhase
{
    /// <summary>Nothing in the water.</summary>
    Idle,

    /// <summary>The bobber is out and the fish wander.</summary>
    BobberOut,

    /// <summary>The biting fish is interested and swims to the hook.</summary>
    Nibbling,

    /// <summary>The fish has bitten and fights; reeling in now catches it.</summary>
    Hooked,

    /// <summary>Reeled in; waiting for the fight to end before pulling the catch up.</summary>
    ReelPending,

    /// <summary>The catch is coming up; then it is granted and shown.</summary>
    Reeling
}

/// <summary>
/// One player fishing one hole. The client draws the bobber and the fish by itself, but the bite is the server's:
/// a cast spawns the bobber and the underwater fish, then on timers a fish becomes interested, swims in and bites
/// (<see cref="FishingPacketUpdateProxiedFishingBobber"/>). Reeling in while it is hooked catches it; reeling in
/// before it bites scares it off, and one left on the hook too long gets away.
/// </summary>
/// <remarks>
/// From edenfps' fishing (upstream PR #31), reverse-engineered from the 2014 client and played in game at Sacred Grove
/// Shallows; <c>docs/research/fishing/FISHING_RE_NOTES.md</c> explains every choice below. Everything runs on the
/// Gateway's packet thread: the packet handlers and the tick that calls <see cref="Update"/>.
/// </remarks>
public sealed class FishingSession
{
    /// <summary>The bobber (fishing_bobber_bbe). With 0 the client makes no bobber and then crashes on the fish run.</summary>
    public const int BobberModelId = 1063;

    /// <summary>
    /// The actor the client parks at the hook before the bite: widget_01, the invisible block. It must be a real model
    /// (0 crashes the client) that shows nothing.
    /// </summary>
    public const int InvisibleDecoyModelId = 69;

    /// <summary>The fish that bites. Only fish that aren't catchable swim, bite and fight.</summary>
    public const int BiterFishId = 2;

    /// <summary>Models the client creates as soon as fishing starts.</summary>
    public static readonly int[] PreloadModelIds =
        [FishingCatch.ThinFishModelId, FishingCatch.MediumFishModelId, FishingCatch.FatFishModelId];

    /// <summary>The client ignores a cast this soon after the last: it sends each cast several times.</summary>
    public const int DuplicateCastMs = 500;

    /// <summary>
    /// The bobber floats this long before a fish notices it. The client draws the line to the rod only about 2 s after
    /// the bobber lands, so an earlier bite looked like the line appearing with it.
    /// </summary>
    public const int MinInterestDelayMs = 3500;
    public const int MaxInterestDelayMs = 5000;

    /// <summary>How long the fish takes to swim to the hook.</summary>
    public const int MinApproachMs = 1200;
    public const int MaxApproachMs = 1800;

    /// <summary>How long a hooked fish waits to be reeled in before it gets away.</summary>
    public const int EscapeMs = 12000;

    /// <summary>
    /// The fish's lunge and fight: the client pulls a fish up only after it (about 1.7 s, fixed in the client).
    /// </summary>
    public const int FightMs = 1900;

    /// <summary>The catch coming up, before it is held up and its banner shown.</summary>
    public const int ReelUpMs = 1800;

    /// <summary>
    /// A catch is rolled at most this often, the quickest honest cast to catch. The fish run shows what was rolled (the
    /// chest is the catchable first fish), so without this a client could cast again and again until it saw the chest.
    /// </summary>
    public const int RollCooldownMs = MinInterestDelayMs + MinApproachMs + FightMs + ReelUpMs;

    private readonly object _lock = new();

    private readonly IFishingHost _host;
    private readonly FishingZoneDefinition _hole;
    private readonly ILogger _logger;
    private readonly Random _random;

    private long? _lastCastAt;
    private long? _lastRollAt;
    private long? _lastCastPoseAt;
    private long _nextEventAt;
    private long _hookedAt;

    public FishingSession(IFishingHost host, FishingZoneDefinition hole, ILogger logger, Random random)
    {
        _host = host;
        _hole = hole;
        _logger = logger;
        _random = random;
    }

    /// <summary>The hole's activity id, which is also its zone definition id.</summary>
    public int ActivityId => _hole.Id;

    public FishingPhase Phase { get; private set; } = FishingPhase.Idle;

    /// <summary>What the current cast will bring up, rolled when the line lands.</summary>
    public FishingCatch? Catch { get; private set; }

    /// <summary>The lure in use (its item id), until the player leaves the hole; 0 for none.</summary>
    public int ActiveLureItemId { get; private set; }

    public void SetActiveLure(int lureItemId)
    {
        lock (_lock)
            ActiveLureItemId = lureItemId;
    }

    /// <summary>
    /// The answer to the client's register request: casting range, the hole's scene, where the underwater fish swim,
    /// the water's height, and the Fish Finder.
    /// </summary>
    public FishingPacketRegisterPlayerResponse RegisterResponse(CastRange castRange)
    {
        return new FishingPacketRegisterPlayerResponse
        {
            FishingPlayerConfig = new FishingPlayerConfig
            {
                Unknown = 4,
                Unknown2 = castRange.Min,
                Unknown3 = castRange.Max,
                Unknown4 = 1,
                Unknown5 = 5,
                Unknown6 = 100,
                Unknown7 = 21,
                Unknown8 = 50,
                Unknown9 = 1,
                Unknown10 = 6.0f,
                Unknown11 = 0.444f,
                Unknown12 = 1.85f,
                Unknown13 = 0.2f,
                Unknown14 = 1,
                Unknown15 = 1,
                Unknown16 = 1
            },
            FishingZoneConfig = new FishingZoneConfig
            {
                Unknown = _hole.Name,
                Unknown3 = _hole.UnderwaterBedX,
                // The spawn stands at the water's edge, so its height is the water's: the one value played in game.
                Unknown6 = _hole.SpawnPosition.Y
            },
            // The Fish Finder follows in its own packet, as edenfps played it.
            FishModelIds = [.. PreloadModelIds]
        };
    }

    /// <summary>The Fish Finder, sent after the register answer.</summary>
    public FishingPacketFishInfoUpdate FishInfoUpdate() => new() { ClientFishEntries = FishFinderEntries() };

    /// <summary>
    /// Whether to relay the client's cast pose to the players around: once per cast at most, so a client can't make the
    /// server repeat it to everyone in view as fast as it sends it.
    /// </summary>
    public bool AllowCastPose(long now)
    {
        lock (_lock)
        {
            if (_lastCastPoseAt is long last && now - last < DuplicateCastMs)
                return false;

            _lastCastPoseAt = now;
            return true;
        }
    }

    /// <summary>
    /// The Fish Finder: the hole's fish, by name and icon. The client drops rows whose type it has listed already, so
    /// each fish's type is its item id.
    /// </summary>
    public List<ClientFishEntryInfo> FishFinderEntries()
    {
        var table = FishTables.ForHole(ActivityId);
        var entries = new List<ClientFishEntryInfo>(table.Length);

        foreach (var fish in table)
        {
            entries.Add(new ClientFishEntryInfo
            {
                Type = fish.ItemId,
                NameId = fish.NameId,
                IconId = fish.IconId,
                FishCatchable = true
            });
        }

        return entries;
    }

    /// <summary>The line lands: the bobber goes out, the catch is rolled, and the bite's timer starts.</summary>
    public void OnCast(Vector4 target, long now)
    {
        lock (_lock)
        {
            if (_lastCastAt is long lastCastAt && now - lastCastAt < DuplicateCastMs)
                return;

            // A catch on its way up is the player's; a new cast doesn't throw it back.
            if (Phase is FishingPhase.ReelPending or FishingPhase.Reeling)
                return;

            _lastCastAt = now;

            target.W = 1f;

            // The second vector is a forward direction; a zero one collapses the bobber to nothing.
            _host.SendToSelfAndVisible(new FishingPacketSpawnProxiedFishingBobber
            {
                Guid = _host.Guid,
                Unknown = BobberModelId,
                Position = target,
                Rotation = new Vector4(0f, 0f, 1f, 0f)
            });

            if (Catch is null || _lastRollAt is not long lastRollAt || now - lastRollAt >= RollCooldownMs)
            {
                Catch = FishingCatch.Roll(ActivityId, ActiveLureItemId, _random);
                _lastRollAt = now;
            }

            var fishCatch = Catch;

            var fish = new List<UnderwaterFishSpawnInfo>();
            int[] wanderers = [FishingCatch.ThinFishModelId, FishingCatch.MediumFishModelId, FishingCatch.FatFishModelId];

            if (fishCatch.WaitsOnHook)
            {
                // Already on the line: the first fish of the run, catchable, parked at the hook. Reeling brings it up.
                fish.Add(UnderwaterFish(1, fishCatch.HeldModelId, catchable: true));

                for (var index = 0; index < 3; index++)
                    fish.Add(UnderwaterFish(11 + index, wanderers[index], catchable: false));

                Phase = FishingPhase.Hooked;
                _hookedAt = now;
                _nextEventAt = long.MaxValue;
            }
            else
            {
                // The first fish wanders (so the decoy at the hook goes when the biter bites), the biter matches the
                // catch, and four more wander.
                fish.Add(UnderwaterFish(10, wanderers[0], catchable: false));
                fish.Add(UnderwaterFish(BiterFishId, fishCatch.UnderwaterModelId, catchable: false));

                for (var index = 0; index < 4; index++)
                    fish.Add(UnderwaterFish(11 + index, wanderers[index % wanderers.Length], catchable: false));

                Phase = FishingPhase.BobberOut;
                _nextEventAt = now + _random.Next(MinInterestDelayMs, MaxInterestDelayMs);
            }

            // To the caster only: the fish run uses the bobber without checking for it, so a client that hasn't
            // made the bobber (another player's, perhaps) would crash.
            _host.Send(new FishingPacketSpawnFishRun
            {
                Unknown = true,
                Unknown2 = InvisibleDecoyModelId,
                UnderwaterFishSpawns = fish
            });

            _logger.LogDebug("Fishing: {player} cast in {hole}; {catch} is {where}.",
                _host.Guid, _hole.Name, fishCatch.Name, fishCatch.WaitsOnHook ? "on the hook" : "on its way");
        }
    }

    /// <summary>The player reels in: a catch if the fish is hooked, nothing if it hadn't bitten yet.</summary>
    public void OnReel(long now)
    {
        lock (_lock)
        {
            switch (Phase)
            {
                case FishingPhase.Hooked:
                    // The pull-up only animates once the fight is over, so a quick reel waits for it.
                    Phase = FishingPhase.ReelPending;
                    _nextEventAt = Math.Max(now, _hookedAt + FightMs);
                    break;

                case FishingPhase.BobberOut:
                case FishingPhase.Nibbling:
                    // Too soon: the fish swims off.
                    SendBite(hooked: false, interested: false);
                    SendNothingCaught();
                    Phase = FishingPhase.Idle;
                    break;
            }
        }
    }

    /// <summary>Moves the bite along. Called often by the Gateway.</summary>
    public void Update(long now)
    {
        lock (_lock)
        {
            if (Phase == FishingPhase.Idle || now < _nextEventAt)
                return;

            switch (Phase)
            {
                case FishingPhase.BobberOut:
                    SendBite(hooked: false, interested: true);
                    Phase = FishingPhase.Nibbling;
                    _nextEventAt = now + _random.Next(MinApproachMs, MaxApproachMs);
                    break;

                case FishingPhase.Nibbling:
                    SendBite(hooked: true, interested: false);
                    Phase = FishingPhase.Hooked;
                    _hookedAt = now;
                    _nextEventAt = now + EscapeMs;
                    break;

                case FishingPhase.Hooked:
                    // Not reeled in in time: it gets away.
                    SendBite(hooked: false, interested: false);
                    SendNothingCaught();
                    Phase = FishingPhase.Idle;
                    break;

                case FishingPhase.ReelPending:
                    SendReelUp();
                    Phase = FishingPhase.Reeling;
                    _nextEventAt = now + ReelUpMs;
                    break;

                case FishingPhase.Reeling:
                    if (Land())
                        Phase = FishingPhase.Idle;
                    break;
            }
        }
    }

    /// <summary>
    /// The catch reaches the rod: granted first (the bag fills as the reel ends), then held up. False to try again on
    /// the next tick.
    /// </summary>
    private bool Land()
    {
        var fishCatch = Catch!;

        FishingGrantResult result;

        try
        {
            result = _host.Grant(fishCatch.ItemId);
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "Fishing: granting {item} ({name}) to {player} failed.", fishCatch.ItemId, fishCatch.Name, _host.Guid);
            result = FishingGrantResult.Failed;
        }

        if (result == FishingGrantResult.Busy)
            return false;

        if (result == FishingGrantResult.Failed)
        {
            // Don't show a catch the player didn't get.
            _logger.LogError("Fishing: could not grant {item} ({name}) to {player}.", fishCatch.ItemId, fishCatch.Name, _host.Guid);
            SendNothingCaught();
            return true;
        }

        _logger.LogInformation("Fishing: {player} caught {name} (item {item}) in {hole}.",
            _host.Guid, fishCatch.Name, fishCatch.ItemId, _hole.Name);

        _host.SendToSelfAndVisible(new FishingPacketFishingResult
        {
            Guid = _host.Guid,
            ResultType = FishingPacketFishingResult.ResultScoredCatch,
            Caught = fishCatch.IsItem,
            FishId = fishCatch.NameId,
            Unknown1 = fishCatch.IconId,
            Unknown2 = fishCatch.Weight,
            Unknown3 = fishCatch.Size,
            Unknown4 = fishCatch.Weight,
            Unknown5 = fishCatch.Difficulty,
            Unknown6 = fishCatch.Rarity,
            Unknown7 = fishCatch.Score,
            Unknown8 = fishCatch.HeldModelId,
            Unknown9 = fishCatch.EffectId,
            Unknown12 = fishCatch.Size
        });

        return true;
    }

    /// <summary>The biting fish: interested, hooked, or (neither) gone. It also becomes the fish the reel pulls up.</summary>
    private void SendBite(bool hooked, bool interested)
    {
        _host.SendToSelfAndVisible(new FishingPacketUpdateProxiedFishingBobber
        {
            Guid = _host.Guid,
            Unknown = BiterFishId,
            Flag1 = hooked,
            Flag2 = interested
        });
    }

    private void SendReelUp()
    {
        var fishCatch = Catch!;

        _host.SendToSelfAndVisible(new FishingPacketFishingResult
        {
            Guid = _host.Guid,
            ResultType = FishingPacketFishingResult.ResultReelUp,
            Caught = fishCatch.IsItem,
            FishId = fishCatch.NameId,
            Unknown8 = fishCatch.HeldModelId,
            Unknown12 = fishCatch.Size
        });
    }

    private void SendNothingCaught()
    {
        _host.SendToSelfAndVisible(new FishingPacketFishingResult
        {
            Guid = _host.Guid,
            ResultType = FishingPacketFishingResult.ResultNothingCaught
        });
    }

    /// <summary>
    /// An underwater fish. Its speeds and pauses are floats the client reads every frame; the catchable one still
    /// needs them, or it jumps to the rod instead of gliding up.
    /// </summary>
    private static UnderwaterFishSpawnInfo UnderwaterFish(int id, int modelId, bool catchable) => new()
    {
        Unknown = id,
        ModelId = modelId,
        Unknown5 = 1,
        Unknown7 = catchable,
        Unknown8 = 1.5f,
        Unknown9 = 2.0f,
        Unknown10 = 1.5f,
        Unknown11 = 1.0f,
        Unknown12 = 1.0f,
        Unknown13 = 2.5f,
        Unknown14 = 1.5f,
        Unknown15 = 0.5f,
        Unknown16 = 2.5f,
        Unknown17 = 0.3f,
        Unknown18 = 1.2f
    };
}
