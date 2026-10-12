# Minigames scout (task 24)

*Research only, 2026-10-11. Nothing here is ported or verified in the game client. Every source's licence is
recorded; "reference only" means read it, don't copy code from it.*

Free Realms had 179 minigame activities in the client's own data, on top of 107 encounters and 131 events. We have
no minigame code. Six public code sources exist, all unfinished. The protocol is now well understood from three
independent directions: EDITz's `minigame` branch (packet classes), 2014 packet captures of the live game, and the
Clone Wars Adventures server `oxide`, whose Flash minigame messages literally carry a `FRServer_` prefix.

## 1. Summary: the five best finds

Ranked by value to players against effort to bring in.

1. **The Flash minigame framework is small and known.** A Flash game needs five server packets (activity list,
   launch invite, minigame info, start, end) plus tab-separated text messages inside `MiniGamePayloadPacket`.
   EDITz's [`minigame` branch](https://github.com/Open-Source-Free-Realms/Sanctuary/tree/minigame) has the packet
   classes and a working "Mining Practice" launch; [`soir20/oxide`](https://github.com/soir20/oxide) (AGPL-3.0,
   Clone Wars Adventures) has the full message vocabulary for a daily wheel, trivia, single-player puzzles and a
   two-player relay game. The cheapest playable minigame is **Spin For The Win**, the daily wheel: one Flash file,
   about ten messages, pays coins. See section 4.
