# Threat model and ranked findings

*Task 9 (`security-audit`). Analysis only — no code under `src/` was changed. Produced against `main` plus the
work on this branch, 2026-10-06.*

This maps every way into the server and the ways it can be abused, ranked by severity, and names a fix and a
task-sized estimate for each. The server is going **public on a budget VPS** (the Optiplex becomes staging), and
the source is **AGPL, public at launch** — so every finding assumes the attacker has read all of this code, and
no finding relies on code being secret. The stated adversary is a capable insider who knows the codebase
(`raisingkaines`, who wrote the housing and trading we merged); the backdoor review of his code is in its own
section below.

**How to read "Proven":** only the dependency scan (F12) was run as a live command. Everything else is confirmed
by reading the code, with file and line. The `#if DEBUG` findings (F1) are compile-time certain: they are not
"maybe", they are what the Debug binary does. Nothing was reproduced against a running server, because the task
is analysis-only and a load/repro harness is Task 10's job; where a finding would be cheap to prove at runtime it
says so.

## Severity table

| # | Sev | What an attacker does | What it costs us | Where | Fix | Est. |
|---|-----|-----------------------|------------------|-------|-----|------|
| F1 | **Critical** | Runs a **Debug** build in production (or an attacker reaches one): logs in as any character with no ticket, and every player silently has **Admin** chat commands | Total account takeover and world control; with F11, server-side code execution | `Sanctuary.Gateway/Handlers/PacketLoginHandler.cs:101`, `Sanctuary.Login/Handlers/LoginRequestHandler.cs:63,75`, `Sanctuary.Login/LoginConnection.cs:104`, `Sanctuary.Game/Helpers/ChatHelper.cs:34` | Make Release the only shippable config; add a startup guard that refuses to run if built `DEBUG`; document "never deploy Debug" | S |
| F2 | **Critical** | Reads committed secrets straight from the public repo | The Login↔Gateway trust boundary and the dev economy grants are open to everyone at launch | `src/Sanctuary.Login/login.json`, `src/Sanctuary.Gateway/gateway.json`, `src/Docker/docker-compose.yml` | Remove tracked config from git, ship `.example` files, load real values from env/secrets, rotate the challenge, `git` history scrub before going public | M |
| F3 | **High** | Connects to the Login UDP port and registers a rogue game server (passes the static challenge from F2) | Players are handed an attacker-controlled gateway address; account-online state is forgeable | `Sanctuary.Login/Handlers/Gateway/GatewayLoginRequestHandler.cs:30`, `Sanctuary.Login/GatewayServer.cs:18` | Firewall the LoginGateway port to the gateway host only; rotate the challenge out of the repo; treat it as a bootstrap secret, not an auth | S |
| F4 | **High** | Sends a crafted packet whose handling throws inside the UDP library (outside the handler try/catch) | The single server loop's exception stops the `BackgroundService` → whole server down (one packet) | `Sanctuary.UdpLibrary/UdpConnection.cs:734` (`ProcessCookedPacket`), `Sanctuary.Gateway/GatewayService.cs:156`, `Sanctuary.Login/LoginService.cs:78` | Wrap per-connection `GiveTime`/packet processing in try/catch that drops the offending connection, not the host; set `BackgroundServiceExceptionBehavior.Ignore` deliberately and log | M |
| F5 | **High** | Floods unauthenticated UDP: connection/handshake spam up to 2000, or fragmented packets that each allocate up to 20 MB | CPU and memory exhaustion on one core; all players stall or the box OOMs | `Sanctuary.UdpLibrary/UdpManager.cs:536` (`MaxConnections`), `Sanctuary.UdpLibrary/Internal/UdpReliableChannel.cs:843` (`new byte[BigDataTargetLen]`), default `IncomingLogicalPacketMax` 20 MB | Per-IP connection and rate caps; lower `IncomingLogicalPacketMax` to the real max packet; cap concurrent in-flight fragment buffers; put the VPS behind the planned DDoS filter and rate-limit at the firewall | M |
| F6 | **High** | POSTs to WebAPI `/image` with any `characterId` (no auth on the endpoint) | Overwrites any player's portrait; fills disk with uploads | `Sanctuary.WebAPI/Endpoints/PortraitEndpoints.cs:33` | Require a valid session and check it owns `characterId`; cap request body size; cap per-character storage; rate-limit | M |
| F7 | **High** | Hits WebAPI `/register` and `/login` with no throttle, over plain HTTP | Unlimited account creation; password brute-force; credentials sniffable in transit | `Sanctuary.WebAPI/Endpoints/AuthEndpoints.cs:108,109`, `src/Sanctuary.WebAPI/appsettings.json:13` (`http://`) | Terminate TLS in front of WebAPI (the launcher, Task 12, should use HTTPS); add per-IP rate limiting and lockout; CAPTCHA or invite on register | M |
| F8 | **Medium** | Observes or tampers with game traffic | No confidentiality/integrity on gameplay; Login uses a client-hardcoded RC4 key, Gateway encryption is compiled off | `Sanctuary.Gateway/GatewayConnection.cs:42` (`_useEncryption` false), `Sanctuary.Login/LoginConnection.cs:30` (hardcoded RC4 key) | Accept and **document** as a client-protocol limitation; rely on it for nothing; never send a server secret over these channels; keep WebAPI (the one thing we control) on TLS | S (doc) |
| F9 | **Medium** | Registers and immediately owns a maxed account | Every new account starts with ~1e9 coins + station cash, all titles/profiles, member — griefing and a meaningless economy | `src/Sanctuary.Login/login.json` (`StartingCoins`, `StartingStationCash`, `UnlockAll*`), `appsettings.json` (`MemberByDefault`) | Set launch-appropriate starting values and `MemberByDefault:false`; move to config not in the repo (folds into F2) | S |
| F10 | **Medium** | — (defence-in-depth gap) | Release swallows handler exceptions silently (good for uptime, bad for visibility): a packet that repeatedly throws is invisible beyond a log line | `Sanctuary.Gateway/GatewayConnection.cs:119`, `Sanctuary.Login/LoginConnection.cs:80` | Keep the catch, but add a per-connection error counter that disconnects a connection throwing repeatedly, plus a metric/alert | S |
| F11 | **Medium** (Critical under F1) | An Admin (or any player in a Debug build, via F1) runs `!script add` / `!admin` | Lua runs with the **full** standard library, including `os.execute` and `io` — effectively code execution on the box | `Sanctuary.Scripting/ScriptRuntime.cs:21` (`OpenStandardLibraries`), `Sanctuary.Game/ChatCommands/ScriptChatCommand.cs` | Open only the safe Lua libraries (base/table/string/math), not `os`/`io`; keep `!script`/`!admin` Admin-only and fix F1 so Debug can't grant Admin | M |
| F12 | **Medium** | Exploits a known-vulnerable native dependency | `SQLitePCLRaw.lib.e_sqlite3 2.1.10` is flagged High (GHSA-2m69-gcr7-jv3q); it ships via EF Core Sqlite in every project | `src/Directory.Packages.props` (EF Core 9.0.17) | Bump EF Core / pin `SQLitePCLRaw.lib.e_sqlite3` to a patched version; re-run `dotnet list package --vulnerable` | S |
| F13 | **Medium** | Reads default DB creds from the public compose file; reaches the DB port | DB compromise if 3306 is exposed; default `root`/`root` and `sanctuary`/`sanctuary` | `src/Docker/docker-compose.yml` | Require strong env-provided creds (no defaults); never publish 3306; folds into F2 | S |

