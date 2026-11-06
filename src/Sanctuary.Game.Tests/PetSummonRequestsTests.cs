using Microsoft.VisualStudio.TestTools.UnitTesting;

using Sanctuary.Game.Entities;

namespace Sanctuary.Game.Tests;

/// <summary>
/// A player's pet is summoned at most once a second, the latest request winning, so a client can't make the zone rebuild
/// a pet for everyone nearby on every packet it sends (<see cref="PetSummonRequests"/>).
/// </summary>
[TestClass]
public sealed class PetSummonRequestsTests
{
    private const long Now = 1_000_000;
    private const int ZoneTickMs = 100;
    private const int Rex = 1;
    private const int Tom = 2;

    [TestMethod]
    public void NothingAsked_TakesNothing()
    {
        Assert.IsFalse(new PetSummonRequests().TryTake(Now, out _));
    }

    [TestMethod]
    public void ASummon_IsCarriedOutOnce()
    {
        var requests = new PetSummonRequests();

        requests.Summon(Rex);

        Assert.IsTrue(requests.TryTake(Now, out var petId));
        Assert.AreEqual(Rex, petId);
        Assert.IsFalse(requests.TryTake(Now + 5000, out _));
    }

    [TestMethod]
    public void AClientSendingSummonsAsFastAsItCan_GetsOneASecond()
    {
        var requests = new PetSummonRequests();
        var summoned = 0;

        // Ten seconds of a summon every millisecond, alternating pets, with the zone ticking as it does.
        for (var now = Now; now < Now + 10_000; now++)
        {
            requests.Summon(now % 2 == 0 ? Rex : Tom);

            if (now % ZoneTickMs == 0 && requests.TryTake(now, out _))
                summoned++;
        }

        Assert.AreEqual(10, summoned);
    }

    [TestMethod]
    public void ASummonTooSoon_WaitsInsteadOfBeingDropped()
    {
        var requests = new PetSummonRequests();

        requests.Summon(Rex);
        Assert.IsTrue(requests.TryTake(Now, out _));

        requests.Summon(Tom);

        Assert.IsFalse(requests.TryTake(Now + 500, out _));
        Assert.IsTrue(requests.TryTake(Now + PetSummonRequests.SummonIntervalMs, out var petId));
        Assert.AreEqual(Tom, petId);
    }

    /// <summary>The activity portal puts the pet that is out away and summons the chosen one in the same moment.</summary>
    [TestMethod]
    public void PuttingAPetAwayThenSummoningAnother_SummonsTheOther()
    {
        var requests = new PetSummonRequests();

        requests.Recall();
        requests.Summon(Tom);

        Assert.IsTrue(requests.TryTake(Now, out var petId));
        Assert.AreEqual(Tom, petId);
        Assert.IsFalse(requests.TryTake(Now + ZoneTickMs, out _));
    }

    [TestMethod]
    public void PuttingThePetAway_IsCarriedOutAtOnce_EvenRightAfterASummon()
    {
        var requests = new PetSummonRequests();

        requests.Summon(Rex);
        Assert.IsTrue(requests.TryTake(Now, out _));

        requests.Recall();

        Assert.IsTrue(requests.TryTake(Now + ZoneTickMs, out var petId));
        Assert.AreEqual(PetSummonRequests.NoPetId, petId);
    }

    [TestMethod]
    public void PuttingThePetAway_CancelsASummonStillWaiting()
    {
        var requests = new PetSummonRequests();

        requests.Summon(Rex);
        Assert.IsTrue(requests.TryTake(Now, out _));

        requests.Summon(Tom);
        requests.Recall();

        Assert.IsTrue(requests.TryTake(Now + ZoneTickMs, out var petId));
        Assert.AreEqual(PetSummonRequests.NoPetId, petId);
        Assert.IsFalse(requests.TryTake(Now + 5000, out _), "Tom was asked for before the pet was put away.");
    }
}
