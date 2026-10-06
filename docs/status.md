# Status

*Where the project stands. **Only Nate or his local Claude session edits this file**, after merges. Cloud
sessions read it but never change it, so parallel PRs can't conflict on it.*

Last updated: 2026-10-06. CI on `main` is green after PR #8.

## Merged into `main`

| PR | Task | What it did | Verified in game? |
|---|---|---|---|
| #1 | 1: env-check | Cloud setup installs .NET 10 SDK, .NET 9 runtime and MariaDB; environment recorded in `docs/cloud-environment.md` | n/a |
| #2 | 2: housing-merge | raisingkaines' housing: lots, editor, fixtures, directory, ratings. 3 migrations | **No** |
| #4 | 3: quests-merge | JadenY's quests plus fixes: opcode guards, turn-in queue, `SubOpCode` naming | **No** |
| #3 | 5: security-recheck | S1 (no HTTP bodies in debug logs), S3 (portrait size check), S5 (banned-account login), P1 (broadcast serialized once) | **No** |
| #5 | 4: trading-port | Player trading from FreeRealms-Legacy (raisingkaines): ordered locks, no item duplication mid-trade | **No** |
| #6 | 6: unreachable-quests | Five quests with missing npcs moved to `Quests.disabled.json` (19 load); tests for npc guids and quest references | **No** |
| #7 | 8: housing-toggles | Lock, flora and pet-autospawn toggles; owner-only, which fixed a hole in JadenY's version | **No** |
| #8 | 7: opcode-guards | 58 `&&` opcode guards fixed in 41 files, with a test per guard; cake broadcasts serialized once | **No** |

## Direction (2026-10-06)

Going **public** on a budget VPS with DDoS protection; the Optiplex becomes staging. Security is a launch
blocker: assume a capable insider attacker who knows this code, and keep every secret out of the repo, because
the source goes public at launch. Strictly defensive.

## Next

- **Playtest.** Nothing is verified in game yet. Task 11 compiles one ordered plan from every PR's checklist.
- **Merge** the task 9 audit (no PR yet), #9 (playtest plan) and #10 (load-test bot); finish task 12 (launcher).
- **Security and stability fixes:** tasks 13–17 in `docs/cloud-tasks.md`, from the audit (F1–F13) and the
  load test. 13–15 are launch blockers. The audit's backdoor review of imported code found nothing deliberate.

## Known problems

- The quest loader doesn't reject references to quest ids that don't exist. Only a test catches them (PR #6).
- An empty house shuts down as soon as its last player leaves. Housing originally waited a moment first; watch
  relogs inside a house.
- Locking a house doesn't remove visitors already inside, and the client has a second lock packet nothing handles
  yet (PR #7).
- A trade is refused if a player's in-game inventory doesn't match the database, so any feature that changes one
  without the other breaks trading (PR #5).
- Player titles: the dispatcher reads the sub-opcode as 2 bytes while the packet reads 4. It works, but it's
  inconsistent (PR #8).
- WebAPI needs the ASP.NET Core 9 runtime, which cloud sessions don't install yet.

## Cloud credit

$100 one-time credit, expires 4 November 2026. Measured costs: task 1 ~$2 (including a blocked first attempt),
task 2 ~$5, tasks 3 and 5 ~$7 together, tasks 4, 6, 7 and 8 ~$8 together, tasks 9-11 plus task 12's first attempt ~$29 together. $49 left.
