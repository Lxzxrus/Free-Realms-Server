# Adding Quests

Quests are entirely data-driven: everything from goals to rewards to NPC gating lives in
[`Quests.json`](Quests.json). Adding a quest means adding a JSON entry - no C# code changes
required.

`Quests.json` is loaded at startup, and again whenever the file changes while the server runs, by
`QuestDefinitionCollection.Load` (called from `ResourceManager`). A reload replaces every quest at once.
If the file is broken (a quest with no goals, the same `QuestId` twice, or a link to a quest that isn't
in the file), the server refuses it and logs why: at startup it won't start, and on a reload it keeps
the quests it had. Quests are loaded into `src/Sanctuary.Game/Resources/Definitions/QuestDefinition.cs` /
`QuestGoals/QuestGoal.cs` - read those two files for the authoritative field list. This doc is a guide for
using them, not a substitute.

## Before you start

`GiverNpcId`, `TargetNpcId` and each talk goal's `Targets[].NpcId` name an NPC by its `Npcs.json` id,
and that NPC must be spawned by a zone script: `zone:spawnFixedNpc(id, x, y, z, heading)` in
`src/Scripts/Zone/FabledRealms.lua`. The server derives the NPC's GUID from that id (upstream OSFR #164),
so never write a GUID into quest data.

