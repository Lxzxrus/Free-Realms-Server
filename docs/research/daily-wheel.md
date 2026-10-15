# Spin For The Win: how the client runs the daily wheel

Found on 2026-10-13 by reading the 2014 client: `FreeRealms.exe` decompiled with Ghidra, the UI's Lua decompiled with
unluac, and `game_wheel.gfx` with JPEXS FFDec. Nothing from the client is committed; addresses are for the client the
launcher pins. Read with `minigames-scout.md`, whose section 9 lists the wheel's Flash messages.

## The flow

1. **The server sends the spin count.** Repeating activity packet, opcode 143, sub-opcode 1 (`RepeatingActivityAdd`):
   `short 143, byte 1, int id, int count, int consecutive, int seconds, int seconds, string name, string name2, int`.
   The client keeps a list of these (handler `0xadb990`, body reader `0xadafa0`) and finds the wheel by the name
   `"wheel"`: `Ui.GetRepeatingActivityCount("wheel")` and `GetRepeatingActivityNextTime("wheel")`. An existing id is
   updated in place. Evergrove sends it on every zone entry and after each spin.
2. **The player opens the wheel** from the activity portal (`startWheelMinigame`), the activity calendar
   (`OpenWheel`) or, if the HUD decides to, at login (`HUD:ShowCoinWheel`). All of them call
   `MiniGameFlashC.StartWheel()` (`0xc08260` → `0x9bdbd0`), which **does nothing unless the wheel's count is above
   zero**, then sends `CommandPacketInteractionStartWheel` (command opcode 26, sub-opcode 11, no body). The Games menu
   instead sends `JoinActivityRequest` for activity 8; Evergrove treats both the same.
3. **The server opens a minigame session, then the Flash.** The client forwards the Flash's messages only inside a
   minigame session (`MiniGame:SendPayload`, `0x9be6b0`, needs a session and tags each message with its state id).
   A `MiniGameInfo` packet (39/16) creates one (`MiniGame:JoinGame`, `0x9beb70`): Evergrove sends type 22 (Wheel),
   the wheel's name, description and icon 20985, and its own state id. With the info's start-screen flag (offset
   `0x267`, our `Unknown11`) off, the client skips the start screen and asks to start by itself
   (`MiniGame:RequestStartGame`, `0x9be7d0`, sub-op 5). The server answers with `GameStart` (39/17) and
   `CommandPacketStartFlashGame` (26/12: `"MiniGameFlash"`, `"game_wheel.gfx"`). The first version started the Flash
   without a session: the frame opened empty, no message reached the server, and nothing could close it. The
   minigame frame's close button sends `End` (39/6), answered with `Leave` (39/19).
4. **The Flash and the server talk** through minigame payload packets (39/14), tab-separated and NUL-terminated, the
   order of SOE's own `Wheel.WheelGameServer` inside the Flash file:
   - client `FRServer_Connect` → server `OnWheelDataMsg` (id, type, slot categories, name string, message string),
     `OnWheelUpdateMsg` (id, spins, consecutive days, seconds to next spin), `OnServerReadyMsg`;
   - client `OnWheelSpinRequestMsg` (id) → server `OnSpinInfoMsg` (the wedge to aim for; the Flash adds it to a
     random spin, so the needle isn't guaranteed to stop on it);
   - client `OnWheelStopMsg` (id) → server `OnRewardInfoMsg` (icon, tooltip string, quantity, name string, message
     string, tint);
   - client `FRServer_GameClose` when the wheel is closed.

## The wheel's art

Wheel type 1 has ten wedges (`mcCategory1` to `mcCategory10`); another frame of the wheel has eight. Each wedge shows
a frame (1 to 25) of one sprite (character 194), and the pictures themselves are icons loaded at runtime, so they
can't be previewed outside the game. The live game's prize tables (`MicroGameWheel*.txt`) were server data and are not
in the client, so Evergrove's prizes are its own (`src/Resources/DailyWheel.json`).

## Why it can't be shown yet (2026-10-14)

In game the wheel's frame opened with its title but nothing in it. `game_wheel.gfx` keeps its pictures as 83 external
images (`img<number>.dds`), and **82 of them are in neither the client's packs nor its asset list**. The same check
passes for `game_mining.gfx` (85 of 85), `MinigameStartScreen.gfx` (47 of 47) and `LootWheelOfAwesomeness.gfx`
(28 of 28). OSFR's asset server also lacks every minigame `.swf`, but the client loads the `.gfx`. So the wheel is
switched off (`Enabled` in `DailyWheel.json`) until its art exists: new art made for Evergrove as a client mod, or the
images from another client copy. Before building any other Flash minigame, run the same image check on its `.gfx`.

## Still unknown

- Which of the two seconds fields in `RepeatingActivityAdd` is "next spin", and what the second string and last int
  mean. Evergrove sends the same value in both seconds fields and `"wheel"` in both strings.
- What `MiniGameInfo` byte `+0x907` is (the micro-game switch), and whether the HUD opens the wheel at login by itself.
- What the client does with `MiniGameLeave` (39/19), which Evergrove sends when the wheel closes.
