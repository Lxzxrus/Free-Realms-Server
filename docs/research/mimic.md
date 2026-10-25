# Mimic: how Cooking (and Smelting, Sword, Music) talk to the server, and Evergrove's server for them

Found on 2026-10-25 by reading the Cooking Flash (`game_cooking.gfx`, decompiled with JPEXS FFDec) and checking the
shared classes against Smelting, Sword and Music. Nothing from the client is committed. Read with `mining.md` (the
minigame session and the match-three protocol) and `minigames-catalog.md`. Qwen's overnight report on Cooking was the
starting point; the claims below were checked against the code, and a few of the report's were wrong (noted at the end).

## What Cooking is

A recipe is played as a row of timed rounds ("phases"), each a different kitchen action: tenderize the meat with a
hammer, chop a leaf, slice a vegetable along a line, fillet a fish, grate, smash fruit, crack eggs, measure a broth,
churn ice cream, then add everything to the pan and cook or stir it. The HUD shows the recipe's rounds as a bar (up to
8). A round is won by finishing its action before its timer runs out; a round that times out or is botched (too much
broth, burnt food, smashed eggs) is shown as failed and simply restarts, because Cooking sets the lives a timeout costs
to 0. So a Cooking game can't be lost, only abandoned. Points come per action (perfect, good, bad) and per round.

The client has 28 Cooking activities ("Cooking: Simone's Spiralmint Steak"...), activity ids 450 to 477, on
MiniGameData rows 173 to 199 (461 is on row 560), all of minigame type 9 (a Flash type). They were the Chef job's
recipes.

## Two servers: the one inside the Flash, and ours

Like the match-three games, each Mimic Flash carries a server (`GameServer` on `SoeGameFramework`). Unlike them, that
server runs **inside the Flash**: `GameClient.networking` is `TYPE_LOCAL`, so the client half and the server half talk
over an in-memory connection, and the in-Flash server plays the rounds (next level, start level, stop level). The real
server never sees a round start or stop.

The Flash opens a second connection, `GameExternalNetwork`, to Free Realms itself (the `TYPE_FREEREALMS` network: out
with `fscommand("FR_event", "SendMsg(...)")`, in through `_root.OnMessage`, carried by minigame payload packets 39/14).
On that connection the in-Flash server asks for the data files it would otherwise read from disk, and the client
reports how the game went. That is everything Evergrove's server does.

The in-Flash server reads two files: `MimicMiniGameData.txt` (one row per game) and `MimicMiniGamePhaseData.txt` (one
row per round). In Free Realms (`GameServer.sLocal` false) it never opens them: the game row is replaced by the game id
alone, and the phase rows arrive one message each. Both files were server data and are lost (not in the client, not on
the asset mirror), so Evergrove writes its own recipes.

## The conversation

Messages are tab-separated and NUL-terminated, like every Flash minigame's. The Flash's external network turns each
field that reads as a number into a number, `true`/`false` into booleans, and passes at most seven fields.

1. The player presses Go; the server sends GameStart (39/17) and StartFlashGame (26/12) with `game_cooking.gfx`, as
   for Mining.
2. When its pictures and sounds are loaded (`GameClient.GameSetupComplete`), the Flash connects: `OnConnectMsg`, then
   `FRServer_ReadyForGameData`. Nothing answers the first.
