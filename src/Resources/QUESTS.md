# Adding Quests

Quests are entirely data-driven: everything from goals to rewards to NPC gating lives in
[`Quests.json`](Quests.json). Adding a quest means adding a JSON entry - no C# code changes
required.

`Quests.json` is loaded at startup, and again whenever the file changes while the server runs, by
`QuestDefinitionCollection.Load` (called from `ResourceManager`). A reload replaces every quest at once.
If the file is broken (a quest with no goals, the same `QuestId` twice, or a link to a quest that isn't
in the file), the server refuses it and logs why: at startup it won't start, and on a reload it keeps
the quests it had. Quests are loaded into `src/Sanctuary.Game/Resources/Definitions/QuestDefinition.cs` /
`QuestGoal.cs` - read those two files for the authoritative field list. This doc is a guide for
using them, not a substitute.

## Before you start

`GiverGuid` and `TargetGuid` must be the guid of an NPC already spawned in a zone script (the
`100000000000 + definitionId` convention used by `zone.spawnNpcWithGuid` calls, e.g.
`src/Scripts/Zone/FabledRealms.lua`) - not the `Npcs.json` definition id itself.

The server doesn't validate this: if the guid doesn't match a spawned NPC, the quest silently can
never be offered or turned in - no error, no log. The test
`QuestDefinitionCollectionTests.ShippedQuests_NameOnlyNpcsThatFabledRealmsSpawns` catches it instead,
so `dotnet test` fails on any quest naming a guid `FabledRealms.lua` doesn't spawn.
`/npc spawn <definitionId>` (admin-only) prints a guid you can use for ad hoc testing.

## Minimal quest

The smallest valid entry is a single "talk to this NPC" quest with no reward:

```json
{
  "Comment": "My New Quest",
  "QuestId": 5000,
  "TitleId": 0,
  "DescriptionId": 0,
  "GiverDialogueId": 0,
  "ObjectiveDescriptionId": 0,
  "IconId": 43278,
  "GiverGuid": 100000001201,
  "TargetGuid": 100000001201,
  "RewardCoins": 0,
  "RewardExperience": 0,
  "RewardItems": [],
  "PrerequisiteQuestId": 0,
  "NextQuestId": 0,
  "Goals": [
    { "NameId": 0, "TargetGuid": 100000001201 }
  ]
}
```

`Comment` is ignored by the loader - it's purely a label for whoever's reading the file. `QuestId`
must be unique; the same id twice makes the loader refuse the file. `IconId` 43278 is what every
existing quest uses. Every quest needs at least one goal: the loader refuses a quest without `Goals`.
`3090` "Misplaced Mugwopp" is a real single-goal quest, and `3040`-`3043` a real multi-step chain.

## Multi-goal quests

For anything with more than one step, use `Goals` - an ordered checklist. Goals complete in order;
the active goal is the first incomplete one, and the quest is ready to turn in once every goal is
done. Each goal becomes its own tracker row.

```json
"Goals": [
  { "NameId": 100103, "DescriptionId": 100104, "DialogueId": 384144, "TargetGuid": 100000003018 },
  { "NameId": 94511,  "DescriptionId": 94512,  "DialogueId": 94257,  "TargetGuid": 100000002049 }
]
```

