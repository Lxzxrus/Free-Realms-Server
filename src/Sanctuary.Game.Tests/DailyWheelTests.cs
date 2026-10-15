using System;
using System.IO;
using System.Linq;
using System.Text;

using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.VisualStudio.TestTools.UnitTesting;

using Sanctuary.Core.IO;
using Sanctuary.Game.Minigames;
using Sanctuary.Packet;

namespace Sanctuary.Game.Tests;

[TestClass]
public sealed class DailyWheelTests
{
    private static readonly DateTimeOffset Noon = new(2026, 10, 13, 12, 0, 0, TimeSpan.Zero);

    private static DailyWheel TenSlots() => new()
    {
        Slots = Enumerable.Range(1, 10).Select(category => new DailyWheelSlot { Category = category, Coins = category * 10, Weight = 1 }).ToList()
    };

    private static string FindShippedWheel()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            var candidate = Path.Combine(directory.FullName, "Resources", "DailyWheel.json");
            if (File.Exists(candidate))
                return candidate;
        }

        throw new FileNotFoundException("src/Resources/DailyWheel.json not found above the test output.");
    }

    [TestMethod]
    public void OneSpinPerUtcDay()
    {
        Assert.IsTrue(DailyWheel.CanSpin(null, Noon), "Never spun.");
        Assert.IsFalse(DailyWheel.CanSpin(Noon.AddHours(-11), Noon), "Spun earlier the same UTC day.");
        Assert.IsTrue(DailyWheel.CanSpin(Noon.AddHours(-12).AddSeconds(-1), Noon), "Spun before midnight.");

        // A player west of UTC spinning late in their evening is already on the next UTC day.
        var lateEvening = new DateTimeOffset(2026, 10, 13, 20, 0, 0, TimeSpan.FromHours(-5));
        Assert.IsTrue(DailyWheel.CanSpin(Noon, lateEvening));
    }

    [TestMethod]
    public void NextSpin_IsAtUtcMidnight()
    {
        Assert.AreEqual(TimeSpan.Zero, DailyWheel.UntilNextSpin(null, Noon));
        Assert.AreEqual(TimeSpan.FromHours(12), DailyWheel.UntilNextSpin(Noon.AddMinutes(-5), Noon));
    }

    [TestMethod]
    public void Roll_FollowsWeights_AndNeverPicksAZeroWeightSlot()
    {
        var wheel = TenSlots();
        wheel.Slots[0].Weight = 0;
        wheel.Slots[9].Weight = 9;

        var random = new Random(1234);
        var counts = new int[10];
        for (var i = 0; i < 18_000; i++)
            counts[wheel.Roll(random)]++;

        Assert.AreEqual(0, counts[0]);
        Assert.IsTrue(counts[9] > counts[1] * 6, $"Weight 9 against weight 1: {counts[9]} vs {counts[1]}.");
    }

    [TestMethod]
    public void Problems_CatchABrokenWheel()
    {
        Assert.AreEqual(0, TenSlots().Problems().Count());

        var short9 = TenSlots();
        short9.Slots.RemoveAt(0);
        Assert.AreEqual(1, short9.Problems().Count(), "Type 1 has ten wedges.");

        var noWeight = TenSlots();
        noWeight.Slots.ForEach(slot => slot.Weight = 0);
        Assert.AreEqual(1, noWeight.Problems().Count());

        var badPicture = TenSlots();
        badPicture.Slots[0].Category = 26;
        Assert.AreEqual(1, badPicture.Problems().Count(), "The Flash has 25 pictures.");
    }

    [TestMethod]
    public void ShippedWheel_Loads()
    {
        Assert.IsTrue(DailyWheel.TryLoad(FindShippedWheel(), NullLogger.Instance, out var wheel));
        Assert.AreEqual(10, wheel.Slots.Count);
        Assert.AreEqual("1 2 3 4 5 6 7 8 9 10 ", wheel.SlotList(), "SOE's test server sends the list with a trailing space.");
    }

    [TestMethod]
    public void SpinsPacket_MatchesTheClientsReader()
    {
        // FreeRealms.exe 0xadbf00/0xadae70/0xadafa0: short 143, byte 1, five ints, two strings, an int.
        var packet = DailyWheelSession.SpinsPacket(TenSlots(), null, Noon);

        using var expected = new PacketWriter();
        expected.Write((short)143);
        expected.Write((byte)1);
        expected.Write(1);  // wheel id
        expected.Write(1);  // spins left
        expected.Write(1);  // consecutive days
        expected.Write(0);  // seconds until next spin
        expected.Write(0);
        expected.Write("wheel");
        expected.Write("wheel");
        expected.Write(0);

        CollectionAssert.AreEqual(expected.Buffer, packet.Serialize());

        var spent = DailyWheelSession.SpinsPacket(TenSlots(), Noon, Noon);
        Assert.AreEqual(0, spent.Count);
        Assert.AreEqual(12 * 3600, spent.SecondsUntilNext);
    }

    [TestMethod]
    public void SwitchedOff_OffersNoSpin()
    {
        var wheel = TenSlots();
        wheel.Enabled = false;

        Assert.AreEqual(0, DailyWheelSession.SpinsPacket(wheel, null, Noon).Count);
    }

    [TestMethod]
    public void Session_PaysOneSpin()
    {
        var session = new DailyWheelSession();

        Assert.IsTrue(session.TryClaimSpin(3));
        Assert.IsFalse(session.TryClaimSpin(4));
        Assert.AreEqual(3, session.PrizeSlot);

        session.ReleaseSpin();
        Assert.IsTrue(session.TryClaimSpin(5), "A spin the database refused can be tried again.");
    }

    [TestMethod]
    public void Messages_AreTabSeparatedAndNulTerminated()
    {
        var session = new DailyWheelSession { StateId = 7 };

        var packet = session.Message("OnSpinInfoMsg", 4);

        Assert.AreEqual(7, packet.StateId);
        Assert.AreEqual("OnSpinInfoMsg\t4\0", Encoding.UTF8.GetString(packet.Payload));
        CollectionAssert.AreEqual(new[] { "OnWheelStopMsg", "1" }, DailyWheelSession.Parse(Encoding.UTF8.GetBytes("OnWheelStopMsg\t1\0")));
    }

    [TestMethod]
    public void MiniGameInfo_WritesAnIntAfterEachRewardBundle()
    {
        // Every reward bundle on the wire is followed by an int; EDITz's start screen parsed with that layout.
        using var writer = new PacketWriter();
        new MiniGameInfo().Serialize(writer);

        using var bundle = new PacketWriter();
        new Sanctuary.Packet.Common.RewardBundleBase { Success = false }.Serialize(bundle);

        // Before the bundles: six ints and a bool. After them: the objectives count, five bools, the swf string
        // (empty: a zero length), an int, a bool, an int, four bools and an int.
        const int before = 6 * 4 + 1;
        const int after = 4 + 5 + 4 + 4 + 1 + 4 + 4 + 4;

        Assert.AreEqual(before + 3 * (bundle.Buffer.Length + 4) + after, writer.Buffer.Length);
    }
}
