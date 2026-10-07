# Playtest plan

One ordered test session for everything merged in PRs #2–#8, for two players with two clients. It combines the
in-game checklists from those PRs, with duplicates removed. Nothing in them has been verified in the game client yet.

- **N** is Nate: house owner, admin, runs the servers.
- **W** is Nate's wife: visitor and trade partner.
- **Both** means both players do the step, usually together.

Each step has an ID (`H7`, `T3`…) and a **From** column naming its source, such as `#2.4` for item 4 of PR #2's
checklist. Mark the last column ✓ or ✗ and write anything odd next to it. To report back, see
[Reporting results](#reporting-results).

| Part | What | Who | Time | When |
|---|---|---|---|---|
| [0](#part-0-setup) | Setup: fresh database, servers, accounts, characters | N | 25 min | Evening 1 |
| [1](#part-1-smoke-test-together) | Smoke test: login, movement, chat, seeing each other | Both | 15 min | Evening 1 |
| [2](#part-2-housing-on-your-own-n-solo-sweep-w) | Housing on your own (N), solo packet sweep (W) at the same time | N, W apart | 45 min | Evening 1 |
| [3](#part-3-housing-with-a-visitor) | Housing with a visitor: directory, toggles, lock, friends | Both | 40 min | Evening 1 |
| [4](#part-4-trading) | Trading | Both | 35 min | Evening 1, or start of evening 2 |
| [5](#part-5-quests-each-player-runs-their-own-list) | Quests, one list each, at the same time | N, W apart | 75 min | Evening 2 |
| [6](#part-6-together-again-cakes-portraits-bans) | Cakes, portraits, bans | Both | 25 min | Evening 2 |
| [7](#part-7-cross-feature-checks-and-wrap-up) | Cross-feature checks and wrap-up | Both | 15 min | Evening 2 |

**Order.** Housing is the priority, so it comes straight after the smoke test. Trading follows because it is the
riskiest change: it moves items between two characters, and a bug there can duplicate or lose items. Quests are
long walks, so they take most of evening 2 with each player working through their own list. If evening 1 runs
long, stop at the end of any part. Every part after Part 1 starts from a normal logged-in state.

**Steps that set up later ones:**

| Step | Sets up | Why |
|---|---|---|
| S5 (N is admin) | P4, P7, any `!teleport` | `!mod` and `!teleport` need a moderator or admin |
| H1 (N buys the Seaside Lot) | All of Part 3, T13, QN9 | Every house step uses it |
| H5–H7 (furniture placed, one picked up) | H9, H10, V3, V13, C1 | These check the furniture is still there; C1 trades the picked-up item |
| Not friends until V9 | V1, V3–V8 | Friends bypass the house rules, so the friend steps come last |
| V9 (friends), V12 (unfriended) | V10–V11, T15 | A friend can't be ignored, so T15 needs V12 done first |
| QW3 (W finishes Nomi's Little Brother) | QW4 | Hasti's Homework Heist needs it first |
| Parts 2–5 (items from many sources) | C1 | The trade drift check uses those items |

---

## Part 0: Setup

### Which build to run

Run a **Debug build in the Development environment** for this playtest:

- In a Debug build the Gateway logs `received an unhandled packet` for any packet nothing handles. Several steps
  below rely on that warning. A Release build drops it silently.
- In the Development environment the Gateway logs every packet it receives (`Received <PacketName> packet`) at
  Trace level. Position updates are excluded, so the log stays readable.
- In a Debug build an exception in a packet handler **crashes the Gateway** instead of being logged. If the Gateway
  window closes with a stack trace, copy the trace into the step's notes. Then restart the Gateway (Login can keep
  running) and have both players log back in.
- The WebAPI only logs request and response lines in a Debug build in Development, which P5 needs.

> **Never expose this setup to the internet.** A Debug build only starts because S1's files set
> `AllowDebugBuild`. A Debug Gateway skips the login-ticket check (`#if !DEBUG` in
> `PacketLoginHandler`), so anyone who reaches port 20260 can log in as any character with
> `run_client.py -g <id>`. Play on your home network only.

### Two clients

Two clients on the server PC is the simplest setup. `run_client.py` makes the client folder safe to run twice.

A second PC needs three settings changed from `127.0.0.1` to the server's LAN address first, in the local files
from S1: `Urls` in `appsettings.local.json`, `Server:ServerAddress` in `gateway.local.json`, and
`Portrait:UploadUrl` in the WebAPI's `LaunchArguments`. The `*.local.example.json` beside each file shows where
each setting goes. Then run `run_client.py -a <server-ip> …` on that PC.

`run_client.py` logs every account in with the password `testtest`. That is a developer shortcut, and Task 12
replaces it with a real launcher.

### Steps

| ID | From | Who | Do | Expect | Gateway log | ✓/✗ |
|---|---|---|---|---|---|---|
| S1 | – | N | Create the three local settings files below, then build: `cd src && dotnet build -c Debug`. The build copies the files next to each server. WebAPI needs the ASP.NET Core 9 runtime installed | Build succeeds | – | |
| S2 | – | N | Fresh database: delete your old SQLite file, or pick a new path. In each of the three server terminals, set the variables below, then start **Login** from `src/Sanctuary.Login/bin/Debug/net9.0`, **Gateway** from `src/Sanctuary.Gateway/bin/Debug/net9.0` and **WebAPI** from `src/Sanctuary.WebAPI/bin/Debug/net9.0`, in that order. Each runs as `./Sanctuary.<Name>` (or `.exe` on Windows) | Login applies migrations to the empty file and listens on 20042. Gateway listens on 20260 and connects to Login. WebAPI listens on 20040 | Gateway: `Loaded 19 quest definitions`, `Activated 40 collection node(s) across 7 pool(s)`, `Loaded 7 interactions`, `Loaded 12 chat command(s)`, `GatewayServer started and is listening on port '20260'`, `127.0.0.1:20041 connected`. No WARN or ERROR apart from the `DEBUG BUILD` banner each server prints first. A `Refusing to start` line names the setting S1's files are missing | |
| S3 | – | N | Register three accounts (commands below): `nate`, `wife`, `bantest`, all with password `testtest` | Each `curl` prints HTTP 200 | WebAPI: one `POST /register` line each | |
| S4 | – | N | Check the three rows exist: `sqlite3 <db> "SELECT Id, Username, IsMember, MaxCharacters FROM Users;"` | 3 rows, `IsMember` 1 (`MemberByDefault` in `appsettings.local.json`), `MaxCharacters` 10 | – | |
| S5 | – | N | Make `nate` an admin: `sqlite3 <db> "UPDATE Users SET IsAdmin = 1 WHERE Username = 'nate';"` | 1 row changed | – | |
| S6 | #8.1 | N | `python run_client.py -l nate`. Create your character; the client checks the name as you type it | The name check answers and the character is created. New characters start with 999,999,999 coins (`StartingCoins` in `login.local.json`) | Login log: no `Failed to deserialize` | |
| S7 | #8.1 | W | `python run_client.py -l wife`. Create a throwaway character, delete it, then create your real one | Delete removes it from the list. The second character is created normally | Login log: no `Failed to deserialize` | |
| S8 | #8.1 | Both | Enter the world | Both load into Fabled Realms. Quest givers near the start show a yellow marker overhead and on the map | `Received PacketClientIsReady`. No `Failed to deserialize`, no `unhandled packet` | |
| S9 | – | N | Type `!help` in chat | The list includes `admin`, `mod`, `teleport` and `house`. If `admin` is missing, S5 didn't take: log out and back in | – | |

Environment for S2 (bash; in PowerShell, use `$env:NAME = "value"` instead):

```bash
export DOTNET_ENVIRONMENT=Development
export ASPNETCORE_ENVIRONMENT=Development
export Database__Provider=Sqlite
export Database__ConnectionString="Data Source=/home/nate/sanctuary-playtest.db"
```

Local settings files for S1. They are git-ignored, and they hold the playtest values: rich new characters with
every title and job, member accounts, and permission for a Debug build to start. Make one challenge with
`openssl rand -base64 32` and put the same value in the first two files.

`src/Sanctuary.Login/login.local.json`:

```json
{
  "AllowDebugBuild": true,
  "Server": {
    "LoginGatewayChallenge": "<the challenge>",
    "StartingCoins": 999999999,
    "StartingStationCash": 999999999,
    "UnlockAllTitles": true,
    "UnlockAllProfiles": true
  }
}
```

`src/Sanctuary.Gateway/gateway.local.json`:

```json
{
  "AllowDebugBuild": true,
  "Server": {
    "LoginGatewayChallenge": "<the challenge>"
  }
}
```

`src/Sanctuary.WebAPI/appsettings.local.json`:

```json
{
  "AllowDebugBuild": true,
  "WebAPI": {
    "MemberByDefault": true
  }
}
```

Accounts for S3:

```bash
for user in nate wife bantest; do
  curl -s -o /dev/null -w "$user %{http_code}\n" -X POST http://127.0.0.1:20040/register \
    -H 'Content-Type: application/json' -d "{\"username\":\"$user\",\"password\":\"testtest\"}"
done
```

The variables override `database.json` and user secrets for all three servers, so they all share one database. I
booted Login and Gateway this way against an empty SQLite file before writing this plan. The WebAPI couldn't be
run in the cloud session.

**SQLite is not MariaDB.** If the VPS will run MariaDB, repeat Part 4 (trading) against MariaDB once before
launch. Trades are written in a serializable transaction, and the two databases enforce that differently.

### Logs

- The Gateway console shows everything. The same lines go to `src/Sanctuary.Gateway/bin/Debug/net9.0/Logs/`:
  `Sanctuary.Gateway-Info-<date>.log` (Trace and Info) and `Sanctuary.Gateway-Error-<date>.log` (WARN and above).
  Login and WebAPI have the same pair in their own `Logs/` folders.
- **At the end of every part, open the Gateway's Error log** and copy any new lines into that part's notes.

| Log line | Meaning |
|---|---|
| `Failed to deserialize <Packet>` (ERROR) | The client sent a packet the server can't read: a wrong layout, or an opcode guard refusing it. Note what you clicked |
| `received an unhandled packet. ( OpCode: 5, Data: … )` (WARN) | No server code handles that packet. Copy the `Data` hex |
| `Unknown BaseQuestPacket sub-opcode` (WARN) | The same, for a quest packet |
| `Player trade failed closed before durability. ( … Failure: <Kind> … )` (WARN) | The server refused a trade. `<Kind>` says why |
| The Gateway exits with a stack trace | A handler threw an exception. Copy the trace |

---

## Part 1: Smoke test (together)

Stand next to each other in Fabled Realms. If anything here fails, stop and report it: every other part depends on it.

| ID | From | Who | Do | Expect | Gateway log | ✓/✗ |
|---|---|---|---|---|---|---|
| M1 | #8.2, #3.3 | Both | Walk, jump and turn the camera. Watch each other | Each sees the other move and jump smoothly | No `Failed to deserialize PlayerUpdatePacketUpdatePosition` or `…Jump` | |
| M2 | #8.3, #3.3 | Both | Say something in a chat channel. Send a quick-chat message and a quick-chat tell to each other | Every message arrives once | No `Failed to deserialize` for chat or quick chat | |
| M3 | #3.3 | Both | Emote at each other | Each sees the other's emote | – | |
| M4 | #3.3 | Both | Stand near an npc that walks or talks | Both see it move and see its speech bubbles | – | |
| M5 | #8.6 | N | Choose a title in the title window | N's title changes, and **W sees the new title**. Titles are the one packet family whose sub-opcode is read at two widths (2 bytes by the dispatcher, 4 by the packet), so watch this one | No `Failed to deserialize PlayerTitleRequestSelectPacket` | |
| M6 | #8.2 | N | Zone-teleport using the map | N arrives at the chosen spot. W sees N disappear | No `Failed to deserialize PacketZoneTeleportRequest` | |
| M7 | #8.5, #3.3 | Both | Put a consumable on the action bar and use it while the other watches | It works, its count drops by one, and the other sees the effect | No `Failed to deserialize` for abilities | |

---

## Part 2: Housing on your own (N); solo sweep (W)

N and W work separately now. N's steps (H) need nobody else. W's steps (W) cover the packet families that only need
one player. Both lists take about 45 minutes.

### N: housing

| ID | From | Who | Do | Expect | Gateway log | ✓/✗ |
|---|---|---|---|---|---|---|
| H1 | #2.1 | N | Write down your coins and station cash. In the Marketplace, buy the **Seaside Lot** (bundle 4536). Type `!house list` | Charged once (members pay a 1-coin member price, so the drop may be small). The house list refreshes. `!house list` says `Owned houses: …` | No `Failed to deserialize` for in-game purchases | |
| H2 | #2.1 | N | Try to buy the Seaside Lot again | Refused, with no charge | – | |
| H3 | #2.2 | N | Enter it: housing UI enter button, or `!house enter seaside` | Loading screen, then the lot with the housing sky, at the lot's spawn point. Your big-head effect is back to normal size. The housing HUD shows the house name and owner | `Received ClientHousingPacketEnterRequest` if you used the button | |
| H4 | #2.2, #2.3 | N | Open the Marketplace inside the house. Buy one housing item. Turn on edit mode | The Marketplace opens. The furniture list shows your housing items, including the new one, without a relog | `Received ClientHousingPacketSetEditMode` | |
| H5 | #2.4 | N | Pick an item, move the cursor, click to place it. Place **three** items | A preview follows the cursor. Each stays where you put it, and its inventory count drops by one | `Received ClientHousingPacketPlaceFixtureRequest` / `…PlaceFixture`. No `Unable to place fixture` | |
| H6 | #2.5, #2.6 | N | Select a placed item, move or rotate it, save. Then change a wall or floor | The item stays in its new position. The wall or floor changes at once | `…SaveFixture`, `…ApplyCustomizationToFixtureGroupAndType` | |
| H7 | #2.7 | N | Pick up one item | It disappears and goes back to your inventory. **Leave the other two placed**, since V3 and V13 check them | `…PickupFixture` | |
| H8 | #2.8 | N | Leave three times, re-entering between each: the leave button, `!house leave`, and the safe-teleport (stuck) option | Each time you return to Fabled Realms where you stood before entering, not at the world spawn | `…LeaveHouse`; `PacketZoneSafeTeleportRequest` for the third | |
| H9 | #2.9, #2.13 | N | Re-enter | Everything from H5–H6 is where you left it. The house was empty after H8, and the server closes an empty house as soon as its last player leaves, so this also checks that it reloads from the database | – | |
| H10 | #2.10, #7.1, #7.2, #7.3 | N | In the house panel, turn **Lock** on, then off. Turn **Allow flora** off. Turn **Pet autospawn** on. Then log out **inside the house**, log back in and re-enter | Logging out inside a house doesn't leave you stuck on login. After re-entering, the furniture is there, Lock is off, Allow flora is off and Pet autospawn is on. **Turn Allow flora back on** for Part 3 | `<N> set Locked to True.`, `…False.`, `<N> set FloraAllowed to False.`, `<N> set PetAutospawn to True.` (see the note below) | |
| H11 | #2.1, #2.2 | N | Optional: buy the **Blackspore Swamp House** (4709) and `!house enter blackspore`, then leave | It loads like the Seaside Lot | – | |

**If a toggle logs nothing** in H10: look for `Failed to deserialize ClientHousingPacketToggleLocked` (the client
sends a payload the packet doesn't expect), or an `unhandled packet` warning whose `Data` contains `7F001600`. That
would be the client's second lock packet, sub-opcode 22, which nothing handles yet. Note which one you see.

### W: solo packet sweep

| ID | From | Who | Do | Expect | Gateway log | ✓/✗ |
|---|---|---|---|---|---|---|
| W1 | #8.4 | W | Equip an item, unequip it, and put one on the action bar | Each works | No `Failed to deserialize` for inventory | |
| W2 | #8.4 | W | Preview a style card, then use one | The preview shows. Using it applies the style and removes one card | – | |
| W3 | #8.7 | W | Summon a mount, ride, dismount | Each works | No `Failed to deserialize` for mounts | |
| W4 | #8.8 | W | Open the coin store: buy an item, sell an item. Open the member store (Marketplace) and buy something | Coins go down and up by the right amounts. The items appear and disappear | No `Failed to deserialize` for the coin store or in-game purchases | |
| W5 | #8.9, #3.2 | W | Open the Fotomat and upload a portrait | The upload succeeds. A correct portrait (70×70 thumbnail, 180×330 image) is accepted | WebAPI: a `POST /image` request with status 200 | |
| W6 | #8.10 | W | If the client lets you, check a new name and request a name change | The check answers. Cancel the change if you don't want it | No `Failed to deserialize` for name change | |
| W7 | #8.11 | W | Click an npc and pick a dialog option | The dialog continues | – | |
| W8 | #4.13 | W | Gather a few collection nodes that aren't quest nodes, such as mushrooms | They drop items as before | – | |

---

## Part 3: Housing with a visitor

N and W together. Before V1 they must **not** be friends: W isn't on N's list and N isn't on W's. N stays in Fabled
Realms unless a step says otherwise.

| ID | From | Who | Do | Expect | Gateway log | ✓/✗ |
|---|---|---|---|---|---|---|
| V1 | #2.11 | W | The house is unpublished. Type `!house visit seaside <N's full character name>` | Refused: "Only the owner and their friends can enter that house." | – | |
| V2 | #2.12 | N, then W | N publishes the house. W searches the directory, views featured houses, and votes for N's house | The house appears in the search. The vote counts | No `Failed to add a rating for the active house` | |
| V3 | #2.11 | W | Enter N's house from the directory, or with `!house visit` | W gets in and sees N's furniture from H5–H6 | – | |
| V4 | #2.11, #7.4 | W | Try to edit: turn on edit mode, move or pick up an item. If the house panel shows the Lock, Flora or Pet toggles, flip one | Nothing changes. A flipped checkbox flips straight back, and N's panel doesn't change | **No** `set …` line | |
| V5 | #7.2 | N | Join W in the house. Turn **Allow flora** off, then on | Expected: the lot's ground plants disappear and come back. We don't know what the client does with this flag, so **both write down exactly what you see, including "nothing"** | `<N> set FloraAllowed to False.`, then `…True.` | |
| V6 | #7.1 | N | Turn **Lock** on with W still inside | W stays inside: locking doesn't remove visitors already in the house (a known gap) | `<N> set Locked to True.` | |
| V7 | #7.1 | W | Leave the house. Look for it in the directory, then try to enter | It's gone from the directory, and entering is refused | – | |
| V8 | #7.1, #2.12 | N, then W | N turns Lock off. W enters again, then leaves. N unpublishes. W searches and tries again | Unlocked: W gets in and the house is back in the directory. Unpublished: it's gone from the directory and W is refused | `<N> set Locked to False.` | |
| V9 | #8.11 | N, then W | N adds W as a friend; W accepts | Each appears on the other's friends list | `Received CommandPacketAddFriendRequest`, then `…ConfirmFriendResponse` | |
| V10 | #2.11, #7.1 | N, then W | N turns Lock on (the house is still unpublished). W uses `!house visit seaside <N's name>` | W gets in: friends bypass both the lock and publishing | – | |
| V11 | #2.11 | W | Leave. With N inside the house, teleport to N from the friends list | W arrives in N's house | – | |
| V12 | #8.11 | N | Turn Lock off. Remove W from the friends list. W leaves and tries `!house visit` again | W is refused, since the house is unpublished and they're no longer friends | `Received CommandPacketRemoveFriendRequest` | |
| V13 | #2.13 | Both | Both leave. N re-enters | The furniture is still there | – | |

**End of Part 3:** open the Gateway Error log and copy any new lines.

---

## Part 4: Trading

Stand next to each other in Fabled Realms. You must not be friends (V12) and neither of you may ignore the other.
A trade window that sits for 5 minutes with no changes closes by itself, so don't leave one idle.

| ID | From | Who | Do | Expect | Gateway log | ✓/✗ |
|---|---|---|---|---|---|---|
| T1 | #5.1 | Both | Right-click the other player | "Trade" is in the menu | – | |
| T2 | #5.2 | N, then W | N invites W. W declines | N sees a "rejected" notice | `Created a player trade invitation`, `Player trade invitation declined` | |
| T3 | #5.3 | N | Invite W. Nobody answers for 60 seconds | The invite closes on both clients | `Expired a player trade session` | |
| T4 | #5.1 | N | Start a trade with W and keep it open. Right-click W again, and have W right-click N | Neither menu offers "Trade" while you're already in a trade | `Started a player trade session` | |
| T5 | #5.4 | Both | In that trade, each adds an item stack, changes its count, removes it, adds it again, and sets a coin amount | Every change shows on both clients | No `Rejected a malformed ordinary trade packet` | |
| T6 | #5.5 | Both | N locks. W then changes W's offer | N's lock drops | – | |
| T7 | #5.6 | Both | Both lock | The Accept button waits about 10 seconds before it works | – | |
| T8 | #5.7 | Both | **Write down both inventories and coin counts first.** Both accept | Items and coins move. Both inventories and coin counts update without a relog. Both see "trade completed" | `Atomically completed a player inventory trade` | |
| T9 | #5.8 | Both | Both log out and back in | Inventories and coins match what you saw after T8 | – | |
| T10 | #5.9 | N | Give W an item W already has | It joins W's existing stack, not a new one | Completed line as in T8 | |
| T11 | #5.10 | N | Offer a NoTrade item or a trading card, if you have one | Refused with an "item invalid" message | – | |
| T12 | #5.10 | N | Equip one item from a stack of more than one, then offer the whole stack | All but the equipped one can be offered | – | |
| T13 | #5.12 | N | Start a trade, then `!house enter seaside` | Both get the "changed instance" message and the window closes. N ends up in the house | `Closed a player trade before zoning` | |
| T14 | #5.11, #5.13 | Both | N leaves the house and walks back to W. Start a trade and cancel it from W's side. Start another and **close N's client** | Cancel: both windows close. Closed client: W gets "partner disconnected" | `Closed a player trade after disconnect` | |
| T15 | #5.1, #8.11 | W | Ignore N. Both right-click each other. Then W removes the ignore | While ignored, neither menu offers "Trade". After the ignore is removed, it's back | `Received CommandPacketIgnoreRequest` (twice) | |

**End of Part 4:** open the Gateway Error log and copy any new lines. A good place to stop for the night.

---

## Part 5: Quests (each player runs their own list)

Quest progress is per character, so N and W each work through their own list at the same time. Coordinates are
`x y z` in Fabled Realms. N can jump to them with `!teleport x y z` (W isn't a moderator, so she walks or uses Take
Me There). For a **Reach** goal, teleport somewhere nearby and walk the last stretch.

The two "Introduce Yourself" quests (2563 and 2564) exclude each other: once a character takes one, the other is
never offered. So N takes 2563 and W takes 2564.

### N's list

| ID | From | Who | Do | Expect | Gateway log | ✓/✗ |
|---|---|---|---|---|---|---|
| QN1 | #4.2 | N | Talk to the giver of **Introduce Yourself (2563)**, npc 100000002045 at `-1879 -46 443` | The offer window shows the title, text and a reward preview. **If pressing interact does nothing, the reward preview is misaligned**: note that | No `Failed to deserialize QuestReplyPacket` | |
| QN2 | #4.3 | N | Accept | The quest appears in the journal and tracker with a banner. The giver's marker changes | – | |
| QN3 | #4.4 | N | Click **Take Me There** in the tracker | N auto-walks toward npc 100000002049 at `-1910 -44 463` | No `Failed to deserialize TakeMeThereRequestPacket` | |
| QN4 | #4.5 | N | Talk to 100000002049 and accept the turn-in | The turn-in marker shows on it. The turn-in window opens. Accepting grants coins, XP and items. You see the completed toast, and the journal's completed count goes up | – | |
| QN5 | #4.6 | N | Talk to 100000002049 again | **Call the Crew (1801)** is offered. Accept it and finish it: 100000003018 at `-2247 -17 599`, then back to 100000002049 | – | |
| QN6 | #4.8 | N | **Welcome to Seaside (3020)**: take it from 100000001885 at `-683 20 -992`. Walk to the spot near `-690 2 -1060` | Arriving within about 15 m completes the goal. Finish it at 100000001885, then 100000001748 at `-575 5 -1130` | – | |
| QN7 | #6.1, #4.9 | N | **A Reliable Source (3011)**: npc 100000002419 at `-1229 -14 432` | It shows "!" and offers the quest at once, without Looking for Lavender first. Accept it, then **abandon it** from the journal. The quest leaves the journal and the giver offers it again | – | |
| QN8 | #6.1 | N | Accept 3011 again. Talk to 100000001419 at `174 61 1171`, then turn it in at 100000002419 | It completes | – | |
| QN9 | #4.10, #4.11 | N | Accept **Aiding our Allies (3080)** from Arch Druid Camellia, 100000001454 at `104 69 1236`, and track it. Log out and back in. Then `!house enter seaside`, check the journal, and leave | After the relog: the journal, tracked quest and npc markers all come back. **Inside the house**, note what the journal shows (it is only restored in world zones). After leaving, the journal and tracker are correct | – | |
| QN10 | #6.3 | N | Turn 3080 in to Headmaster Merk, 100000003841 at `482 65 1747` | It completes and offers nothing after it. Dorn Geargrinder (100000003840, next to Merk) shows no quest badge | – | |

### W's list

| ID | From | Who | Do | Expect | Gateway log | ✓/✗ |
|---|---|---|---|---|---|---|
| QW1 | #6.4, #4.2 | W | Go to npc 100000002912 at `-484 -7 203` | It offers **only "Introduce Yourself" (2564)**, not "Brawler: Restless Rumors". Accept and finish it at 100000002889 (`-417 -4 136`) | – | |
| QW2 | #6.4 | W | While travelling, watch for the removed quests: Looking for Lavender, Purr...fect Secret, Sobering Homecoming and both Brawler quests | None of them is offered anywhere | – | |
| QW3 | – | W | Take **Nomi's Little Brother (3001)** from 100000002335 at `-1116 5 318`. Talk to 100000003016 (`-2253 -16 564`), then back to 100000002335 | It completes. It is the prerequisite for QW4 | – | |
| QW4 | #4.7 | W | Take **Hasti's Homework Heist (3002)** from the same npc. Gather 8 homework pickups | The tracker counts up to 8 and the goal completes. Turn it in at 100000002335 | – | |
| QW5 | #4.7 | W | Take **A Booming Problem (3061)** from 100000001925 at `-252 -26 -1156`. Mine 5 tin ore | It counts up to 5 and the goal completes. Turn it in. (This replaces #4.7's "Tools for Smashing", which needs three earlier quests first) | – | |
| QW6 | #6.2 | W | **Who Done It? (3032)**: npc 100000003380 at `-392 -7 -752` | It shows "!" and offers the quest at once, without Purr...fect Secret first. Finish it (100000003389, 100000003388, then 100000003386 at `-340 -1 -798`) | – | |
| QW7 | #6.2 | W | Turn it in | **Making Amends (3033)** appears on 100000003386 without a relog | – | |
| QW8 | #6.5 | W | While doing QW6 and N's QN8, read the dialogue | 3011 and 3032 may mention the quest that used to come before them. We can't change client strings; note whether it reads oddly | – | |

### W: two turn-ins in a row

| ID | From | Who | Do | Expect | Gateway log | ✓/✗ |
|---|---|---|---|---|---|---|
| QX1 | #4.12 | W | First take **The Party Pooper (3070)** from 100000002177 at `-1108 -40 -424`, and **Making Amends (3033)** from 100000003386. Do Making Amends: talk to 100000003370 (`-473 0 -698`), then 100000003379 (`-392 -7 -751`). When its turn-in window opens, **close it without accepting**, if the client allows that. Then talk to 100000004355 (`-1016 -38 -302`) and **accept The Party Pooper's** turn-in window | Correct: The Party Pooper completes, and Making Amends still waits for its turn-in. Possible bug: Making Amends completes instead. The server queues waiting turn-ins, and the client's accept doesn't say which quest it means, so the server hands in the oldest one. Write down which quest completed, and what happens when you talk to 100000003379 again | – | |

#4.12 asked for two quests whose final goals complete at the same moment. None of the 19 shipped quests can do that:
every pair that ends at the same npc is a prerequisite chain. QX1 tests the same queue a different way. If the client
won't let you close a turn-in window without answering, mark QX1 "n/a" and say so.

**End of Part 5:** N opens the Gateway Error log and copies any new lines.

---

## Part 6: Together again: cakes, portraits, bans

| ID | From | Who | Do | Expect | Gateway log | ✓/✗ |
|---|---|---|---|---|---|---|
| P1 | #8.12, #3.3 | N | Next to W, use a cake with several spawn effects, then one with a one-shot animation | W sees every spawn effect, and sees the animation switch on and back off | – | |
| P2 | #3.2 | W | If the Fotomat lets you, upload a portrait with the wrong size in only one dimension | Refused (the old check only refused images where both dimensions were wrong) | WebAPI: `POST /image` with a 4xx status | |
| P3 | – | W | Log out. Log in as `bantest` (`python run_client.py -l bantest`), create a character and enter the world | Works | – | |
| P4 | #3.1 | N | `!mod ban <bantest's character name> 10` | N sees "… has been banned until …". bantest is disconnected | Audit line `mod\|Ban\|…`. In Development it goes to the Gateway console and Info log, not `Audit-<date>.log` | |
| P5 | #3.1, #3.4 | W | `python run_client.py -l bantest` again | The script prints `Failed to login (403)`, then fails on registering (409 Conflict), because the script retries banned logins as new registrations. Nothing reaches character select | WebAPI: `Login failed, account is banned for username: bantest`. The request and response lines show **no body, password or session id** | |
| P6 | #3.1 | W | Try the Gateway path too: `python run_client.py -g <bantest's character id>` (the id is in `SELECT Id, FullName FROM Characters;`) | Refused at the Gateway | `connected with a banned account` | |
| P7 | #3.1 | N | `!mod unban <name>`. W logs in as bantest again, then logs out and back in as `wife` | bantest gets in again. (A timed ban also clears itself once it expires) | Audit line `mod\|Unban\|…` | |

---

## Part 7: Cross-feature checks and wrap-up

| ID | From | Who | Do | Expect | Gateway log | ✓/✗ |
|---|---|---|---|---|---|---|
| C1 | #5.14 | Both | Trade one item from each source the playtest produced: a Marketplace purchase, the furniture item N picked up in H7, a quest reward item, a collection-node drop, and a consumable stack you used part of (M7). One trade per source, or several in one trade | Every trade completes. If one fails with "item invalid", **write down where that item came from and what you did with it last** | A failed trade logs `Player trade failed closed before durability`; copy the whole line | |
| C2 | – | N | Close all three servers with Ctrl+C. Keep the database file | Each shuts down without an error | – | |
| C3 | – | N | Copy each server's `Logs/*-Error-<date>.log` next to your results | – | – | |

---

## Reporting results

To turn the results into the next task, paste these into a new session or an issue:

1. Every row marked ✗, or with a note, as `ID: what happened`. For example: `V5: flora off, nothing changed for
   either of us.`
2. The rows you skipped, and why.
3. The Error log lines from C3, with the step ID they appeared during if you know it.
4. Any Gateway stack trace, with the step ID.
5. The commit of `main` you played on: `git rev-parse --short HEAD`.

Rows marked ✓ need nothing more. Nate updates `docs/status.md`'s "Verified in game?" column from them.