The server doesn't validate this: if no spawned NPC has the id, the quest silently can never be offered
or turned in - no error, no log. The test
`QuestDefinitionCollectionTests.ShippedQuests_NameOnlyNpcsThatFabledRealmsSpawns` catches it instead,
so `dotnet test` fails on any quest naming an NPC `FabledRealms.lua` doesn't spawn.
Quests match an NPC by its id, so one spawned for ad hoc testing with `!npc spawn <definitionId>`
(admin-only) works too.

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
  "GiverNpcId": 1201,
  "TargetNpcId": 1201,
  "RewardCoins": 0,
  "RewardExperience": 0,
  "RewardItems": [],
  "Goals": [
    { "Type": 0, "NameId": 0, "Targets": [ { "NpcId": 1201 } ] }
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
  { "Type": 0, "NameId": 100103, "DescriptionId": 100104, "DialogueId": 384144, "Targets": [ { "NpcId": 3018 } ] },
  { "Type": 0, "NameId": 94511,  "DescriptionId": 94512,  "DialogueId": 94257,  "Targets": [ { "NpcId": 2049 } ] }
]
```

- `NameId` is the tracker row text AND the goal's identity, on the client and in the database - it must
  be unique across a quest's goals, or the client can't tell them apart (checkmarks/progress won't
  render right). The loader warns on a duplicate. Players' progress is saved under it, so changing the
  `NameId` of a goal live characters may be on needs a migration (see "Database").
- `DialogueId` on the **final** goal becomes the turn-in speech bubble; an earlier talk goal's
  `DialogueId` is what its NPC says as that step is ticked off.
- `Type` defaults to `0` (TalkToNpc) if omitted.

## Goal types

| Type | Value | Completes when... |
|---|---|---|
| `TalkToNpc` | 0 | player interacts with the NPC in `Targets` (or the quest's `TargetNpcId` when it names none) |
| `ReachLocation` | 1 | player gets within `ReachRadius` (default 12) of `ReachPosition`, checked on every position update (2D, X/Z only; give Y the ground height all the same, as Take Me There finds its path from it) |
| `Collect` | 2 | player gathers `RequiredCount` pickups of the node type `CollectNodeType` |

That's the full set - this branch has no combat system, so there's no Kill/hunt or
battle-instance-encounter goal type. Don't add one without also building what would drive it.

### ReachLocation example

Use `!whereami` in-game to grab coordinates while standing where you want the goal to trigger.

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
- `CollectionNodePools.json`: a pool of that type in a zone (`merry-vale-firewood`) with
  `"PlayerSpecific": true`. `MaxActiveNodes` `0` puts a node on every spawn point.
- `CollectionNodeSpawns/<zone id>/<pool key>.json`: the spawn points. Ids are unique across every
  file. Stand on the spot and use `!whereami`, or take the height from an NPC or a waypoint
  (`Maps/FabledRealms.map`) right beside it, so the node sits on the ground.

A Collect goal counts only player-specific pickups (upstream OSFR #166): each player sees a pool's
pickups only while one of their quests needs them, a picked one disappears for that player alone, and
each spawn point counts once. The step's map marker and Take Me There point at the nearest pickup the
player can still take, and are hidden when none is left. The server warns at startup about a Collect
goal whose node type has no player-specific pool, since nothing can ever credit it. A node type may
also name a quest collection entry (`CollectionId`, `CollectionEntryId`), ticked when the player picks
one up: Hasti's homework ticks collection 313.
`RequiredCount` must be at least `1`, and the test
`ShippedCollectGoals_HaveEnoughPickupsOutAtOnce` fails when a goal needs more pickups than its
pools put out at once, or names a type that doesn't exist.

### Counted talk

A talk goal with `RequiredCount` above 1 and several `Targets` completes after the player has
clicked that many of the listed NPCs, in any order, each counting once. Every one not yet clicked
shows the "?". Root of the Problem (3103) picks five of the fourteen chugaroot plants on Farmer
Murglot's patch:

```json
{ "Type": 0, "NameId": 386167, "RequiredCount": 5, "Targets": [ { "NpcId": 1714 }, { "NpcId": 1715 }, { "NpcId": 1716 } ] }
```

A target's `DialogueId` (with `ResponseId`) gives that NPC its own line.

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
name npcs no zone script spawns yet. It is empty: the five it held (104146, 104154, 3010, 3031,
3081) came back with upstream OSFR's spawns. To park a quest, move its entry there unchanged and cut
the chain links (`PrerequisiteQuestId`, `NextQuestId`) other quests have to it, noting them here so
they can be restored.

## Rewards

- `RewardCoins`, `RewardExperience` - plain ints, granted on completion.
- `RewardItems` - a list of item definition ids added to the player's bags. These **are**
  validated against the item definitions at grant time; an unknown id won't grant (check the
  server log if a reward item silently doesn't show up).
- `RewardCollectionId` - a quest collection (`Collections.json`) shown with the rewards on the offer
  and the turn-in (upstream OSFR #166). Its entries are ticked by the quest's pickups, not by the
  turn-in.
- `TeachesProfileId` - a job (a `Profiles.json` id; 45 is Chef) the turn-in teaches before the
  rewards, the way the job's trainer gives it (`IJobManager.Grant`): saved with its starter kit, and the
  player switched into it, so the quest's stars go to the new job. A player who has the job already is
  taught nothing and keeps the job they have on. The server warns at startup about a job
  `Profiles.json` doesn't have.

`RewardExperience` stars go to the job the player has on at the turn-in.

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

## Database

Adding a quest needs no migration. `CharacterQuests` stores each character's quests by `QuestId`, with
`CurrentGoalId` the `NameId` of the goal they are on, and `CharacterQuestProgress` stores progress
per goal `NameId` (the NPCs talked to, the pickups gathered). So changing a live quest's goals matters:
a character on a goal whose `NameId` is gone restarts the quest at login. When a goal's `NameId` has to
change, add a migration for both providers that moves the rows, as `ReconcileUpstreamQuestGoals` does
for Hasti's Homework Heist (46489 to 74443).

## Checklist

1. Confirm the giver/target NPCs are already spawned in a zone script - note their `Npcs.json` ids.
2. Pick a unique `QuestId`.
3. Write the entry with its `Goals`, one per step.
4. Wire `PrerequisiteQuestId`/`NextQuestId` if it's part of a chain, `ExcludesQuestIds` if it's
   mutually exclusive with another quest.
5. If it has a `Collect` goal, add its node type, pool and spawn points (see Collect example).
6. Build, spawn any test NPCs with `/npc spawn`, and walk through accept -> goals -> turn-in
   in-client.