Severity is "impact if exploited against the public server" × "ease". F1–F3 are launch blockers in the strongest
sense: F1 is a config mistake away from total compromise, and F2/F3 become public knowledge the moment the repo
does.

## Details

### F1 — Debug-build authentication and authorization bypasses (Critical)

Three independent bypasses are compiled only into `DEBUG` builds:

- **Gateway login skips the ticket.** `PacketLoginHandler.cs:101` wraps `&& x.Ticket == ticket` in `#if !DEBUG`.
  In Debug, the character is loaded by guid alone, so the handshake ticket is never checked — any character can be
  logged into by id. `:172` also skips clearing the ticket in Debug, so even the Release single-use property is a
  no-op there.
- **Login session checks are skipped.** `LoginRequestHandler.cs:63` (`|| !user.SessionCreated.HasValue`) and
  `:75` (the 5-minute expiry and the single-use clear) are `#if !DEBUG`. In Debug any row with a matching
  `Session` string logs in, with no expiry and no invalidation.
- **Everyone is Admin.** `ChatHelper.GetRoleFromFlags` (`ChatHelper.cs:34`) returns `ChatCommandRole.Admin` for a
  non-admin, non-mod player under `#if DEBUG`. Every chat command gate is `invoker.ChatCommandRole < RequiredRole`
  (`ChatCommandManager.cs:76`), so in Debug every player can run `!admin`, `!npc`, `!script`, `!reward`,
  `!collection`, etc.

