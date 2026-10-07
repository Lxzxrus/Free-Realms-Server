# Performance baseline

What the Gateway costs as players are added, measured on 2026-10-06 by Task 10 (`load-test-bot`) with
`src/Sanctuary.LoadTest`, against `main` at `3063aa6` plus that task's branch, which changes no server code.

**This was measured on the cloud VM, not the Optiplex, and is only a relative baseline.** The bots, all three
servers and MariaDB shared one 4-core VM, so the bots took CPU the Gateway could otherwise have used, and all
traffic went over loopback, which has no real network latency or loss. Trust how the numbers grow with players and
what breaks first. Don't read the absolute numbers as what the Optiplex or a VPS will do. To measure the Optiplex
properly, run the bots from another machine: see `src/Sanctuary.LoadTest/README.md`.

## Setup

| | |
|---|---|
| Machine | Cloud VM: 4 vCPUs (Intel Xeon, 2.1 GHz), 15 GB, Ubuntu 24.04 |
| Servers | WebAPI, Login and Gateway, Release builds, Production settings, started by `local-servers.sh` |
| Database | MariaDB 10.11, its own `sanctuary_loadtest` database |
| Run | `Sanctuary.LoadTest run --steps 0,10,25,50,100,150,200 --threads 3`; other options at their defaults |
| Windows | 15 s warmup, then 60 s measured, per bot count and layout. Bots stay logged in from one step to the next |

**What a bot does:** logs in through WebAPI, Login and Gateway exactly as the client does. In the world it walks to
a random point within 25 units of its home at 6 units/s, sending 5 position updates per second, then walks on (half
the time) or stands for 5 s on average. It jumps every 20 s on average while walking and says a line in `/say`
every 45 s on average. That works out to about 3.1 position updates per second per bot.

**Layouts:** *clustered* puts every bot's home within 10 units of the spawn point, so every bot sees every other
bot, as in a crowded hub. *Spread* scatters homes across the playable part of FabledRealms, about 4,600 by
5,000 units, so most bots see nobody (players see 2 tiles of 64 units around their own tile).

## Results

CPU is % of one core. In and out are from the Gateway's side, and bytes are UDP payload only, without the
28 bytes of IP and UDP header per packet. "Move copies" are position updates the Gateway forwarded to other bots.
Latency is from a bot sending a move to another bot receiving it.

| Layout | Bots | Gateway CPU avg | peak | Busiest thread | Packets/s in | out | KiB/s in | out | Move copies/s | Others seen per bot | Move latency p50 / p95 / p99 / max (ms) | Gateway RSS (MiB) | Gateway UDP drops | Bot CPU | Errors |
|---|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---|---:|---:|---:|---:|
| idle | 0 | 8.9% | 11.0% | 3.8% | 0 | 0 | 0.0 | 0.0 | 0 | 0.0 | - | 351 | 0 | 11.5% | 0 |
| clustered | 10 | 8.9% | 10.0% | 4.8% | 141 | 121 | 3.6 | 11.5 | 256 | 9.0 | 51 / 65 / 66 / 101 | 474 | 0 | 14.8% | 0 |
| spread | 10 | 8.3% | 14.0% | 4.3% | 34 | 34 | 1.6 | 0.4 | 0 | 0.0 | - | 474 | 0 | 14.2% | 0 |
| clustered | 25 | 11.5% | 18.0% | 7.5% | 680 | 631 | 18.9 | 100.0 | 1,900 | 24.0 | 29 / 62 / 65 / 114 | 455 | 0 | 17.9% | 0 |
| spread | 25 | 8.4% | 10.0% | 4.5% | 86 | 90 | 4.2 | 1.3 | 0 | 0.0 | - | 457 | 0 | 15.6% | 0 |
| clustered | 50 | 17.7% | 23.0% | 13.8% | 2,398 | 2,359 | 63.5 | 411.7 | 7,352 | 49.0 | 19 / 44 / 58 / 92 | 496 | 0 | 23.7% | 0 |
| spread | 50 | 9.1% | 10.0% | 5.1% | 188 | 186 | 8.0 | 4.2 | 28 | 0.3 | 61 / 66 / 73 / 108 | 496 | 0 | 19.1% | 0 |
| clustered | 100 | 40.5% | 53.9% | 36.1% | 8,722 | 9,347 | 211.2 | 1,752.9 | 31,173 | 99.0 | 15 / 27 / 35 / 225 | 558 | **56** | 40.9% | 0 |
| spread | 100 | 10.6% | 12.0% | 6.3% | 463 | 430 | 16.7 | 12.1 | 147 | 0.6 | 61 / 65 / 71 / 125 | 558 | 0 | 25.8% | 0 |
| clustered | 150 | 63.3% | 90.9% | 59.1% | 13,477 | 18,063 | 312.6 | 3,604.9 | 68,149 | 149.0 | 15 / **6,262 / 10,000+** / 26,201 | 633 | **7,054** | 53.6% | 0 |
| spread | 150 | 10.7% | 12.0% | 6.5% | 741 | 682 | 25.5 | 23.0 | 306 | 0.9 | 61 / 65 / 67 / 115 | 635 | 0 | 28.9% | 0 |
| clustered | 200 | 50.9% | 103.9% | 46.0% | 2,646 | 11,166 | 92.3 | 2,653.9 | 49,318 | 163.4 | **10,000+** for all | 914 | **9,122** | 48.6% | 0 |
| spread | 200 | 40.5% | 105.3% | 36.1% | 1,001 | 9,045 | 55.3 | 2,328.5 | 440 | 1.1 | 340 / **10,000+** / 10,000+ / 59,285 | 948 | **9,179** | 45.7% | 0 |

