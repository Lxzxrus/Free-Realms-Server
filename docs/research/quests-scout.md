# Quests scout (task 23)

*Research only, 2026-10-12. Nothing here is ported or verified in the game client. Every source's licence is
recorded; "reference only" means read it, don't copy code from it. Repositories were fetched over git and read in
this session; nothing was built or run from them. One wrong turn is recorded in section 7.*

We have JadenY's quest engine with 19 quests loaded and 5 parked. Nobody in the Sanctuary network has more quest
content than we do: Sulphural's `quest-upstream-v2` tip is byte-for-byte our `import/quest-upstream-v2`, and the
other 36 forks hold no quest data at all. What exists in the open is **data, not code**: a 1,401-page fan wiki with
every quest's giver, type, objectives and rewards; the 2014 live-server captures, which carry a real character's
quest log with its text ids; a parser for SOE's string-table format; and three upstream PRs (experience, collection
rewards, the quest data model) that are small and mergeable. The text problem has a concrete test now, section 3.

## 1. Summary: the five best finds

Ranked by value to players against effort to bring in.

1. **The string table's format is public, and the live quest log gives 22 known-good text ids to test it with.**
   SOE's locale files (`en_us_data.dir` + `en_us_data.dat`, the same pair PlanetSide 2 and Clone Wars Adventures
   ship) are parsed by [`psemu/soe-locale`](https://github.com/psemu/soe-locale) (MIT, 2015): the `.dir` is a text
   index of `id<TAB>offset<TAB>length`, the `.dat` holds `id<TAB>flags<TAB>text` per line, and the id **is** the
   number the protocol sends. Decoding the 2014 captures (find 2) yields 22 quests with their title, description,
   helper and objective ids from the live server, so Nate can settle "do the protocol's ids equal the table's row
   ids" with one lookup, section 6 question 1. If yes, every quest's text is recoverable by searching the table for
   the wiki's quest title and reading the neighbouring rows. Effort S, value high: it unblocks all quest content.
2. **The 2014 captures hold a real quest log, decodable with 300 lines of Python.** The archived
   [`OpenSourceFreeRealms`](https://github.com/Open-Source-Free-Realms/OpenSourceFreeRealms) repo's 14 captures
   (reference only, no licence) are compressed but not encrypted. Decoded here: the character's `SendSelf` carries
   22 active quests (ids, text ids, objectives, rewards), 33 to 238 `QuestObjectiveActivated` packets per capture,
   one objective completion, 2,671 distinct NPC adds with name ids, and 130 NPC badge notifications. The quest
   table is in section 3. Effort S (the decoder exists in this session's notes and is described in section 3),
   value high.
3. **Experience and level-ups exist as a PR.** ImAlko's upstream
   [PR #120](https://github.com/Open-Source-Free-Realms/Sanctuary/pull/120) (AGPL-3.0, open, 18 files, 571 lines)
   grants stars, fills the level bar and ranks up at 20 levels from `RankLevels.json`. Our `Player.AwardXp` sends a
   zeroed experience packet and stores nothing, so quest `RewardExperience` is cosmetic today. Effort M, value high:
   the first time a quest pays stars that stick.
4. **The Allakhazam wiki is the quest database.** 1,401 quest pages with giver, place, type (Combat, Timed,
   Minigame, Errand, Search), objectives, rewards and series order, plus 60 per-place and 17 per-job categories.
   Errand and Search quests are what our engine can run. Section 4 ranks the questlines by it. Effort none (data to
   read), value high.
5. **Quest NPCs are mostly already in the world.** Our `Npcs.json` names 1,656 of 3,422 definitions and
   `FabledRealms.lua` spawns all of them. Of the 91 NPCs the wiki names for the 15 questlines in section 4, 65 are
   spawned under their wiki name, including every NPC of the Seaside Tour, the Forest Troll Village chain, the
   Pixie Nursery chain, Wugachug's Sasparilla Hunt and the Chugawug Friends chain. Snowhill's hub quests are the
   big gap: none of their seven NPCs is named.

Dead on arrival: the Clone Wars Adventures server `oxide` has an empty `Quest {}` struct; the old OSFR server has
no quest code; Free Realms Sunrise and New Dawn publish nothing beyond upstream's code.

## 2. Findings

| Name | Link | Licence | Last activity | What it holds | State | Porting it | Effort | Value |
|---|---|---|---|---|---|---|---|---|
| Upstream PR #120 "Exp granting & level-ups" (ImAlko, `feature/progression`) | [pull/120](https://github.com/Open-Source-Free-Realms/Sanctuary/pull/120), head `4c580de` | AGPL-3.0 (LICENSE in the fork and upstream) | 2026-09-04; open, "enhancement" label, no reviews | `RankLevels.json` (20 levels, 100 to 1,000 stars each), `RewardHelper` (grant item, currency, experience; rank-up notification; reward toast with coins), `RankLevelDefinitionCollection`, `ClientUpdatePacketUpdateProfileExperience` (op 14: profile, rank, rank percent, stars available, stars earned) and `UpdateProfileRank` (op 18), `/exp` test command, `Player.OnLevelUp` hook, new characters start at level 1 not 20, `RewardBundleBase.Unknown1/RewardKind` renamed `Coins/Experience` | Works for its author; one curve for all jobs; author notes the client plays level-up effects at 4, 8, 12, 16 (hardcoded in `notifications.gfx` and `ScriptsBase.bin`) while icons change at 5, 10, 15, 20 | Replaces our `RewardManager`; our `QuestManager` must call `TryGrantExperience`; our `RewardBundleBase` field rename matches ours (`RewardKind` carries experience in the live log too, section 3) | M | High |
| Upstream PR #123 "collection reward bundle entry" (amuralle) | [pull/123](https://github.com/Open-Source-Free-Realms/Sanctuary/pull/123), head `871b2ef` | AGPL-3.0 | 2026-09-10; open, draft | One 15-line class: entry type `0xA` + collection id in a reward bundle. Wire format only; does not grant the collection | Serializer only | Our `QuestDefinition.RewardCollectionId` already exists; this is the packet half | S | Low alone |
| Upstream PR #130 "quest definitions, JSON loading, CharacterQuests" (Sulphural, `quest-data-model`) | [pull/130](https://github.com/Open-Source-Free-Realms/Sanctuary/pull/130), head `c370ffc` | AGPL-3.0 | 2026-09-26; open, reviewed by ImAlko | The data layer of our quest system, split out for upstream. Differs from our `main` in two places worth taking: exclusions are checked **before** the prerequisite (ImAlko's review found that two exclusive branches sharing a prerequisite could both be offered), and talk targets are a `TalkTarget {Guid, DialogueId, ResponseId}` list instead of our three parallel lists. Same 24 quests as ours | Under review; ImAlko also asks for `QuestGoal` subclasses per goal type | Port the exclusion-order fix (one `if` moved) and consider `TalkTarget` | S | Medium (a real offer bug) |
| Upstream PR #102 (closed) and #109 (closed), Sulphural | [pull/102](https://github.com/Open-Source-Free-Realms/Sanctuary/pull/102), [pull/109](https://github.com/Open-Source-Free-Realms/Sanctuary/pull/109) | AGPL-3.0 | Closed 2026-08-06 and 2026-09-19 | #102 was the whole quest system (80 files), closed by datah4zard as too large to review, with a six-PR split plan (data model, quest packets, dialog packets, badges, manager, Take Me There). #109 is #130's predecessor. #102's description says the 18 quests were "hand-extracted from the author's local main branch"; the notification format was "byte-exact against a real capture" | Superseded | Nothing; already ours via `import/quest-upstream-v2` | — | — |
| Sulphural/Sanctuary-fork | [repo](https://github.com/Sulphural/Sanctuary-fork), branches `quest-upstream-v2` `8d63294`, `quest-data-model`, `main` `b35114f` | AGPL-3.0 | 2026-09-26 | `quest-upstream-v2` is identical to our `import/quest-upstream-v2` (same commit). `main` has no `Quests.json`. The "big merge" `archive/sulphural-main-2026-08-18` named in `CLAUDE.md` is **not on our origin** (`git ls-remote` lists no `archive/*` branch) | Nothing new | — | — | — |
| 2014 live captures | [ofrserver/Packets](https://github.com/Open-Source-Free-Realms/OpenSourceFreeRealms/tree/main/ofrserver/Packets), repo `ace6a82` | **None; archived repo. Reference only.** Field values (ids) are protocol facts | Captures 2014-03; repo archived 2024-12 | 14 files, 170 MB, one character on 64.37.170.6 udp/20232. `possible_character_data.pcapng` is exactly the `SendSelf` burst. `packets_1*`, `p1` to `p12`, `packets_2` to `_4`: the same character's sessions; `packets_2`, `p2`, `p12` include one objective completing. Decoded contents in section 3 | Raw SOE UDP, zlib-compressed, 2-byte CRC, **no encryption** (confirm packet methods `(1, 0)`) | Read with the decoder described in section 3; no code to port | S | High |
| `psemu/soe-locale` | [repo](https://github.com/psemu/soe-locale), `a06ed34` | MIT | 2015-02-14, 2 files | Node parser and writer for SOE `.dat`/`.dir` locale pairs: `.dir` lines `id\toffset\tlength\td`, `.dat` lines `id\tflags\ttext`, UTF-8 BOM. Written for PlanetSide 2, whose client ships the same `Locale/en_us_data.dir` and `.dat` names | Working, tiny | Re-implement in 30 lines of C# or Python as a dev tool, or run as is | S | High |
| `psemu/ps2ls` | [repo](https://github.com/psemu/ps2ls), `23fc871` | None stated | 2013 | ForgeLight pack and model inspector. **No locale code** | — | Nothing | — | None |
| Allakhazam Free Realms wikibase | [Category:Quests (FR)](https://everquest.allakhazam.com/wiki/Category:Quests_(FR)) | Site content, reference only. Quest text is SOE's | 2009 to 2013 | 1,401 quest pages; subcategories Members Only (425), Obsolete (81), Repeatable (145), by Job (17 jobs), by Place (60 places, counts in section 4). The [FR Quest template](https://everquest.allakhazam.com/wiki/Template:FR_Quest) fields: `type` (Combat default, Timed, Minigame, Errand, Search), `job`, `membersonly`, `repeatable`, `startmob`, `place`, `intro`, `text`, `objectives`, `rewards`. Place pages list series order and givers | Readable; many pages last edited 2009 to 2010; "Obsolete" marks removed quests | Data for `Quests.json` authoring | — | High |
| Upstream `main` Adventurer's Journal data | [`WorldZone.cs`](https://github.com/Open-Source-Free-Realms/Sanctuary/blob/main/src/Sanctuary.Game/Zones/WorldZone.cs) `SendAdventurersJournalInfo` | AGPL-3.0 | On our `main` already | 4 regions, 10 hubs, **58 hub quest ids** (2489 to 2600) and **16 sticker quests** (2563 to 2606) with their name and description string ids (5100479 and up, 442851 and up). A comment says the packet is required by the client's `FabledRealmsAreas.xml` "Newbiezone" area. These are the 2013 journal's real quest ids; our 2563 and 2564 "Introduce Yourself" come from here. 14 of the 16 sticker quests have no definition | Data | None; it tells us which quest ids the client expects for the journal | — | Medium |
| Our `Npcs.json` and `FabledRealms.lua` | `src/Resources/Npcs.json`, `src/Scripts/Zone/FabledRealms.lua` | AGPL-3.0 | On `main` | 3,422 NPC definitions, 1,656 with `Name` and `NameId`, all spawned (upstream's #42 "community-contributed NPCs", #69 made the guids stable "because quests depend on them"). Upstream has 3,426 | Data | None | — | High for quests |
| ImAlko/Sanctuary other branches | [repo](https://github.com/ImAlko/Sanctuary) | AGPL-3.0 | 2026-09-28 | `feature/drop-tables-and-rewards` (weighted drop tables, `Rewards.json`, merged upstream in part), `pathfinding` (the `.map` pathfinder our Take Me There uses), `prototype/npc-behavior` and `feature/task-management` (NPC action sequencing: move-to, wait, parallel), `major-feature/zones-testing` (three instance zone files) | Prototypes | NPC behaviours would matter for escort or "follow" quests later | M | Low now |
| amuralle/Sanctuary `npc-visible-notifications` | [repo](https://github.com/amuralle/Sanctuary) | AGPL-3.0 | 2026-07-08, 84 lines | NPCs carry a notification list sent on spawn (`NotificationInfo`) | Superseded by JadenY's retail-format badges on our `main` | Nothing | — | None |
| raisingkaines/FreeRealms-Legacy (public) | [repo](https://github.com/raisingkaines/FreeRealms-Legacy), `9e08202` | AGPL-3.0 (LICENSE present) | 2026-09-23 | Our own `CharacterQuests` migration (`20260915000000`), `DbCharacterQuest` and `QuestDefinitionCollectionTests`, nothing more. Confirms the task note | — | Nothing | — | None |
| brennengreen `feat/minigame-combat-quest-protocol` | [branch](https://github.com/brennengreen/Sanctuary/tree/feat/minigame-combat-quest-protocol), `33ecdd5` | No licence file | 2026-07-04 | Recognises op 49 as `BaseQuestPacket` and routes it to a logger; lists "quest state tracking" as not done. His `research/packet-analysis/` reads the first tunnel packet of each datagram only (no fragment reassembly), which is why he saw op 49 but no quest contents | Recognition only | Nothing | — | None |
| `soir20/oxide` (Clone Wars Adventures) | [repo](https://github.com/soir20/oxide), `ca3b50e` | AGPL-3.0 | 2026-10-08 | `pub struct Quest {}` and a `quests: Vec<Quest>` in player data, a `NewQuestRewardEntry` (quest guid) reward type, `is_quest_objective` on interactions. No quest logic | Not implemented | Nothing | — | None |
| OSFR old server (`ofrserver`) | same repo as the captures | None | 2023 | Login, NPC, mount, pet and chat systems; **no quest code**; `PacketCodes.cs` names the quest sub-opcodes | Abandoned | Nothing | — | None |
| DeanSawyer/Free-Realms-New-Dawn | [repo](https://github.com/DeanSawyer/Free-Realms-New-Dawn) | AGPL-3.0 | 2026-10-11, 1 commit | Upstream `main` with a rebranded README | Rebrand | Nothing | — | None |

## 3. The text problem, and what the captures say

### The string table

Free Realms is a ForgeLight game, and ForgeLight clients keep their strings in a locale pair. PlanetSide 2's are
`Locale/en_us_data.dir` and `Locale/en_us_data.dat` (the names appear in players' launcher logs, for example
[this Steam thread](https://steamcommunity.com/app/218230/discussions/0/1471967615853359450/)); the Free Realms
Reconnected client listing on GitHub shows the same pair under the TCG module's `locale/` folder (section 7 says why
that repo was not read further). The format, from `soe-locale`:

- `.dir`: one line per string, `<id>\t<byte offset into .dat>\t<byte length>\td`, after a `## Count:` header.
- `.dat`: UTF-8 with BOM; at each offset, `<id>\t<flags>\t<text>\r\n`. The parser's `flags` field is the `ucdt`
  token seen in PlanetSide 2 files.
- The id is a plain integer, the same integer PlanetSide 2's API calls `name_id`. Nothing hashes or remaps it.

So the working hypothesis is that `TitleId` 94247 in our `Quests.json` is literally row 94247 of the client's
`en_us_data.dir`. That is the hypothesis the task calls unsolved; the captures give the data to test it without
guessing, because they show which ids the **live server** sent for quests whose names we can find on the wiki.

### The live quest log (2014-03, one character, Sacred Grove)

From `possible_character_data.pcapng` and `packets_1_just_gameserver.pcapng`, the `PacketSendSelfToClient` (op 12,
204,329 bytes) of the captured character, whose quest section our `ClientPcData` still marks `// TODO Quests`.
Each record parses with our `QuestAddPacket` layout (quest id, title, description, helper text, members-only,
start time, profile, banner flag, completion, reward bundle, objectives, icon) to within one byte of the next record:

| Quest id | Title id | Description id | Helper id | Objective name ids | Reward in bundle | Started |
|---|---|---|---|---|---|---|
| 435 | 36830 | 36831 | 103958 | 3364 (done in `packets_2`), 3365 added | 9 coins | 2011-04 |
| 439 | 36927 | 36928 | 104070 | 3373 | 3 coins | 2011-01 |
| 462 | 37602 | 37603 | 116104 | 37604 (6 to collect) | 4 coins, item 3614 | 2010-08 |
| 624 | 47534 | 47535 | 47542 | 4140, 4141, 4142, 4143 | 10 coins, item 3850 | 2011-02 |
| 1019 | 90071 | 90072 | 90123 | 90125 (2) | 75 stars, item 75799, profile 16 | 2011-04 |
| 1029 | 90150 | 90151 | 90189 | 15576 | 9 coins | 2010-06 |
| 1077 | 91211 | 91212 | 91219 | 17711, 17712 (25) | 8 coins | 2010-06 |
| 1304 | 103744 | 103745 | 103738 | 103739 | 300 stars, item 51187, profile 120 | 2010-09 |
| 1346 | 115533 | 115534 | 141708 | 13354 | 6 coins | 2011-03 |
| 1593 | 388503 | 388504 | 388519 | 13914 (20), 13915 (10), 13916 (4) | 10 coins, 63 stars, profile 43 | 2010-07 |
| 1617 | 389427 | 389428 | 389455 | 389456 (20) | 9 coins, item 1843 | 2010-07 |
| 1625 | 389648 | 389649 | 389656 | 389657 | 5 coins, item 31256 x5 | 2010-09 |
| 1648 | 390570 | 390571 | 390238 | 390239 (6) | 5 coins, item 11011 x5 | 2010-07 |
| 1673 | 391210 | 391211 | 391217 | 391218 | items 56735, 56763 | 2010-08 |
| 1680 | 391345 | 391346 | 391350 | 391351 | items 56741, 56762 | 2010-07 |
| 1875 | 407019 | 407020 | 407044 | 15573, 15574 | 3 coins, 100 stars, profile 120 | 2010-09 |
| 2061 | 415050 | 415051 | 415370 | 16787 | none | 2010-10 |
| 2248 | 428089 | 428090 | 428644 | 17635 (5), 17636 (5) | none | 2011-06 |
| 2270 | 429315 | 429316 | 429319 | 12879 | none | 2010-06 |
| 2287 | 416778 | 432127 | 432142 | 17882 | none | 2010-10 |
| 2347 | 436701 | 436702 | 436721 | 18072, 18073 | 5 coins | 2012-11 |
| 2513 | 4419559 | 4419560 | 4419601 | 18930, 19189 | 7 coins; system quest | 2013-09 |

What this shows, independent of the client:

- Title and description are consecutive ids, and a quest's strings cluster (435: 36830, 36831, 36834; 1617:
  389427, 389428, 389455, 389456). Finding one string of a quest in the table finds the rest nearby.
- Objective ids in `QuestObjectiveActivated` are the objective **name ids** (435's objective 3364, 2347's 18073),
  which is what `QUESTS.md` already says about `NameId` being the goal's identity.
- Quest ids are small and dense (6, 92, 435 ... 2608). 2513 is hub 1's first journal quest in upstream's
  `WorldZone.cs`; 2608 appears as a `QuestObjectiveActivated` id in `packets_2` with a 5-count objective 29459.
  The journal's 58 hub quests and 16 stickers are therefore real 2013 quest ids, and the client ties sticker
  rewards to them.
- Experience travels in the reward bundle's second int, the field PR #120 renames `Experience` and our code calls
  `RewardKind`; `profileId` is the job the stars go to (16, 43, 120).
- 2,671 distinct NPC adds in `packets_2` carry name ids; our `Npcs.json` names (Cole 93337, Spritzy 21292, Nomi
  46272, Feldman 390781) are the same kind of id. Whoever built upstream's NPC list has already matched name ids to
  text once, which is one more reason to expect a plain lookup to work.
- The login burst's first badge packet (op 35 sub 10) lists 130 NPC notifications, each with the NPC guid, a type,
  an image id and the NPC's name id. These say which NPCs in that zone offered something in 2014. Not decoded further here.

Three live layouts differ from ours by trailing bytes: `QuestObjectiveActivated` is 19 bytes on the wire (quest,
objective, required count, one byte) where ours writes 22 (an extra int); `QuestObjectiveUpdate` is 27 (quest,
objective, current, percent, one byte, int) where ours writes 30; and each `SendSelf` quest record ends one byte
before our `QuestAddPacket` tail of three bools. Our packets work in game, so the client tolerates the extra
bytes, but anyone touching those serializers should know the live server did not send them.

### How the captures were decoded

The SOE transport in these captures is: connect and confirm packets in the clear (confirm byte 10 is the CRC size,
bytes 11 and 12 the two encrypt methods, here `1, 0` meaning compression only); every other datagram has a 2-byte
CRC trailer and, after the 1- or 2-byte header, a flag byte that is `1` when the rest is a zlib stream. Inside:
multi (`00 03`, byte lengths), group (`00 19`, variable lengths), reliable (`00 09`, 2-byte sequence) and fragments
(`00 0D`, first fragment carries a 4-byte total), with groups nested inside reliable payloads. Application packets
are little-endian; the gateway tunnels world packets as op 5 or 6, a byte, an int length and the inner packet;
quest packets are inner op 49 with an int sub-opcode (1 info, 3 add, 7 objective added, 8 activated, 9 update, 10
complete, 13 end). All of this matches `Sanctuary.UdpLibrary` and `Sanctuary.Packet`, which is how the layouts were
checked. The decoder written for this report is about 300 lines of standard-library Python and lives only in the
session's scratch space; it is described here so it can be rewritten in an hour, and so nothing from the
unlicensed repo enters ours.

## 4. Which questlines first

Rule: a questline is feasible now when every quest is Errand or Search on the wiki (talk, go somewhere, collect),
its NPCs are spawned, and it is not members-only (Evergrove accounts are not members at launch). Combat, Timed and
Minigame quests wait for systems we don't have. NPC presence is checked by name against our `Npcs.json`; "unnamed"
means no definition carries that name, which can also mean the NPC is one of the 1,766 definitions without a name.

Quest counts by place, from the wiki's [Quests by Place](https://everquest.allakhazam.com/wiki/Category:Quests_by_Place_(FR))
(all types together): Snowhill 135, Sanctuary 106, Blackspore Swamp 81, Seaside 80, Bristlewood 65, Shrouded
Glade 54, Merry Vale 48, Wugachug 41, Briarwood 34, Farnum's Farm 34, Lakeshore 32, Nettleseed 32, Greenwood Forest
26, Thunder Falls Speedway 24, Stillwater Crossing 24, Wilds North 23, Shrouded Gloam 23, Pixie Nursery 20,
Robgoblin Camp 20, Crossroads 19, Frost Ridge Speedway 17, Queensfields 16, Royal Palace 16, Pixiewood Hills 16,
Forest Troll Village 15, Wilds Farm 15, Singing Crystal Mines 14, Greenwood Camp 13, Southvale Quarry 13. Sanctuary's
106 are mostly job quests (Medic 15, Postman 9, Card Duelist 11, Wizard 8, Warrior 5) and seasonal events.

| Rank | Questline (place) | Quests | We have | Missing | Why this rank |
|---|---|---|---|---|---|
| 1 | **Seaside Tour** (Seaside): Welcome to Seaside, The Oldest Building, Touring Seaside, Testing the Tour | 4, Errand | 3020, 3021 | Two quest entries. Text ids for both. Touring Seaside is three finds (Koodini, the Boardwalk, Diggy) and a return; Testing the Tour is "answer Brenna's questions", a quiz our dialogue lines can only approximate as a conversation. All six NPCs spawn: Spritzy 1885, Cole 1748, Seaspray 1756, Koodini 1766, Diggy 4265, Brenna 1749 | Half done, every NPC present, all Errand, the hub players land in |
| 2 | **Forest Troll Village**: Lazy Trolls, Question at the Quarry, Tools for Smashing, Box O' Rocks | 4, Search | all four (3040 to 3043) | Nothing, except that the wiki marks Box O' Rocks members-only in the live game; ours isn't | Done; verify in game |
| 3 | **Sanctuary, Nomi**: Nomi's Little Brother, Hasti's Homework Heist | 2, Errand | both (3001, 3002) | Nothing | Done; verify |
| 4 | **Sanctuary, Feldman**: Looking for Lavender, A Reliable Source | 2, Errand | 3011; 3010 parked | Dani (unnamed) and the guids `100000040011/2`; Honey 4028 and Barin Battlerock 2359 are spawned | One NPC short of restoring 3010 |
| 5 | **Chugawug Friends** (Greenwood Camp to Bristlewood): Misplaced Mugwopp, How Bad is it?, Briarwood's Not So Bad | 3, Search | none | Three entries and text ids. Blorple 3463, Mugwopp 2301, Smoggo 2741 spawn. Last quest is in Briarwood, which players can walk to | Cheap cross-zone chain |
| 6 | **Wugachug, Sasparilla Hunt**: Join the Party ... A Refreshing Reward | 7 | none | Types unverified on the wiki pages (not fetched); Boranug 1611, Wugawump 1661, Brockoom 1621 spawn | Likely Errand; check types first |
| 7 | **Pixie Nursery**: Making Amends, Who Done It?, Purr...fect Secret, Fishy Business | 4, Search | 3032, 3033; 3031 parked | **Members-only** in the live game. The missing guids `100000040016` to `40019` are the four cat bowls, not NPCs; Blazing Star 3380, Oxalis 3386, Zinnea 3389, Funnel Bindweed 3388, Bulb 3370, Flax 3379 spawn | Decide the members rule first |
| 8 | **Take a Number → Chapter 1** (Sanctuary, Royal Palace, Briarwood, Blackspore) | 1 + 27 | none | Pux Frillwillow, Geoffrey, Vittorio, Roxana, Frolic Steadywings, Ladyslipper, Guard Reli, Sorrowspine unnamed; Trusty 1952, Quills 2352, Bertaboo 2427, Queen Valerian 6424, Snibug 1324, Chugug 6452, Darkthorne 6445, Milford 1561, Eggert 1539, Freidi 2560, Spano 2573, Grimple 2575 spawn. Several chapter quests are Combat | The main story; large, and combat-gated past the first few |
| 9 | **Hero's Journal** (Briarwood Caverns, Farnum's Farm, Cobblestone) | 17 | none | Combat tutorials and job tutorials (mine, forge). NPCs spawn (Pappy 3211, Mae 2538, Cleetus 3232, Bartle 2087, Darkthorne) | Needs combat and minigames |
| 10 | **Introduce Yourself / Call the Crew** (Crossroads) and **Power Problems** (Stillwater) | 2 + ? | 2563, 2564, 1801 | Stillwater's continuation (Power Problems) not fetched | Starter quests, done |
| 11 | **Brawler** (Highroad Junction): Restless Rumors, The Growler Report | 45 job quests | both parked | Gerold and `100000033082/3` unnamed; Restless Rumors gives a job shard and switches job | Job quests need the job system |
| 12 | **Helping Snowhill** (12 quests) and **Snowhill Protector** | 12 + 6 | none | Frostpetal, Mayor Crystalline, Tevin, Murphy all unnamed; first two quests are Search, later ones (Wayward Worms, The Hole Thing) unverified; the Protector quests are hunts by name, types unverified | Biggest hub, worst NPC coverage |
| 13 | **Sunstone Valley** (Bike Retrieval → Sobering Homecoming → Aiding Our Allies) | ? | 3080; 3081 parked | Dorn Geargrinder 3840 and Golmie Greasebeard 3842 spawn, but the quest wants "3 Freewheelers" and Headmaster Merk (3841 spawns); guids `100000040032` to `40034` unnamed | 3081's name on the wiki is Sobering Homecoming (Errand) |
| 14 | **Merry Vale** (Cotton Candy 7, Ronnie Britesmile 3, Birthday Gift 3) | 13 | none | Ronnie 2195, Aunt Daisy 3216 spawn; Lugunwump unnamed; types unverified | Check types |
| 15 | **Shrouded Glade** (Halle Belleflower 3, Embir Clearwell 3, Diamond Shard 10), **Lakeshore** (4) | 20 | none | Embir Clearwell unnamed; Halle 1354, Merk 3841 spawn; Diamond Shard chains reward shards (collection system) | Later |

Our quests 3060, 3061, 3070 and 3071 (title ids 101400, 400355, 385597, 74199) have no `Comment` and were not
matched to wiki names here; section 6 asks the client for their titles.

## 5. Recommended first step: finish the Seaside Tour, and prove the text pipeline doing it

One PR, from `main`, after section 6 questions 1 to 3 are answered in the client:

1. **Resolve the two quests' text.** In the client's `en_us_data.dat`, find "Touring Seaside" and "Testing the
   Tour" and take the ids of the title, the description, each objective line ("Find the Fortune Teller, The Great
   Koodini", "Find the Boardwalk", "Speak to Diggy", "Return to Cole"; "Answer Brenna's questions correctly",
   "Return to Cole in Seaside") and Cole's and Brenna's dialogue lines, which the live data says sit within a few
   rows of the title. Record them in `QUESTS.md` as the worked example of how text ids are found.
2. **Add the two entries to `Quests.json`.** Touring Seaside: giver Cole `100000001748`, goals talk Koodini
   `100000001766`, reach the Boardwalk (`/whereami` on the boardwalk, radius 15), talk Diggy `100000004265`, return
   to Cole; `PrerequisiteQuestId` 3021 and set 3021's `NextQuestId`. Testing the Tour: giver Cole, a talk goal on
   Brenna `100000001749` with a multi-line `Dialogue` that walks through her questions and the right answers (our
   engine has no branching quiz; say so in the entry's `Comment`), return to Cole; 30 coins each as the wiki says.
3. **Carry the exclusion-order fix from PR #130** into `QuestDefinition.IsOfferableFor` (check `ExcludesQuestIds`
   before `PrerequisiteQuestId`), with a test for two exclusive quests sharing a prerequisite. Credit Sulphural and
   ImAlko.
4. Tests: the existing `QuestDefinitionCollectionTests` already fail on unknown npc guids and broken links; add
   the two quests to whatever fixture lists chain lengths.

**How Nate tests it in game:** a new character walks to Seaside; Spritzy has a "!"; accept Welcome to Seaside, look
at the view, return, talk to Cole; accept The Oldest Building, talk to Seaspray, return; Cole now offers Touring
Seaside with the right title and description text, no `[94247]`-style unresolved ids; the tracker shows the four
goals by name; standing on the Boardwalk ticks the second goal; Diggy and Cole complete it and pay 30 coins; Cole
offers Testing the Tour; Brenna's conversation plays her questions; Cole completes it. Relog mid-chain and confirm
the tracker and Take Me There survive. Then abandon Touring Seaside and confirm Cole offers it again.

Second step, its own PR: port PR #120 so the stars on every quest land on the active job's bar and level it up,
with our `QuestManager` calling `RewardHelper.TryGrantExperience` and new characters starting at level 1 behind a
setting (the launch economy decides). Third: restore Looking for Lavender once Dani is spawned, and author the
Chugawug Friends chain.

## 6. Check in the client

Only the client can answer these. Each one blocks a step in section 5.

1. **Is the mapping identity?** Open the client's locale pair (expected `locale/en_us_data.dir` and `.dat` next to
   `FreeRealms.exe`, or inside a pack; the TCG module has its own under `tcg/locale/`). Look up row **36830**: it
   should be a quest title, and **36831** its description, because the live server sent those as quest 435's
   title and description. Then **94247** should read "Introduce Yourself", **46280** "Nomi's Little Brother",
   **93283** "Welcome to Seaside", **38385** "Lazy Trolls". If all six hold, the ids are the row ids and the text
   problem is a lookup. If the file has no such rows, report what the smallest and largest ids are and whether a
   second table exists (PlanetSide 2 also ships `en_us_hashes.dat`).
2. **Do 7-digit ids exist?** The 2013 journal strings (5100479, 4419559) are far above the 2010 range. Is row
   5100479 present ("Introduce Yourself" sticker name) and 4419559 (hub quest 2513's title)? If not, the client
   predates them and the journal quest ids in `WorldZone.cs` point at text the client cannot show.
3. **Find the Seaside Tour's rows.** Search the `.dat` for "Touring Seaside" and "Testing the Tour" and list the
   ids of every row within 40 ids on either side, with text. That is step 1 of section 5.
4. **Which quests are 3060, 3061, 3070, 3071?** Look up title ids 101400, 400355, 385597 and 74199.
5. **What does the client do with trailing bytes?** With a debug build, does shortening `QuestObjectiveActivated`
   to the live 19 bytes (drop `Unknown2`) and `QuestObjectiveUpdate` to 27 change anything in the tracker? This is
   a tidy-up, not a blocker.
6. **The Adventurer's Journal.** Does the journal UI open on our `main`, and does it list hubs with text? Do the
   two "Introduce Yourself" stickers (quest 2563 and 2564) light up after completing those quests? That tells us
   whether sticker ids must stay the 2013 ids.
7. **Snowhill's NPCs.** Walk Snowhill with `/npc` tools and note whether Frostpetal, Mayor Crystalline, Tevin and
   Murphy exist unnamed (a definition with no `Name`) or are absent. The 135 Snowhill quests hinge on it.
8. **Local copies of what the cloud could not reach.** From a normal browser: the Wayback Machine for
   `freerealmsinsider.com`, `freerealmswiki.com` and `freerealms.com` quest guides (section 7), and
   `freerealms.fandom.com/wiki/Quests`. If any has per-quest dialogue text, note it; the wiki's `intro` fields are
   paraphrases.

## 7. Dead ends

- **The 37 forks of upstream Sanctuary** (GitHub lists 37 on 2026-10-12, not 39). Every fork's branch names were
  listed with `git ls-remote`. Quest-named branches exist only in Sulphural's fork (both identical to what we
  imported) and amuralle's `collections/quest-reward` (PR #123). ImAlko's, datah4zard's, cillow19's, raisingkaines',
  AlchemyDevelopment's, CoderA05's, GroaxyVRC's, edenfps', bigmommlors', erwynne's and strophiccat's branches hold
  no quest work. Twenty-one forks have only a `main` branch; their contents were not diffed against upstream.
- **Upstream PRs and issues.** All PRs matching "quest" read: #102, #109, #123, #130 (above), #137 (mounts), #88
  (Lua), #86 (collections), #69 (NPC ids). Zero issues match "quest".
- **`archive/sulphural-main-2026-08-18`.** Not on our origin; `CLAUDE.md` names it, `git ls-remote` does not list
  it. Nothing was lost for this task, since Sulphural's public `quest-upstream-v2` equals our import, but the
  `CLAUDE.md` table is stale on this row.
- **Clone Wars Adventures (`oxide`).** Quests are an empty struct. CWA's quests were never emulated.
- **h1emu and PlanetSide 2 emulators.** Not searched for quest code: H1Z1 and PlanetSide 2 have no quest system
  and share only the transport, which this report decodes from our own `UdpLibrary` anyway.
- **The old OSFR server.** No quest code; only the opcode name table, which our `PacketReaderExtensions` already has.
- **Free Realms Sunrise, Free Realms New Dawn.** Closed source and a README rebrand respectively.
- **The fandom wiki** (`freerealms.fandom.com`): HTTP 402 from this environment, as in task 24.
- **`freerealmswiki.com`**: domain no longer resolves. **Free Realms Insider**, **Free Realms Fans**, **Mystic
  Mayhem** and the other fansites in the wiki's [Community (FR)](https://everquest.allakhazam.com/wiki/Community_(FR))
  page: only reachable through the Wayback Machine, and `web.archive.org` is blocked by this environment's proxy
  (connection reset). Section 6 question 8 hands them to a local browser.
- **Web search for the string-table format** under Free Realms' name finds nothing; it is documented only through
  PlanetSide 2 tooling (`soe-locale`). Searches for quest data dumps, "QuestDefinitions", packet logs and
  walkthrough playlists return only the Allakhazam pages.
- **`Bik182/Free-Realms`** ("unfinished client and server files from FreeRealms Reconnected", 768 MB, no licence).
  Cloned by mistake while looking for an unpacker, found to contain the game client itself (`FreeRealms.exe`,
  `locale/` folders), and deleted without reading any file contents or running anything. The GitHub listing is
  the only thing used from it: it confirms the client ships `locale/*.dir` and `*.dat` pairs. Do not fetch it.
- **`psemu/ps2ls`**: no locale code despite the search result suggesting it.
- **Allakhazam quest pages for our later quests**: "Call the Crew" has no page (the wiki calls it "Call the Crew!"
  under Crossroads). Place pages for Briarwood and Blackspore list series names only, without givers.

## 8. Sources read

Upstream Sanctuary `main` `3fff847` (2026-10-11); PR heads #102 `ddf5d9a`, #109 `eca08cd`, #120 `4c580de`, #123
`871b2ef`, #130 `c370ffc`; Sulphural/Sanctuary-fork `main` `b35114f`, `quest-upstream-v2` `8d63294`,
`quest-data-model` `c370ffc`; ImAlko/Sanctuary `feature/progression` `4c580de` and six other branches;
amuralle/Sanctuary three branches; DeanSawyer/Free-Realms-New-Dawn `4001225`; raisingkaines/FreeRealms-Legacy
`9e08202`; brennengreen `33ecdd5`; soir20/oxide `ca3b50e`; psemu/soe-locale `a06ed34`; psemu/ps2ls `23fc871`;
OpenSourceFreeRealms `ace6a82` (captures `p1`, `p2`, `p3`, `p12`, `packets_1` to `_4`, `packets_1_just_gameserver`,
`possible_character_data`). Our branches `import/quest-upstream-v2` `8d63294` and `import/frl-quests` `a2a4961`.
Allakhazam pages: Quests (FR), Quests by Place, Quests by Job, Template:FR Quest, Community (FR), the place pages
for Sacred Grove, Sanctuary, Seaside, Snowhill, Blackspore Swamp, Wugachug, Merry Vale, Shrouded Glade, Greenwood
Forest, Briarwood, Lakeshore and Forest Troll Village, and 22 quest pages named in section 4.
