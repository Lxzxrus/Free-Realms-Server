# Mining: how the match-three games work, and Evergrove's server for them

Found on 2026-10-15 by reading the Mining Flash (`game_mining.gfx`, decompiled with JPEXS FFDec). Nothing from the
client is committed. Read with `daily-wheel.md` (the minigame session and payload packets) and `minigames-scout.md`.

## What Mining is

Mining Practice (activity 1113, MiniGameData row 375, minigame type 8) and the other match-three games (Forging,
Farming...) share one Flash program. The player drags a chain through three or more touching rocks of one kind; the
rocks break, everything above falls, and new tiles drop in from the top. Ore pieces ride down with the falls and
drop out through the drop zone at the bottom of the board, filling the level's quota bins. Gems score extra,
power-ups add time, multiply the score, or blow up, shuffle or convert tiles. The client's game name (`mining`)
picks the pictures: five rock types, fifteen ore pieces (iron, tin, silver, gold and copper in three qualities) and
ten gems.

**The Flash file holds SOE's server too.** Each match-three Flash carries both halves of the game: the client, and the
server SOE ran (`GameServer`, `GameLevel`, `ServerGrid`, `ServerTileGenerator`...). Evergrove's server is a C# port of
that server code, so it answers the client exactly as the live game did. What the Flash doesn't carry is SOE's level
tables (`MatchThreeGameData.txt`, `...LevelData.txt`, `...SpawnData.txt` and four more); they were server data and
are lost, so Evergrove designs its own levels in `src/Resources/MatchThree.json`.

## The conversation

Messages ride minigame payload packets (39/14) inside the minigame session, tab-separated and NUL-terminated. Lists
are comma-separated numbers inside one field. Booleans are `true` and `false`.

1. The player presses Go on the start screen; the server sends GameStart (39/17) and StartFlashGame (26/12) with
   `game_mining.gfx` (`FlashMinigameHandler`, PR #87).
2. The Flash connects (`FRServer_Connect`, and the framework's own `OnConnectMsg`; which arrive is still to be seen in
   the Gateway log). The server answers `OnActiveMsg false` and `OnServerReadyMsg`, once.
3. The Flash shows its tutorial and sends `OnStartGameMsg`. The server sends `OnNextLevelMsg level goal best`.
4. The Flash builds its screen and sends `OnStartLevelMsg`. The server sends, in this order:
   - `OnTileInfoMsg value type` for each rock, ore and gem type: which picture a tile value uses;
   - `OnTileQueueMsg -1 list`: the board's own tiles, one per cell, row by row;
   - `OnBuildMapMsg width height shape`: the grid's cell codes;
   - `OnTileQueueMsg -1 list`: 70 more tiles, the refill queue;
   - `OnRandomQueueMsg list`: 70 random numbers (0 to 998);
   - `OnLevelInitMsg level seconds type`, `OnQuotaInitMsg bin value quota perReward icon` for each bin,
     `OnLevelEventMsg 32 mold total` (the meter), `OnPowerupInitMsg value seconds power` for each power-up,
     `OnScoreMsg 0 0`, and `OnActiveMsg true`.
5. The client pauses (`FRServer_Pause true`) while the board drops in, then counts down and unpauses. The server's
   clock only runs while unpaused; it answers `OnPauseMsg false` to a pause and `OnPauseMsg true` to a resume.
6. Play:
   - `OnChainMsg x,y,x,y,...` a chain. The server replays it: rocks break, falls resolve, refills come from the queue.
     It answers with `OnLevelEventMsg 21 points reason` (score) and `25 seconds 1` (time won).
   - `OnLevelEventMsg 50 x y` ore at x, y fell out through the drop zone; `OnQuotaUpdateMsg bin count quota
     perReward` follows if it has a quota.
   - `OnActionMsg value x,y,...` a power-up clicked (dynamite 950, wild tiles 951, destruct 952, shuffle 953), with
     the tiles it took.
   - `OnIdleMsg x,y,...` tiles whose animation finished; `OnVerifyMsg x,y,value,...` the client's whole board.
   - `OnLevelEventMsg 60` / `61` the client accepts or rejects a queue reset (below).
7. The level ends when time runs out, or on a stop-at-quota level when every quota is met: `OnActiveMsg false`,
   `OnStopLevel level success`. A won level's end animation sends `OnNextLevelMsg`; the server names the next level
   or, after the last, sends `OnGameWonMsg`. A lost level ends the game on the client by itself.
8. The Flash's exit button asks the client to quit, and the minigame frame's close sends End (39/6); `FRServer_GameClose`
   and `FRServer_GameQuit` close it too. The server answers Leave (39/19).

### Lockstep

Both sides hold the board. The client never makes tiles or rolls dice: new tiles come from the server's tile queue,
and every random choice (which tile dynamite takes next, which tiles a shuffle moves) uses the next number from the
shared random queue. So the server can replay each move and check the client's. Every 100 tiles collected the
server rerolls the queue ("next wave", `OnLevelEventMsg 12 wave`) and sends it with `OnTileQueueMsg count list`: the
client replaces its queue only if it still holds `count` tiles, and answers accept (60) or reject (61); a reject means
a chain crossed in flight, and the server sends again with its new count. When no move is left the server builds a
new board (`OnLevelEventMsg 11 1`, then the board messages again).

### Tile values

A tile's category is its value rounded down to the hundred: 0 empty, 100 drop zone, 200 wildcard, 300 hazard (a
locked rock arrives as 500 plus its type), 400 rocks, 600 ore, 700 gems, 800 blocker, 900 to 953 power-ups, 1000 to
1002 loot drops. Rocks are 401 to 405 in Mining (pictures `SelectableTile1` to `5`; there is no 400). Ore is 600 plus
0 to 14, gems 700 plus 0 to 9.