"10,000+" means 10 s or more, the top of the latency histogram. The busiest Gateway thread was always a
`.NET TP Worker`: the thread-pool thread that runs the main loop in `GatewayService.ExecuteAsync`.

Every row had 0 errors on the bot side: every bot logged in and stayed connected. The server logs for the run have
no errors or warnings, and show 200 logins and 200 clean logouts. Logging bots in took 6 to 18 s per step, four at
a time, and the measurement leaves it out.

### The 4 MiB experiment

One extra run, outside the table: a Gateway built locally with `IncomingBufferSize = 4 * 1024 * 1024` in
`Sanctuary.Gateway/Program.cs`. That change wasn't committed. Both runs had 200 clustered bots and 30 s windows,
on fresh servers:

| Gateway | UDP drops in window | Move latency p50 / p99 / max (ms) | Busiest thread | Packets/s in / out | Others seen per bot |
|---|---:|---|---:|---:|---:|
| As committed (64 KiB receive buffer) | 3,720 | 1,720 / 10,000+ / 30,103 | 52.1% | 4,547 / 12,537 | 160.3 |
| 4 MiB receive buffer | 0 | 14 / 32 / 73 | 79.8% | 20,639 / 28,247 | 199.0 |

## What the numbers say

**1. Packet loss in the Gateway's own socket buffer breaks it first, not CPU.** The Gateway's player-facing UDP
socket is created with `new UdpParams { ... }` in `Sanctuary.Gateway/Program.cs`, which takes the library's default
64 KiB receive buffer. The Login server's gateway link uses `ManagerRole.ExternalServer`, which gets 2 MiB. Linux
doubles the 64 KiB, and the socket's queue topped out at about 128 KiB in `/proc/net/udp` before the kernel started
dropping. While the Gateway's one main thread is busy sending, incoming packets pile up. In a crowd of 100 the
queue overflowed a little (56 drops a minute), at 150 it overflowed badly, and at 200 everything fell apart. Each
drop is a reliable packet that has to be resent, which means more traffic, which means more drops.

With the buffer at 4 MiB, the same 200-bot crowd ran cleanly at 80% of a core with a 14 ms median latency. This is
the cheapest fix in this document: one line, plus making sure the VPS allows it (`net.core.rmem_max` must be at
least 4 MiB; it was 4 MiB on the cloud VM, but distributions differ).

**2. After an overload, the Gateway didn't recover within a minute.** The *spread 200* row ran right after
*clustered 200*. Spread costs about 10% CPU at 150 bots, yet at 200 it still dropped 9,179 packets, sent
9,045 packets/s to bots that each saw about one other bot, and its memory kept growing (948 MiB). It was still
working off the reliable data queued during the collapse. `UdpParams.ReliableOverflowBytes` is 0 (no limit), so a
connection that falls behind queues without bound instead of being dropped. A crowd that forms once, such as at a
launch event, could leave the server sluggish long after it disperses.

**3. Crowds cost the square of their size; spread players cost almost nothing.** A move goes to everyone who can
see the mover, so a crowd of N sends N × (N − 1) copies. CPU above idle came to roughly 1% of a core per 1,000
copies a second. 100 players spread out cost 10.6%, about idle. 100 players in one spot cost 40.5% and 1.75 MiB/s
of upload.

**4. Upload bandwidth grows the same way, and a VPS bills for it.** Out of the Gateway: 0.4 KiB/s for 10 spread
players, 412 KiB/s for a crowd of 50, 1.75 MiB/s (about 16 Mbit/s with headers) for a crowd of 100. A crowd of 100
held for a full month would be about 4.7 TB. Compression is on (`UseCompression`); each forwarded move costs about
58 bytes on the wire. Check the VPS plan's transfer allowance against how big crowds are expected to get.

