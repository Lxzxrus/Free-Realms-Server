# Free Realms server (standalone)

Nate's own Free Realms server: Nate and Claude develop, his wife playtests, and it will run on a Linux PC.
The code is Sanctuary, the Open Source Free Realms (OSFR) emulator in C#/.NET. `main` is OSFR `main` plus our
work. The repo is private for now. The code is AGPL-3.0, so it goes public, or source is offered to players,
at launch.

**Priorities:** housing first, then opening unreleased map zones, then items made by volunteer artists.
Minigames and more quests wait until there's a lull.

## Build and self-check

```bash
cd src && dotnet restore && dotnet build --no-restore && dotnet test --no-build
```

This is what CI runs (`.github/workflows/build.yml`). A task isn't done until it passes. Needs the .NET 10
SDK, the .NET 9 runtime and a running MariaDB (the MySQL test fails, not skips, without one), which
`scripts/cloud-setup.sh` installs and starts in cloud sessions. Measurements and quirks: `docs/cloud-environment.md`.

## Branches

| Branch | What it is | Rule |
|---|---|---|
| `main` | OSFR `main` plus our work | Never push to it directly. Changes arrive by PR |
| `import/housing-full-archive` | The housing system by `raisingkaines`, from his OSFR fork | Read-only source. Credit him in commits |
| `import/quest-upstream-v2` | JadenY's quest system, from Sulphural's fork | Read-only source. Credit JadenY |
| `import/frl-main` | FreeRealms-Legacy. **No git history in common with `main`**; port by hand | Read-only. Trading lives in `src/Sanctuary.Game/Trading` |
| `import/frl-quests` | Our port of JadenY's quests to FreeRealms-Legacy, verified in the game client | Read-only reference for tests and lessons |
| `archive/sulphural-main-2026-08-18` | JadenY's big merge of housing, pets, mounts and more. The only surviving copy | Read-only reference. **Don't merge it**: 124+ conflicts |

Both imports share OSFR history with `main`, so git merges them natively. Housing was merged in Task 2
(12 conflicted files) and quests in Task 3 (5 conflicted files). Quest hooks on zone entry live in
`WorldZone.OnClientIsReady`. JadenY's comment stripping of shared OSFR files was not taken, so upstream's comments
stay and future OSFR merges stay small.

Housing was written against an early prototype of upstream's zoning rewrite. The merge kept `main`'s final
zoning (`ZoneManager.TryMovePlayerToZone`, `EvictIfEmpty`), so enter zones through `Player.TeleportToZone`,
never by creating a zone instance directly.

## Rules for every session

- One task = one session = one PR into `main`. Work on the branch the session was assigned (`claude/...`) and
  title the PR `Task N: <task name>`. Never push to `main` or to `import/*`/`archive/*`.
- Never commit game client files (`client/` is ignored) or extracted assets.
- Don't touch `.github/workflows/update-public-server.yml`.
- End commit messages with a `Co-Authored-By: Claude` trailer, and credit the original author when merging an import.
- Build and tests passing isn't the finish line. List exactly what Nate must check in the game client, because
  nothing here has been verified in game until he says so.

## Lessons from porting quests (read before merging or porting anything)

- When a feature arrives from another fork, its changes to **shared** packet files matter as much as its own
  files. Reward previews, the npc-add packet's quest marker and `NotificationInfo` all broke silently because
  our copies of shared files were kept. Resolve conflicts in shared packet files toward the side that the
  feature's author tested against the client, and say which way you went.
- A handler that exists but isn't routed in its dispatcher does nothing. Check every new handler has a route.
- `.Include(...)` in the login character query decides what a player gets back on relog.
- Static packet handlers get services in `ConfigureServices`. A field declared `= null!` and never assigned
  compiles cleanly and crashes at runtime.
- The defects found in JadenY's quest code (`&&` for `||` in `BaseQuestPacket.TryRead` and
  `TakeMeThereRequestPacket`, a single pending turn-in slot, two packets calling their sub-opcode `OpCode`) were
  fixed in Task 3. The same `&&` opcode guard is in about 30 of upstream OSFR's packet base classes, so any
  packet's `TryRead` accepts the wrong opcode. Don't copy that pattern; fix it upstream-wide as its own task.

Task specs for cloud sessions: `docs/cloud-tasks.md`.
