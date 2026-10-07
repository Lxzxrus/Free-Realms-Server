# Cloud tasks

One task = one cloud session = one PR into `main`, on the branch the session was assigned. Title the PR
`Task N: <name>`, where the names below (`env-check` and so on) are labels, not branch names. Read `CLAUDE.md` first. The
self-check is `cd src && dotnet restore && dotnet build --no-restore && dotnet test --no-build`.

Order: 1 is the cost calibration and runs alone. 2 is the priority. 3–5 can run in parallel once 2 is merged.

---

## 1. `env-check`: calibrate the environment

- **Task:** confirm a cloud session can build and test `main`, and record how.
- **Scope:** `docs/cloud-environment.md` (new). `scripts/cloud-setup.sh` only if the install fails for a
  reason you can fix.
- **Out of scope:** everything under `src/`.
- **Check:** the self-check passes.
- **Deliverable:** a PR whose doc records the SDK and runtime versions, test counts, how long setup, restore,
  build and test each took, and anything the environment blocked.

## 2. `housing-merge`: bring in housing

- **Task:** merge `import/housing-full-archive` into a branch from `main`, resolve every conflict, and get the
  self-check and a server boot passing.
- **Scope:** anything the merge touches.
- **Out of scope:** `import/*`, `archive/*`, and new features beyond the merge.
- **Notes:** the trial merge conflicts in 12 files, including both EF model snapshots, `DbHouse`,
  `DatabaseContext`, `ZoneManager`, `BaseZone`, `HousingZone` and `Player`. Don't hand-merge the model
  snapshots blindly. Try `dotnet tool install --global dotnet-ef` and regenerate. If that's unavailable, merge
  by hand and prove it with a test that asserts `Database.HasPendingModelChanges()` is false for SQLite. Check
  that migration timestamps from both sides still apply in order. Where shared packet files conflict, prefer
  the housing side and list each decision in the PR.
- **Check:** the self-check, plus a boot. Start Login, then Gateway, from their own output folders with
  `database.json` pointing at a fresh SQLite file. Pass means both log that they started, housing resources load,
  and there are no errors. Stop them afterwards.
- **Deliverable:** a PR crediting `raisingkaines` in the merge commit, with a list of the housing actions Nate
  should try in the client (enter a lot, place, move and save furniture, leave, re-enter, relog) and what each
  one should do.

## 3. `quests-merge`: bring in quests *(after 2 is merged)*

- **Task:** merge `import/quest-upstream-v2` into a branch from `main`, then fix the known defects listed in
  `CLAUDE.md`.
- **Scope:** anything the merge touches, plus the defect fixes and tests.
- **Reference:** `import/frl-quests` is our FreeRealms-Legacy port, verified in game. Use its
  `QuestPacketTests.cs` and `RewardBundleTests.cs` as a model for tests. Don't copy its code blindly: that port
  adapted JadenY's code to a different codebase.
- **Check:** the self-check and a boot logging `Loaded N quests`.
- **Deliverable:** a PR crediting JadenY, with an in-game checklist.

## 4. `trading-port`: port trading from FreeRealms-Legacy

- **Task:** hand-port player-to-player trading from `import/frl-main` (`src/Sanctuary.Game/Trading` and its
  handlers, registrations, and database changes if any).
- **Scope:** the trading files and the minimum wiring. No unrelated changes.
- **Check:** the self-check. Add tests for any trade-commit logic that can be tested without a client.
- **Deliverable:** a PR listing every file ported and every adaptation made.

## 5. `security-recheck`: carry over our FreeRealms-Legacy fixes

- **Task:** for each fix below, check whether `main` still has the problem. If it does, port the fix from
  `import/frl-main` as its own commit.
  - S1: debug builds log HTTP request and response bodies, including passwords.
  - S3: the portrait upload size checks are inverted.
  - S5: banned accounts can still get a launcher session.
  - P1: broadcasts re-serialize the packet once per recipient.
- **Scope:** only the files those fixes touch.
- **Check:** the self-check.
- **Deliverable:** a PR with one commit per fix and a table saying which were already fixed upstream.

## 6. `unreachable-quests`: take out quests that can't be finished

- **Task:** these five quests name npcs that nothing spawns, so players get stuck: 104146 (Brawler: Restless
  Rumors), 104154 (Brawler: The Growler Report), 3010 (Looking for Lavender), 3031 (Purr...fect Secret) and 3081
  (Sobering Homecoming). Move them out of `src/Resources/Quests.json` into a new
  `src/Resources/Quests.disabled.json` that the server doesn't load, so they can come back when their npcs exist.