**5. The floor on latency is the server's 50 ms send hold.** When few players are nearby, a move took about 61 ms
(the *spread* rows) or 51 ms (clustered, 10 bots). The UDP library holds small outgoing packets for up to
`MaxDataHoldTime` (50 ms by default; the Gateway doesn't change it) in case more data comes to bundle with them, and
sends early only when 512 bytes have built up. That's why latency *fell* as crowds grew: 51 ms at 10, 15 ms at 100.
Loopback added almost nothing, so on the internet add each player's network delay on top. Lowering the hold to
about 10 to 20 ms would make quiet areas feel quicker, at the cost of more, smaller packets; the bot can measure
that trade.

**6. Idle cost and memory.** With nobody online, the Gateway uses 9% of a core. The main loop polls the socket with
`Thread.Sleep(1)` (3.8%), and each zone ticks 10 times a second on thread-pool threads. Memory: 351 MiB idle,
about 470 MiB with the first players in, 633 MiB at 150, and over 900 MiB once data piled up in the collapse.

**7. What the next ceiling is.** All packet handling and forwarding runs on one thread. With the buffer fixed, a
crowd of 200 used 80% of it, so a single crowd of about 250 is the likely next limit, whatever the core count.
Players spread over the map stay cheap well beyond that.

## Proposed follow-ups

None of these are done here: this task changes no server behaviour. Task 14 did 1 and 2; see the next section.

1. **Raise the Gateway's socket buffers** (and probably the Login server's player-facing ones, which also use the
   64 KiB default) to 2 to 4 MiB, and set `net.core.rmem_max` on the VPS to match. One line each, tested above.
   Re-run the bots to confirm.
2. **Cap what a connection can fall behind by** (`ReliableOverflowBytes`), so a slow or stuck client gets
   disconnected instead of growing the Gateway's memory and keeping it saturated. This is also a resource-exhaustion
   question for Task 9's threat model. A client that never acknowledges data while standing in a crowd makes the
   server queue for it until `OldestUnacknowledgedTimeout` (2 minutes) ends the connection. That reasoning comes
   from reading the code; it hasn't been tested.
3. **Measure a lower `MaxDataHoldTime`** for the Gateway (10 to 20 ms): latency in quiet areas against packets per
   second in crowds.
4. **Measure on the Optiplex** with the bots on another machine, then on the VPS before launch.

## After Task 14 (udp-hardening)

Measured on 2026-10-07, on the same kind of cloud VM (4 vCPUs, 15 GB, `net.core.rmem_max` and `wmem_max` 4 MiB),
against `main` at `8d99daa` plus Task 14's branch. Same bots and options as above
(`--steps 0,100,150,200 --threads 3`, 15 s warmup and 60 s measured), and the same caveat: this is the cloud VM,
not the Optiplex, so only the comparison with the table above means anything.

Task 14 gave the Login and Gateway player sockets 4 MiB receive and send buffers and the limits in
[`docs/udp-limits.md`](../udp-limits.md). `local-servers.sh` turns the per-address limits off, since every bot
connects from one address; everything else ran at the launch defaults.

### Run A: launch defaults

| Layout | Bots | Gateway CPU avg | peak | Busiest thread | Packets/s in | out | KiB/s in | out | Move copies/s | Others seen per bot | Move latency p50 / p95 / p99 / max (ms) | Gateway RSS (MiB) | Gateway UDP drops | Bot CPU | Errors |
|---|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---|---:|---:|---:|---:|
| idle | 0 | 6.0% | 9.0% | 2.9% | 0 | 0 | 0.0 | 0.0 | 0 | 0.0 | - | 214 | 0 | 9.2% | 0 |
| clustered | 100 | 39.0% | 45.0% | 35.9% | 8,812 | 9,501 | 213.6 | 1,782.2 | 31,567 | 99.0 | 14 / 26 / 32 / 58 | 408 | 0 | 34.2% | 0 |
| spread | 100 | 8.9% | 11.0% | 5.6% | 478 | 439 | 16.9 | 12.8 | 164 | 0.7 | 61 / 65 / 66 / 80 | 408 | 0 | 20.8% | 0 |
| clustered | 150 | 67.2% | 76.9% | 63.9% | 16,027 | 19,240 | 362.5 | 3,665.7 | 68,875 | 149.0 | 14 / 23 / 27 / 72 | 472 | 0 | 51.2% | 0 |
| spread | 150 | 10.0% | 12.0% | 6.6% | 825 | 742 | 26.6 | 25.9 | 368 | 1.1 | 61 / 65 / 66 / 86 | 472 | 0 | 25.3% | 0 |
| clustered | 200 | 86.9% | 92.9% | 83.6% | 20,271 | 28,055 | 392.9 | 5,438.3 | 122,340 | 199.0 | 14 / 24 / 30 / 56 | 554 | 0 | 64.4% | 0 |
| spread | 200 | 11.3% | 20.9% | 7.7% | 1,184 | 1,046 | 35.9 | 42.7 | 621 | 1.3 | 60 / 65 / 66 / 78 | 527 | 0 | 30.0% | 0 |