2. **Fishing exists, server-driven, 3,861 lines.** edenfps's upstream
   [PR #31](https://github.com/Open-Source-Free-Realms/Sanctuary/pull/31) reverse-engineered opcode 138 from the
   client binary: cast, bite, fight, reel, catch, six holes' fish tables, treasure and junk, multiplayer sync,
   catches saved to the database. Author-verified at one hole only; no reviews, no tests, open since July. It
   depends on the `minigame` branch's activity packets and on its own zoning, so it is an L-sized port.
3. **The 2014 captures.** The archived
   [`OpenSourceFreeRealms`](https://github.com/Open-Source-Free-Realms/OpenSourceFreeRealms) repo holds 14 packet
   captures of the live SOE servers from 25 and 31 March 2014 (the last week), including full **soccer, kart
   racing and demolition derby** sessions (`ofrserver/Packets/`). brennengreen decoded them far enough to list
   every sub-opcode of those three games. Nobody has decoded the field layouts yet. No licence on that repo, so
   the captures are reference data, not something to copy into ours.
4. **Soccer has 25 packet layouts and a physics ball**, in cillow19's
   [`soccer` branch](https://github.com/cillow19/Sanctuary/tree/soccer) (AGPL-3.0). It is a single-player test
   harness: a soccer zone, a ball that bounces and broadcasts, four client-to-server handlers that do nothing yet.
   No teams, scoring, AI or matchmaking. The hardest half of soccer is still unwritten.
5. **The client's activity table.** `ClientActivityDefinitions.json` on the `minigame` branch (7,414 lines, 417
   entries, extracted from the client) is the authoritative list of every minigame, its category, difficulty,
   members-only flag, tutorial and images. Section 2 is built from it.

Everything else is thinner: a 7,500-line trading-card-game start-screen experiment with gameplay disabled, a
farming prototype, packet serializers nobody sends, and a fork whose six `minigames-*` branches are identical copies
of EDITz's branch with a new README.

## 2. Every Free Realms minigame, grouped by what the server must do

Source: `ClientActivityDefinitions.json`, `AppSystemId = 1` (179 entries). Category ids are the client's; the names
are mine, from the entries in each. Wiki pages from the
[Allakhazam Free Realms wikibase](https://everquest.allakhazam.com/wiki/Category:Minigames_(FR)) (325 minigame
pages, 2009 to 2013) give the goals and prize-wheel rewards. The fandom wiki is behind a bot wall and could not be
read from here.

### Tier A: Flash games inside the client (server launches, scores, rewards)

The client runs these as Flash (`.gfx`) files started by `CommandPacketStartFlashGame`. The server only has to
launch them, answer their messages, and pay out. The fandom wiki's one-line description, quoted in search results,
matches: "Flash-based games that take place on a table (like Checkers) or a Crafting Station (like Cooking or
Forging), and games where you seem to stay in the world (like Racing)". **How much each Flash file asks of the
server is unknown until someone decompiles it** (oxide shows the range: the daily wheel needs the server to pick
the prize; Force Connection needs the server to relay every move).

| Category | Count | Entries | Server work beyond launch and reward |
|---|---|---|---|
| 17 Daily wheel | 1 | Spin For The Win! (id 8) | Pick the prize, enforce once a day. Cheapest game there is. |
| 15 Harvesting (Chef) | 6 | Lakesfield, Queensfields, Chugaroot Farm, Farnum's Farm, Perry's Pastures, Thorny Tillage | Match-3 tiles ([wiki](https://everquest.allakhazam.com/wiki/FR_Minigame:Lakesfield_Harvesting)). Grant harvested ingredients as items. |
| 19 Mining + 21 Smelting (Miner) | 6 + 5 | Copper, Tin, Iron, Silver, Gold Mining, Mining Practice (id 1113); Smelting ×5 | Match-3 ([wiki](https://everquest.allakhazam.com/wiki/FR_Minigame:Copper_Mining)). Ore in, bars out: inventory. |
| 18 Table games | 14 | Chess, Checkers, Mail Sorting ×6 (Postman), Feeling the Beat ×4 (music), Hack 'n' Slash (Warrior), Artifact Hunt | Mail sorting is match-3 ([wiki](https://everquest.allakhazam.com/wiki/FR_Minigame:Merry_Vale_Mail_Sorting)). Hack 'n' Slash is mouse gestures ([wiki](https://everquest.allakhazam.com/wiki/FR_Minigame:Hack_'n'_Slash)). **Chess and Checkers are two-player**: the server must relay moves and referee, as oxide does for Force Connection. |
| 24 Spot the Difference | 3 | Easy, Medium, Hard | Likely self-contained. |
| 12 Cooking (Chef) | 25 (+3 in 99) | "Cooking: <recipe>" | Recipes consume ingredients and produce food items. Needs the item and recipe side done right. |
| 14 Forging (Blacksmith) | 30 (+2 in 99) | "Forging: <weapon>" | Same, with weapons as output. |
| 23 Tower defence and farm puzzles | 44 (24 members-only) | Protect the Hive, Crop Dusting, Gardening 101, Barnyard Bonanza, Honey I'm Home, My Fish MINE, Penguin Produce, Plants Alive, Weed Killing, Weeding the Weeds, We've Come for the Honey, A Fishy Situation; each Easy/Medium/Hard | Tower defence ([wiki](https://everquest.allakhazam.com/wiki/FR_Minigame:Protect_the_Hive)). Self-contained in Flash, most likely. Half are members-only in the data. |
| 26 Treasure War | 1 | Treasure War | Unknown. raisingkaines' code treats it as a trading-card-game alias (category 26 normalised to 11). |
| 99 Misc | 8 | Kai's Challenge, extra recipes and decks | Mixed. |

### Tier B: in-world games the server must simulate or referee

In the live game these ran on a **dedicated minigame server** (udp/20267 in the captures, separate from the world
server on 20232) that the client connected to like a gateway. We would run them as instanced zones in the Gateway,
the way housing lots run.

| Category | Count | Entries | What exists | Server work |
|---|---|---|---|---|
| 13 Fishing (Fisherman) | 6 holes | Sacred Grove Shallows (563), Rainbow Lake (562), Brambleback's Bayou (561), Darklit Lagoon (560), Wintery Basin (564), Frostbitten Banks (565) | PR #31: whole loop, opcode 138, 20 packets | Bite timing, catch roll, rewards. The code exists; its author says the bite "is 100% server-driven" and the client only animates. |
| 22 Soccer (Soccer Star) | 4 | Sacred Grove, Snowhill, Briarwood, Soccer Tutorial | cillow19: 25 packet layouts, ball physics, zone | Teams, matchmaking (queue 11, 1 to 3 per team, 2 teams), ball ownership, goals, AI players (`SendAIInfo`), pickups, timer. Large. |
| 16 Kart Racing (Kart Driver) | 5 + tutorial | Wildwood, Frost Ridge, Tangletrack, Thunder Falls Speedway, Sandstorm Speedway | brennengreen: 34 sub-opcode names from captures | Vehicle state sync at high frequency, NPC racers, gates, pickups, missiles, mines, boosts, countdown. The largest. |
| 16 Demolition Derby (Demo Derby Driver) | 3 + tutorial | Wildwood, Frost Ridge, Tangletrack | brennengreen: 20 sub-opcode names | As racing, minus the track, plus hits and an announcer. |
| Matchmaking-only entries | | Pirate's Plunder (queue 5, members only, 1 to 5 players), Snowball Fighting (queue 51, exactly 4 players) | EDITz's hard-coded queue list | Unknown protocol. brennengreen saw an unnamed "ship combat" family, opcode 94. |

### Tier C: the trading card game

Category 11, 16 entries: the TCG lobby (id 7), seven NPC decks in Trick and Tournament form, a tutorial. It was a
separate game module inside the client (raisingkaines' code refers to `TradingCardGameHandler` scripts and a
`FreeRealmsTCG.dll` launch) with its own rules engine, card collection and a matchmaking queue (41). Nobody has a
card engine. This is a project on its own, not a minigame port.

### Not minigames in the client's sense

The wiki's 84 "Timed" minigames (Beat Finn's Record, Chicken Run and so on) are quests with a timer, started from
the quest log, not activities. They belong to the quest system. `AppSystemId 2` (107 entries: 38 dungeons such as
Bixie Hive and Briarheart Caverns, 65 world encounters, two race circuits) and `AppSystemId 3` (131 entries:
community events, parties, playgrounds) also live in the same file and use the same activity list packets; they
are out of scope here but will reuse this plumbing.

### Cheapest to bring back first

1. Spin For The Win (daily wheel). One Flash file, server picks the prize, pays coins. Fits "Station Cash is
   earned, never bought".
2. Single-player match-3 Flash games (Harvesting, Mining, Mail Sorting) and Spot the Difference, once one Flash
   file's message vocabulary is confirmed in the client. Mining Practice already launches on the `minigame`
   branch.
3. Tower defence (Protect the Hive family). Same framework; many are members-only in the data, which matters for
   Evergrove's "accounts aren't members" launch setting.
4. Cooking, Forging, Smelting. Same framework plus recipes, ingredients and item grants.
5. Fishing. Code exists; L-sized port.
6. Chess and Checkers. Two-player relay; oxide's Force Connection is the model.
7. Soccer, then Demolition Derby, then Kart Racing. Server simulation to write from captures.
8. Trading card game. Not before everything else.

## 3. The minigame protocol

Sources: EDITz's packet classes, the archived OSFR `PacketCodes.cs`
([link](https://github.com/Open-Source-Free-Realms/OpenSourceFreeRealms/blob/main/ofrserver/Server/LibSOE/Core/PacketCodes.cs),
reference only), CarterW24's [PR #76](https://github.com/Open-Source-Free-Realms/Sanctuary/pull/76),
brennengreen's capture notes, oxide, and PR #31's notes. All are tunnelled packets: `short opcode` then a family
sub-opcode. Field layouts are only as good as their source says; "RE" below means reverse-engineered from the
client binary or captures, "guess" means EDITz's class with `Unknown` fields.

### Families

| Opcode | Family | Sub-opcodes known | Source, quality |
|---|---|---|---|
| 167 | Activity service | 1 Activity: 1 ListOfActivities (S→C), 2 JoinActivityRequest (C→S: activityId, timezoneOffset), 4 ListRefreshRequest, 5 UpdateFeaturedStatus; 2 ScheduledActivity: 1 list | EDITz classes, OSFR codes. Works in his test. |
| 147 | Activity manager | 1 ActivityProfileList (job id → quest, icon, badge, title, description), 2 JoinError | EDITz (commented-out sample data for 16 jobs). |
| 175 | Client activity launch (int sub-op) | 1 InviteDetails, 5 ActivityLaunched; the launch request carries `SysHashkey` = 526123817, which is `JenkinsHelper.OneAtATimeHash("Minigame")` in Sanctuary's signed-shift variant (textbook Jenkins gives a different value), min/max members, flags, image and string ids, and a system-specific payload = `MiniGameInfo` + two ints + `MiniGameGroupInfo` | EDITz, confirmed by raisingkaines' independent code. |
| 39 | MiniGame (byte sub-op, then StateId, GroupId, GameId ints) | 1 DataLocked, 2 DataUnlocked, 5 StartGame (C→S), 6 End (C→S), 14 Payload (both ways), 15 Message, 16 Info (S→C), 17 GameStart (S→C), 18 GameOver, 19 Leave, 20 UpdateGameTimeScalar, 21 StarsEarned, 22 RewardStatus, 23 KnockOut, 40 TradingCardStartGame, 44 StoreAdvertisement, 45 LootWheelSetItemToLandOn, 46 LootWheelOnRotationStopped, 47 GameEndScore, 50 GroupInfo, 62 Boss, 67 CreateGameResult, 68 UpdateReward | Names: OSFR codes and `PacketReaderExtensions` on our `main`. Layouts: EDITz (5, 6, 14, 16, 17, 19, 40), PR #76 (17, 18, 23, 45, 46, 47 as serializers). |
| 26 | Command | 12 StartFlashGame {LuaClass `"MiniGameFlash"`, swf name, bool}; 42 ClosedMinigameEndScreen (C→S) | EDITz; oxide has the identical struct. |
| 141 | Matchmaking (short sub-op) | 1 ListQueuesRequest (guid), 2 ListQueuesResponse (list of `MatchmakingQueueDefinition`: id, name, match type, min/max players and teams, start delay, 7 params, description, icon, members-only) | EDITz, hard-coded five queues: 5 Pirate's Plunder, 11 Soccer, 21 Kart Racing, 31 Demo Derby, 51 Snowball Fighting. |
| 138 | Fishing (short sub-op) | 20 packets: RegisterPlayer request/response, CastAnim, Cast, ReelIn, Special request/response, SpawnProxiedFishingBobber, UpdateProxiedFishingBobber (bite flags), SpawnFishRun, Spawn/Update/DespawnProxiedFishingSchool, FishInfoUpdate, UpdateData, FishingResult | PR #31, RE from the binary, with field notes in `FISHING_RE_NOTES.md`. |
| 76 | Soccer (short sub-op) | 25: UpdatePlayerPos 1, GoalMade 2, RegisterPlayer 3, UpdateSoccerBall 4, UpdateGameState 5, SetPlayerTeam 6, SetClientConfig 7, TeamColours 8, SpawnPickUp 9, AcquirePickUp 10, AcquireBall 11, FreeBall 12, ImpulseBall 13, UpdatePlayerState 14, UpdatePlayerStats 15, UpdatePlayerControls 16, StatRollInfo 17, HighArcBall 18, PlayerVariableStats 19, SetSoccerPlayerOneTimeStats 21, SpecialAnimTime 22, UpdateGameTime 23, SendAIInfo 24, BallBounce 25 | cillow19 (layouts, "disassembly-confirmed", meanings mostly `Unknown`), brennengreen (sub-ops seen on the wire: 100% match). |
| 33 | Vehicle race (short sub-op) | 34 names: game/player state, player and NPC counts, client/server race data, GateHit, VehicleHit, Register, Countdown, Create/Destroy/Kick/Reset proxied race, progress, pickups, UseItem, boosts, missiles, explosions, mines, SetRaceConfig | brennengreen, names only. Observed on the wire: 1, 2, 3, 5, 6, 8, 9, 10, 14, 15, 16, 17, 34. |
| 34 | Demolition derby (short sub-op) | 20 names, parallel to racing, plus AnnouncerMessage | brennengreen, names only. Observed: 1, 2, 3, 5, 6, 7, 11, 12, 13, 16, 18, 19. |
| 32 | Combat | names only | brennengreen. |
| 94, 95, 143, 177, 180, 188 | Ship combat (unnamed), MiniGameAdmin (63 FlashCommandAdmin), RepeatingActivity, ClientFlashTimer, InviteAndStartMiniGame, Factory (toolshed, bigmommlors) | Names only. |
| 50 | RewardBundle | sub-type 2 single item, 27 bytes: shows the yellow "You received" text | PR #31, RE. Our `main` already has `RewardBundle*` packets from the quest merge. |

### The Flash game flow

As EDITz's Mining Practice path does it, cross-checked against oxide (whose messages are the same framework; the
Clone Wars client even sends `FRServer_*`):

```
C→S  167/1/2 JoinActivityRequest(activityId)
S→C  175/1   InviteDetails   (MiniGameInfo: name, icon, description, difficulty, Type 3 = client Flash,
                              Unknown13 = swf name; MiniGameGroupInfo: BackgroundSwf)
S→C  175/5   ActivityLaunched(player guid)
S→C  39/16   MiniGameInfo    (same MiniGameInfo; the client shows the start screen)
C→S  39/5    StartGame       (player pressed Go)
S→C  39/17   GameStart
S→C  26/12   StartFlashGame  ("MiniGameFlash", "<game>.gfx")
C→S  39/14   Payload "OnConnectMsg"              ← the Flash file is up
S→C  39/14   Payload "OnServerReadyMsg"
...  39/14   game-specific messages, tab-separated, NUL-terminated UTF-8
C→S  39/14   Payload "FRServer_ScoreInfo\t<text>\t<iconSetId>\t<scoreType>\t<count>\t<max>\t<points>"  (oxide)
C→S  39/14   Payload "FRServer_GameWon" | "FRServer_GameLost" | "FRServer_GameClose"                     (oxide)
S→C  39/18   GameOver (won, scores)  then rewards (loot wheel 45/46 or RewardBundle 50)
C→S  39/6    End           S→C 39/19 Leave
C→S  26/42   ClosedMinigameEndScreen
```

oxide's daily wheel, which is the model for Spin For The Win: on `OnConnectMsg` the server sends
`OnWheelDataMsg\t0\t0\t\t0\t0`, `OnWheelUpdateMsg\t0\t<spins>\t0\t0`, `OnServerReadyMsg`; on
`OnWheelSpinRequestMsg` it rolls a weighted prize and sends `OnSpinInfoMsg\t1\t<prize>`; on `OnWheelStopMsg` it
pays and sends `OnRewardInfoMsg\t0\t0\t<prize>\t0\t0\t0`. Whether Free Realms' wheel Flash uses the same strings
is a client question (section 6).

### Rewards

`MiniGameInfo` carries three `RewardBundleBase` blocks (normal, member, preview) whose fields EDITz left unknown.
PR #76 has `MiniGameLootWheel*` serializers and a `RewardBundle` type. The wiki records each game's prize wheel
contents (coins, clothing, job items, Chrono Limecake, Treasure Tickets), so the data to fill them exists.

## 4. Recommended first step: the Games menu lists activities, and Spin For The Win pays coins

One PR, from `main`:

1. Port from the `minigame` branch (AGPL): `ClientActivityDefinitions.json`, `ClientActivityDefinition` and its
   collection, the opcode 167, 175, 141 and 39 packet classes, `CommandPacketStartFlashGame` and
   `CommandPacketClosedMinigameEndScreen`, the dispatcher routes in both tunnel handlers, and
   `SendListOfActivities` on zone entry (both server types). Fix the `&&` opcode guards as you go; EDITz's
   `TryRead`s have them (`BaseActivityServicePacket`, `BaseMatchmakingPacket`, `BaseMiniGamePacket`,
   `ClientActivityLaunchBasePacket`). Credit EDITz.
2. Replace his two hard-coded activity ids with a small `MinigameLauncher` that answers `JoinActivityRequest` for
   activity 8 only (everything else: log and ignore, no crash), sends the invite, launched and info packets, then on
   `StartGame` sends `GameStart` and `StartFlashGame` with the wheel's `.gfx` name from the client.
3. A `DailyWheelGame` modelled on oxide's `DailySpinGame` (AGPL, rewrite in C#): weighted prize buckets from a
   settings file, one spin per account per day stored in the database, coins granted through the existing coin
   path, the yellow reward text through `RewardBundle`.
4. Tests: packet round-trips, the once-a-day rule, the prize distribution.

Needs from Nate's client before coding (section 6, questions 1 to 3): the wheel's Flash file name and its message
strings. Without them, step 2 and 3 are guesses.

**How Nate tests it in game:** open the Games menu; the list should fill with real names, icons and difficulty
stars (179 client entries, 238 world entries). Click Spin For The Win; the start screen shows the right name and
description; Go opens the wheel; spinning stops on a prize; the coin total rises and the yellow "You received" text
shows; closing returns to the world; a second spin the same day is refused with whatever the wheel does on
`OnWheelUpdateMsg` with zero spins; after relog the refusal holds; the next day it spins again. Also click any other
game: the client must not hang (the server ignores the request and logs it).

Second step after that: fishing, as its own task, once the activity plumbing is in. Port PR #31 onto our zoning
(`Player.TeleportToZone`, zone definition files for the six `*_fishing_*` zones), keep its docs, confirm with
edenfps that the code is offered under AGPL (the fork has no licence file; the PR targets an AGPL repo).

## 5. Findings

| Name | Link | Licence | Last activity | What it holds | State | Porting it | Effort | Value |
|---|---|---|---|---|---|---|---|---|
| OSFR `minigame` branch (EDITz) | [tree/minigame](https://github.com/Open-Source-Free-Realms/Sanctuary/tree/minigame) | AGPL-3.0 (LICENSE in branch) | code 2026-05-31 ("Work in progress"), main merged 2026-07-18; 162 commits behind `main`, not 138 | 49 files, 9,723 lines: `ClientActivityDefinitions.json` (417 activities), activity list/join, launch invite, MiniGame info/start/payload/end/leave, matchmaking queue list (5 hard-coded), `StartFlashGame`; `StartingZone` sends both activity lists on zone entry | Work in progress. Two activity ids hard-coded (7 TCG lobby, 1113 Mining Practice); payload handler only answers `OnConnectMsg`; no rewards, no scoring, no persistence; `&&` opcode guards | Merge the packet layer; rewrite the two handlers | M | High as the foundation; medium on its own (a populated Games menu, one Flash game that opens) |
| Fishing, upstream PR #31 (edenfps) | [pull/31](https://github.com/Open-Source-Free-Realms/Sanctuary/pull/31), branch [edenfps:fishing](https://github.com/edenfps/Sanctuary/tree/fishing) | No licence file in the fork at that commit (upstream added LICENSE 2026-07-16). Offered as a PR to the AGPL repo; confirm with the author | Opened 2026-07-04, labelled 2026-07-21, no reviews; targets the `minigame` branch | 51 files, 3,861 lines: 20 fishing packets (op 138), `FishingSession` state machine (868 lines), 6 holes' fish tables with real item ids, rods and lures, treasure and junk, multiplayer sync, DB persistence of catches, lure use via the ability handler, debug commands; four docs (`FISHING_HANDOFF.md`, `FISHING_RE_NOTES.md`, `FISHING_1TO1_PLAN.md`, `FISHING_WIKI.md`) | Author says it works end to end; only Sacred Grove Shallows (563) has a verified spawn, the other five holes' positions are guesses; no XP, no level gating, no collections, no tension minigame; reel timing decides catch or miss; one tester | Depends on the `minigame` packets; adapts `BaseZone`, `StartingZone`, `Player`, `GatewayService` tick; needs zone definitions for the fishing zones in our zoning | L | High |
| `soir20/oxide` (Clone Wars Adventures server) | [github.com/soir20/oxide](https://github.com/soir20/oxide) | AGPL-3.0 | 2026-10-08, 574 commits, active | Rust. 11,400 lines of minigame code plus a 10,400-line `config/minigames.yaml`: Flash game lifecycle (`FlashPayload`, `StartFlashGame`, create/start/end/leave, score entries, score-to-credits expressions), daily wheel, daily trivia, daily holocron, two-player Force Connection relay, Fleet Commander, 3D saber duel and saber strike, matchmaking groups, stage groups and portal categories | Working, players use it with the 2014 CWA client | Reference for the Flash message vocabulary and the server design; rewrite in C#. Opcodes differ (CWA minigame op 0x27) but the message strings are Free Realms' | S per Flash game | High |
| 2014 live captures, archived `OpenSourceFreeRealms` | [ofrserver/Packets](https://github.com/Open-Source-Free-Realms/OpenSourceFreeRealms/tree/main/ofrserver/Packets) | **None. Reference only** (repo archived 2024-12-04, no LICENSE) | Captures dated 2014-03-25 and 2014-03-31 | 14 pcap/pcapng files, 170 MB, SOE servers at 64.37.170.6: world on udp/20232, minigame server on udp/20267. `packets_soccer_minigame` (9,822 packets), `packets_racing_minigame` (45,378), `packets_demoderby_minigame` (41,969). Also `PacketCodes.cs`, the full opcode and sub-opcode name table | Raw data, encrypted and compressed SOE transport | Decode with Sanctuary's `UdpLibrary` or brennengreen's stdlib scripts; then map field layouts | M to decode, L per game to simulate | High for racing, derby, soccer; nothing else has their layouts |
| brennengreen `feat/minigame-combat-quest-protocol` | [branch](https://github.com/brennengreen/Sanctuary/tree/feat/minigame-combat-quest-protocol) | No licence file (pre-LICENSE fork). Sub-opcode names are protocol facts; the tooling is 230 lines of Python | 2026-07-04, one commit | `MINIGAME_RE_NOTES.md`; enums for op 33 (34 names), 34 (20), 76 (25), 32, 49; routing handlers that log; `research/packet-analysis/` (pcapng reader, SOE decode, histogram, verify) | Recognition layer only; author says so | Re-derive the enums from our own opcode table or ask for an AGPL grant | S | Medium |
| cillow19 `soccer` | [branch](https://github.com/cillow19/Sanctuary/tree/soccer) | AGPL-3.0 | 2026-09-10 ("test"); 229 commits past the minigame branch, most unrelated (referee, yo-yo, silly string) | 39 soccer files, 2,788 lines: 25 packet classes with wire layouts, `SoccerZone` (bw_soccer, zone file `BwSoccer.json`), `SoccerBall` server physics (gravity, bounce, friction, broadcast every tick), 4 C→S handlers (empty), `/soccer` admin command and an opcode probe | Test harness. Single player, one team, no scoring, no AI, no matchmaking; field meanings are `Unknown`; the top-level opcode 76 is marked unconfirmed in the code though brennengreen's captures confirm it | Port packets and zone after the activity plumbing; write the game | L | High later |
| bigmommlors `farming-prototype` | [branch](https://github.com/bigmommlors/Sanctuary/tree/farming-prototype) | AGPL-3.0 | 2026-09-24, 5 commits (Cursor-generated) | Bandit Hideout encounter (AppSystem 2) enter, exit, re-enter via `InstanceZone`; farming: private Wilds farm instance, Factory op 188 toolshed packets, shovel clears rocks, weeds and trees persisted in a new `FarmObstacles` table (SQLite and MySQL migrations), tests, `!farmtest` command, `factory-shovel-animation-investigation.md` | Prototype with experiment files; farming is not a Free Realms minigame (the wiki's Farming type is the Flash puzzles) | The encounter flow is the useful part, for dungeons later | M | Low for minigames |
| raisingkaines `trading-card-game` | [branch](https://github.com/raisingkaines/Sanctuary/tree/trading-card-game) | **No licence file at that commit.** The task allows his public AGPL repos; this fork carried no licence then. Reference only | 2026-07-03, one commit, 7,511 lines | TCG start screen and lobby definitions, `GenericMinigameStartScreenBridge` (reads the client's `MiniGameData.txt`, builds invite, launched and info packets for any activity, uses `game_hidden.gfx` for Flash-style games), matchmaking state, session registry, 288-line diagnostics logger, a 1,360-line `TcgDetailUiPatch` | Experiment. `TCG_START_SCREEN_ONLY = true` suppresses the gameplay launch; no card engine; code style is decompiler output (`num`, `result2`, `flag`) | Do not port. The useful facts (TCG lobby row 727, queue 41, string ids 3388 and 401571) are in this report | XL | Low now |
| CarterW24 PR #76 | [pull/76](https://github.com/Open-Source-Free-Realms/Sanctuary/pull/76) | Submitted 2026-07-15 to the then-unlicensed repo; closed unmerged 2026-08-30 | 17 files, 908 lines | Serializers: encounter op 41 (details, state, zone ready, player enter, is fighting), minigame GameOver, GameStart, KnockOut, LootWheel set and stopped, StateRemove, objectives op 45 and 47, combat ruleset op 62, `RewardBundle`; fixes a missing 8-byte header in `BaseEncounterPacket.Write` | "Nothing sends these yet" | Re-type the loot wheel and game over packets from the OSFR code table | S | Medium (prize wheel after a game) |
| datah4zard `minigame` | [branch](https://github.com/datah4zard/Sanctuary/tree/minigame) | AGPL-3.0 | 2026-08-02 | EDITz's branch merged with main as of August, one fix: `Unknown3` → `Unknown` deserialization, and a note that `AppSystemId` 1 = single player, 2 = multiplayer or dungeon, 3 = event | Same as EDITz's | Take the note | S | Same as row 1 |
| CoderA05 `Sanctuary-Mainline` | [repo](https://github.com/CoderA05/Sanctuary-Mainline) | AGPL-3.0 | 2026-07-07 | Branches `minigames-Mining`, `-cooking`, `-derby`, `-micro-games`, `-racing`, `-smelting`, `playgrounds` | **All seven are the same commit**: EDITz's minigame branch plus a README that promises "minigame logic, TCG service restoration, GFX transitions". No code of its own | Nothing | — | None |
| `ClientActivityDefinitions.json` | in row 1 | Client data, extracted by EDITz | 2026-05-31 | 417 activities: id, system, category, images, string ids, server type, members-only, tutorial, difficulty, featured windows | Complete for the 2014 client as far as anyone knows | Ship as a resource | S | High |
| Allakhazam FR wikibase | [Category:Minigames (FR)](https://everquest.allakhazam.com/wiki/Category:Minigames_(FR)) | Site content, reference only | 2009 to 2013 | 325 minigame pages: type, starter object, difficulty, goals in five tiers, prize-wheel rewards, tiles, related quests; 17 type categories, 27 starters | Readable | Data for goals and rewards | — | Medium |
| Upstream `main` | [Sanctuary](https://github.com/Open-Source-Free-Realms/Sanctuary) | AGPL-3.0 | 2026-10-11 | Already on our `main`: fishing stats in `ClientPcData`, `NonMiniGame` item flag, the opcode 39 names in `PacketReaderExtensions`, `LobbyGameDefinition` request and response (sends one "Test Lobby Game"), points-of-interest teleport to Sacred Grove Shallows fixed in PR #97 | | | | |

## 6. Check in the client

Only the client can answer these. Each one blocks a step in section 4.

1. **Which Flash files are the minigames?** List the `.gfx` and `.swf` files in the client's UI packs whose names
   mention wheel, daily, harvest, mining, mail, chess, checkers, hidden, cooking, forge, defense, spot. EDITz used
   `game_hidden.gfx` for Mining Practice; what is the daily wheel's file? The activity's images are
   `daily_wheel_detail.dds` and `daily_wheel_thumb.dds`.
2. **What does the wheel Flash say?** Open its file in JPEXS/FFDec and list the strings sent to and expected from
   the server: do `OnConnectMsg`, `OnWheelDataMsg`, `OnWheelSpinRequestMsg`, `OnSpinInfoMsg`, `OnRewardInfoMsg`
   and `FRServer_*` appear, and with how many tab-separated arguments?
3. **`MiniGameData.txt`**: does the client data hold this table (raisingkaines' code reads rows of id, type id,
   name id, description id, image id, difficulty; TCG row 727, type 23)? It would map activities to Flash files
   and minigame types.
4. **Does the Games menu already open on our `main`?** It should list nothing. Does clicking anything hang the
   client? That is the baseline for the first PR.
5. **Fishing holes**: PR #97 fixed the Sacred Grove Shallows teleport. Does the points-of-interest list show the
   six holes, and does teleporting there put the player by the water (fishing UI absent, as expected)?
6. **The string ids**: do `409962` and `409969` (Spin For The Win name and description) resolve to text in the
   client's string table? Same for `426304` (Kart Racing) and `3388` (Trading Card Game). This also tests the
   quest-text question from task 23.
7. **The members-only puzzles**: with accounts not members, does the client grey out the 24 members-only tower
   defence entries by itself from the `MembersOnly` flag, or does the server have to filter them out?

## 7. Dead ends

- **Upstream PRs and issues.** All 127 PRs scanned by title; only #31 (fishing) and #76 (serializers) touch
  minigames. The issue search for minigame, fishing, racing, soccer, derby and activity returns nothing.
- **The 39 forks.** Branch names of every fork were listed. Minigame-named branches: CoderA05's seven identical
  copies (above), cillow19 (soccer: real; `minigame`: identical to upstream), brennengreen (`minigame` identical;
  the protocol commit: real), datah4zard, AlchemyDevelopment and Sulphural (`minigame`: identical to upstream or a
  main merge). ImAlko's `feature/drop-tables-and-rewards` and `feature/progression` are reward and XP work, useful
  for payouts later, not minigames. GroaxyVRC, amuralle, raisingkaines' housing branches: nothing.
- **edenfps/Sanctuary `main`.** The "racing + minigame merg" of 2026-06-27 and its merge of the `minigame` branch
  were both reverted the same day. The reverted content is the upstream `minigame` branch; there is no racing code
  in it.
- **This repo's `import/*` branches.** No minigame code in `housing-full-archive`, `quest-upstream-v2`, `frl-main`
  or `frl-quests`. FreeRealms-Legacy never published minigames.
- **Free Realms Sunrise** ([frsunrise.com](https://www.frsunrise.com/)). Advertises fishing, footraces, chicken
  round-ups, tower defence, chess and checkers. Closed source, no licence, no repository. Nothing to use.
- **CWAEmu** ([org](https://github.com/Clone-Wars-Adventures-Emulator)). A Unity remake, not a server; its FAQ
  says so. Its `ForgeLightToolkit` (MIT) imports ForgeLight assets into Unity, which may matter for the housing
  scout, not for minigames. `starwarsfreak219/oxide` is a fork of soir20's.
- **h1emu and PlanetSide 2 emulators.** Not searched: those games have no minigames and the activity, matchmaking
  and Flash families are Free Realms and Clone Wars specific.
- **The fandom wiki** (`freerealms.fandom.com`, including the `Mini-Games` page). Behind a JavaScript challenge;
  every fetch returned 402 or 403. The Allakhazam wikibase covers the same ground.
- **Web search for protocol documentation.** No blog, forum post or video documents the minigame packets. The only
  documentation is in the sources above.
- **`Spin For The Win` on the wiki.** The Allakhazam page does not exist; no source describes its prizes.

## 8. Sources read

Repositories were fetched over git and read in this session; nothing was built or run from them. Upstream
Sanctuary `main` at `3fff847`, `minigame` at `6fcdf6b`; PR heads #31 (`70c664b`), #76 (`9236220`); edenfps
`fishing` `70c664b`; cillow19 `soccer` `10d6192`; brennengreen `feat/minigame-combat-quest-protocol` `33ecdd5`;
bigmommlors `farming-prototype` `17b5a4c`; raisingkaines `trading-card-game` `961b0a3`; datah4zard `minigame`
`c9ebad6`; CoderA05 `minigames-*` `4f5c7ea`; soir20/oxide `ca3b50e`; OpenSourceFreeRealms `ace6a82`.

## 9. Answers from the client (local session, 2026-10-12)

Checked against the 2014 client on Nate's PC by reading its packs and data files, and decompiling `game_wheel.gfx`
with JPEXS FFDec. Nothing from the client is committed; this section only describes what's there.

**1. Which Flash files are the minigames?** All in the client's packs (`Assets_manifest.txt`), as `.gfx` with a
matching `.swf`: `game_wheel` (the daily wheel), `game_mining`, `game_smelting`, `game_cooking`, `game_forging`,
`game_mailsorting`, `game_hidden` (EDITz's Mining Practice launch used this one), `game_mahjongmatch`,
`game_artifact`, `game_farming`, `game_music`, `game_sword`, `game_halloween`, `game_chess`, `game_checkers`,
`towerdefense`, most with a `*_tutorial` or `tutorial_*` file. Shared screens: `MinigameStartScreen`,
`MinigameEndStageScreen`, `MinigameDetail`, `MinigameTitleBar`, `minigameTutorial`, `minigameLevel`, the
`minigameStatus*Pane` files, `game_backdrop`, and the post-game prize wheel `LootWheelOfAwesomeness`.

**2. What does the wheel Flash say?** The same vocabulary as oxide. All Flash games share SOE's framework
(`com.sonyonline.game.SoeGameFramework`, `SoeClient`, `SoeInterfaceFreeRealms`), which sends through
`fscommand("FR_event", "SendMsg(...)")`. The wheel's own messages and arguments, from its client and server
classes:

| Direction | Message | Arguments |
|---|---|---|
| S→C | `OnWheelDataMsg` | id, type, slots, wheelStringID, msgStringID |
| S→C | `OnItemDataMsg` | itemIconID, tooltipID, quantity, itemNameID, index, tintAlias (one per slot: the prize shown there) |
| S→C | `OnItemDataCompleteMsg` | spinCount |
| S→C | `OnWheelUpdateMsg` | id, spins, consecutiveTimes, timeUntilNextSpin |
| S→C | `OnServerReadyMsg` | none |
| C→S | `OnWheelSpinRequestMsg` | wheelID |
| S→C | `OnSpinInfoMsg` | desiredCategory (the slot index to land on) |
| C→S | `OnWheelStopMsg` | wheelID |
| S→C | `OnRewardInfoMsg` | itemIconID, tooltipID, quantity, itemNameID, rewardMsgStringID, tintId |
| C→S | `OnChangeWheelRequestMsg` | wheelID (the client can page between several wheels) |
| S→C | `OnWheelChangedMsg` | wheelID |

The framework's common messages, for every Flash game: from the client `FRServer_Connect`,
`FRServer_ReadyForGameData`, `OnStartGameMsg`, `FRServer_StatUpdate` (stage, stat, count), `FRServer_Pause`,
`FRServer_RoundTransitionPause`, `FRServer_CompleteLevel`, `FRServer_StopLevel` (level, …),
`FRServer_ScoreIncrement`, `FRServer_ScoreSet`, `FRServer_GameQuit` (score), `FRServer_GameWon` (score, quantity),
`FRServer_GameLost` (score), `FRServer_GameOver`, `FRServer_GameClose`; to the client `OnLevelInitMsg`,
`OnLevelEventMsg`, `OnNextLevelMsg`, `OnStopLevelMsg`, `OnGameStoppedMsg`, `OnGameWonMsg`, `OnGameLostMsg`,
`OnActiveMsg`.

**The wheel's Flash file carries SOE's own test server** (`Wheel.WheelGameServer`), a precise reference for the
flow: on start it sends, per wheel, `OnWheelDataMsg` then `OnWheelUpdateMsg` (spins left, consecutive days 1, time
until next spin 0). On `OnWheelSpinRequestMsg` with spins left it answers `OnSpinInfoMsg` with a slot index (random
in the test server), and with none left it refuses ("client hacking"). On `OnWheelStopMsg` it sends
`OnRewardInfoMsg`. On `OnChangeWheelRequestMsg` it switches wheel if that wheel has spins. The live game's prize
tables (`MicroGameWheel.txt`, `MicroGameWheelCategory.txt` and `MicroGameWheelSlots.txt`: wheels with start and end
dates, categories with reward sets, slots) were server data and **are not in the client**, so Evergrove designs its
own prizes.

**3. `MiniGameData.txt`:** yes, in `Resources/`, 783 games with columns ID, TYPE, NAME_ID, DESCRIPTION_ID, ICON_ID,
DIFFICULTY, MEMBERS_ONLY, SHORTCUT, MIN/MAX_PLAYERS, START_SOUND_DEFINITION, PRELOAD_FILE, LEADERBOARD_ID. Alongside
it: `MiniGameTypeData.txt` (22 types; types 3, 8, 9, 14, 16 and 23 are flagged IS_FLASH, type 22 IS_MICRO),
`MiniGameGroupData.txt` (20 groups: background swf, stage progression, purchase item/bundle) and
`MiniGameGroupLinks.txt` (610 links: group, parent game, linked game, stage number). It doesn't name the `.gfx`
file of each game; that comes from the server (`MiniGameInfo`, EDITz's `Unknown13`).

**6. String ids:** not checked. The client looks numeric string ids up itself, and the quest ids we already send
show as text in game, so a populated Games menu will answer it.

**Still for Nate in game:** 4 (does the Games menu open on `main`, and does clicking hang the client), 5 (the
fishing holes in the points-of-interest list), 7 (does the client grey out members-only games by itself).