- **Watch the chains:** 3011, 3032 and 3080 reference these quests, and the two Brawler quests reference each
  other. The loader refuses unknown references, so fix every reference. Keep a remaining quest reachable where
  you can (for example, clear a prerequisite that pointed at a removed quest) and list each decision in the PR.
- **Scope:** the two JSON files, `src/Resources/QUESTS.md` if it lists these quests, and tests.
- **Check:** the self-check, plus a boot that logs the new quest count with no loader errors. Add or update a
  test that fails if any shipped quest names an npc guid that `FabledRealms.lua` doesn't spawn.
- **Deliverable:** a PR listing what moved and how each chain was repaired.

## 7. `opcode-guards`: fix the `&&` opcode guards upstream-wide

- **Task:** about 30 packet base classes check their opcode with
  `if (!reader.TryRead(out short opCode) && opCode != OpCode)`, which accepts the wrong opcode. Change each to
  `||`, and the same for sub-opcode checks written the same way.
- **Before changing anything:** confirm, for each family, that the bytes the handler receives start at the
  family opcode, as Task 3 did for quests. If a family's guard is currently masking an offset mismatch, don't
  fix it blind. Leave it and list it in the PR.
- **Also:** `CakeAbility.cs` has the same per-recipient serialization loop that Task 5's P1 fixed elsewhere. Apply
  the same fix.
- **Scope:** the guard lines, `CakeAbility.cs`, and tests.
- **Check:** the self-check. Add a table-driven test that feeds each fixed family a packet with the wrong opcode
  and expects rejection.
- **Deliverable:** a PR with a table of every guard changed and every one skipped, with the reason.

## 8. `housing-toggles`: port the three extra housing toggles

- **Task:** `archive/sulphural-main-2026-08-18` has three housing handlers `main` lacks:
  `ClientHousingPacketToggleFloraAllowedHandler`, `ClientHousingPacketToggleLockedHandler` and
  `ClientHousingPacketTogglePetAutospawnHandler`. Port them by hand, since the archive can't be merged, along
  with their packets and any data they need.
- **Before porting:** `main`'s housing already has `DbHouse.IsLocked` and checks it when a visitor enters.
  Reuse what exists; add database columns only if a toggle has nowhere to store its state, via a proper
  migration with the model-snapshot tests passing.
- **Scope:** the three handlers, their packets and routes in `BaseHousingPacketHandler`, the minimum data and
  migration, and tests.
- **Check:** the self-check and a boot. Every new handler must be routed and must get its services in
  `ConfigureServices`.
- **Deliverable:** a PR crediting JadenY, with in-game checks for each toggle: what the owner does, and what a
  visitor should then see.

## 9. `security-audit`: threat model and ranked findings

- **Task:** map every way into the server and every way it could be abused, then produce a ranked list of
  findings. **This is analysis only: change no code.** The server is going public on a budget VPS (the Optiplex
  becomes staging), and Nate's goal is that no attack succeeds and the game stays smooth for everyone.
- **The adversary:** assume a capable insider. The former FreeRealms-Legacy owner (`raisingkaines`) wrote the
  housing and trading code we merged, knows this codebase well, and is expected to attack us. The source will be
  public at launch (AGPL), so assume the attacker has read every line, and never count secrecy of code as a defence.
