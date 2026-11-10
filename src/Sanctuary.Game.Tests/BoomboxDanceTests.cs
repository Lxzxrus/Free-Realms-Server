using System;
using System.Collections.Generic;

using Microsoft.VisualStudio.TestTools.UnitTesting;

using Sanctuary.Game.Helpers;
using Sanctuary.Game.Resources.Definitions;

namespace Sanctuary.Game.Tests;

[TestClass]
public sealed class BoomboxDanceTests
{
    [TestMethod]
    public void Playback_Advance_CyclesThroughSequence()
    {
        var playback = new BoomboxDancePlayback(new[] { 11, 12, 13 }, new[] { 1000, 2000, 3000 });

        Assert.AreEqual(11, playback.Advance(0));
        Assert.AreEqual(1000, playback.NextClipAt);

        Assert.AreEqual(0, playback.Advance(999));

        Assert.AreEqual(12, playback.Advance(1000));
        Assert.AreEqual(3000, playback.NextClipAt);

        Assert.AreEqual(13, playback.Advance(3000));
        Assert.AreEqual(6000, playback.NextClipAt);

        Assert.AreEqual(11, playback.Advance(6000));
    }

    [TestMethod]
    public void Playback_Blend_AdjustsNextClipAt()
    {
        var playback = new BoomboxDancePlayback(new[] { 1 }, new[] { 1000 }, blendMs: 200);
        playback.Advance(0);
        Assert.AreEqual(800, playback.NextClipAt);

        var playbackClamped = new BoomboxDancePlayback(new[] { 1 }, new[] { 1000 }, blendMs: 5000);
        playbackClamped.Advance(0);
        Assert.AreEqual(1, playbackClamped.NextClipAt);

        var playbackNegative = new BoomboxDancePlayback(new[] { 1 }, new[] { 1000 }, blendMs: -100);
        playbackNegative.Advance(0);
        Assert.AreEqual(1000, playbackNegative.NextClipAt);
    }

    [TestMethod]
    public void Playback_EmptyOrMismatchedLengths_ReturnZero()
    {
        var empty = new BoomboxDancePlayback(new int[0], new int[0]);
        Assert.AreEqual(0, empty.Advance(0));

        var mismatched = new BoomboxDancePlayback(new[] { 1, 2 }, new[] { 1000 });
        Assert.AreEqual(0, mismatched.Advance(0));
    }

    [TestMethod]
    public void Playback_ZeroDuration_ReturnsZeroAndDoesNotAdvance()
    {
        var playback = new BoomboxDancePlayback(new[] { 1, 2 }, new[] { 0, 1000 });

        Assert.AreEqual(0, playback.Advance(0));
        Assert.AreEqual(0, playback.Advance(1000));
    }

    [TestMethod]
    public void Playback_Resume_ResetsNextClipAt()
    {
        var playback = new BoomboxDancePlayback(new[] { 1, 2 }, new[] { 1000, 1000 });
        playback.Advance(0);
        Assert.AreEqual(1000, playback.NextClipAt);

        playback.Resume();
        Assert.AreEqual(0, playback.NextClipAt);

        Assert.AreEqual(2, playback.Advance(0));
    }

    [TestMethod]
    public void CreateSynchronized_ReturnsNull_WhenNotSynchronizedOrNoDurations()
    {
        var notSync = new BoomboxDefinition
        {
            SynchronizedDances = false,
            DanceSequence = new[] { 1 },
            DanceDurationsMs = new Dictionary<int, int[]> { [0] = new[] { 1000 } },
            DanceBlendMs = 0
        };
        Assert.IsNull(BoomboxDancePlayback.CreateSynchronized(notSync));

        var noDurations = new BoomboxDefinition
        {
            SynchronizedDances = true,
            DanceSequence = new[] { 1 },
            DanceDurationsMs = new Dictionary<int, int[]>(),
            DanceBlendMs = 0
        };
        Assert.IsNull(BoomboxDancePlayback.CreateSynchronized(noDurations));
    }

    [TestMethod]
    public void CreateSynchronized_UsesMaxDurationPerIndex()
    {
        var definition = new BoomboxDefinition
        {
            SynchronizedDances = true,
            DanceSequence = new[] { 1, 2 },
            DanceBlendMs = 0,
            DanceDurationsMs = new Dictionary<int, int[]>
            {
                [0] = new[] { 1000, 2000 },
                [1] = new[] { 500, 3000 }
            }
        };

        var playback = BoomboxDancePlayback.CreateSynchronized(definition);
        Assert.IsNotNull(playback);

        Assert.AreEqual(1, playback.Advance(0));
        Assert.AreEqual(1000, playback.NextClipAt);

        Assert.AreEqual(2, playback.Advance(1000));
        Assert.AreEqual(4000, playback.NextClipAt);
    }

