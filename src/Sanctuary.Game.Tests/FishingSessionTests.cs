using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;

using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.VisualStudio.TestTools.UnitTesting;

using Sanctuary.Core.IO;
using Sanctuary.Game.Minigames.Fishing;
using Sanctuary.Game.Resources.Definitions.Zones;
using Sanctuary.Packet;

namespace Sanctuary.Game.Tests;

/// <summary>
/// The catch rules of fishing (task 33, from upstream PR #31): what a cast brings up, and the bite, reel and catch
/// timeline that decides whether the player gets it.
/// </summary>
[TestClass]
public sealed class FishingSessionTests
{
    private const ulong PlayerGuid = 0x1234;
    private static readonly Vector4 Target = new(449f, -65f, 365f, 0f);

    private static FishingZoneDefinition SacredGroveShallows() => new()
    {
        Id = 563,
        Name = "sg_fishing_medpond",
        SpawnPosition = new Vector4(435.05676f, -64.46508f, 370.70682f, 1f),
        UnderwaterBedX = 68f
    };

    /// <summary>Records what the session sends and grants. Grants answer from <see cref="Results"/>, then Granted.</summary>
    private sealed class FakeHost : IFishingHost
    {
        public ulong Guid => PlayerGuid;

        public List<(ISerializablePacket Packet, bool ToVisible)> Sent { get; } = [];
        public Queue<FishingGrantResult> Results { get; } = new();
        public List<int> GrantAttempts { get; } = [];
        public bool Throw { get; set; }

        public void Send(ISerializablePacket packet) => Sent.Add((packet, false));

        public void SendToSelfAndVisible(ISerializablePacket packet) => Sent.Add((packet, true));

        public FishingGrantResult Grant(int itemDefinitionId)
        {
            GrantAttempts.Add(itemDefinitionId);

            if (Throw)
                throw new InvalidOperationException("The database is gone.");

            return Results.Count > 0 ? Results.Dequeue() : FishingGrantResult.Granted;
        }

        public IEnumerable<T> All<T>() => Sent.Select(sent => sent.Packet).OfType<T>();

        public IEnumerable<FishingPacketFishingResult> ResultsOf(int resultType) =>
            All<FishingPacketFishingResult>().Where(result => result.ResultType == resultType);
    }

    /// <summary>Answers NextDouble from a script (repeating the last value), and every range with its minimum.</summary>
    private sealed class ScriptedRandom(params double[] values) : Random
    {
        private int _next;

        public override double NextDouble() => values[Math.Min(_next++, values.Length - 1)];

        public override int Next(int minValue, int maxValue) => minValue;
    }

    /// <summary>0.5 is never treasure or junk (they are the first 15%), and lands on one of the hole's fish.</summary>
    private static ScriptedRandom AFish() => new(0.5);

    /// <summary>0 is treasure or junk, then 0 again picks the first of them, the treasure chest.</summary>
    private static ScriptedRandom TheChest() => new(0.0, 0.0, 0.5);

    /// <summary>Treasure or junk, then the last of them: the stick of dynamite, which bites like a fish.</summary>
    private static ScriptedRandom Junk() => new(0.0, 0.999, 0.5);

    private static FishingSession Session(FakeHost host, Random random) =>
        new(host, SacredGroveShallows(), NullLogger.Instance, random);

    /// <summary>Casts at 1000 and runs the timeline to the bite; returns when the fish was hooked.</summary>
    private static long CastAndWaitForTheBite(FishingSession session)
    {
        session.OnCast(Target, 1000);

        var now = 1000L;
        while (session.Phase != FishingPhase.Hooked)
        {
            now += 50;
            session.Update(now);
            Assert.IsTrue(now < 100_000, "The fish never bit.");
        }

        return now;
    }

