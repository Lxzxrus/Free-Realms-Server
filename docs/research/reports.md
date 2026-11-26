# In-game reports and the staff log

The client already has three ways to report something: **Report** on a name in the chat log (and `/report`),
**Report Offensive** on a house's side panel, and the **bug report** window. Until this change the server ignored all
three. Now they are kept in the database, shown to staff in game with `!mod`, and can also be posted to a private
Discord channel.

The packets were first decoded by BubbaJoeX (`BubbaJoeX/Sanctuary` 982a800, AGPL-3.0). The staff log and report shapes
follow Sulphural's `feature/web-character-create` branch (AGPL-3.0). Both are credited in the code.

## Wire formats (checked in FreeRealms.exe)

All three arrive inside `PacketTunneledClientPacket` (op 5) and are routed in
`Sanctuary.Gateway/Handlers/PacketTunneledClientPacketHandler.cs`. Strings are an int32 length and then the bytes.
The client expects no reply to any of them.

| Packet | Opcode | Fields, in order | Confidence |
|---|---|---|---|
| ReportPacketReportPlayer | 68 (int16), sub 1 (int16) | string name; int32 count (0 to 10); per line: int32 channel, string message, uint64 time | Confirmed in the exe (serializer 0x0091b9d0) |
| ReportPacketReportHouse | 68, sub **3** | uint64 house instance guid; uint64 owner character guid; string house name; string owner name | Confirmed in the exe (0x00ac19a0). The scout plan said sub 2; that was wrong. |
| BugSubmissionPacketAddBug | 83 (int16), sub 1 (int16) | int32 (always 0); string (always empty); int32 (always 0); string XML | Confirmed in the exe (0x009fc2e0, 0x009fc9c0) |

Notes:
- The player report carries the last 10 lines the **reporter's** client holds from that name. If the client has no
  chat from them, it sends nothing at all and shows "PlayerNotReported". The lines come from the client and can be
  forged, so the server also stores its own record of what the target said (below).
- The time field's unit is not known. It is stored as sent.
- The house report echoes the house record the server sent (`PlayerHousingInstanceInfo`). The server uses only the
  guids, and checks them against its own data. The names are for display.
- The bug XML has the root `<BugSubmission Category= SubCategory= Severity= BugDescription=>` and a `PlayerData` child
  (position, gear, machine and so on). BubbaJoeX's regex for it never matched: `\b` in a non-verbatim C# string is a
  backspace. This port reads it with an `XmlReader` that refuses DTDs, resolves nothing, and stops at 64K characters.
  A document that does not parse is kept as a cut, cleaned copy.
- None of this has been tried with a real client yet. A packet that does not parse is logged at **Warning** with its
  bytes (rate limited, `ReportRefusalLog`). If a real report does not show up in `!mod reports`, look in the Gateway
  log for that line.

## What is stored

`Reports` table (`DbReport`): kind (player, house or bug), reporter account and character, target account, character
or house, zone and position, the client's text (chat lines or bug fields, capped), the server's own context, created,
status, and who closed it with a note.

**Server-side chat evidence.** Each player keeps their last 10 accepted chat lines from the last 30 minutes in memory
(`ChatEvidence`). Only public channels count: say, shout, trade, LFG, area and members-only. Tells, whispers, group and
guild chat are never kept, so private chat never reaches staff or Discord on someone else's word. The list goes away
when the player logs out. A player report stores the target's lines beside the client's, so staff can compare them.

`StaffActions` table (`DbStaffAction`): every `!mod` and `!admin promote/demote` action, with who did it, the target
account and character, and details. It is written as well as the existing Audit log file. If the database write
fails, the error is logged and the command still counts.

`Users` gains `BanReason` and `MuteReason`, set by `!mod ban` and `!mod mute` and cleared by unban and unmute.

## Limits

All in `Sanctuary.Game/Reports/ReportLimits.cs`.
- Per account: 5 player or house reports an hour and 20 a day. Bug reports have their own 5 and 20. At most 30
  attempts of any kind in 10 minutes, dropped ones included.
- The account must be at least 10 minutes old. One open report per reporter, kind and target. Reports about yourself
  are ignored.
