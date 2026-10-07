# UDP limits on the player ports

Since Task 14 (`udp-hardening`), the Login server's player port (UDP 20042) and the Gateway's (UDP 20260) run with
limits meant for a public server. They're in `PlayerUdpOptions` (`src/Sanctuary.UdpLibrary/Configuration`), read from
the `Udp` configuration section, so each can be changed without a rebuild: in the environment as `Udp__<Name>`
(two underscores), or in a `"Udp": { ... }` block in `login.json` or `gateway.json`. Each server reads its own.

The server-to-server link between Login and Gateway (UDP 20041) doesn't use them. It must not face the internet at all
(finding F3 in `docs/security/threat-model.md`).

## The settings

| Setting | Default | What it does |
|---|---:|---|
| `IncomingBufferSize` | 4 MiB | Kernel receive buffer. The load test showed 64 KiB overflowing at 100 clustered players and 4 MiB enough for 200. Linux caps it at `net.core.rmem_max`; see below |
| `OutgoingBufferSize` | 4 MiB | Kernel send buffer, capped at `net.core.wmem_max` |
| `MaxConnectionsPerIp` | 10 | Connections one IP address may hold. Players behind one router share an address |
| `ConnectRatePerIp` | 5 | New connections one address may open per `ConnectRateWindow` |
| `ConnectRateGlobal` | 200 | New connections the server accepts per `ConnectRateWindow` in total: the limit that holds when a flood comes from many addresses |
| `ConnectRateWindow` | 10000 ms | The window the two rates count over |
| `HandshakeTimeout` | 10000 ms | A connection that hasn't sent one valid packet within this is dropped without a reply. A real client talks as soon as it's connected; a connect request with a forged sender address never can |
| `UnverifiedReplyRatePerIp` | 20 | Replies one address may get per `UnverifiedReplyWindow` before it has proven it owns that address: see [Replies to forged addresses](#replies-to-forged-addresses) |
| `UnverifiedReplyWindow` | 10000 ms | The window `UnverifiedReplyRatePerIp` counts over |
| `IncomingLogicalPacketMax` | 64 KiB | Largest packet a client may send, after reassembly. Was 20 MB |
| `IncomingFragmentBytesMax` | 64 KiB | Bytes of fragmented packets one connection may have half-received at once, across its four reliable channels |
| `ReliableOverflowBytes` | 16 MiB | Reliable data that may wait for one player before they're disconnected: a hard ceiling on memory per player. Entering a zone queues about 4.2 MiB at once, so it can't be lower |
| `ReliableBacklogTimeout` | 60000 ms | A player with more than `ReliableBacklogBytes` waiting for them for this long without a break is disconnected: they've fallen behind and aren't catching up. This is what lets an overloaded server recover |
| `ReliableBacklogBytes` | 512 KiB | See `ReliableBacklogTimeout` |
| `FaultLimit` | 5 | Exceptions handling one connection's packets, within `FaultWindow`, before it's disconnected |
| `FaultWindow` | 60000 ms | The window `FaultLimit` counts over |

A connect request over a connection limit is ignored, so the client times out as if the server were full. All other
limits disconnect. Every one of them logs a warning saying which limit and why; refused connects are logged once a
minute with a count, since a refused client retries every second.

Two more things changed with no setting:

- An exception while processing one connection's packets (in the UDP library or a handler) drops that connection and
  logs the error, with the packet's first bytes. It never stops the server.
- The Gateway's disconnect line says the most reliable data that was waiting for that player at once, for tuning
  `ReliableOverflowBytes` and `ReliableBacklogBytes`.

## Replies to forged addresses

Since Task 18 (`status-reflection`). A UDP packet's sender address can be forged, so anything the server sends in
answer to a packet from an address that hasn't proven it owns it can be aimed at somebody else. These are all such
replies on the Login and Gateway ports:

| Request | Reply | Who needs it |
|---|---|---|
| Server status (2 bytes in OSFR) | Online, locked, player count. Was 20 bytes, now 6 | The launcher's status ping (`ServerStatusHelper`). Nothing in OSFR suggests the game client sends it, but that's unconfirmed |
| Connect request (at least 24 bytes from the game client: it must carry the protocol name, or the server's name check would refuse it) | Confirm, 21 bytes | Every login |
| Any packet from an address with no connection | Unreachable, 2 bytes | A client whose connection the server already dropped |
| A connect request with a new code, on a connection | Terminate, 11 bytes | A client reconnecting from the same address and port |
| Unreachable (2 bytes), on a connection | Remap request, was 21 bytes | Nothing: only the side that opened a connection needs to ask for a remap |

The rules now, in `UdpManager.TryAdmitUnverifiedReply`:

- No such reply is ever larger than the request that caused it, so the server can't amplify a flood. The launcher pads
  its status request to 6 bytes; the game client's connect request carries the protocol name, so it's already larger
  than the confirm. An unpadded status request or a nameless connect request goes unanswered.
- One address gets at most `UnverifiedReplyRatePerIp` of them per window, so the server can't relay a flood at one
  victim either. Over the limit, the request goes unanswered, and a warning is logged at most once a minute.
- A connection that hasn't proven itself gets nothing but the confirm: no keep-alives, no terminate. A wrong protocol
  name or a new connect code drops it without a word.
- The server never sends a remap request. A client still does (the Gateway's link to Login).
- The Gateway's link to Login (its client side, on a port the system picks) doesn't answer strangers at all.

There's no limit across all addresses: since a reply is never larger than its request, the server's total reply traffic
can't exceed what an attacker sends it, and a global limit would let one attacker stop every launcher seeing the server.

## On the VPS: raise the kernel's socket buffer limits

Linux silently caps socket buffers at `net.core.rmem_max` and `net.core.wmem_max`. Many distributions default to
about 208 KiB. The servers log a warning at startup when the cap is below what they ask for. Raise both:

```bash
sudo sysctl -w net.core.rmem_max=4194304 net.core.wmem_max=4194304
printf 'net.core.rmem_max=4194304\nnet.core.wmem_max=4194304\n' | sudo tee /etc/sysctl.d/90-sanctuary.conf
```

Then restart Login and Gateway. If either still logs `is capped at`, the setting didn't take.

Both servers stop (exit) rather than run on if their main loop ever fails, so run them under a supervisor that
restarts them, for example systemd with `Restart=always`.

## Load tests and playtests

`src/Sanctuary.LoadTest/local-servers.sh` sets `Udp__MaxConnectionsPerIp=0`, `Udp__ConnectRatePerIp=0` and
`Udp__UnverifiedReplyRatePerIp=0`, because
every bot connects from one address. Do the same on the Optiplex when the bots run on another machine (see
`src/Sanctuary.LoadTest/README.md`).

Two players in one house share an address, and that's well inside the defaults. Nothing needs changing for a
playtest.