    [TestMethod]
    public void Cast_SendsTheBobberToEveryone_AndTheFishOnlyToTheCaster()
    {
        var host = new FakeHost();
        var session = Session(host, AFish());

        session.OnCast(Target, 1000);

        Assert.AreEqual(2, host.Sent.Count);

        // The bobber first: the fish run uses it.
        var (bobberPacket, bobberToVisible) = host.Sent[0];
        var bobber = (FishingPacketSpawnProxiedFishingBobber)bobberPacket;
        Assert.IsTrue(bobberToVisible);
        Assert.AreEqual(PlayerGuid, bobber.Guid);
        Assert.AreEqual(FishingSession.BobberModelId, bobber.Unknown, "With model 0 the client makes no bobber, then crashes.");
        Assert.AreEqual(new Vector4(Target.X, Target.Y, Target.Z, 1f), bobber.Position);
        Assert.AreEqual(new Vector4(0f, 0f, 1f, 0f), bobber.Rotation, "A direction, not a quaternion: (0,0,0,1) hides the bobber.");

        var (runPacket, runToVisible) = host.Sent[1];
        var run = (FishingPacketSpawnFishRun)runPacket;
        Assert.IsFalse(runToVisible, "Another player's client may have no bobber, and would crash on the fish run.");
        Assert.AreEqual(FishingSession.HookLureModelId, run.Unknown2, "0 crashes the client.");

        Assert.IsFalse(run.UnderwaterFishSpawns[0].Unknown7, "A fish that bites: the first fish wanders.");
        var biter = run.UnderwaterFishSpawns.Single(fish => fish.Unknown == FishingSession.BiterFishId);
        Assert.IsFalse(biter.Unknown7, "Only fish that aren't catchable swim in and bite.");
        Assert.AreEqual(session.Catch!.UnderwaterModelId, biter.ModelId);
        Assert.IsTrue(run.UnderwaterFishSpawns.All(fish => fish.Unknown14 > 0f), "Speeds of 0 freeze the fish.");

        Assert.AreEqual(FishingPhase.BobberOut, session.Phase);
    }

    [TestMethod]
    public void ReelingInAHookedFish_CatchesIt_Once()
    {
        var host = new FakeHost();
        var session = Session(host, AFish());

        var hookedAt = CastAndWaitForTheBite(session);

        var bites = host.All<FishingPacketUpdateProxiedFishingBobber>().ToList();
        Assert.AreEqual(2, bites.Count);
        Assert.IsTrue(!bites[0].Flag1 && bites[0].Flag2, "First the fish gets interested...");
        Assert.IsTrue(bites[1].Flag1 && !bites[1].Flag2, "...then it bites.");
        Assert.IsTrue(bites.All(bite => bite.Unknown == FishingSession.BiterFishId && bite.Guid == PlayerGuid));

        session.OnReel(hookedAt + 100);
        Assert.AreEqual(FishingPhase.ReelPending, session.Phase);

        // The pull-up waits for the fight to end.
        session.Update(hookedAt + FishingSession.FightMs - 1);
        Assert.AreEqual(0, host.ResultsOf(FishingPacketFishingResult.ResultReelUp).Count());

        session.Update(hookedAt + FishingSession.FightMs);
        Assert.AreEqual(1, host.ResultsOf(FishingPacketFishingResult.ResultReelUp).Count());
        Assert.AreEqual(0, host.GrantAttempts.Count, "Not granted before the catch reaches the rod.");

        var landedAt = hookedAt + FishingSession.FightMs + FishingSession.ReelUpMs;
        session.Update(landedAt);

        var fishCatch = session.Catch!;
        CollectionAssert.AreEqual(new[] { fishCatch.ItemId }, host.GrantAttempts);

        var banner = host.ResultsOf(FishingPacketFishingResult.ResultScoredCatch).Single();
        Assert.AreEqual(PlayerGuid, banner.Guid);
        Assert.IsFalse(banner.Caught, "A fish: held up at its size, with size and weight.");
        Assert.AreEqual(fishCatch.NameId, banner.FishId);
        Assert.IsTrue(banner.Unknown8 > 0, "Without a model to hold up there is no banner.");
        Assert.AreEqual(fishCatch.Size, banner.Unknown3);
        Assert.AreEqual(fishCatch.Size, banner.Unknown12);
        Assert.AreEqual(FishingPhase.Idle, session.Phase);

        // Nothing more, however long the session runs on.
        for (var now = landedAt; now < landedAt + 60_000; now += 50)
            session.Update(now);

        Assert.AreEqual(1, host.GrantAttempts.Count);
        Assert.AreEqual(1, host.ResultsOf(FishingPacketFishingResult.ResultScoredCatch).Count());
    }