- `NameId` is the tracker row text AND the goal's client-side identity - it must be unique across
  a quest's goals, or the client can't tell them apart (checkmarks/progress won't render right).
  The loader warns on a duplicate.
- `DialogueId` on the **final** goal becomes the turn-in speech bubble; an earlier talk goal's
  `DialogueId` is what its NPC says as that step is ticked off.
- `Type` defaults to `0` (TalkToNpc) if omitted.

## Goal types

| Type | Value | Completes when... |
|---|---|---|
| `TalkToNpc` | 0 | player interacts with `TargetGuid` |
| `ReachLocation` | 1 | player gets within `ReachRadius` (default 12) of `ReachPosition`, checked on every position update (2D, X/Z only) |
| `Collect` | 2 | player gathers `RequiredCount` pickups of the node type `CollectNodeType` |

That's the full set - this branch has no combat system, so there's no Kill/hunt or
battle-instance-encounter goal type. Don't add one without also building what would drive it.

### ReachLocation example

Use `/whereami` in-game to grab coordinates while standing where you want the goal to trigger.

```json
{ "NameId": 93294, "Type": 1, "ReachPosition": [-690.38, 2.3, -1060.25], "ReachRadius": 15 }
```

### Collect example

Ronnie's firewood (quest 3072):

```json
{ "NameId": 41752, "DescriptionId": 41755, "Type": 2, "RequiredCount": 10, "CollectNodeType": "firewood" }
```

The pickups are collection nodes, the same system as mushrooms and exploration coins, so the quest
doesn't place them. Three files do:

- `CollectionNodeTypes.json`: the node type. `Key` is what `CollectNodeType` names; `ModelId` is a
  `Models.txt` id (`156` = `sg_treasure_hunter_plank_01`, since `748` = `sg_log_wFirefly` has no mesh on the
  asset mirror); `Scale` sizes it (that plank is a 14 m bridge board, so `0.07` makes a stick about 1 m long);
  `NameId` is the hover text ("Stick of Firewood");
  `DropTable` is empty, so gathering one gives nothing but the quest credit.
- `CollectionNodePools.json`: a pool of that type in a zone (`merry-vale-firewood`).
  `MaxActiveNodes` `0` puts a node on every spawn point; `RespawnSeconds` is how long a gathered one
  stays gone.
- `CollectionNodeSpawns/<zone id>/<pool key>.json`: the spawn points. Ids are unique across every
  file. Stand on the spot and use `/whereami`, or take the height from an NPC or a waypoint
  (`Maps/FabledRealms.map`) right beside it, so the node sits on the ground.

Anyone can gather a quest's nodes, on the quest or not, so each needs a short respawn.
`RequiredCount` must be at least `1`, and the test
`ShippedCollectGoals_HaveEnoughPickupsOutAtOnce` fails when a goal needs more pickups than its
pools put out at once, or names a type that doesn't exist.

### Counted talk

A talk goal with `RequiredCount` above 1 and a `TargetGuids` list completes after the player has
clicked that many of the listed NPCs, in any order, each counting once. Every one not yet clicked
shows the "?". Root of the Problem (3103) picks five of the fourteen chugaroot plants on Farmer
Murglot's patch:

```json
{ "NameId": 386167, "RequiredCount": 5, "TargetGuids": [100000001714, 100000001715, 100000001716] }
```

`TargetDialogueIds` (with `TargetResponseIds`) gives each NPC in `TargetGuids` its own line, in the
same order.

## Conversations

A talk goal's NPC says its `DialogueId` as one line. For more, give the goal a `Dialogue`: lines shown one
after another, each with one response button (`ResponseTextId`, default "You got it!").

A line with `Choices` is a question instead, with one button per answer. The answer picked plays its own
`Then` lines next. An answer with `"Retry": true` is wrong: after its `Then` lines, the question is asked again.
Brenna's quiz in quest 3023:

```json
{
  "TextId": 93466,
  "Choices": [
    { "ResponseTextId": 21311, "Retry": true, "Then": [ { "TextId": 93471, "ResponseTextId": 93472 } ] },
    { "ResponseTextId": 21302, "Then": [ { "TextId": 93470, "ResponseTextId": 93473 } ] }
  ]
}
```

A step completes when its conversation starts, so the NPC speaks after the step is ticked off. With
`"CompleteAfterDialogue": true` on the goal it completes only when the last line is answered, so a quiz has to
be passed: closing the window part way leaves the step open, and talking to the NPC again starts over. It works
on a goal with one NPC, not a counted talk.

The loader warns, and the tests fail, when a question has only wrong answers, an answer has no text, or
`CompleteAfterDialogue` is set where it can't work.

## Chaining and gating

- `PrerequisiteQuestId`: must be completed before this quest can be offered. `0` = none.
- `NextQuestId`: purely cosmetic automation - once this quest completes, the next quest's giver
  badge refreshes immediately (no relog needed) so players see it's available right away. It does
  **not** replace setting that quest's own `PrerequisiteQuestId`.
- `ExcludesQuestIds`: quests that block this one while active *or* completed, and vice versa - list
  both directions. Used for the two race-specific "Introduce Yourself" quests (`2563`/`2564`) so a
  player only ever gets one. Abandoning a quest clears it from the player's quest state, which lifts
  the exclusion automatically. Exclusions are checked before the prerequisite, so two exclusive
  branches can share one prerequisite and only the branch taken stays on offer.
- The loader refuses the file if any of these ids names a quest that isn't in `Quests.json`, and
  logs each one (`Quest 3011's PrerequisiteQuestId is quest 3010, which isn't in ...`). The tests
  check the shipped file the same way.

## Disabled quests

[`Quests.disabled.json`](Quests.disabled.json) holds quests the server doesn't load, because they
name npcs no zone script spawns yet. Their entries are kept exactly as they were, so bringing one
back means moving it into `Quests.json` once its npcs spawn, and restoring the chain links that
were cut when it left:

| Quest | Missing npcs | Restore when it comes back |
|---|---|---|
| 104146 Brawler: Restless Rumors | `100000033082` | Move together with 104154; they chain to each other |
| 104154 Brawler: The Growler Report | `100000033082`, `100000033083` | Move together with 104146 |
| 3010 Looking for Lavender | `100000040011`, `100000040012` | Set 3011's `PrerequisiteQuestId` back to `3010` |
| 3031 Purr...fect Secret | `100000040016`-`100000040019` | Set 3032's `PrerequisiteQuestId` back to `3031` |
| 3081 Sobering Homecoming | `100000040032`-`100000040034` | Set 3080's `NextQuestId` back to `3081` |

## Rewards

- `RewardCoins`, `RewardExperience` - plain ints, granted on completion.
- `RewardItems` - a list of item definition ids added to the player's bags. These **are**
  validated against the item definitions at grant time; an unknown id won't grant (check the
  server log if a reward item silently doesn't show up).

## Badges

`NotificationAvailable` (default `2`, the "!" icon) and `NotificationActive` (default `6`, the "?"
icon) control the world badge shown above the giver/target's head. You'll rarely need to change
these - the defaults match every existing quest.

The giver shows the "!" while the quest is on offer. Once it's accepted, only the NPC the current step needs shows
the "?" (for a counted talk, each NPC not yet talked to), and each step's NPC is refreshed as the steps go by.

## Text ids

`TitleId`, `DescriptionId`, `GiverDialogueId`, goal `NameId`/`DescriptionId`/`DialogueId`, etc. are
SOE T4 client localization ids (resolved client-side as `Global.Text.<id>`). There's no
server-side validation - any int is accepted - but an id with no matching client string just shows
as unresolved text in-game. You need ids that already exist in the client's string table; this
repo can't add new client-side strings.

### Finding text ids

The client's `locale/en_us_data.dat` keeps each string under Bob Jenkins' lookup2 hash of
`Global.Text.<id>`, so ids can be looked up, and searched for by their words, with
`tools/client-strings/strings.py` and your own client folder (nothing from the client goes in the repo):

```bash
python tools/client-strings/strings.py "<client folder>" search "Touring Seaside" --around 12
python tools/client-strings/strings.py "<client folder>" lookup 93420 93421 93419
```

A quest's rows usually sit together: its title, then within a few ids its description, the giver's
dialogue, the objective text, each goal's name and the turn-in line. Touring Seaside (`3022`) is the
worked example: title 93420, description 93421, giver dialogue 93419, objective text 93424, goals
93425, 93427, 93429 and the turn-in line 93422. Some rows the live game used (a goal's own dialogue,
say) aren't in the client we ship; leave those ids out rather than borrowing unrelated text. When
a lost row's exact words exist under another id, use that one: Birthday Surprises' "Speak with
Tumbolina" row is gone, and 71001, from another quest, reads the same.

## No database migration needed

`CharacterQuests` stores `QuestId, CharacterId, Completed, GoalProgress, GoalCount` generically -
adding quests to `Quests.json` is purely additive and needs no EF Core migration.

## Checklist

1. Confirm the giver/target NPCs are already spawned in a zone script - note their guids.
2. Pick a unique `QuestId`.
3. Write the entry with its `Goals`, one per step.
4. Wire `PrerequisiteQuestId`/`NextQuestId` if it's part of a chain, `ExcludesQuestIds` if it's
   mutually exclusive with another quest.
5. If it has a `Collect` goal, add its node type, pool and spawn points (see Collect example).
6. Build, spawn any test NPCs with `/npc spawn`, and walk through accept -> goals -> turn-in
   in-client.
