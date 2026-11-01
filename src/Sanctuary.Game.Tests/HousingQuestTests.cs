using System.Linq;
using System.Numerics;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Microsoft.VisualStudio.TestTools.UnitTesting;

using Sanctuary.Game.Entities;
using Sanctuary.Game.Helpers;
using Sanctuary.Game.Housing;
using Sanctuary.Game.Resources.Definitions.QuestGoals;
using Sanctuary.Game.Zones;

namespace Sanctuary.Game.Tests;

/// <summary>
/// Samantha's Housing quest (9100, Nate's decisions 7 and 8 of 1 November): Samantha, the Housing door behind her, then
/// Samantha again, between Back for More and Samantha's Mail. The door's step is completed as the player really goes into
/// their house, just before the move.
/// </summary>
[TestClass]
public sealed class HousingQuestTests
{
    private const ulong Character = QuestWorld.CharacterId;
    private const int Samantha = 2045;

    private const int BackForMore = 3290;
    private const int Housing = 9100;
    private const int SamanthasMail = 9101;

    private HouseWorld _world = null!;

    [TestInitialize]
    public void Setup() => _world = new HouseWorld();

    [TestCleanup]
    public void Cleanup() => _world.Dispose();

    /// <summary>
    /// The rows Nate chose (the note's section 5.1), each checked in the client's strings: the 2012 housing booth's pitch,
    /// said by Samantha, as offer and description; its tour's rows for the objective and the door step, which says
    /// nothing; then back to Samantha, whose welcome back is the turn-in line. 10 coins and 50 stars, and nothing else.
    /// </summary>
    [TestMethod]
    public void Housing_IsAsDecided()
    {
        var housing = QuestWorld.Shipped(Housing);

        Assert.AreEqual(437413, housing.TitleId);
        Assert.AreEqual(436644, housing.GiverDialogueId);
        Assert.AreEqual(436644, housing.DescriptionId);
        Assert.AreEqual(437741, housing.ObjectiveDescriptionId);
        Assert.AreEqual(Samantha, housing.GiverNpcId);
        Assert.AreEqual(Samantha, housing.TargetNpcId);
        Assert.AreEqual(BackForMore, housing.PrerequisiteQuestId);
        Assert.AreEqual(SamanthasMail, housing.NextQuestId);

        Assert.AreEqual(2, housing.Goals.Count);

        var door = (TalkToNpcGoal)housing.Goals[0];
        Assert.AreEqual((437426, 437367), (door.NameId, door.DescriptionId));
        CollectionAssert.AreEqual(new[] { HouseDoors.CobblestoneVillage }, door.AllTalkTargetNpcIds().ToArray());
        Assert.AreEqual(0, door.ConversationFor(HouseDoors.CobblestoneVillage).Count, "Nobody at the door to say a line.");

        var back = (TalkToNpcGoal)housing.Goals[1];
        Assert.IsFalse(back.WithPrevious);
        Assert.AreEqual((94363, 46177), (back.NameId, back.DescriptionId));
        CollectionAssert.AreEqual(new[] { Samantha }, back.AllTalkTargetNpcIds().ToArray());
        Assert.AreEqual(78487, housing.TurnInDialogueId);

        Assert.AreEqual((10, 50), (housing.RewardCoins, housing.RewardExperience));
        Assert.AreEqual(0, housing.RewardsBeyondCoinsAndStars().Count(), "No items, collection or job.");
        Assert.AreEqual(0, housing.RewardProfileId);
        Assert.AreEqual(0, HouseDoors.QuestProblems(housing).Count());
    }