    [TestMethod]
    public void ReelingInBeforeTheBite_ScaresTheFishOff()
    {
        var host = new FakeHost();
        var session = Session(host, AFish());

        session.OnCast(Target, 1000);
        session.OnReel(1100);

        var escape = host.All<FishingPacketUpdateProxiedFishingBobber>().Single();
        Assert.IsFalse(escape.Flag1 || escape.Flag2, "Neither flag: the fish swims off.");
        Assert.AreEqual(1, host.ResultsOf(FishingPacketFishingResult.ResultNothingCaught).Count());
        Assert.AreEqual(FishingPhase.Idle, session.Phase);

        for (var now = 1100L; now < 30_000; now += 50)
            session.Update(now);

        Assert.AreEqual(0, host.GrantAttempts.Count);
        Assert.AreEqual(0, host.ResultsOf(FishingPacketFishingResult.ResultScoredCatch).Count());
    }

    [TestMethod]
    public void AHookedFishNotReeledIn_GetsAway()
    {
        var host = new FakeHost();
        var session = Session(host, AFish());

        var hookedAt = CastAndWaitForTheBite(session);

        session.Update(hookedAt + FishingSession.EscapeMs - 1);
        Assert.AreEqual(FishingPhase.Hooked, session.Phase);

        session.Update(hookedAt + FishingSession.EscapeMs);
        Assert.AreEqual(FishingPhase.Idle, session.Phase);
        Assert.AreEqual(1, host.ResultsOf(FishingPacketFishingResult.ResultNothingCaught).Count());

        session.OnReel(hookedAt + FishingSession.EscapeMs + 100);
        Assert.AreEqual(0, host.GrantAttempts.Count, "Too late.");
    }

    [TestMethod]
    public void TheClientsRepeatedCast_IsOneCast()
    {
        var host = new FakeHost();
        var session = Session(host, AFish());

        session.OnCast(Target, 1000);
        session.OnCast(Target, 1000 + FishingSession.DuplicateCastMs - 1);
        Assert.AreEqual(1, host.All<FishingPacketSpawnProxiedFishingBobber>().Count());

        // A real second cast replaces the first.
        session.OnCast(Target, 1000 + FishingSession.DuplicateCastMs);
        Assert.AreEqual(2, host.All<FishingPacketSpawnProxiedFishingBobber>().Count());
        Assert.AreEqual(FishingPhase.BobberOut, session.Phase);
    }

    [TestMethod]
    public void CastingAgainAndAgain_DoesNotRollAgain_UntilAnHonestCatchCouldHaveLanded()
    {
        var host = new FakeHost();
        var session = Session(host, new Random(5));

        session.OnCast(Target, 1000);
        var first = session.Catch;

        // Cast, see what's coming, reel in to scare it off, cast again: still the same catch.
        for (var now = 1000L + FishingSession.DuplicateCastMs; now < 1000 + FishingSession.RollCooldownMs; now += FishingSession.DuplicateCastMs)
        {
            session.OnReel(now);
            session.OnCast(Target, now + 1);
            Assert.AreSame(first, session.Catch);
        }

        var later = 1000 + FishingSession.RollCooldownMs + FishingSession.DuplicateCastMs;
        session.OnReel(later);
        session.OnCast(Target, later);
        Assert.AreNotSame(first, session.Catch, "A new roll once an honest cast could have landed.");
    }

