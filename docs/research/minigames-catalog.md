# The Flash minigames: what each one needs from the server

*2026-10-25. Drafted from an overnight run of Nate's local Qwen model over the decompiled ActionScript of every Flash
minigame on the asset mirror (one report per game, plus the shared framework). Claude checked the reports against the
code: all twelve game numbers, the engine families, the server classes and the framework's event codes are right; a
handful of claims were looser and are left out here. The raw reports and the runner are in Nate's sandbox
(`qwen-sandbox/reports/003-minigames`, `tools/overnight_minigames.py`), outside the repo.*

Read with `mining.md` (the match-three protocol, worked out by hand) and `minigames-scout.md`.

## The main finding: every game ships its own server

Each Flash file contains a `GameServer` class (on SOE's `SoeGameFramework`) that SOE used to run the game locally. What
that server does differs by family, and that decides how much work a game is for Evergrove:

| Family | Games (game number) | What the built-in server does | Work for Evergrove |
|---|---|---|---|
| **Match-three** | Mining (1), Artifact (11), Mail Sorting (12), Halloween (13), Farming (60), Forging (62) | Everything: the grid, tile queues, chains, power-ups, quotas, scoring, timers (`ServerGrid`, `GameLevel`, `Progression`) | The engine is already ported (`Sanctuary.Game/Minigames/MatchThree`). Each game needs its own levels in `MatchThree.json` and its game number. Farming's server is byte-for-byte Mining's |
| **Mimic** (rounds of timed actions) | Smelting (1), Cooking (6), Sword (7), Music (8) | Only reads the data files (`MimicMiniGameData.txt`, `MimicMiniGamePhaseData.txt`); the play is all client-side and the client reports the score | The cheapest: send level data and accept the result. The data files are lost (not in the client or the mirror), so the levels are ours to write; the reports give the columns (`LD_ID` to `LD_DATA11`) |
| **Hidden object** | Hidden (17) | Authoritative: hit-tests each click, hints, hourglass and magnifier counters, level flow (`GameServer`, `GameLevel`, `HiddenItem`) | A port like Mining's, smaller. Its three data files (`HiddenObjectMiniGame*.txt`) are lost too: item positions would have to be measured from the art |
| **Board games** | Chess (20), Checkers (21) | Turn-taking: relays each move to both players and starts the next turn; moves are validated client-side (`MoveFactory`). Chess has a random-move computer player (`AI.GetRandomMove`) | A small relay, but two-player: needs matchmaking or a computer opponent |

The two engines are shared too: the match-three games use the same client and server classes (`core.Grid`,
`core.ServerGrid`), the Mimic games the same round framework (`Core.Round`, `Core.GameHUD`, `Data.*Rounds`).

## The games

- **Farming (60), Forging (62), Mail Sorting (12):** match-three with bins and a mold meter, each with its own tiles,
  sounds and music. Farming's code differs from Mining's only in its config and art.
- **Halloween (13), Artifact (11):** match-three where you drag a chain of tiles (Mining's power-ups, loot drops and
  waves). Artifact carries its own, slightly older copy of the engine.
- **Smelting (1):** four stages: break down the ore, heat the furnace, and so on; the result depends on the ore type
  and a performance score.
- **Cooking (6):** a recipe as a sequence of timed rounds, each a different cooking action.
- **Sword (7):** cut a training dummy with slice, thrust and sword-rain techniques; nine body parts with their own
  resistance, three lives.
- **Music (8):** conduct an orchestra along a curved path in time with the beat; five songs of rising difficulty.
- **Hidden (17):** find the listed objects in a scene before time runs out, with hints and tools.
- **Chess (20), Checkers (21):** the classic games for two players.

## Assets

Like Mining, every game's pictures and its `.gfx` live only on the asset mirror, and the client loads them on the spot,
so a game we add needs its files on the launcher's pre-cache list (`tools/client-mods/precache`) before it will draw.
The daily wheel is the exception: its art is in the official packs.

## Suggested order

1. **Farming, Forging, Mail Sorting:** the engine is done; each needs levels, its game number and the pre-cache.
2. **One Mimic game** (Cooking or Smelting fits the jobs): send data, take the score; the levels are ours to design.
3. Halloween and Artifact (match-three again, a seasonal and a showcase one).
4. Hidden, then the board games.
