# Status

*Where the project stands. **Only Nate or his local Claude session edits this file**, after merges. Cloud
sessions read it but never change it, so parallel PRs can't conflict on it.*

Last updated: 2026-09-26.

## Merged into `main`

| PR | Task | What it did | Verified in game? |
|---|---|---|---|
| #1 | 1: env-check | Cloud setup installs .NET 10 SDK, .NET 9 runtime and MariaDB; environment recorded in `docs/cloud-environment.md` | n/a |
| #2 | 2: housing-merge | raisingkaines' housing: lots, editor, fixtures, directory, ratings. 3 migrations | **No** |
| #4 | 3: quests-merge | JadenY's quests (24) plus fixes: opcode guards, turn-in queue, `SubOpCode` naming | **No** |
| #3 | 5: security-recheck | S1 (no HTTP bodies in debug logs), S3 (portrait size check), S5 (banned-account login), P1 (broadcast serialized once) | **No** |

## Next

- **Task 4 (trading-port):** unblocked now that quests are merged.
- **Tasks 6–8:** specced in `docs/cloud-tasks.md`. Independent of each other and of task 4.
- **In-game housing test** on Nate's PC with his wife: the checklist is in PR #2. It needs a fresh database,
  because OSFR's schema history differs from FreeRealms-Legacy's.

## Known problems

- Five quests reference npcs that nothing spawns (task 6).
- About 30 OSFR packet `TryRead` guards use `&&` where `||` was meant (task 7).
- An empty house shuts down as soon as its last player leaves. Housing originally waited a moment first; watch
  relogs inside a house.
- WebAPI needs the ASP.NET Core 9 runtime, which cloud sessions don't install, so it builds there but can't run.

## Cloud credit

$100 one-time credit, expires 4 November 2026. Measured costs: task 1 ~$2 (including a blocked first attempt),
task 2 ~$5, tasks 3 and 5 ~$7 together. $86 left after those.