## Grid shapes

`Grid` in `MatchThree.json` is rows of cell codes: 0 nothing, 1 a rock (or a hazard or wildcard), 2 anything the level
spawns, 3 a blocker, 4 drop zone. Row 0 and column 0 must be 0: the fall code on both sides starts new tiles in the
row above the first full one and never looks at column 0. Mining Practice is 8 by 8 rocks (columns 3 to 10, rows 2 to
9) over a row of drop zone (row 10).

### Where the board appears on screen

Found on 2026-10-28: every match-three board opened left of centre, where the 2009 screenshots show it in the middle.
The server doesn't place the window. The client loads the Flash, asks for its stage size (`getResizeInfo`, answered
with `Resize`), fits the window to the screen and centres it (`MiniGameFlash` in the UI's Lua), and GFx draws the movie
centred in it (its default "show all" mode; no Flash answers `getXYOffset`). StartFlashGame's flag never reaches that
Lua, and MiniGameInfo's start-screen flag only picks `Show` (with the backdrop) or `ShowMicro`, which centre alike.

The level places the board. The Flash draws cell (x, y) at (40x, 40y) in its game clip (`mcGame`) and centres
nothing, and each game's frame (rock, field, furnace, mail rack), bins and meters are drawn around columns 1 to 12. On
the 2009 screenshots Mining's board covers columns 1 to 12 and rows 1 to 9, Harvesting's columns 1 to 12 and rows 2
to 11, Forging's columns 2 to 11 and rows 2 to 11: all centred on column 6.5. Our first boards used columns 1 to 8,
two columns (about 100 pixels on a 1080p screen) left of the frame's middle.

So a board is centred on column 6.5 (columns 3 to 10 for 8 wide), and `MatchThreeLevelDefinition.Problems` refuses one
that isn't. Mining, Farming and Forging use rows 2 to 10; Mail Sorting keeps rows 1 to 9, because its bins start 427
pixels down its game clip (Mining's 474, Farming's 516, Forging's mold meter 545) and row 10 would overlap them.

## Farming, Forging and Mail Sorting

Found on 2026-10-25 by diffing each game's decompiled Flash against Mining's and listing what each `.gfx` exports. The
three run the same server code as Mining byte for byte (only `ConfigServer.GAME_ID` differs) and the same client code
apart from sounds, music, the tutorial and the game name their first frame passes to `GameClient`. What does differ is
the art, and that decides how each game plays:

| Game | Activity wired (MiniGameData row) | Game name (`sGAME`) | GAME_ID | Tiles it can draw | Bins | Mold meter |
|---|---|---|---|---|---|---|
| Mining | Mining Practice 1113 (375) | `mining` | 1 | rocks 1-5, ore 0-14, gems 0-9 | 5 | no |
| Farming | Queensfields Harvesting 410 (109) | `farming` | 60 | crops 1-16 | 5 | no |
| Mail Sorting | Sanctuary Mail Sorting 414 (376) | `mail` | 12 | letters 1-8 | 5 | no |
| Forging | Forging: Quick Shot Bow 420 (138) | `forging` | 62 | metals 1-5, weapon pieces 0-26 | 2 | yes, molds 1-27 |

- **Farming and Mail Sorting have no ore or gem pictures at all** (no `Collectible_Combined_*` clip). Their bins count
  the tiles the player chains: the client flies each chained tile into the bin of its value (`Grid` calls `TileToBin`
  for every tile of a chain), and the server counts chained rocks against their quota. So their levels put quotas on
  selectables.
- **Forging** drops weapon pieces through the drop zone like Mining's ore, and has a **mold meter**: `OnLevelEventMsg
  32 mold total` shows clip `mold<mold>` (mold N is the outline of weapon piece N - 1; 14 is the bow, checked by
  rendering the pictures), and each piece in a bin fills it by `100 / total` frames. With a mold meter the bins show no
  label or count, only the filling mold.
- **No game draws the score power-ups.** Every `PowerUp_Combined` holds more time, dynamite, wild tiles, destruct and
  shuffle; double and five-times score (901, 902) show its first picture instead. The shipped levels leave them out.
- **Bin labels are one off.** A bin's label is the client's `<game>Value<value>` string (`Resources/CodeStringMappings.txt`,
  then the locale). SOE numbered them from 1 where the pictures count from 0: `miningValue612` (copper's low-quality
  picture) says Gold, and 613-614 say Copper; `forgingValue601` is Sword, the picture of piece 0. Farming's 401-416
  (Gummyfruit to Tumtumnip) and Mail Sorting's 401-408 (Sealed Envelope to Box) match their pictures.
- **The game number is only the server's.** `GAME_ID` picked the game's rows in SOE's level tables; no message carries
  it. Evergrove keeps it as `GameNumber` in `MatchThree.json` and logs it.
- Each `.gfx`, its tutorial (`<game>_tutorial.gfx`, `GameTutorialData.txt` rows 2 to 5) and their `img*.dds` pictures
  are on the launcher's pre-cache list.

## Where Evergrove's server differs from SOE's

SOE's server trusted its own client. Evergrove's checks, and recovers instead of ending the game:

- **A level is only passed if it was won.** SOE moved on at `OnNextLevelMsg` whatever the level's result.
- **A power-up must be on the board.** SOE never checked that the clicked tile was the power-up named, so any idle
  tile could be blown up as "dynamite".
- **Nothing counts while paused**, and the game can't be restarted mid-way. SOE's debug "skip level" is ignored.
- **Queue answers must be asked for.** An unasked accept used to empty the server's queue; rejects are resent at most
  10 times.
- **A top-up race is closed.** SOE's server could top up the queue while a reset waited for the client's answer,
  sending the same tiles twice.
- **Out of step means a fresh board.** Where SOE stopped the game on any mismatch (a chain not on the board, ore that
  isn't falling out, a different power-up result), Evergrove rebuilds the board, up to 5 times a level, then stops the
  game (`OnGameStoppedMsg`). The shared random queue can't be resynced, so a power-up desync may recur; the cap ends
  it.
- **The client's own fall rule is mirrored:** new ore that lands straight on the drop zone falls out.
- **Bins are opt-in for rocks.** SOE gave every type a quota bin unless its spawn data said otherwise; here a type gets
  a bin with a quota (or `RequiredPerReward` above 0), and gems always do.

The code: `src/Sanctuary.Game/Minigames/MatchThree/` (`MatchThreeGame`, `MatchThreeLevel`, `MatchThreeGrid`,
`MatchThreeTileGenerator`, `MatchThreeTile`), wired in `FlashMinigameHandler`; the Gateway's main loop ticks the clock
every 50 ms. Tests: `MatchThreeTests`, with a scripted client that plays whole games, dynamite included.

## The pictures must be on the player's disk

The Mining Flash loads its 75 pictures (`img<n>.dds`) and `mining_tutorial.gfx` synchronously, and synchronous loads
only read the packs on disk. The official client lacks them, so before the launcher pre-cached them (2026-10-19,
`tools/client-mods/precache`), Mining showed no copper, bin or frame, and `FailedSyncLoadAssets.txt` listed each
picture. A minigame added later needs its `.gfx` checked the same way: every picture it names must be in the official
packs or on the pre-cache list.

## The end of a game

When its game is over (won, after the end animation; lost; or stopped from the pause menu), the Flash sends
`FRServer_GameOver` and waits. Until 2026-10-25 Evergrove didn't answer, so the game sat with its own mouse pointer
frozen on screen and the player had to find the exit button blind. From FreeRealms.exe, the server's answer is two
packets:

1. `MiniGameGameOverPacket` (39/18): the base fields, then a bool (won), and three ints: stars earned, a time in
   seconds and the score. `MiniGame:OnGameOver` (0x9ad480) keeps them with the game.
2. `MiniGameLeavePacket` (39/19). `MiniGame:LeaveMiniGame` (0x9bf190) then shows the end screen, if the game's own
   client data has its end-screen flag set (game offset 0x908, copied from the client's minigame definition by
   0xc42c60). Otherwise it calls the UI's `QuitGameWithNoEndGameScreen`, the same as the exit button.

`MiniGame:ShowMiniGameEndDialog` (0x9b7170) fills the end screen through the UI's `setMiniGameEndData(status, goals,
bonus goals, time, stars, score)`. Goals come from the game's objectives, which Evergrove doesn't send, so they show
0/0. Closing the end screen sends `CommandPacketClosedMinigameEndScreen` (command 42), which nothing handles yet.

**The client must not be left paused.** `OnActiveMsg(false)` makes the client inactive *and* paused
(`SoeClient.OnActiveMsg` calls `OnPauseMsg(false)`), and `GameClient.GameApplyTick` returns at once while paused. That
tick runs the end-of-level animation, the fallback timer that asks for the next level (`mEndStageTimer`) and the
end-of-game timer that sends `FRServer_GameOver` (`mEndGameTimer`). SOE's built-in server sends `OnActiveMsg(false)`
when a level stops and nothing resumes the client, so after #113 the game still never ended and level 2 never started
(round 4). Evergrove follows it with `OnPauseMsg(true)` (resume), leaving the client inactive but running. Whether
SOE's live server did the same is unknown; this is the fix to check in game.

## What a win pays

In the original game a won match-three game showed "YOU WIN!" and paid coins, stars, ore, gems and a Treasure Ticket
(2009 Mining video). The amounts are lost with SOE's server data. Since 2026-10-29 Evergrove pays a win of the whole
game, every level, the way Cooking pays a recipe: the game's `Coins`, and its `Experience` (job stars) on its
`RewardProfileId` job if the player has that job, both from `MatchThree.json`, through `MinigameReward.Pay` (shared
with `MimicHandler`) and `RewardHelper`, with a reward toast for each. Mining pays Miner stars, Mail Sorting Postman,
Forging Blacksmith, and Farming Chef: Harvesting was the Chef's, and its crops are Cooking's ingredients; there is no
Farmer job. Every amount is a placeholder for the economy pass. Mining Practice pays a little although the
original's practice may have paid nothing, because for now it's the only mining there is. Ore, gems and the ticket
aren't paid.

The end messages come from the client (`FRServer_GameOver`, `OnNextLevelMsg`...), so none of them decides a win: the
server's own game does (`MatchThreeGame.Result`, won only when the client asks for the level after the last one and
the server counted that level's quotas met). `FlashMinigameSession.TryTakeMatchThreeReward` hands the reward over the
first time, and never again in that session, however often the Flash repeats or replays its end; the Gateway asks
after every message and tick. A loss, a game stopped for falling out of step, a quit and `FRServer_GameOver` pay
nothing.

## Still unknown

- **What the quota bin's icon id is.** Mining Practice sends 698, the Copper Ore item's icon. The client stores it and
  never draws it; the bin shows the tile's own picture.
- **The real rewards.** What each game paid, and whether practice paid at all. Real Mining (copper to gold) should
  also pay the ore collected, once the economy pass sets amounts.
- Strings the client looks up by name (`miningValue613`, `MatchThreeReady`...) are in its own
  `Resources/CodeStringMappings.txt`, which maps each name to a string id; see the section on the other games.