- Across all accounts: 30 reports an hour and 200 a day, and the same again for bugs. Accounts under 7 days old share
  a smaller part (20 and 130). These count only kept reports.
- At most 10 open reports about one player (house reports count against the owner), 1000 open player and house
  reports, and 500 open bug reports. At 80% of a ceiling the log warns.
- Open reports are closed automatically after 60 days. Closed reports are deleted after 30 days
  (`ReportRetentionService`, hourly).
- A report refused by a limit sends the reporter nothing, because the client has no reply packet for it. Refusals by a
  shared limit are logged at Warning, at most once every 10 minutes per reason.

## Staff commands

```
!mod ban <player> [minutes] [| reason]      no minutes = permanent
!mod mute <player> [minutes] [| reason]
!mod kick <player> [| reason]               disconnect, no ban
!mod warn <player> | <text>                 system message to the player, logged
!mod history <player>                       last staff actions and reports about them
!mod unban | unmute | resetrename <player>
!mod reports [open|all] [bug|player|house] [page]
!mod report <id>
!mod close <id> [| note]
!mod close all bug|player|house [| note]
```

The `|` starts the reason, text or note, because names can contain spaces.

The rank rule applies everywhere: a moderator cannot act on, read, close or be told about reports about themselves or
other staff they don't outrank. A report about staff goes to Discord only as a short line with no names or text.
Reading a report is written to the staff log.

## Discord webhook (optional, off by default)

Each kept report is posted to a Discord channel, and online staff who may read it also get a one-line system message
in game.
1. Make a **private** staff-only channel, for example `#staff-reports`.
2. Channel settings > Integrations > Webhooks > New Webhook > Copy Webhook URL.
3. On the VPS, put it in `src/Docker/.env` as `REPORTS_DISCORD_WEBHOOK_URL=...` (or set the environment variable
   `Reports__DiscordWebhookUrl`, or put `"Reports": { "DiscordWebhookUrl": "..." }` in `gateway.local.json`). Never
   put it in a tracked file.
4. Restart the Gateway. The log should say "Reports are posted to a Discord webhook." An address that is not
   `https://discord.com/api/webhooks/...` (or discordapp.com) logs one error, and the webhook stays off.
5. If the address leaks, delete the webhook in Discord and make a new one.

How it is sent: from a bounded queue (100), drained by a hosted service, at most one post every 2 seconds, with a 5 s
timeout and no redirects. A 429 is waited out for up to 5 minutes, over 3 tries. The address is never logged, and the
HTTP client's own logging is switched off for it. Player text only ever appears inside code spans or blocks,
`allowed_mentions` is empty and embeds are suppressed, so a report cannot ping anyone or post a link preview.

## In-game test list

Use two characters on different accounts (A reports B), plus a staff character. Accounts must be over 10 minutes old.
1. **Chat report.** B says something in Say. A right-clicks B's name in the chat log and picks Report. The staff
   character gets "Report #N: A reported B". `!mod report N` shows the client's lines and "What the server saw", and
   they match.
2. **`/report B`**, if the client has it. Same result.
3. **No chat.** Report a name that has not chatted. The client refuses on its own and nothing reaches the server.
4. **House report.** A visits B's house and uses Report Offensive on the side panel. A house report appears with B as
   the owner.
5. **Bug window.** Send a bug report with a category, a severity and some text. `!mod report N` shows the fields. Try
   a long text too.
6. **Limits.** Report B again: no second open report. Six reports in an hour: the sixth is not kept.
7. **Commands.** `!mod ban B 5 | test` (B is disconnected; the Users row has the reason), `!mod unban B`,
   `!mod mute B 5 | test`, `!mod kick B` (B can log straight back in), `!mod warn B | test` (B sees "Warning from staff"),
   `!mod history B`, `!mod reports`, `!mod close N | done`, `!mod close all bug`.
8. **Rank rule.** A moderator cannot ban, read reports about, or close reports about an admin or another moderator.
9. **Discord,** once the webhook is set: each kept report shows up in the channel. A message with `@everyone` or a link
   does not ping or embed.
10. After deploying, check that the `StaffActions` and `Reports` tables exist on MariaDB and fill up.

If a report in steps 1 to 5 never appears, search the Gateway log for "Refused" and send the hex bytes to Claude.