    [TestMethod]
    public void Selection_SelectAndOwns_Basic()
    {
        var selection = new BoomboxDanceSelection();
        var player = 100ul;

        var result = selection.Select(player, 0, new[] { (5ul, 1) });
        Assert.AreEqual(5ul, result);
        Assert.IsTrue(selection.Owns(player, 5ul));

        var empty = selection.Select(player, 0, Array.Empty<(ulong, int)>());
        Assert.AreEqual(0ul, empty);
        Assert.IsFalse(selection.Owns(player, 5ul));
    }

    [TestMethod]
    public void Selection_HighestPriorityWins()
    {
        var selection = new BoomboxDanceSelection();
        var player = 100ul;

        var result = selection.Select(player, 0, new[] { (5ul, 1), (6ul, 2) });
        Assert.AreEqual(6ul, result);
    }

    [TestMethod]
    public void Selection_SamePriority_SelectsSmallestGuid()
    {
        var selection = new BoomboxDanceSelection();
        var player = 100ul;

        var result = selection.Select(player, 0, new[] { (5ul, 1), (7ul, 1) });
        Assert.AreEqual(5ul, result);
    }

    [TestMethod]
    public void Selection_KeepsOwnerUntilEndsAt()
    {
        var selection = new BoomboxDanceSelection();
        var player = 100ul;

        selection.Select(player, 0, new[] { (5ul, 1), (7ul, 1) });
        selection.Started(player, 5ul, endsAt: 1000);

        Assert.AreEqual(5ul, selection.Select(player, 500, new[] { (5ul, 1), (7ul, 1) }));
        Assert.AreEqual(7ul, selection.Select(player, 1000, new[] { (5ul, 1), (7ul, 1) }));
    }

    [TestMethod]
    public void Selection_RotatesOverThreeSources()
    {
        var selection = new BoomboxDanceSelection();
        var player = 100ul;
        var sources = new[] { (5ul, 1), (7ul, 1), (9ul, 1) };

        var first = selection.Select(player, 0, sources);
        Assert.AreEqual(5ul, first);
        selection.Started(player, 5ul, endsAt: 0);

        var second = selection.Select(player, 0, sources);
        Assert.AreEqual(7ul, second);
        selection.Started(player, 7ul, endsAt: 0);

        var third = selection.Select(player, 0, sources);
        Assert.AreEqual(9ul, third);
        selection.Started(player, 9ul, endsAt: 0);

        var fourth = selection.Select(player, 0, sources);
        Assert.AreEqual(5ul, fourth);
    }

    [TestMethod]
    public void Selection_StartedForNonOwner_ChangesNothing()
    {
        var selection = new BoomboxDanceSelection();
        var player = 100ul;

        var sources = new[] { (5ul, 1), (7ul, 1), (9ul, 1) };

        Assert.AreEqual(5ul, selection.Select(player, 0, sources));
        selection.Started(player, 7ul, endsAt: 0);
        Assert.IsTrue(selection.Owns(player, 5ul));

        // Had the call above counted a play for 7, the least played would now be 9 alone.
        selection.Started(player, 5ul, endsAt: 0);
        Assert.AreEqual(7ul, selection.Select(player, 0, sources));
    }

    [TestMethod]
    public void Selection_DisappearedSource_LosesPlayCount()
    {
        var selection = new BoomboxDanceSelection();
        var player = 100ul;
        var both = new[] { (5ul, 1), (7ul, 1) };

        var r1 = selection.Select(player, 0, both);
        Assert.AreEqual(5ul, r1);
        selection.Started(player, 5ul, endsAt: 0);

        var r2 = selection.Select(player, 0, both);
        Assert.AreEqual(7ul, r2);
        selection.Started(player, 7ul, endsAt: 0);

        var r3 = selection.Select(player, 0, both);
        Assert.AreEqual(5ul, r3);
        selection.Started(player, 5ul, endsAt: 0);

        var r4 = selection.Select(player, 0, new[] { (7ul, 1) });
        Assert.AreEqual(7ul, r4);
        selection.Started(player, 7ul, endsAt: 0);

        var r5 = selection.Select(player, 0, both);
        Assert.AreEqual(5ul, r5);
        selection.Started(player, 5ul, endsAt: 0);

        var r6 = selection.Select(player, 0, both);
        Assert.AreEqual(5ul, r6);
        selection.Started(player, 5ul, endsAt: 0);

        var r7 = selection.Select(player, 0, both);
        Assert.AreEqual(7ul, r7);
    }

    [TestMethod]
    public void Selection_Remove_ForgetsPlayer()
    {
        var selection = new BoomboxDanceSelection();
        var player = 100ul;

        selection.Select(player, 0, new[] { (5ul, 1) });
        Assert.IsTrue(selection.Owns(player, 5ul));

        selection.Remove(player);
        Assert.IsFalse(selection.Owns(player, 5ul));
    }
}
