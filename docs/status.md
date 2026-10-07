# Status

*Where the project stands. **Only Nate or his local Claude session edits this file**, after merges. Cloud
sessions read it but never change it, so parallel PRs can't conflict on it.*

Last updated: 2026-10-08.

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
| #11 | 9: security-audit | Threat model `docs/security/threat-model.md` (F1–F13); backdoor review of imported code found nothing deliberate | n/a |
| #10 | 10: load-test-bot | `src/Sanctuary.LoadTest` and `docs/performance/baseline.md`: first limit is the Gateway's 64 KiB UDP receive buffer, not CPU | n/a |
| #9 | 11: playtest-plan | `docs/playtest-plan.md`: two evenings, ordered, with pass/fail columns | n/a |
| #12 | 13: launch-config | F1 Debug guard, F2 challenge out of the repo (local `*.local.json`), F3 LoginGateway on 127.0.0.1, F9 launch economy defaults (0 coins: placeholder), F13 compose credentials | **No** |
| #14 | 14: udp-hardening | F4 fault isolation, F5 per-IP and size limits, F10 fault counter, 4 MiB buffers, backlog limits (`docs/udp-limits.md`); 200 clustered bots, no drops | **No** |
| #13 | 15: webapi-hardening | F6 signed `/image/<token>` portraits, F7 rate limits and lockouts, forwarded headers, Caddy/HTTPS doc (`docs/webapi.md`) | **No** |
| #15 | 12: launcher | OSFR's launcher made ours: no stored passwords, HTTPS-only outside the LAN, path guards. Name, domain and client hosting are placeholders | **No** |
| #16 | 18: status-reflection | Every reply to an unverified sender is no larger than its request and rate-limited (five reflection paths closed); launcher pads its status ping | **No** |
| #17 | 20: launcher-supply-chain | Launcher updates only from this repo's GitHub Releases (manual workflow on `main`); `FreeRealms.exe` hash pin, which must be filled in before a release | **No** |
| #18 | 16: defence-in-depth | F11 Lua opens only base/table/string/math (no `os`, `io`, `package`, `debug`, `dofile`, `loadfile`); F12 EF Core 9.0.20, vulnerability scan clean | **No** |

## Direction (2026-10-06)

Going **public** on a budget VPS with DDoS protection; the Optiplex becomes staging. Security is a launch
blocker: assume a capable insider attacker who knows this code, and keep every secret out of the repo, because
the source goes public at launch. Strictly defensive.

## Next

- **Playtest.** Nothing is verified in game yet. Task 11 compiles one ordered plan from every PR's checklist.
- **Now:** task 21 (creative housing), started on the last $5 of credit and continuing on Nate's plan. Then the
  playtest, then the phases in `docs/design/evergrove-housing.md`. Specced but not started: 17 (quest turn-in
  id), 19 (coin store resend). To spec: whole-client file pinning, and forged-packet disconnects.
- **Before launch:** name decided (Evergrove; confirm the launcher app id), domain bought (`evergrove.fyi` at Porkbun), decide on client
  hosting, fill in the `FreeRealms.exe` hash, and run the playtest. Self-hosting OSFR's asset server comes later,
  with the art pipeline.
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

## Cloud credit

$100 one-time credit, expires 4 November 2026. Measured costs: task 1 ~$2 (including a blocked first attempt),
task 2 ~$5, tasks 3 and 5 ~$7 together, tasks 4, 6, 7 and 8 ~$8 together, tasks 9-11 plus task 12's first attempt ~$29 together, tasks 13-15 ~$23 together, task 12's launcher ~$11, tasks 18 and 20 ~$8, task 16 ~$2. The last $5 went to task 21.
