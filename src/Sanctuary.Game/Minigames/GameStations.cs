using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;

using Microsoft.Extensions.Logging;

using Sanctuary.Game.Entities;
using Sanctuary.Game.Helpers;
using Sanctuary.Game.Interactions;
using Sanctuary.Game.Resources;

namespace Sanctuary.Game.Minigames;

/// <summary>World objects of one kind that open the same games: cooking tables, a town's postboxes, a farm's tools.</summary>
/// <param name="Name">The objects' name in <c>Npcs.json</c>.</param>
/// <param name="NpcIds">The objects' <c>Npcs.json</c> ids. Each is spawned once by <c>FabledRealms.lua</c>, under a GUID
/// derived from its id, so an object is matched on <c>Npc.DefinitionId</c>, as a job trainer is.</param>
/// <param name="ActivityIds">The games they open (ClientActivityDefinitions.json ids), in the order of their buttons. A
/// click offers those the server can open (<see cref="PlayableActivities"/>); the others wait for their game, so wiring
/// one lights up its objects with no change here.</param>
public sealed record GameStation(string Name, IReadOnlyList<int> NpcIds, IReadOnlyList<int> ActivityIds);

/// <summary>
/// The world objects that open games when clicked, as in the original, where the client's own lines send a player to any
/// cooking table for its recipes (430971), to Flitter, whose postboxes stand beside her, for Sanctuary's mail sorting
/// (141814), and to the harvesting tools behind Perry (139231). A click adds a button for each game the server can open
/// there, after any quest buttons (<see cref="GameStationMenu"/>): one game opens its start screen at once, several show
/// a menu. The games open as the Games menu opens them, from anywhere, so an object gives nothing the menu doesn't. Like
/// the Games menu, an object asks for no job.
/// </summary>
public static class GameStations
{
    // Every Cooking recipe, 450 to 477 (one image set): any table cooks any recipe (391126). The server serves those in
    // Mimic.json.
    private static readonly int[] Cooking = [.. Enumerable.Range(450, 28)];

    // Every Forging recipe, 420 to 445 and 573 to 576 (one image set), but 571 and 572: the client's placeholder copies of
    // 440 and 441, under the same names. The server serves 420.
    private static readonly int[] Forging = [.. Enumerable.Range(420, 26), 573, 574, 575, 576];

    /// <summary>
    /// Clicked from farther than this, the player is walked over first (CommandPacketMoveAndInteract), as to an NPC a quest
    /// sends them to (<see cref="Resources.Definitions.QuestGoals.QuestGoal.InteractRange"/>), so a game opens beside its
    /// object. 0 would let a click open it from anywhere within the object's InteractRange (100).
    /// </summary>
    public const int WalkInRange = 12;

    public static readonly IReadOnlyList<GameStation> All =
    [
        // The last six are Evergrove's own (FabledRealms.lua), standing on the zone's tables at the Cooking Table map
        // points that had no table NPC: Simone's, Auguste Frydaze's, Helena's, Bristlewood's, Shrouded Glade's, Wugachug's.
        new("Cooking Table", [1525, 2216, 2417, 2537, 3472, 4062, 6391, 33482, 33636, 90001, 90002, 90003, 90004, 90005,
            90006], Cooking),

        // Sanctuary's mail sorting: the postboxes beside Flitter, its postmistress, and those on the warpstone plaza.
        new("mailbox", [2375], [414]),
        new("Sanctuary Postboxes", [33389], [414]),

        new("Queensfields Harvesting", [33213], [410]),

        // Smitty's anvil in Snowhill, the Lakeshore camp's, and Rumble Ridge's beside its forging map point (149).
        new("Anvil", [3600, 33481], Forging),
        new("Spectrum Anvil", [3853], Forging),

        // The fishing hole on the Snowhill river bank: an invisible cube bound to the bank.
        new("Frostbitten Banks", [33620], [565]),

        // Nothing to open until their games are wired, each by one FlashMinigames.Definitions row: the other farms, the
        // other towns' mail sorting, and copper mining (23220 sends a player to a copper node near Therin).
        new("Lakesfield Harvesting", [33484, 33507, 33525, 33551, 33552, 33559], [570]),
        new("Perry's Pastures Harvesting", [33851, 33883, 33907, 33918, 33927, 33941], [566]),
        new("Seaside Postboxes", [1881], [415]),
        new("Merry Vale Postboxes", [2297], [1038]),
        new("Shrouded Glade Postboxes", [1426], [1037]),
        new("Snowhill Postboxes", [33695], [567]),
        new("Copper Vein", [33585, 33587, 33608, 33616, 33629, 33738, 33771, 33820, 33821, 33860, 33939, 33940, 33999,
            34010, 34012, 36116, 36303], [400])
    ];

