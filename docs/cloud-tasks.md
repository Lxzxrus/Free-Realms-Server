# Cloud tasks

One task = one cloud session = one branch `cloud/<name>` = one PR into `main`. Read `CLAUDE.md` first. The
self-check is `cd src && dotnet restore && dotnet build --no-restore && dotnet test --no-build`.

Order: 1 is the cost calibration and runs alone. 2 is the priority. 3–5 can run in parallel once 2 is merged.

---

## 1. `cloud/env-check`: calibrate the environment

- **Task:** confirm a cloud session can build and test `main`, and record how.
- **Scope:** `docs/cloud-environment.md` (new). `scripts/cloud-setup.sh` only if the install fails for a
  reason you can fix.
- **Out of scope:** everything under `src/`.
- **Check:** the self-check passes.
- **Deliverable:** a PR whose doc records the SDK and runtime versions, test counts, how long setup, restore,
  build and test each took, and anything the environment blocked.

## 2. `cloud/housing-merge`: bring in housing

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

## 3. `cloud/quests-merge`: bring in quests *(after 2 is merged)*

- **Task:** merge `import/quest-upstream-v2` into a branch from `main`, then fix the known defects listed in
  `CLAUDE.md`.
- **Scope:** anything the merge touches, plus the defect fixes and tests.
- **Reference:** `import/frl-quests` is our FreeRealms-Legacy port, verified in game. Use its
  `QuestPacketTests.cs` and `RewardBundleTests.cs` as a model for tests. Don't copy its code blindly: that port
  adapted JadenY's code to a different codebase.
- **Check:** the self-check and a boot logging `Loaded N quests`.
- **Deliverable:** a PR crediting JadenY, with an in-game checklist.

## 4. `cloud/trading-port`: port trading from FreeRealms-Legacy

- **Task:** hand-port player-to-player trading from `import/frl-main` (`src/Sanctuary.Game/Trading` and its
  handlers, registrations, and database changes if any).
- **Scope:** the trading files and the minimum wiring. No unrelated changes.
- **Check:** the self-check. Add tests for any trade-commit logic that can be tested without a client.
- **Deliverable:** a PR listing every file ported and every adaptation made.

## 5. `cloud/security-recheck`: carry over our FreeRealms-Legacy fixes

- **Task:** for each fix below, check whether `main` still has the problem. If it does, port the fix from
  `import/frl-main` as its own commit.
  - S1: debug builds log HTTP request and response bodies, including passwords.
  - S3: the portrait upload size checks are inverted.
  - S5: banned accounts can still get a launcher session.
  - P1: broadcasts re-serialize the packet once per recipient.
- **Scope:** only the files those fixes touch.
- **Check:** the self-check.
- **Deliverable:** a PR with one commit per fix and a table saying which were already fixed upstream.
