using System.Linq;

using Microsoft.VisualStudio.TestTools.UnitTesting;

using Sanctuary.Game.Interactions;
using Sanctuary.Game.Minigames;
using Sanctuary.Game.Resources.Definitions;
using Sanctuary.Game.Resources.Definitions.QuestGoals;

namespace Sanctuary.Game.Tests;

/// <summary>
/// The world objects that open games as the server itself spawns them and answers a click on them: through
/// <see cref="Zones.BaseZone.TrySpawnNpc"/>, which readies a station (<see cref="GameStations.SetUp"/>), and
/// <see cref="Zones.WorldZone.GetInteractionOptions"/>, which puts its games after its quest buttons
/// (<see cref="GameStationMenu.OptionsAt"/>). <see cref="GameStationMenuTests"/> tests the buttons and the click on a
/// stand-in zone; this tests that the real zone makes them, so a merge that drops one of those calls fails here.
/// </summary>
[TestClass]
public sealed class GameStationSpawnTests
{
    private const int Table = 2417;            // beside Bordon Cloo, in Sanctuary
    private const int LakesfieldTools = 33484; // Lakeshore Fields, whose game (570) isn't wired
    private const int Simone = 2069;

    private const int Steak = 450;
    private const int CookingIcon = 7546;

    // The recipes Mimic.json serves, in a table's order.
    private static readonly int[] Recipes = [450, 451, 456, 460, 471];

    private QuestWorld _world = null!;
    private QuestWorldZone _zone = null!;

    [TestInitialize]
    public void Setup()
    {
        _world = new QuestWorld();
        _zone = new QuestWorldZone(_world);
    }

    [TestCleanup]
    public void Cleanup()
    {
        _zone.Dispose();
        _world.Dispose();
    }

    private NpcInteractionOption[] Options(Entities.Npc npc) => [.. _zone.Zone.GetInteractionOptions(_world.Player, npc)];

    /// <summary>
    /// A cooking step (the steak) whose game <paramref name="offeredBy"/> offers, then back to Simone, taken by the player
    /// and indexed as the quest loader indexes a play step's NPC (QuestDefinitionCollection), so the zone asks the quest
    /// manager for its buttons.
    /// </summary>
    private void TakeCookingStepAt(int offeredBy)
    {
        var quest = _world.Accept(new QuestDefinition
        {
            QuestId = 1,
            GiverNpcId = Simone,
            TargetNpcId = Simone,
            Goals =
            [
                new PlayActivityGoal { NameId = 21837, ActivityId = Steak, NpcId = offeredBy },
                new TalkToNpcGoal { NameId = 94536, Targets = [new TalkTarget { NpcId = Simone }] }
            ]
        });

        _world.Resources.Quests.ByTarget.GetOrAdd(offeredBy, _ => []).Add(quest.QuestId);
    }

    [TestMethod]
    public void ATable_AsTheZoneSpawnsIt_WalksThePlayerOver_AndOffersTheServedRecipes()
    {
        var table = _zone.Spawn(Table);

        Assert.AreEqual(GameStations.WalkInRange, table.QuestInteractRange);

        var options = Options(table);

        CollectionAssert.AreEqual(Recipes, options.Select(option => option.ActivityId).ToArray());
        Assert.IsTrue(options.All(option => option.IconId == CookingIcon));

        options[2].Invoke(_world.Player);

        CollectionAssert.AreEqual(new[] { 456 }, _world.Launched, "The third recipe opens through the Gateway's launcher.");
    }

    [TestMethod]
    public void AnObjectWhoseGameIsntWired_AsTheZoneSpawnsIt_WalksThePlayerOver_AndOffersNothing()
    {
        var tools = _zone.Spawn(LakesfieldTools);

        Assert.AreEqual(GameStations.WalkInRange, tools.QuestInteractRange);
        Assert.AreEqual(0, Options(tools).Length);
    }

    /// <summary>The quest's steak is the table's first button, under the quest's icon; the other recipes follow it.</summary>
    [TestMethod]
    public void AQuestsGameAtATable_IsTheZonesFirstButtonThere_AndShowsOnce()
    {
        TakeCookingStepAt(Table);

        var options = Options(_zone.Spawn(Table));

        Assert.AreEqual(ContextIcons.QuestTurnIn, options[0].IconId, "The quest's button.");
        Assert.IsTrue(options.Skip(1).All(option => option.IconId == CookingIcon));
        CollectionAssert.AreEqual(Recipes, options.Select(option => option.ActivityId).ToArray());
    }

    /// <summary>Anyone else keeps the quest manager's buttons alone, and isn't walked over as a station is.</summary>
    [TestMethod]
    public void AQuestsNpc_AsTheZoneSpawnsHer_KeepsHerQuestButtonsAlone()
    {
        TakeCookingStepAt(Simone);

        var simone = _zone.Spawn(Simone);
        var option = Options(simone).Single();

        Assert.AreEqual((ContextIcons.QuestTurnIn, Steak), (option.IconId, option.ActivityId), "The quest's steak.");
        Assert.AreEqual(0, simone.QuestInteractRange);
    }
}