    private static readonly Dictionary<int, GameStation> ByNpc = All
        .SelectMany(station => station.NpcIds, (station, npcId) => (NpcId: npcId, Station: station))
        .ToDictionary(entry => entry.NpcId, entry => entry.Station);

    /// <summary>The station <paramref name="npcId"/> (an <c>Npcs.json</c> id) is one of, if any.</summary>
    public static bool TryGet(int npcId, [MaybeNullWhen(false)] out GameStation station) =>
        ByNpc.TryGetValue(npcId, out station);

    /// <summary>
    /// Readies an NPC just spawned (BaseZone.TryCreateNpc): a station walks the player over when clicked from farther than
    /// <see cref="WalkInRange"/>. Any other NPC is left as it is.
    /// </summary>
    public static void SetUp(Npc npc)
    {
        if (TryGet(npc.DefinitionId, out _))
            npc.QuestInteractRange = WalkInRange;
    }
}

/// <summary>
/// The buttons a click on a world object shows, and what each does: the object's quest buttons, then one for each of its
/// games (<see cref="GameStations"/>) the server can open. WorldZone builds one; so do the tests.
/// </summary>
/// <param name="activities">The client's Games menu entries, which name each game and give its icon.</param>
/// <param name="isPlayable">Whether the server can open a game now (<see cref="PlayableActivities.IsPlayable(IResourceManager, int)"/>).</param>
/// <param name="launcher">Opens a game as the Games menu does, with its checks.</param>
/// <param name="logger">Where a game that didn't open is logged.</param>
public sealed class GameStationMenu(ClientActivityDefinitionCollection activities, Func<int, bool> isPlayable,
    IActivityLauncher launcher, ILogger logger)
{
    /// <summary>
    /// The buttons a click on <paramref name="npc"/> shows: <paramref name="questOptions"/> first, as they are, then a
    /// button for each game of its station that the server can open and no quest button already opens (a quest's game at
    /// the object shows once, as the quest's). The button reads the game's name in the Games menu (its NameId, which names
    /// the recipe) under the game's icon (its ImageSetId). An NPC that isn't a station, or one whose games aren't wired
    /// yet, keeps its quest buttons alone.
    /// </summary>
    public IReadOnlyList<NpcInteractionOption> OptionsAt(Npc npc, IReadOnlyList<NpcInteractionOption> questOptions)
    {
        if (!GameStations.TryGet(npc.DefinitionId, out var station))
            return questOptions;

        List<NpcInteractionOption>? options = null;
        var npcId = npc.DefinitionId;

        foreach (var activityId in station.ActivityIds)
        {
            if (!isPlayable(activityId) || !activities.TryGetValue(activityId, out var activity)
                || questOptions.Any(option => option.ActivityId == activityId))
                continue;

            options ??= [.. questOptions];
            options.Add(new NpcInteractionOption
            {
                IconId = activity.ImageSetId,
                ButtonTextId = activity.NameId,
                ActivityId = activityId,
                Invoke = player => Open(player, station, npcId, activityId)
            });
        }

        return options ?? questOptions;
    }

    // Opens the game as the Games menu does. A player whose other game stopped it is told so; what stopped it is logged as
    // what it is: the player's other game, a player who has left, or a game the server stopped serving since the button
    // was shown (a recipe taken out of Mimic.json, say).
    private void Open(Player player, GameStation station, int npcId, int activityId)
    {
        var result = launcher.Launch(player, activityId);

        if (ActivityLaunches.Explain(result) is { } line)
            ChatHelper.SendSystemMessage(player, line);

        switch (result)
        {
            case ActivityLaunchResult.AnotherGameOpen:
                logger.LogDebug("{station} {npcId} didn't open activity {activityId} for character {characterId}: " +
                    "another game is open.", station.Name, npcId, activityId, player.CharacterId);
                break;

            case ActivityLaunchResult.NotConnected:
                logger.LogInformation("{station} {npcId} didn't open activity {activityId} for character {characterId}: " +
                    "they are no longer connected.", station.Name, npcId, activityId, player.CharacterId);
                break;

            case ActivityLaunchResult.NotAGamesMenuGame:
                logger.LogWarning("{station} {npcId} couldn't open activity {activityId} for character {characterId}: " +
                    "it isn't a game the Games menu opens.", station.Name, npcId, activityId, player.CharacterId);
                break;
        }
    }
}