    /// <summary>
    /// Housing as a new player plays it, on the real quest and house managers: Samantha offers it as Back for More is
    /// handed in; the door, with no house on the account yet, gives the Apartment and takes them in, its step completed
    /// before they leave the world; back out, Samantha takes it in, pays its coins and stars, and offers Samantha's Mail.
    /// </summary>
    [TestMethod]
    public void Housing_AsANewPlayerPlaysIt()
    {
        var (player, door, samantha) = StandBySamantha();
        player.Quests.AddCompleted(BackForMore);

        Assert.IsTrue(_world.Quests.Quests.OfferNextQuest(player, BackForMore), "Samantha offers Housing as Back for More is handed in.");

        _world.Quests.Quests.AcceptQuest(player, Housing);
        Assert.IsTrue(player.Quests.TryGetInProgress(Housing, out var progress));

        var goalAtTheMove = -1;
        _world.OnMove = () => goalAtTheMove = progress.GoalIndex;

        Assert.AreEqual(HouseDoorResult.GoingIn, Doors().Use(player, door, () => { }, now: 1_000));

        Assert.AreEqual(1, goalAtTheMove, "The door's step was completed before the player left the world.");
        Assert.AreEqual(HouseWorld.Apartment, _world.Moves.Single().ZoneDefinitionId, "Into the Apartment, given at the door.");
        Assert.IsInstanceOfType<HousingZone>(player.Zone);

        // Out again by the exit bar (this house manager has no zone manager to do it), and back to Samantha.
        player.Zone = _world.World;
        var coins = _world.Balance(Character).Coins;

        _world.Quests.Quests.GetInteractionOptions(player, samantha)
            .Single(option => option.ButtonTextId == QuestWorld.Shipped(Housing).TitleId)
            .Invoke(player);

        Assert.AreEqual(Housing, player.Quests.PendingTurnInQuestId, "Samantha's reward window.");
        Assert.IsTrue(_world.Quests.Quests.CompleteQuest(player, Housing));

        Assert.AreEqual(coins + 10, _world.Balance(Character).Coins);
        Assert.AreEqual(_world.Quests.Resources.RankLevels.Accrue(1, 0, 50), _world.Quests.SavedJob(QuestWorld.Adventurer));
        Assert.IsTrue(_world.Quests.Quests.OfferNextQuest(player, Housing), "Then Samantha's Mail.");
    }

    /// <summary>
    /// The step waits for a real entry. A player who comes out of a house and clicks the door at once waits for their
    /// move (<see cref="ZoneTeleports"/>); if the house is gone by then, the entry is refused and the step stays open.
    /// </summary>
    [TestMethod]
    public void Housing_DoorStep_StaysOpenWhenTheEntryIsRefused()
    {
        var (player, door, _) = StandBySamantha();
        player.Quests.AddCompleted(BackForMore);
        var apartment = _world.AddHouse(Character, HouseWorld.Apartment);

        _world.Quests.Quests.AcceptQuest(player, Housing);
        Assert.IsTrue(player.Quests.TryGetInProgress(Housing, out var progress));

        player.NextZoneTeleportAt = 5_000;
        Assert.AreEqual(HouseDoorResult.GoingIn, Doors().Use(player, door, () => { }, now: 1_000));

        using (var dbContext = _world.Quests.CreateDbContext())
            dbContext.Houses.Where(house => house.Id == apartment).ExecuteDelete();

        ZoneTeleports.Tick(player, 5_000);

        Assert.AreEqual(0, progress.GoalIndex, "Still on the door's step.");
        Assert.AreEqual(0, _world.Moves.Count);
        Assert.AreSame(_world.World, player.Zone);
        Assert.AreEqual(437426, _world.Quests.ReadCharacter().Quests.Single().CurrentGoalId);
    }

    private HouseDoorService Doors() =>
        new(_world.Houses, _world.Quests.Quests, _world.Quests.Resources, Options.Create(_world.Options));

    /// <summary>
    /// The quests from Back for More to Samantha's Mail as shipped, and a player beside Samantha and the door, where
    /// FabledRealms.lua spawns them.
    /// </summary>
    private (Player Player, Npc Door, Npc Samantha) StandBySamantha()
    {
        foreach (var questId in new[] { BackForMore, Housing, SamanthasMail })
            _world.Quests.Add(QuestWorld.Shipped(questId));

        var door = _world.AddNpc(HouseDoors.CobblestoneVillage, new Vector4(-1873.5f, -46.46f, 444.0f, 1));
        var samantha = _world.AddNpc(Samantha, new Vector4(-1879.158f, -45.93526f, 442.7892f, 1));

        var player = _world.PlayerInTheWorld(Character, _world.Account);
        player.UpdatePosition(new Vector4(-1876f, -46f, 443f, 1), Quaternion.Identity);

        return (player, door, samantha);
    }
}
