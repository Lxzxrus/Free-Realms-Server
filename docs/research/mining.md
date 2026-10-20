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
row above the first full one and never looks at column 0. Mining Practice is 8 by 8 rocks over a row of drop zone.

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

## Still unknown

- **What the client does at the end of a game.** It sends `FRServer_GameOver` after a win or loss; the live server
  probably answered with a results screen and rewards (`OnShowEndRoundScreenMsg`?). Evergrove sends nothing yet: the
  player leaves with the exit button.
- **What the quota bin's icon id is.** Mining Practice sends 698, the Copper Ore item's icon; check the bin.
- **Rewards.** Practice pays nothing. Real Mining (copper to gold) should pay the ore collected, through
  `RewardHelper`, once the economy pass sets amounts.
- Strings the client looks up by name (`miningValue612`, the tutorial) come from its own tables; any missing show
  blank.