    [TestMethod]
    public void CastingWhileTheCatchComesUp_DoesNotLoseIt()
    {
        var host = new FakeHost();
        var session = Session(host, AFish());

        var hookedAt = CastAndWaitForTheBite(session);
        var fishCatch = session.Catch;

        session.OnReel(hookedAt);
        session.OnCast(Target, hookedAt + 1000);

        Assert.AreEqual(1, host.All<FishingPacketSpawnProxiedFishingBobber>().Count());
        Assert.AreSame(fishCatch, session.Catch);

        for (var now = hookedAt; now < hookedAt + 10_000; now += 50)
            session.Update(now);

        CollectionAssert.AreEqual(new[] { fishCatch!.ItemId }, host.GrantAttempts);
    }

    [TestMethod]
    public void TheTreasureChest_WaitsOnTheHook()
    {
        var host = new FakeHost();
        var session = Session(host, TheChest());

        session.OnCast(Target, 1000);

        Assert.AreEqual("Treasure Chest", session.Catch!.Name);
        Assert.AreEqual(FishingPhase.Hooked, session.Phase, "No bite: it is on the line already.");

        var run = host.All<FishingPacketSpawnFishRun>().Single();
        Assert.IsTrue(run.UnderwaterFishSpawns[0].Unknown7, "The chest is the catchable first fish, parked at the hook.");
        Assert.AreEqual(FishTables.TreasureChestModelId, run.UnderwaterFishSpawns[0].ModelId);
        Assert.IsTrue(run.UnderwaterFishSpawns[0].Unknown8 > 0f, "Without speeds the chest jumps to the rod.");

        // It doesn't get away, however long it waits.
        session.Update(1000 + 10 * FishingSession.EscapeMs);
        Assert.AreEqual(FishingPhase.Hooked, session.Phase);
        Assert.AreEqual(0, host.All<FishingPacketUpdateProxiedFishingBobber>().Count());

        session.OnReel(2_000_000);
        for (var now = 2_000_000L; now < 2_010_000; now += 50)
            session.Update(now);

        CollectionAssert.AreEqual(new[] { 3016 }, host.GrantAttempts);

        var banner = host.ResultsOf(FishingPacketFishingResult.ResultScoredCatch).Single();
        Assert.IsTrue(banner.Caught, "An item: held up as it is, with its name only.");
        Assert.AreEqual(FishTables.TreasureChestModelId, banner.Unknown8);
        Assert.AreEqual(FishingCatch.ChestEffectId, banner.Unknown9);
    }

    [TestMethod]
    public void Junk_BitesLikeAFish_AndComesUpAsAnItem()
    {
        var host = new FakeHost();
        var session = Session(host, Junk());

        var hookedAt = CastAndWaitForTheBite(session);
        Assert.AreEqual("Soggy Stick of Dynamite", session.Catch!.Name);

        session.OnReel(hookedAt);
        for (var now = hookedAt; now < hookedAt + 10_000; now += 50)
            session.Update(now);

        CollectionAssert.AreEqual(new[] { 68002 }, host.GrantAttempts);
        Assert.IsTrue(host.ResultsOf(FishingPacketFishingResult.ResultScoredCatch).Single().Caught);
    }

    [TestMethod]
    public void ACatchWaitsForATradeBeingSaved()
    {
        var host = new FakeHost();
        host.Results.Enqueue(FishingGrantResult.Busy);
        host.Results.Enqueue(FishingGrantResult.Busy);

        var session = Session(host, AFish());

        var hookedAt = CastAndWaitForTheBite(session);
        session.OnReel(hookedAt);

        var landedAt = hookedAt + FishingSession.FightMs + FishingSession.ReelUpMs;
        session.Update(hookedAt + FishingSession.FightMs);
        session.Update(landedAt);
        session.Update(landedAt + 50);

        Assert.AreEqual(FishingPhase.Reeling, session.Phase);
        Assert.AreEqual(0, host.ResultsOf(FishingPacketFishingResult.ResultScoredCatch).Count());

        session.Update(landedAt + 100);

        Assert.AreEqual(3, host.GrantAttempts.Count);
        Assert.AreEqual(1, host.ResultsOf(FishingPacketFishingResult.ResultScoredCatch).Count());
        Assert.AreEqual(FishingPhase.Idle, session.Phase);
    }

