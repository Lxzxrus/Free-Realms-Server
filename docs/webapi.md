# WebAPI: limits, HTTPS deployment and the launcher contract

WebAPI (`src/Sanctuary.WebAPI`) is the one HTTP service: the launcher calls `/register` and `/login`, and the
game client posts portraits to `/image/{token}`. Task 15 hardened it against findings F6 and F7 in
`docs/security/threat-model.md`. This page covers what it now enforces, how to run it behind HTTPS on the VPS, and
what the launcher (task 12) must expect.

## What WebAPI enforces

| Endpoint | Limit | Default | Answer when hit |
|---|---|---|---|
| `POST /login` | Requests per client address | 10 a minute | `429` + `Retry-After` |
| `POST /login` | Failed logins from one address on one account | locked after 5; 1 min, doubling per further failure, at most 15 min | `429` + `Retry-After` |
| `POST /login` | Failed logins on one account from all addresses | locked for 15 min after 20 within 15 min, **except** for addresses that logged into it before | `429` + `Retry-After` |
| `POST /register` | Requests per client address | 5 an hour | `429` + `Retry-After` |
| `POST /login`, `/register` | Body size | 4 KiB; username ≤ 50, password ≤ 100 characters | `413` or `400` |
| `POST /image/{token}` | Valid token from a login in the last 24 h | — | `401` |
| `POST /image/{token}` | The character belongs to the token's user, who isn't banned | — | `403` |
| `POST /image/{token}` | Requests per client address | 6 a minute | `429` + `Retry-After` |
| `POST /image/{token}` | Body size | 1 MiB | `413` |
| `POST /image/{token}` | Storage per character | two files of fixed size (70×70 and 180×330 PNG), overwritten each time | — |

**How a portrait upload is authorised.** The client posts portraits with no credentials, to whatever
`Portrait:UploadUrl` it was launched with. So on each successful login WebAPI appends
`Portrait:UploadUrl={PortraitUploadUrl}/{token}` to the launch arguments, where the token is 64 hex characters:
the user id, an expiry, and an HMAC over both with a key generated at start-up. Nothing is stored. Restarting
WebAPI invalidates every token, so a player who was logged in before a restart can't upload a portrait until they
log in again. The old `/image` route without a token is gone.

**How lockouts stay temporary.** Every lockout expires. A lockout on one address only stops that address, so an
attacker guessing from their own connection locks out only themselves. Guessing spread over many addresses locks
the account itself, but only for addresses that haven't logged into it in the last 30 days, so the owner can still
log in from home. Unknown usernames are counted the same way as real ones, so a `429` doesn't reveal whether an
account exists, and a missing account takes as long to answer as a wrong password. Lockout state is in memory: a
restart clears it.

**Client address.** Limits are counted per address, and an IPv6 client per /64 (one home connection usually owns a
whole /64). WebAPI takes the address from `X-Forwarded-For` only when the request comes from a trusted proxy:
loopback by default, so Caddy on the same machine works as is, and a client talking to WebAPI directly can't pick
its own address.

## Settings

All under `WebAPI`, in the git-ignored `appsettings.local.json` (copy `appsettings.local.example.json`), or as
environment variables with `__` for `:` (for example `WebAPI__RateLimits__LoginPerMinute=1000`).

| Setting | Default | Meaning |
|---|---|---|
| `LaunchArguments` | OSFR's asset server | Arguments handed to the client. **Must not** contain `Portrait:UploadUrl`; WebAPI refuses to start if it does. `AssetDelivery:IndirectServerAddress` is where the game streams assets from while playing; a public server should host its own copy (`tools/asset-mirror`) rather than trust a third party's |
| `PortraitUploadUrl` | `http://127.0.0.1:20040/image` | Public URL of the portrait endpoint, with no token. Empty turns portrait uploads off |
| `PortraitUploadTokenLifetime` | `1.00:00:00` | How long a login's upload token works |
| `PortraitMaxRequestBytes` | `1048576` | Largest upload request |
| `ImagesDirectory` | `Images` | Where portraits are kept, one folder per character guid. The Gateway reads `Images/` from its own working directory, so the two must point at the same place |
| `StatusLoginServer` | `127.0.0.1:20042` | The Login server that `/status` and `/status.json` ask, as the launcher does. `sanctuary.login:20042` in Docker Compose |
| `StatusTimeout` / `StatusCacheDuration` | `00:00:02` / `00:00:10` | How long `/status` waits for Login, and how long one answer is reused so the page can't flood Login |
| `TrustedProxies` | loopback | Extra proxy addresses or CIDR ranges whose `X-Forwarded-For` is believed, for example `172.16.0.0/12` for Docker |
| `RateLimits:LoginPerMinute` / `RegisterPerHour` / `ImagePerMinute` / `StatusPerMinute` | 10 / 5 / 6 / 30 | Per-address request limits |
| `LoginLockout:FailuresPerAddress` | 5 | Failures before one address is locked out of one account |
| `LoginLockout:BaseLockout` / `MaxLockout` | `00:01:00` / `00:15:00` | First lockout, and the cap it doubles up to |
| `LoginLockout:ForgetFailuresAfter` | `01:00:00` | An address's failure count resets after this long without a failure |
| `LoginLockout:FailuresPerAccount` / `AccountWindow` | 20 / `00:15:00` | Account-wide failures within the window before the account locks for one window |
| `LoginLockout:TrustedAddressLifetime` | `30.00:00:00` | How long an address that logged in stays exempt from the account-wide lock |

