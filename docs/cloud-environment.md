# Cloud environment

What a Claude Code cloud session gives this repo, measured on 2026-09-26 (Task 1, `env-check`) against `main` at
`f03d49e`. Re-measure if the setup script, `global.json` or the target framework changes.

## Result

The self-check passes: `cd src && dotnet restore && dotnet build --no-restore && dotnet test --no-build`, with 0
warnings, 0 errors and 30 of 30 tests passing.

That required one fix. As first measured, the self-check **failed**. `MySqlTest.IsValidAsync` does not skip when no
database is running, whatever `CLAUDE.md` said. It retries 6 times over about a minute, then throws
`RetryLimitExceededException`, and `dotnet test` exits 1. `scripts/cloud-setup.sh` now installs and starts MariaDB
and creates the same database and user that CI's MariaDB service has, so the test runs for real, the way it does in CI.

## Machine

| | |
|---|---|
| OS | Ubuntu 24.04.4 LTS, x64, running as root |
| CPU / RAM | 4 cores, 15 GB, no swap |
| Disk | 30 GB free at session start. .NET takes 0.7 GB and the NuGet cache another 0.7 GB |
| Init | No systemd. Start services with `service <name> start` |
| Docker | CLI 29.3.1 installed, **no daemon** (`/var/run/docker.sock` missing). Don't plan on containers |

## Versions

| Component | Version | Notes |
|---|---|---|
| .NET SDK | 10.0.401 | `src/global.json` asks for 10.0.100 with `latestFeature` roll-forward |
| Microsoft.NETCore.App | 9.0.20 and 10.0.12 | Every project targets `net9.0` |
| Microsoft.AspNetCore.App | 10.0.12 only | **No 9.x.** Building works, but running `Sanctuary.WebAPI` needs ASP.NET Core 9 (`dotnet-install.sh --channel 9.0 --runtime aspnetcore`). Login and Gateway need only NETCore 9 |
| MariaDB | 10.11.14 (Ubuntu package) | CI uses `mariadb:11.6`, and the test sets `VersionString` to `11.6.0-MariaDB`. Migrations apply cleanly on 10.11 anyway |
| EF Core | 9.0.17 (`Directory.Packages.props`) | |
| dotnet-ef | Not installed by default | See the housing-merge notes below |

## Tests

| Assembly | Tests |
|---|---|
| Sanctuary.Game.Tests | 25 |
| Sanctuary.Scripting.Tests | 2 |
| Sanctuary.Database.Tests | 2 (SQLite in memory, MariaDB) |
| Sanctuary.UdpLibrary.Tests | 1 |
| **Total** | **30 passed, 0 skipped, 0 failed** |

## Timings

"Cold" means an empty NuGet cache and no `bin`/`obj` directories, the state of a fresh session. Times are wall clock.

| Step | Cold | Warm | Notes |
|---|---|---|---|
| Setup: .NET SDK 10 + runtime 9 | 17 s | – | Download from dot.net |
| Setup: whole script, .NET + MariaDB | 31 s | 0.1 s | apt-get update about 3 s, MariaDB install about 9 s, start about 1.5 s |
| Setup: container restarted, MariaDB installed but stopped | – | 1.2 s | Only starts the server |
| `dotnet restore` | 5.7–10.6 s | – | 13 projects, 107 packages, 710 MB. Network speed accounts for the spread |
| `dotnet build --no-restore` | 9.3–18.0 s | – | The first build of a session is the slow one |
| `dotnet test --no-build` | 7.6 s | – | MariaDB running |
| `dotnet test --no-build` without MariaDB | 64 s, **fails** | – | The MySQL test times out, as described above |
| Whole self-check, warm | – | 8.4 s | Incremental build |

A fresh session's cold path costs about 31 s of setup (it runs in the SessionStart hook before the first prompt)
plus about 25–35 s for the self-check.

## What the environment blocked

Nothing that the self-check needs. These hosts were reachable through the agent proxy: `dot.net` and its CDN (the
.NET installer), `api.nuget.org` (restore and `dotnet tool install`), the Ubuntu archive (apt), and GitHub
(`git ls-remote` lists `main`, all four `import/*` branches and the `archive/*` branch).

These are missing but not blocked:

- **systemd.** The MariaDB package's post-install start is refused by `policy-rc.d`. The setup script starts
  MariaDB with `service` instead.
- **Docker daemon.** The CLI is present but has nothing to talk to.
- **ASP.NET Core 9 runtime.** See Versions. Only needed to run `Sanctuary.WebAPI`.

## Notes for the housing merge (Task 2)

- `dotnet tool install --global dotnet-ef --version 9.0.17` works (3 s). Pin the version: an unpinned install gets
  10.x, which doesn't match EF Core 9.0.17. Add `$HOME/.dotnet/tools` to `PATH`.
- The design-time factories (`SqliteDatabaseFactory`, `MySqlDatabaseFactory`) read `Database:*` from user secrets
  (`UserSecretsId` `osfr-sanctuary`, shared by both projects), not from files in the repo. Without them `dotnet ef`
  fails with `Value cannot be null. (Parameter 'databaseOptions')`. This works, one provider at a time, since the
  two share one secrets store:

  ```bash
  P=Sanctuary.Database.Sqlite
  dotnet user-secrets set --project $P Database:Provider Sqlite
  dotnet user-secrets set --project $P Database:ConnectionString "Data Source=design.db"
  dotnet ef migrations has-pending-model-changes --project $P --startup-project $P

  P=Sanctuary.Database.MySql
  dotnet user-secrets clear --project $P
  dotnet user-secrets set --project $P Database:Provider MySql
  dotnet user-secrets set --project $P Database:ConnectionString "server=127.0.0.1;port=3306;uid=user;pwd=password;database=sanctuary_test"
  dotnet user-secrets set --project $P Database:VersionString 11.6.0-MariaDB
  dotnet ef migrations has-pending-model-changes --project $P --startup-project $P
  ```

  On `main` both report "No changes have been made to the model since the last migration."