**No socket drops at any size, and recovery after every crowd.** Before, 100 clustered dropped 56 datagrams, 150
dropped 7,054 with a p95 of 6 s, and 200 collapsed (every latency over 10 s, 9,122 drops). Now 200 clustered bots
see all 199 others with a 14 ms median and 56 ms worst case. Each *spread* row, measured straight after its crowd,
is back to the quiet-area floor (61 ms, 9 to 11% CPU), where before the *spread 200* row was still at 40.5% CPU
and 9,179 drops a minute after the crowd broke up. Peak memory was 554 MiB against 948 MiB.

All 200 bots logged in and out cleanly; the Gateway and Login logs have no warnings or errors. Logouts were all
`Application`, the client leaving.

**The next ceiling is the one thread.** The busiest thread reached 84% of a core at 200 clustered bots, up from 36%
at 100 and 64% at 150. A single crowd of about 230 to 250 is where it saturates, as predicted above. Players spread
over the map stay cheap: 11% at 200.

### Run B: the old 64 KiB buffer, to check recovery from an overload

To check the new backlog limit, the Gateway ran again with everything as in run A except
`Udp__IncomingBufferSize=65536` and `Udp__OutgoingBufferSize=65536`, which brings the collapse back, at 200 bots
clustered and then spread:

| Layout | Bots | Gateway CPU avg | peak | Busiest thread | Packets/s in | out | KiB/s in | out | Move copies/s | Others seen per bot | Move latency p50 / p95 / p99 / max (ms) | Gateway RSS (MiB) | Gateway UDP drops | Bot CPU | Errors |
|---|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---|---:|---:|---:|---:|
| clustered | 200 | 85.0% | 116.9% | 80.7% | 4,737 | 21,618 | 149.8 | 4,905.6 | 94,946 | 195.0 | 855 / 10,000+ / 10,000+ / 43,448 | 864 | 20,275 | 63.3% | 4 |
| spread | 200 | 16.2% | 96.9% | 12.9% | 1,034 | 2,523 | 37.4 | 475.7 | 456 | 1.0 | 62 / 8,056 / 10,000+ / 26,373 | 870 | 1,022 | 31.7% | 0 |

The backlog limit disconnected the 4 bots furthest behind ("fell behind, with over 512 KiB of reliable data waiting
for it for 60 s"; 2.5 to 3.4 MiB each). Compared with the *spread 200* row before Task 14, the minute after the
crowd is much better but not fully recovered: 16% CPU against 40.5%, 2,523 packets/s out against 9,045, 1,022
drops against 9,179, and a 62 ms median against 340 ms. The slowest 5% of moves were still 8 s late, and memory
stayed at 870 MiB; .NET doesn't hand freed memory back to the system quickly.

So the 4 MiB buffer is what prevents the collapse, and the backlog limit only shortens one. The other 196 bots
stayed just under the limit or kept draining, so they weren't disconnected. Lowering `ReliableBacklogTimeout`
(say to 20 s) would shed load sooner after an overload, at the risk of disconnecting a player on a slow link while
a zone loads. That trade can't be judged until a real client has been measured over the internet.

### What entering a zone costs

Measured while choosing the backlog limits: **each zone entry queues about 4.2 MiB of reliable data for that
player** (4,164 KiB at the peak for every bot), before compression. 3.5 MiB of it is a single packet, the item
definitions for the whole coin store, from `BaseZone.SendCoinStoreItemList`. The next largest are 327 KiB, 206 KiB
and 99 KiB. That's why `ReliableOverflowBytes` can't be set low enough to catch a lagging player
(`docs/udp-limits.md`). It is also upload bandwidth the VPS pays for on every login and zone change, and time a
player on a slow link waits. Sending the coin store's definitions once per session, or only when the store is
opened, is worth a task of its own.

## Making the bot behave like a real client

The bot's rates (5 position updates a second while walking, a jump every 20 s, a chat line every 45 s) are guesses:
no real client has been measured yet. To check them, have one person walk around in the game client while this
runs on the server:

```bash
sudo tcpdump -i any -n -q udp port 20260 and src host <client's address> 2>/dev/null | pv -l -i 10 >/dev/null
```

That counts packets per second from the client. Compare it with the bots' *spread* rows: about 4.6 packets/s per
bot (463 for 100), counting acknowledgements and keep-alives. If a real client walking sends clearly more or fewer,
change `--move-hz` and re-run.