**Load tests and playtests from one machine.** The load-test bot logs in up to 200 accounts from one address, which
the default `LoginPerMinute` refuses. `src/Sanctuary.LoadTest/local-servers.sh` raises the limits itself; when
starting WebAPI by hand on staging, use `WebAPI__RateLimits__LoginPerMinute=1000 WebAPI__RateLimits__RegisterPerHour=1000`.

## HTTPS on the VPS with Caddy

WebAPI listens on `http://127.0.0.1:20040` by default (`Urls` in `appsettings.json`), so nothing outside the
machine can reach it. Caddy faces the internet on 443, gets a certificate from Let's Encrypt on its own, and
forwards to WebAPI. You need a domain name (here `play.example.com`) with an A/AAAA record pointing at the VPS.

1. Install Caddy (`apt install caddy` on Debian/Ubuntu) and put this in `/etc/caddy/Caddyfile`:

   ```caddyfile
   play.example.com {
       reverse_proxy 127.0.0.1:20040
   }
   ```

   Then `systemctl reload caddy`. Caddy replaces any `X-Forwarded-For` the client sent with the address it saw,
   which is the one WebAPI counts.
2. Tell WebAPI its public portrait URL: `WebAPI__PortraitUploadUrl=https://play.example.com/image`.
3. Firewall: open TCP 80 and 443 (80 is for the certificate challenge and redirects), UDP 20042 and 20260 for the
   game. Keep 20040, 20041 and 3306 closed.
4. Point the launcher at `https://play.example.com`.

**If the game client can't upload portraits over HTTPS** (it is from 2009, and nobody has checked yet), serve only
the portrait endpoint over plain HTTP as well, and set `PortraitUploadUrl` to `http://play.example.com/image`:

```caddyfile
play.example.com {
    reverse_proxy 127.0.0.1:20040
}

http://play.example.com {
    handle /image/* {
        reverse_proxy 127.0.0.1:20040
    }
    handle {
        redir https://{host}{uri} permanent
    }
}
```

Login and register stay HTTPS-only. Over plain HTTP the token can be read in transit; it lets the reader replace
that one player's portraits until it expires, and nothing else.

**With Docker Compose** (`src/Docker/docker-compose.yml`), WebAPI is published on the host's loopback only
(`127.0.0.1:20040`), so run Caddy on the host as above. Docker hands those connections to the container from its
bridge gateway, so the compose file trusts `X-Forwarded-For` from `172.16.0.0/12`
(`WebAPI__TrustedProxies__0`); without that every player would count as one address. If your Docker networks use
another range (`docker network inspect`), change it. Set `WebAPI__PortraitUploadUrl` there too.

## Contract for the launcher (task 12)

Base URL: `https://<server>` (one setting). Requests and responses are JSON.

`POST /register` with `{"username": "...", "password": "..."}`

| Status | Meaning | Show the player |
|---|---|---|
| 200 | Account created | Log in |
| 400 | Validation problem (`application/problem+json`, field errors under `errors`) | The messages: username 3–50 of `a-z A-Z 0-9 _ .`, password 6–100 ASCII characters |
| 409 | Username taken | "That name is taken" |
| 429 | Too many registrations from this connection | "Try again in N minutes", N from `Retry-After` (seconds) |

`POST /login` with `{"username": "...", "password": "..."}`

| Status | Meaning | Show the player |
|---|---|---|
| 200 | `{"sessionId": "...", "launchArguments": "..."}` | Launch the game |
| 400 | Validation problem | The messages |
| 401 | Wrong username or password (deliberately not saying which) | "Wrong username or password" |
| 403 | Account banned | "This account is banned" |
| 429 | Too many attempts: locked out or rate-limited. `Retry-After` in seconds | "Too many attempts, try again in N minutes". Don't retry automatically |
| 5xx, no answer | Server down | "Can't reach the server" |

- Pass `launchArguments` to the client **unchanged**, as separate arguments (split on spaces), along with
  `SessionId=<sessionId>`. It carries this login's portrait upload URL; a launcher that builds its own
  `Portrait:UploadUrl` breaks portraits.
- The session id works once, within 5 minutes, for the Login server. Log in again for every launch.
- Never store the password. Never retry a `401` or `429` on a timer: retries count as failures.
- `run_client.py` treats any failed login as "account missing" and tries to register. That's fine for a
  developer, not for the launcher.

## Not covered

- **Registration** is limited per address only. Someone with many addresses can still create many accounts.
  An invite code or CAPTCHA would close that, if it becomes a problem.
- **Distributed guessing** against one account can keep it locked for addresses that never logged into it, for as
  long as the attacker keeps going (20 guesses per 15 minutes buys 15 minutes). The owner still gets in from any
  address they've logged in from in the last 30 days, unless WebAPI restarted since.
- **bcrypt cost** is per request; the per-address limits bound one address, not a botnet. A global concurrency
  limit on `/login` would be the next step if CPU becomes the problem.
- **State is per process.** Run one WebAPI instance; a second would keep its own counters and tokens.