The shipping path is Release: `Docker/Sanctuary.Gateway.dockerfile` builds `-c Release`. So this is not a hole in
the deployed binary today — it is a landmine. The whole security model collapses if a Debug build is ever run in
production, and nothing at runtime stops that.

**Fix:** keep Release as the only deployable configuration, and add a startup guard (a `#if DEBUG` check in each
server's `Program`/service start that logs critical and refuses to bind a public port, or at minimum logs a loud
warning) so a Debug build cannot quietly serve the public. Document it in the deploy runbook. Estimate: small.

### F2 — Committed secrets in a soon-public repo (Critical)

`src/Sanctuary.Login/login.json` and `src/Sanctuary.Gateway/gateway.json` are tracked files (`git ls-files`
confirms) and contain:

- `LoginGatewayChallenge` = `OSFR-EDITz@2024` in both — the **only** credential the Login server uses to decide a
  connecting gateway is legitimate (see F3).
- Dev economy grants: `StartingCoins`/`StartingStationCash` = 999999999, `UnlockAllTitles`, `UnlockAllProfiles`
  (see F9).

`src/Docker/docker-compose.yml` carries default DB credentials (see F13). `Sanctuary.Login/LoginConnection.cs:30`
hard-codes the login RC4 key — but that key is also hard-coded in the game client, so it is not a secret we hold;
it is called out under F8, not here.

Because the repo goes public (AGPL) at launch, anything in git is disclosed, and git history keeps it even after a
later edit.

**Fix:** stop tracking the runtime config. Ship `login.example.json` / `gateway.example.json` with placeholders,
add the real files to `.gitignore`, and load actual values from environment variables or a secrets store (the
code already binds config from env — `Program.cs` `AddEnvironmentVariables`). Rotate the challenge to a fresh
random value that lives only in deployment secrets. Scrub the secrets from git history before the repo is made
public. Estimate: medium.

### F3 — Login↔Gateway trust rests on one static string (High)

`GatewayLoginRequestHandler.cs:30` accepts any connection to the LoginGateway UDP port that presents the matching
`LoginGatewayChallenge` and a non-empty server address, then stores that address and `ServerData`
(`:48–52`). `CharacterLoginRequestHandler` later hands clients `gatewayServer.ServerAddress` as the server to
connect to, and `GatewayCharacterLogin/Logout` (`Handlers/Gateway/*`) let a connected gateway set the
`OnlineCharacters` set that gates "already logged in". With the challenge public (F2), anyone who can reach the
port can register a gateway address of their choosing and manipulate online state.

**Fix:** this port must never be internet-facing — firewall it to the gateway host(s) only. Rotate the challenge
out of the repo (F2) and treat it as a deployment bootstrap value. Longer term, a real mutual auth between Login
and Gateway would remove the reliance on a shared string, but network isolation is the cheap, effective control
for a single-box or small-cluster VPS. Estimate: small (firewall + rotate).

### F4 — One bad packet can stop the whole server (High)