    [TestMethod]
    public void ACatchThatCouldNotBeGranted_IsNotShown()
    {
        foreach (var fails in new Action<FakeHost>[] { host => host.Results.Enqueue(FishingGrantResult.Failed), host => host.Throw = true })
        {
            var host = new FakeHost();
            fails(host);

            var session = Session(host, AFish());

            var hookedAt = CastAndWaitForTheBite(session);
            session.OnReel(hookedAt);

            for (var now = hookedAt; now < hookedAt + 10_000; now += 50)
                session.Update(now);

            Assert.AreEqual(1, host.GrantAttempts.Count, "Tried once, not again and again.");
            Assert.AreEqual(0, host.ResultsOf(FishingPacketFishingResult.ResultScoredCatch).Count());
            Assert.AreEqual(1, host.ResultsOf(FishingPacketFishingResult.ResultNothingCaught).Count());
            Assert.AreEqual(FishingPhase.Idle, session.Phase);
        }
    }

    [TestMethod]
    public void RegisterResponse_SendsTheWaterLevel_NotTheSpawnHeight()
    {
        // Frostbitten Banks: the spawn is on a path 7 above the water, and the bobber must land on the water.
        var hole = SacredGroveShallows();
        hole.SpawnPosition = new Vector4(538.99f, 30.28f, 578.01f, 1f);
        hole.WaterLevel = 23f;
        var session = new FishingSession(new FakeHost(), hole, NullLogger.Instance, AFish());

        var response = session.RegisterResponse(new CastRange("Metal Fishing Rod", 3f, 25f));

        Assert.AreEqual(23f, response.FishingZoneConfig.Unknown6);
    }

    [TestMethod]
    public void RegisterResponse_DescribesTheHole()
    {
        var session = Session(new FakeHost(), AFish());

        var response = session.RegisterResponse(new CastRange("Metal Fishing Rod", 3f, 25f));

        Assert.AreEqual(3f, response.FishingPlayerConfig.Unknown2);
        Assert.AreEqual(25f, response.FishingPlayerConfig.Unknown3);
        Assert.AreEqual(6.0f, response.FishingPlayerConfig.Unknown10, "The fishing camera's distance.");

        Assert.AreEqual("sg_fishing_medpond", response.FishingZoneConfig.Unknown);
        Assert.AreEqual(68f, response.FishingZoneConfig.Unknown3, "The underwater bed's X, or the fish swim in the sky.");
        Assert.AreEqual(-64.46508f, response.FishingZoneConfig.Unknown6, "No WaterLevel: the spawn's height.");

        CollectionAssert.AreEqual(FishingSession.PreloadModelIds, response.FishModelIds);

        Assert.AreEqual(0, response.ClientFishEntries.Count, "The Fish Finder comes in its own packet, as edenfps sent it.");

        var finder = session.FishInfoUpdate().ClientFishEntries;
        Assert.AreEqual(FishTables.SacredGroveShallows.Length, finder.Count);
        Assert.AreEqual(finder.Count, finder.Select(entry => entry.Type).Distinct().Count(), "The client drops rows with a type it has listed.");
        Assert.IsTrue(finder.All(entry => entry.FishCatchable && !entry.Unknown4));
    }

    [TestMethod]
    public void Roll_GivesOnlyTheHolesFish_OrTreasureAndJunk_AboutOneCastInSeven()
    {
        foreach (var (activityId, table) in FishTables.ByActivity)
        {
            var random = new Random(activityId);
            var fishNames = table.Select(fish => fish.Name).ToHashSet();
            var misc = FishTables.MiscCatches.Select(miscCatch => miscCatch.Name).ToHashSet();
            var caught = new HashSet<string>();
            var miscCount = 0;

            const int casts = 20_000;
            for (var cast = 0; cast < casts; cast++)
            {
                var fishCatch = FishingCatch.Roll(activityId, 0, random);

                if (fishCatch.IsItem)
                {
                    Assert.IsTrue(misc.Contains(fishCatch.Name), fishCatch.Name);
                    miscCount++;
                }
                else
                {
                    Assert.IsTrue(fishNames.Contains(fishCatch.Name), $"{fishCatch.Name} isn't a fish of hole {activityId}.");
                    caught.Add(fishCatch.Name);
                }
            }

            CollectionAssert.AreEquivalent(fishNames.ToArray(), caught.ToArray(), $"Hole {activityId}: every fish turns up.");
            Assert.AreEqual(FishTables.MiscCatchChance, miscCount / (double)casts, 0.01);
        }
    }