3. The server answers the second with the recipe:
   - `OnGameDataMsg id`: the game id. The in-Flash server takes it as its game id and, when the data is complete,
     treats the text `"id"` as the whole game file, so the game row is found and nothing else of it is read;
   - `OnPhaseDataMsg row`, once per round: one line of `MimicMiniGamePhaseData.txt`, `^`-separated (below). The Flash
     appends each, with a line break, to a buffer;
   - `OnCustomDataMsg row` for a third file, if the game has one (Sword's `MimicSwordData.txt`; Cooking has none);
   - `OnDataComplete`. The in-Flash server parses the buffer, keeps the rows whose `LD_GAME` is the game id, and
     tells its client it is ready.
4. From here the Flash plays on its own, round after round. For each round Cooking sends:
   - `FRServer_RoundTransitionPause false` when the round's scene is up;
   - when the round is won, `FRServer_RoundTransitionPause true` and four `FRServer_StatUpdate stage stat count`
     (bad, good and perfect actions, and the round's time; `stat` is `GameData.STAT_*`, `stage` the round's stage
     type);
   - when the player closes the round's score window, `FRServer_ScoreSet total`, the running score.

   A failed round sends nothing and starts again. `FRServer_Pause true|false` comes and goes with the client's pause.
5. After the last round, four `FRServer_StatUpdate -1 stat count` (the game's totals, `stat` 4 to 7) and
   `FRServer_GameWon score 0`. A game that can be lost (not Cooking) sends `FRServer_GameLost score`.
6. The server answers `OnGameWonMsg` or `OnGameLostMsg`; the Flash ends its game and sends `FRServer_GameOver`.
7. The minigame frame's close button sends End (39/6), and the server answers Leave (39/19), as for Mining. A Flash
   whose `OnPlayerClickedNextLevel` is called by the client sends `FRServer_GameClose` three seconds later, which closes
   the game too.

The other messages the Flash will take on that connection are `OnServerAddScoreMsg points` (adds to its total) and
`OnSkipStageOrLevelMsg` (wins the current round); Evergrove sends neither.

### Quirks of the messages

- `SoeInterface.UpdateStats` and `SoeInterface.StopLevel` build their `SendMsg( "...` without the closing quote, so
  their last field may arrive with a `)` or `"` stuck to it. Evergrove reads numbers leniently.
- The phase buffer starts out undefined, so the first row reaches the parser as `undefined` + row. Only `LD_ID`, which
  no Mimic Flash reads, is spoiled.
- A column that isn't a number is dropped (`setData` skips `NaN`), and several rounds then compute with "undefined"
  (Grate's sway, Slice's cut count). Every column must be sent.
- Cooking puts a promotion banner over every round of game id 47.

## The phase row

`MimicMiniGamePhaseData.txt`, one row per round, `^`-separated (`Cooking.CookingGameServer.LD_*`):

| Column | Name | Read by Cooking |
|---|---|---|
| 0 | `LD_ID` | No |
| 1 | `LD_GAME` | The game id; rows of other games are skipped |
| 2 | `LD_SEQUENCE` | The round's number, 1 first; the rounds are played in this order |
| 3 | `LD_DURATION` | The round's timer, in seconds |
| 4-6 | `LD_OBJECTIVE_ID`, `LD_STARTID`, `LD_ENDID` | No |
| 7 | `LD_STAGE_TYPE` | Which round (`Cooking.StageType`) |
| 8-18 | `LD_DATA1` to `LD_DATA11` | `LD_DATA1` is the ingredient (`Cooking.CookingIngredient` id); the rest are the round's settings |

The Mimic games all share this layout; Sword's server also sets a stage-type offset of 200.

### Cooking's rounds

Stage types, and the ingredients each scene has a picture for (read from the frame labels in `game_cooking.gfx`):

| Stage | Round | `LD_DATA1` | `LD_DATA2` onward |
|---|---|---|---|
| 1 | tenderize | meat 8-11 | hits needed, start speed, acceleration, top speed, hits per smash, points per hit, hammer size |
| 2 | chop | leaf 24-28 | chops, clicks per chop, slices per chop, points per slice |
| 3 | smashFruit | fruit 34-41, tomato 56 | hits per fruit, fruits, hammer size, meter speeds (2), hits for good and perfect, points (3) |
| 4 | crack | egg 23 | cracks per egg, eggs (1-5), points, hammer size, meter speeds (2) |
| 6 | measure | slugmud 19, rootbeer 20, broths 48-53, ice 21-22, egg 23, spaghetti 54 | good and perfect precision, points (2) |
| 7 | stir | meat stew 46, fish soup 47, spaghetti 54 | turns, chance to switch direction, points per turn, bonus |
| 8 | saw | meat 8-11 | pieces, motion per slice, points |
| 9 | grate | chugaroot 31, chocolate 32, honeytwig 33 | swipes, bowl sway time and distance, points, feedback rate |
| 10 | slice | vegetables 12-18, onion 57 | cuts, three slice tolerances, points (3), pieces per perfect cut |
| 11 | cook | meat saute 42, fish saute 43, spaghetti 54 | with `LD_DATA5` 0, the Flash's own heat table; else start offset, heat steps (4), points (5) |
| 12 | cut | fish 0-7 | points good, perfect |
| 13 | setHeat (ice cream) | vanilla 21, chocolate 22 | -, points |
| 17 | addIngredients | the dish: 42, 43, 44 sundae, 45 omelet, 46, 47, 54 | list order, points per ingredient |

Pour (5), final round (14), grind (15) and stuff (16) are named in the code but have no scene in this Flash.

Rounds depend on earlier ones: tenderize, chop, smash, grate, slice and cut put their ingredient on the recipe's list,
which the add-ingredients round asks for and the cook round lays in the pan; a measure round's liquid is the broth the
stew is stirred in; a set-heat round picks the sundae's ice cream.

## What Evergrove does

`Resources/Mimic.json` holds the games and their recipes; each recipe is a Games menu activity and its phases, and
names its own game id (Evergrove uses the recipe's MiniGameData row). The server checks every recipe when it loads,
including Cooking's scene table above, so a recipe the Flash can't draw doesn't load.

Five recipes to start, from the client's own Cooking activities: Simone's Spiralmint Steak and the Gummyfruit Topped
Sundae (easy), the Savory Spiralmint Soup and the Crispy Choychoy Delight (medium), and the Oddly Colored Omelette
(hard: tighter timers, more eggs and cuts). The other 23 Cooking activities still do nothing when clicked.

A win pays the recipe's coins and Chef stars through `RewardHelper`; the amounts are placeholders.

### Where Evergrove differs from SOE

The client plays every round and reports the score, so the server can't check the play itself. It checks what it
can and pays nothing for a win that couldn't have happened:

- **A win needs the recipe first.** A result before the data was sent is ignored.
- **A win needs every round.** For a game that reports its rounds (Cooking, Smelting), each phase needs its
  `FRServer_RoundTransitionPause true`; extra reports don't count twice.
- **A win takes time.** Faster than `MinSecondsPerPhase` per round is refused: 6 s for Cooking, whose end-of-round
  window alone stays 3 to 8 s.
- **One result per game**, and the score is capped (`MaxScore`).

A refused win is logged and answered with `OnGameLostMsg`, which ends the Flash's game normally.

The code: `src/Sanctuary.Game/Minigames/Mimic/` (`MimicGame`, `MimicDefinition`, `CookingStages`), and the gateway's
`MimicHandler`, called from `FlashMinigameHandler`. Tests: `MimicTests`, with a scripted client that reads the data
the way the Flash's parser does and plays every shipped recipe to a win.

## The pictures must be on the player's disk

`game_cooking.gfx` names 731 pictures, and 365 of them aren't in the official packs; neither is the gfx itself nor its
picture list, `game_cooking.lst`. All 367 files are on the launcher's pre-cache list (`tools/client-mods/precache`),
so Cooking needs a launcher release before it will draw.

## Extending to Smelting, Sword and Music

They share `GameServer`, `GameExternalNetwork`, `SoeGameFramework` and the phase layout, so each needs a game in
`Mimic.json` (its Flash, its MiniGameData type, `ReportsRounds`), recipes for its activities, and its own scene table
like `CookingStages`. Differences seen so far:

- **Smelting** (game 1) reports rounds like Cooking, sends `FRServer_StopLevel level won` after the last round, and
  `FRServer_GameWon score 10`.
- **Sword** (game 7) reads a third file, `MimicSwordData.txt`, through `OnCustomDataMsg` (`CustomData` in
  `Mimic.json`), and offsets its stage types by 200. It reports no rounds.
- **Music** (game 8) reports no rounds, sends `FRServer_ScoreSet` many times per round, and loses its score on the way
  out: its `SoeInterface.GameLost` drops the argument, so `FRServer_GameLost undefined` arrives.

Each needs its `.gfx` checked for pictures the official packs lack, as Cooking's was.

## Still unknown

- **The client's tutorial pages.** Each round type's first appearance calls `ShowTutorialPage("6", <round>)`; whether
  the client has tutorial 6 is unchecked. If it doesn't, the round may wait for a tutorial that never closes.
- **What the live server sent after a win.** Probably a reward window; Evergrove sends the reward toasts only.
- **The live recipes.** Which rounds, ingredients and numbers each of the 28 recipes used is lost with the data files.

## Corrections to the overnight report

- The game data is not a file: in Free Realms the "game file" is the game id alone.
- `FRServer_CompleteLevel`, `FRServer_StopLevel` and `SendStartLevel` are not sent by Cooking; its own round code
  replaces the base client's.
- The rows are `^`-separated within one message field; they don't need splitting on `\n` server-side.
- `CookingGameServer` is the in-Flash server; it is not "framework not included". `GameServer` and the network
  classes are all in the Flash.