Each server runs a single loop calling `GiveTime()` (`GatewayService.cs:156`, `LoginService.cs:78`). The
per-packet handlers in `GatewayConnection`/`LoginConnection` have a Release `try/catch` (F10), but the UDP library
below them does not: `UdpConnection.ProcessCookedPacket` (`:734`), the reliable-channel reassembly
(`UdpReliableChannel.cs`), and `ProcessRawPacket` run without a guard. An exception there propagates out of
`GiveTime` into the loop; an unhandled exception from a `BackgroundService` stops the host by default in modern
.NET. That turns a single malformed or adversarial packet into a server-wide outage.

**Fix:** wrap the per-connection processing (or the `GiveTime` body) so that a fault drops the offending
connection and is logged, never the host; and set `BackgroundServiceExceptionBehavior` explicitly so the policy is
intentional. Estimate: medium. Cheap to prove once Task 10's harness exists.

### F5 — No flood protection on unauthenticated UDP (High)

The Connect path creates a connection object for any sender up to `MaxConnections = 2000`
(`UdpManager.cs:536`), and all processing is inline on the one loop thread (F4). Fragment reassembly allocates a
buffer sized by the attacker-declared total length, bounded only by `IncomingLogicalPacketMax`, which defaults to
**20 MB** and is not lowered for the external server role (`UdpReliableChannel.cs:837–843`). So a handshake flood
exhausts connection slots and CPU, and a fragmented-packet sender can drive large per-connection allocations.
There is a `>0` and `> IncomingLogicalPacketMax` guard, so it is bounded — but 20 MB × many connections is still a
memory-exhaustion lever, and the single thread means any flood degrades everyone.

**Fix:** per-IP connection and packet-rate limits; lower `IncomingLogicalPacketMax` to the real maximum logical
packet the game uses; cap the number of concurrent in-progress fragment buffers; and lean on the planned VPS DDoS
protection and firewall rate-limiting for volumetric attacks. Estimate: medium.

### F6 — WebAPI `/image` upload has no authentication (High)

`PortraitEndpoints.cs:33` takes `characterId` as an unauthenticated form field and writes to
`Images/{characterId}/...`. There is no session check and no proof the caller owns the character. The content is
validated (PNG, exact 70×70 / 180×330 dimensions, `Path.GetFileName` so no traversal) and a per-path lock
prevents races, but anyone who can reach port 20040 can overwrite any character's portrait or create directories
and files for arbitrary ids. No explicit request-size cap means ImageSharp loads attacker-supplied bytes into
memory.

**Fix:** require a valid session and verify it owns `characterId`; cap request body size and per-character storage;
rate-limit. Estimate: medium.

### F7 — WebAPI auth endpoints: no throttle, plain HTTP (High)

`/register` and `/login` (`AuthEndpoints.cs:108–109`) have no rate limiting. Register allows unlimited account
creation; login is a password-checking oracle open to brute force. Passwords are stored with bcrypt
(`BC.HashPassword` with a generated salt — good) and login failures are uniform, but without throttling that is
not enough. `appsettings.json` binds `http://127.0.0.1:20040`; on the VPS the launcher (Task 12) will send
passwords here, so without TLS in front they cross the network in clear.

**Fix:** put WebAPI behind a TLS-terminating reverse proxy (document the cert/host needs for deployment); add
per-IP rate limiting and temporary lockout on repeated login failures; gate registration (rate limit, and
consider invite/CAPTCHA). Estimate: medium.

### F8 — Game-traffic encryption is effectively off (Medium, documented limitation)

The Gateway compiles `_useEncryption = false` (`GatewayConnection.cs:42`, with the CS0649 warning suppressed
because it is never assigned); only optional ZLib compression is applied (`DecryptUserSupplied`/`EncryptUserSupplied`,
`:706`). The Login server uses RC4 with a key hard-coded in both server and client
(`LoginConnection.cs:30`). So gameplay and login traffic have no meaningful confidentiality or integrity against a
network observer — and because the client dictates this, we cannot fix it server-side.