    [TestMethod]
    public void Roll_MakesHighLevelFishRarer()
    {
        var random = new Random(7);
        var counts = new Dictionary<string, int>();

        for (var cast = 0; cast < 50_000; cast++)
        {
            var fishCatch = FishingCatch.Roll(563, 0, random);
            counts[fishCatch.Name] = counts.GetValueOrDefault(fishCatch.Name) + 1;
        }

        // Slugmud Skipper is level 1, Baconfish level 20.
        Assert.IsTrue(counts["Slugmud Skipper"] > 3 * counts["Baconfish"], $"{counts["Slugmud Skipper"]} against {counts["Baconfish"]}.");
    }

    [TestMethod]
    public void ALure_MakesItsFishLikelier()
    {
        const int skipperSeeker = 68160; // Frozen Char, Slugmud Skipper, Old Sole: only the skipper lives at 563.

        double Share(int lure)
        {
            var random = new Random(11);
            var skippers = 0;

            for (var cast = 0; cast < 50_000; cast++)
            {
                if (FishingCatch.Roll(563, lure, random).Name == "Slugmud Skipper")
                    skippers++;
            }

            return skippers / 50_000.0;
        }

        // About 0.17 of casts without it, about 0.23 with it.
        Assert.IsTrue(Share(skipperSeeker) > Share(0) + 0.04);
    }

    [TestMethod]
    public void TheTreasureMagnet_BringsUpMoreChests()
    {
        double ChestShare(int lure)
        {
            var random = new Random(13);
            var chests = 0;

            for (var cast = 0; cast < 50_000; cast++)
            {
                if (FishingCatch.Roll(565, lure, random).ItemId == 3016)
                    chests++;
            }

            return chests / 50_000.0;
        }

        // About 0.02 without it (Nate's choice: one cast in 50) and 0.095 with it.
        Assert.AreEqual(0.02, ChestShare(0), 0.003);
        Assert.IsTrue(ChestShare(FishingGear.TreasureMagnetItemId) > 2 * ChestShare(0));
    }

    [TestMethod]
    public void MiscCatchWeights_AreTheirPerCentOfAllCasts()
    {
        Assert.AreEqual(FishTables.MiscCatchChance * 100, FishTables.MiscCatches.Sum(miscCatch => miscCatch.Weight), 0.001);
    }

    [TestMethod]
    public void Rods_CastFurther_NeverShorter()
    {
        Assert.AreEqual(FishingGear.DefaultCastRange, FishingGear.CastRangeFor(0));
        Assert.AreEqual(20f, FishingGear.CastRangeFor(68185).Max);  // Simple Bamboo
        Assert.AreEqual(20f, FishingGear.CastRangeFor(76688).Max);  // Golden Reel's extra id
        Assert.AreEqual(25f, FishingGear.CastRangeFor(68289).Max);  // Metal, last tint
        Assert.AreEqual(25f, FishingGear.CastRangeFor(68290).Max);  // Red Scoped, first tint
        Assert.AreEqual(32f, FishingGear.CastRangeFor(76691).Max);  // Golden Scoped

        for (var id = 68185; id <= 68359; id++)
        {
            Assert.IsTrue(FishingGear.IsRod(id));
            Assert.IsTrue(FishingGear.CastRangeFor(id).Max >= FishingGear.DefaultCastRange.Max);
            Assert.AreEqual(FishingGear.DefaultCastRange.Min, FishingGear.CastRangeFor(id).Min);
        }

        Assert.IsFalse(FishingGear.IsRod(68184));
        Assert.IsFalse(FishingGear.IsRod(68360));
    }
}
