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
