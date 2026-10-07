# Sanctuary.LoadTest

A headless load-test client. It logs fake players in through the real protocol, the same way the game client
and `run_client.py` do:

1. **WebAPI**: `POST /login`, or `POST /register` and then `/login` the first time an account is used.
2. **Login** (UDP 20042, RC4): session login, character list, character create on the first run, character login.
3. **Gateway** (UDP 20260, compressed): ticket login, `ClientIsReady`, then `ClientFinishedLoading` a second later.

In the world, each bot walks between random points near its home, pauses, jumps now and then, and says a line
in `/say` chat now and then. Bots run in steps (10, 25, 50 and 100 by default). At each step they're measured
twice: **clustered** around the spawn point, where every bot sees every other bot, and **spread** across the
zone, where most bots see few or none.

Results for the cloud VM are in [`docs/performance/baseline.md`](../../docs/performance/baseline.md).

## What it measures

| Figure | How |
|---|---|
| Gateway CPU, total | `/proc/<pid>/stat` at the start and end of each window, as % of one core. Peak is the busiest 1 s inside the window |
| Gateway CPU, busiest thread | The same from `/proc/<pid>/task/*/stat`: the thread that used the most CPU in the window |
| Packets and bytes per second, in and out | The bots' UDP counters (UDP payload, without IP/UDP headers). What the bots send is what the Gateway receives, and the other way round |
| Move latency | A bot records when it sends a position update; every other bot that receives it records the delay. The server forwards the position bytes unchanged, so the sender and position identify the move. Sender and receivers share a clock because they're in one process |
| Others seen per bot | How many different bots each bot saw move during the window |
| Gateway UDP drops | Datagrams the kernel threw away because the Gateway's receive buffer was full, from `/proc/net/udp`. Any drop means lost packets and resends |
| Bot loop max | The longest a bot worker took to get round its bots. If it nears the latency figures, the bots are the bottleneck, not the server |
| Errors | Failed logins, dropped connections and corrupt packets on the bot side. Check the server logs separately |

## Running it on one machine (how the cloud baseline was made)

```bash
cd src/Sanctuary.LoadTest
./local-servers.sh start          # Release build; WebAPI, Login and Gateway on a database of their own
dotnet bin/Release/net9.0/Sanctuary.LoadTest.dll run --password '<any password>' --report results.md
./local-servers.sh stop
```

`local-servers.sh` needs MariaDB on 127.0.0.1:3306 and `mariadb` access as an administrator; it creates the
`sanctuary_loadtest` database and leaves your real one alone. With everything on one machine, the bots take CPU
from the server, so read the "Bot CPU" column next to the Gateway's.

## Running it against the Optiplex

The bots should run on a **different** machine from the server, so they don't take the server's CPU. Any machine
with the .NET 10 SDK can build and run the bot (`dotnet build src/Sanctuary.LoadTest -c Release`). The CPU figures
need Linux, so they're taken on the Optiplex itself.

**Use a staging database, never the live one.** The bot registers accounts `loadbot001` to `loadbot100`,
creates a character on each, and its chat lines go to the Gateway's chat log.

On the Optiplex:

1. Let the bot's machine reach the servers. By default WebAPI only listens on 127.0.0.1, and the Login server
   tells clients the Gateway is at `127.0.0.1:20260`. For the test, start them with:

   ```bash
   # WebAPI: listen on the LAN (Urls overrides appsettings.json), and let one address log in every bot
   # (the default limits are per address; docs/webapi.md)
   Urls=http://0.0.0.0:20040 WebAPI__RateLimits__LoginPerMinute=1000 WebAPI__RateLimits__RegisterPerHour=1000 \
       dotnet Sanctuary.WebAPI.dll
   # Gateway: hand out the Optiplex's LAN address (or pass --gateway-address to the bot instead)
   Server__ServerAddress=192.168.1.50:20260 dotnet Sanctuary.Gateway.dll
   ```

   Login and Gateway also need the same `Server__LoginGatewayChallenge`, from the environment or their
   `*.local.json` files (see the main README); they refuse to start without it.

   and open TCP 20040 and UDP 20042 and 20260 to the LAN only. Close them again afterwards: WebAPI over plain
   HTTP must not face the internet.

   Every bot connects from the bot machine's one address, so start Login and Gateway with the per-address limits
   off, or only the first 10 bots get in (see `docs/udp-limits.md`):

   ```bash
   export Udp__MaxConnectionsPerIp=0 Udp__ConnectRatePerIp=0
   ```
2. Start the monitor, which prints the Gateway's CPU and its socket's UDP drops every 5 seconds, with a UTC time stamp:

   ```bash
   dotnet Sanctuary.LoadTest.dll cpu
   ```

On the bot machine:

```bash
dotnet src/Sanctuary.LoadTest/bin/Release/net9.0/Sanctuary.LoadTest.dll run \
    --host 192.168.1.50 --password '<a password for the bot accounts>' --report optiplex.md
```

Each result line prints its UTC start and end, so the matching lines of the CPU monitor give the Gateway's CPU
for that window. Use the same `--password` on every run: the accounts keep the password they were registered with.

### Removing the bot accounts

```sql
DELETE FROM Characters WHERE UserId IN (SELECT Id FROM Users WHERE Username LIKE 'loadbot%');
DELETE FROM Users WHERE Username LIKE 'loadbot%';
```

## Options

`dotnet Sanctuary.LoadTest.dll --help` lists them all. The ones that change the load:

| Option | Default | Meaning |
|---|---|---|
| `--steps` | `10,25,50,100` | Bot counts, increasing. Bots stay logged in from one step to the next. `0` measures the idle server |
| `--layouts` | `clustered,spread` | Measure one or both |
| `--warmup`, `--duration` | 15, 60 | Seconds to settle, then seconds measured, per step and layout |
| `--move-hz` | 5 | Position updates per second while walking |
| `--idle` | 5 | Mean seconds standing still between walks |
| `--jump` | 20 | Mean seconds between jumps while walking |
| `--chat` | 45 | Mean seconds between chat lines |
| `--threads` | 2 | Bot worker threads. Raise it if "Bot loop max" climbs; 3 was enough for 200 bots on 4 cores |

The behaviour rates are guesses until a real client is measured; see the baseline document for how to measure one.

## Limits

- Bots don't load the zone, so the server never has to wait for them, and they don't fall, collide or check the
  ground: their height is fixed at the spawn point's.
- Clustered and spread bots are moved into place by a single position update, which a real client can't do.
- Bots don't use npcs, quests, housing or trading, so this measures movement, chat and login only.
