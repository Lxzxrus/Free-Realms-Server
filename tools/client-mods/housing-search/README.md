# Housing search (client mod)

Adds a search button to the housing Decorate panel: a round button with a magnifying glass, left of the first
category button, opens a search box that filters the current tab's building parts by name as you type. Requested in
the LAN playtest of 2026-10-07; verified in game by Nate.

This is a **client** mod. Players get it through the Evergrove launcher; the server is unaffected (`!build` is the
server-side search and dye command and works with or without the mod).

## What changes

| File in `<client>\UI\` | Change |
|---|---|
| `housingEditPanel.gfx` and `.swf` (new loose files) | The Decorate panel with `evg_search.as` appended to its frame 1 script. The client loads loose UI files in preference to the packed ones. |
| `ScriptsBase.bin` (replaced) | One instruction in `Housing.lua`'s `Main_wndHousingEditPanel_swfHousingEditPanel_OnFocus` becomes `RETURN`, so the panel no longer hands keyboard focus straight back to the game and the search box can be typed in. Side effect: after clicking in the panel, keys go to the panel until the player clicks in the world. |

No game file is stored here. `build.py` reads the originals from a client folder, checks their SHA-1 against the
versions this mod was made for, and writes the modified files.

## Build

Needs Python 3, Java, and [JPEXS FFDec](https://github.com/jindrapetrik/jpexs-decompiler) (`ffdec-cli.jar`).

```
python build.py --client <client folder> --ffdec <path to ffdec-cli.jar> --out <output folder>
```

If the client's `UI\ScriptsBase.bin` is already patched, pass the original with `--scripts`.

## Install by hand (testing)

1. Back up `<client>\UI\ScriptsBase.bin`.
2. Copy the three output files into `<client>\UI\`.

Undo: restore `ScriptsBase.bin` and delete `housingEditPanel.gfx` and `housingEditPanel.swf` from `<client>\UI\`.