**Fix:** treat this as a known protocol limitation and **document** it. Never put a server-held secret on these
channels; keep the one channel we control (WebAPI) on TLS (F7); accept that in-world traffic is observable.
Estimate: small (documentation + a don't-trust-the-channel rule).

### F9 — New accounts start maxed (Medium)

`login.json` sets `StartingCoins`/`StartingStationCash` to 999999999 and `UnlockAllTitles`/`UnlockAllProfiles`
true; `appsettings.json` `MemberByDefault` is true. Fine for a test server, wrong for a public one: every fresh
account is instantly rich, fully unlocked, and a member.

**Fix:** set launch-appropriate values and `MemberByDefault:false`; manage them via the externalized config from
F2. Estimate: small.

### F10 — Release silently swallows handler exceptions (Medium, defence-in-depth)

`GatewayConnection.cs:119` and `LoginConnection.cs:80` catch all exceptions from packet handlers in Release and log
them, which is good for uptime (one bad handler won't crash the server) but means a packet that reliably throws is
invisible beyond log noise and can't be told apart from an attack. (The gap below the handlers is F4.)

**Fix:** keep the catch, add a per-connection error counter that disconnects a connection that throws repeatedly,
and emit a metric so a spike is alertable. Estimate: small.

### F11 — Lua scripts get the full standard library (Medium; Critical under F1)

`ScriptRuntime.cs:21` calls `OpenStandardLibraries()`, which includes `os` (with `os.execute`) and `io` (file
access). Scripts are loaded from the server's `Scripts` directory via `!script add` (`ScriptChatCommand.cs`),
which is Admin-only — so this is safe as long as only trusted operators are Admin and only trusted scripts are on
disk. It becomes code-execution-grade the moment an attacker is Admin, which F1 hands to every player in a Debug
build. Even in Release it widens the blast radius of a compromised or careless admin.

**Fix:** open only the safe Lua libraries (base, table, string, math) and deliberately omit `os` and `io`; keep
script management Admin-only; fix F1 so Debug cannot grant Admin. Estimate: medium.

### F12 — Known-vulnerable native dependency (Medium, proven)

`dotnet list package --vulnerable --include-transitive` reports `SQLitePCLRaw.lib.e_sqlite3 2.1.10` as **High**
(GHSA-2m69-gcr7-jv3q) in every project; it arrives transitively through EF Core's Sqlite provider. Production uses
the MySql/MariaDB provider, but the vulnerable native library still ships in the build output.

**Fix:** bump EF Core (or pin `SQLitePCLRaw.lib.e_sqlite3`) to a patched version in `Directory.Packages.props` and
re-run the scan. Estimate: small. **This is the one finding verified by a live command.**

### F13 — Default DB credentials in the compose file (Medium)

`src/Docker/docker-compose.yml` defaults to `root`/`root` and `sanctuary`/`sanctuary` and publishes 3306. On a VPS
with a careless firewall that is a direct path to the database.

**Fix:** require strong, env-provided credentials with no in-file defaults; never expose 3306 publicly. Folds into
F2. Estimate: small.

## What a VPS deployment exposes

- **Ports:** 20040/tcp (WebAPI, HTTP — F7), 20041/udp + 20042/udp (Login/LoginGateway — F3 says 20041 must not be
  public), 20260/udp (Gateway), 3306 (DB — must not be public, F13). Only 20042 (client→Login), 20260
  (client→Gateway) and a TLS-fronted 443→WebAPI should face the internet.
- **DDoS:** UDP is the exposed game surface; rely on the provider's filtering plus F5's app-level limits.
- **Admin access to the box:** SSH should be key-only and firewalled. The existing deploy workflow
  (`.github/workflows/update-public-server.yml`) uses GitHub Actions secrets for the SSH key and host record,
  which is the right pattern — leave it as is (out of scope per project rules).
- **Backups:** the DB holds bcrypt password hashes (acceptable) and live session strings; back it up encrypted and
  off-box, and treat session rows as sensitive.

## Backdoor review of the imported code (PR #2 housing, PR #5 trading)

The task names `raisingkaines` as the expected insider and asks for a line-by-line review of the housing and
trading he wrote, for anything deliberate. I read all of it:

- **Housing (PR #2):** `GuidHelper` (House/Fixture guid types), `ZoneManager.TryGetHouseId`,
  `HouseManager`, `HousingZoneRuntime` (~1870 lines), `BaseRatingPacketHandler`, every housing packet handler,
  `HousingItemDefinitionGenerator`, `HousingPlacementCatalog`, `HousingSurfaceCatalog`, the three EF migrations,
  the teleport-handler and purchase-handler changes, `StoreInventoryPurchasePolicy`, and the shared-file edits
  (`BaseItemDefinition`, `MarketingBundleDefinition`, `BaseHousingPacket`, `Npc`, `Player`, `BaseZone`,
  `ResourceManager`).
- **Trading (PR #5):** `TradeManager`, `TradeCommitter`, `TradeTransferPlanner`, `TradeCommitContracts`,
  `BaseTradePacket`, `TradeInteraction`, and the `UdpConnection.TryAcquireMutationGuard` addition.

**Checked for and did not find:** hidden or default accounts; chat/admin commands or packet sub-opcodes that grant
privilege; authorization that a special value, id, or name bypasses; hard-coded ids or player names with special
treatment; outbound network connections (no `HttpClient`/`Socket`/`Process`/`os` use anywhere in the merged code —
the only `File`/`Path` use is reading `Resources/HousingPlacementData.txt` and the Scripts/Resources loaders that
predate the merge); timers or date/condition checks that change behaviour later (time-bomb); and obfuscated or
dead logic. The housing migrations contain no `InsertData`/`HasData`/raw SQL seed. Hex constants are chat-bubble
colours (`0x063C67`, `0xD4E2F0`); numeric id literals in `HousingItemDefinitionGenerator` map store bundles to
model assets and are inert data.

**What the imported code actually does, and does carefully:** authorization is consistent. Entering a house checks
owner-or-friend and published/locked/members-only (`HouseManager.EnterHouse:138`); editing requires owner **and**
edit mode (`HousingZoneRuntime.CanEdit:1565`); toggles reject non-owners and echo true state back
(`ToggleSetting:233`); voting rejects self-votes and double-votes under a serializable transaction
(`BaseRatingPacketHandler.TryAddVote:276`). House purchase and fixture placement use serializable transactions
with idempotency tokens and overflow guards (`PacketInGamePurchasePlaceOrderPacketHandler.PurchaseHouse:450`,
`HousingZoneRuntime.CommitPlacement:717`). Trading uses ordered per-connection locks to avoid deadlock
(`TradeCommitter.Commit:82`), a single non-retryable serializable transaction, a runtime-vs-database snapshot
reconciliation that aborts on any mismatch (`TryBuildSnapshot`), `checked` arithmetic and stack/coin/id overflow
checks (`TradeTransferPlanner`), and it refuses no-trade and trading-card item types. This reads as defensive,
careful engineering, not as something hiding an advantage.

**Conclusion:** no backdoor or deliberately planted weakness found in the imported housing or trading code. The
findings above are ordinary security gaps in the project as a whole (chiefly the Debug bypasses, committed
secrets, missing TLS/rate-limits, and DoS surface), not anything attributable to the imported work. This is a
read-only review; it reduces but does not eliminate the possibility of something subtle, and the insider threat is
best contained by the controls above (no Debug in prod, secrets out of the repo, network isolation, least
privilege for Admin) rather than by trust.

## Nate must check / decide

Nothing here is verified in the running game — this is static analysis plus one dependency scan. Before launch,
the fixes above need doing and then verifying in game and on the staging box. The order that matters:

1. **F1, F2, F3** are launch blockers and should be fixed first (Release-only + startup guard; secrets out of git
   with history scrubbed; LoginGateway port firewalled).
2. **F6, F7** (WebAPI auth + TLS + rate limits) before the launcher (Task 12) points real players at WebAPI.
3. **F4, F5** (UDP resilience and flood limits) — cheap to prove and harden once Task 10's load harness exists.
4. **F11, F12, F9, F13** as follow-ups.

Each row's estimate is a rough task size (S/M), not a promise. Every finding must be fixed before the repo is
public, because at that point the source — and anything still in it — is open to the stated adversary.
