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
  plain HTTP. Note anything the WebAPI needs for HTTPS at deployment.
- **Scope:** `launcher/`, its CI build if it fits the existing workflow, and docs. No server changes; if the
  launcher needs one, stop and propose it in the PR.
- **Check:** the self-check still passes, and the launcher builds for win-x64.
- **Deliverable:** a PR with the launcher, what was changed from OSFR's, and in-game steps for Nate: register,
  log in, play, and each error case.