- **Backdoor review first:** read every line that came from him (the housing merge, PR #2, and the trading port,
  PR #5) for anything that looks deliberate: hidden accounts, chat or admin commands, authorization that a special
  value bypasses, hard-coded ids or names, outbound network connections, timers or conditions that change
  behaviour later, and obfuscated logic. Report what you checked and what you found, even if it's nothing.
- **Cover at least:** WebAPI (registration, login, sessions, password storage, `/image` upload, the `#if DEBUG`
  auth bypasses); the Login and Gateway UDP servers (unauthenticated traffic, connection and handshake floods,
  malformed and oversized packets, per-packet allocation, what one bad packet can do in Release vs Debug);
  authorization inside the game (admin and chat commands, housing, trading, anything that trusts client-sent
  ids or positions); secrets and config (`LoginGatewayChallenge`, dev grants in `login.json`, connection
  strings, and everything that must leave the repo before it goes public); game-traffic encryption (declared, never enabled); dependencies (`dotnet list package --vulnerable`);
  and what a VPS deployment exposes (which ports must be open, DDoS, admin access to the box, backups).
- **Method:** confirm each finding in the code, with file and line. Where it's cheap and safe, prove it with a
  test or a local reproduction, and say which findings are proven and which are only read.
- **Deliverable:** a PR adding `docs/security/threat-model.md`: a table ranked by severity (what an attacker
  does, what it costs us, where the code is, the proposed fix, and a task-sized estimate), followed by the
  details. The repo goes public at launch, so write it knowing every finding must be fixed by then.
- **Check:** the self-check still passes (nothing under `src/` changes).

## 10. `load-test-bot`: measure where the server tops out

- **Task:** build a headless load-test client, a new console project `src/Sanctuary.LoadTest`, that logs N fake
  players in through the real protocol (WebAPI session, Login, Gateway), puts them in the world, and has them walk,
  jump and chat at realistic rates. Report the server's cost as N grows.
- **Measure:** Gateway CPU (total and busiest thread), packets and bytes per second in and out, and one
  latency figure: the delay between a bot sending a move and another bot seeing it. Run 10, 25, 50 and 100
  bots, clustered in one spot and spread across the zone.
- **Scope:** the new project, its registration in `Sanctuary.slnx`, test-account seeding it needs, and
  `scripts/cloud-setup.sh` if WebAPI needs the ASP.NET Core 9 runtime (`dotnet-install.sh --runtime aspnetcore
  --channel 9.0`). No changes to server behaviour. If the server needs a hook to be measurable, stop and
  propose it in the PR instead.
- **Reuse:** `Sanctuary.UdpLibrary` already has a client side (the Gateway uses it to connect to Login).
- **Check:** the self-check, plus a run with 10 bots that completes without errors on either side.
- **Deliverable:** a PR with the bot, instructions for running it against a server on another machine (the
  Optiplex), and `docs/performance/baseline.md` with the results. Say plainly that the cloud VM isn't the
  Optiplex, so its numbers are only a relative baseline.

## 11. `playtest-plan`: one ordered in-game test session

- **Task:** read the in-game checklists in the descriptions of PRs #2–#8 and combine them into one plan that
  Nate and his wife can work through in an evening or two, with two clients.
- **Order:** setup first (fresh database, accounts, characters, who should or shouldn't be friends, which
  house to publish), then the most important and most likely to break items first. Remove duplicates, and note
  where one step sets up another.
- **Include:** what should happen at each step, what to look for in the Gateway log, and a column to mark
  pass or fail, so the results can go straight back into a task.
- **Scope:** `docs/playtest-plan.md` only.
- **Check:** the self-check still passes.
- **Deliverable:** a PR with the plan.

## 12. `launcher`: our own launcher, from OSFR's

- **Task:** players need a proper launcher, because `run_client.py` hardcodes the password `testtest` for every
  account and is a developer tool only. Import OSFR's AGPL launcher
  (https://github.com/Open-Source-Free-Realms/Launcher, C#, with Windows, macOS and Linux build scripts) into a
  new `launcher/` folder, keeping its license and crediting it in the README and the commit. Then make it ours.
- **Make it ours:** first, find out how it chooses a server and logs in. Then set our server as its default
  (one setting, so the address can change), use real registration and login against our WebAPI with the
  password the player types, and handle errors clearly (wrong password, banned account, server down). Brand it
  plainly as our server. Don't advertise the old project.
- **Security:** never store a password; store the session token only if the launcher already does that safely.
  Use HTTPS for WebAPI calls wherever the launcher supports it, and say plainly in the PR what still goes over
  plain HTTP. Note anything the WebAPI needs for HTTPS at deployment. Task 15 fixed WebAPI's side: follow the
  launcher contract in `docs/webapi.md` (status codes, `Retry-After`, passing `launchArguments` through unchanged).
- **Scope:** `launcher/`, its CI build if it fits the existing workflow, and docs. No server changes; if the
  launcher needs one, stop and propose it in the PR.
- **Check:** the self-check still passes, and the launcher builds for win-x64.
- **Deliverable:** a PR with the launcher, what was changed from OSFR's, and in-game steps for Nate: register,
  log in, play, and each error case.

## Security and stability fixes (from the task 9 audit and the task 10 load test)

Findings are numbered as in `docs/security/threat-model.md` (F1–F13). Each task below fixes its findings as
separate commits, adds tests where code can be tested without a client, and states what still needs a live
check. The load-test bot (`src/Sanctuary.LoadTest`) is available for anything about load or the network.

## 13. `launch-config`: no Debug, no secrets, no dev economy (F1, F2, F3, F9, F13)

- **F1:** a Debug build must refuse to start unless an explicit setting (for example `AllowDebugBuild=true`)
  is present, and it must log a loud warning when it does start. Release builds are unaffected.
- **F2:** the `LoginGatewayChallenge` value in the repo is OSFR's public default, already known worldwide, so
  **don't rewrite git history**. Remove it from tracked config; Login and Gateway read it from configuration
  (environment variable or a local, git-ignored file); and both refuse to start if it's missing or still the
  old default. Ship `.example` files documenting every setting.
- **F3:** make the address the LoginGateway listener binds configurable, defaulting to `127.0.0.1`, so on a
  single VPS only the local Gateway can reach it.
- **F9:** set launch-appropriate defaults: starting coins and station cash, `UnlockAllTitles`,
  `UnlockAllProfiles` and `MemberByDefault` off. Keep the dev values available through a documented local
  config file, because playtests use them.
- **F13:** `docker-compose.yml` takes database credentials from the environment with no defaults, and never
  publishes the database port.
- **Check:** the self-check, a boot with the new config, and a boot that refuses each bad case (Debug without
  the setting, missing challenge, default challenge). Update `CLAUDE.md` and the README with how to run locally
  now.

## 14. `udp-hardening`: one bad client can't take the server down (F4, F5, F10, task 10's findings)

- **F4:** an exception while processing one connection's packets (including inside `Sanctuary.UdpLibrary`, as
  in `ProcessCookedPacket`) drops that connection and logs it. It must never stop the host.
- **F5:** per-IP connection limits and connect-rate limits; lower `IncomingLogicalPacketMax` for the
  player-facing roles to the largest packet the server really receives; cap fragment buffers in flight per
  connection.
- **F10:** a connection that throws repeatedly is disconnected, with a log line saying why.
- **Task 10's findings:** raise the Gateway's player-facing receive buffer (the load test measured 4 MiB as
  enough for 200 clustered bots), and bound how far a connection can fall behind before it's disconnected, so
  an overloaded server recovers instead of growing without limit (948 MiB in the test).
- **Check:** the self-check, tests for the limits, and load-test runs at 100, 150 and 200 clustered bots that
  show no socket drops and recovery afterwards. Add the results to `docs/performance/baseline.md`.

## 15. `webapi-hardening`: the website side (F6, F7)

- **F6:** `/image` requires a valid session that owns the `characterId`, caps request size, caps storage per
  character, and is rate-limited.
- **F7:** per-IP rate limits on `/register` and `/login`, with a lockout after repeated failures per account
  that doesn't let an attacker lock any account out forever. Make HTTPS deployment straightforward: document
  running WebAPI behind a TLS reverse proxy (for example Caddy) on the VPS, and make the launcher's expectations
  (task 12) match.
- **Check:** the self-check, plus tests with the ASP.NET test host for each limit and each authorization rule.

## 16. `defence-in-depth`: Lua sandbox and dependencies (F11, F12)

- **F11:** open only the safe Lua libraries (base, table, string, math). No `os`, `io`, `package` or
  `debug`. Confirm every shipped script still loads and runs at boot.
- **F12:** update the packages so `dotnet list package --vulnerable --include-transitive` reports nothing,
  without moving off .NET 9 unless that's unavoidable (say so in the PR if it is).
- **Check:** the self-check, a boot, and the vulnerability scan's output in the PR.

## 17. `quest-turn-in-id`: hand in the right quest

- **Task:** the playtest-plan session found that the quest turn-in reply doesn't say which quest it's for, and
  the server hands in the oldest one waiting. Closing one turn-in window and accepting another could complete
  the wrong quest. Confirm it in the code, then make the server hand in the quest whose window was shown last,
  or otherwise tie each reply to its window, and add tests.
- **Check:** the self-check. In-game step: playtest step QX1 in `docs/playtest-plan.md`.

## 18. `status-reflection`: the Login status reply can't be used to flood others

- **Task:** task 14 found that the Login server answers a status request from anyone, and the reply is larger
  than the request. An attacker can forge the sender address and use our server to flood a victim, which can
  also get the VPS suspended. Find every unauthenticated reply on the Login and Gateway UDP ports, and make each
  one no larger than the request that caused it, and rate-limited per source address. Or remove it, if the
  launcher (task 12) and the client don't need it.
- **Check:** the self-check, plus tests that measure reply size against request size and that the rate limit
  applies. Say which client or launcher feature depends on the status reply, if any, and confirm it still works.

## 19. `coin-store-once`: stop resending the coin store on every zone entry

- **Task:** task 14 measured about 4.2 MiB queued per player on every zone entry, houses included, of which
  3.5 MiB is `BaseZone.SendCoinStoreItemList`. Find out whether the client keeps the list across zone changes. If
  it does, send it once per login. If it doesn't, find a smaller way (for example, only items the client
  doesn't have). Don't guess: if the client's behaviour can't be confirmed without the game client, make it a
  setting defaulting to the old behaviour, and list the in-game check for Nate.
- **Check:** the self-check, plus a load-test run showing the queued bytes per zone entry before and after.

## 20. `launcher-supply-chain`: a server break-in can't reach players' PCs *(after PR #15 is merged)*

- **Task:** the launcher (PR #15) updates itself from `{DefaultServerUrl}/launcher`, on the same VPS as the game,
  so anyone who breaks into the VPS could push a malicious launcher to every player. Switch Velopack's update
  source to this repository's GitHub Releases (Velopack's GitHub source), so the VPS has no say in what the
  launcher installs. Keep the HTTPS rule. Add a release workflow, or document the exact manual steps, that
  builds and publishes a release only from `main` with an explicit trigger.
- **Also:** pin the game client's executable. Add a launcher setting holding the expected SHA-256 of
  `FreeRealms.exe` (the client every player already has); before launching, compute the hash and refuse, with a
  clear message, if it doesn't match. A server's client manifest may download other files, but never a
  `FreeRealms.exe` that fails the pin.
- **Scope:** `launcher/` and a release workflow under `.github/workflows/` if you add one. No server changes.
- **Check:** the self-check, the launcher's tests (add tests for the hash pin and the update source), and a
  win-x64 build.
- **Deliverable:** a PR that says how Nate publishes a release, where to put the real hash, and what a player
  sees if the pin fails.

## 21. `creative-housing`: building is free and unlimited, like Minecraft's creative mode

- **Task:** Nate wants housing to play like creative mode: no paying for building parts. Today furniture
  ("fixtures") is inventory: players buy items in the store, placing one consumes it
  (`HousingZoneRuntime`, around the consume paths near lines 572–621 and 738–811), and picking it up returns it.
  Add a setting `Housing:CreativeMode`, defaulting to on. With it on:
  - the editor's fixture list offers every placeable fixture (see `HousingPlacementCatalog` and
    `IsFixtureInventoryItem`), in every tint, with an unlimited count;
  - placing never consumes inventory, and picking up never returns an item, so the editor can't be used to
    create items that could be traded or sold;
  - placed fixtures still save with the house, and fixtures players already own keep working.
  With it off, behaviour is exactly as now.
- **Lots:** add a separate setting `Housing:FreeLots`, defaulting to on, that makes lots cost nothing.
- **Watch the size:** the full fixture catalog may be large. Measure the fixture-list packet. If the client's
  handling of a very large list can't be confirmed without the game, page or cap it, and list the in-game
  check.
- **Check:** the self-check and a boot, plus tests that placing doesn't consume, picking up doesn't return, a
  creative fixture can never reach inventory or a trade, and turning the setting off restores today's behaviour.
- **Deliverable:** a PR with in-game checks: open the editor, browse the catalog, place, move and pick up pieces,
  and confirm the inventory doesn't change.

## 22. `client-pinning`: verify every client file (launch requirement)

- **Task:** task 20 pinned only `FreeRealms.exe`, but the game also loads DLLs and other files beside it. Many
  players will reuse a client that came from the former FreeRealms-Legacy owner's distribution, and he is treated
  as hostile. Ship known-good SHA-256 hashes for **every** client file with the launcher (built from the official
  OSFR client manifest, `https://opensourcefreerealms.com/clientmanifest.xml`, at a pinned version). On first run
  and before each launch, verify the player's client; replace any missing or mismatched file from the official
  OSFR client download, verified against the pinned hash before it's written. Never run a client with an unverified
  executable or DLL. Files the server adds may only be data files, never executables or libraries.
- **Also:** let a player point the launcher at an existing client folder, so returning players don't download
  1 GB again.
- **Scope:** `launcher/`, plus a script under `launcher/` that regenerates the hash list from a manifest.
- **Check:** the self-check, the launcher's tests (a tampered DLL is replaced, a missing file is fetched, a
  server-added `.dll` or `.exe` is refused), and a win-x64 build.
- **Deliverable:** a PR explaining how the hash list is made and refreshed, and what a player sees while their
  client is checked or repaired.
