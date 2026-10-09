# Housing search and colours (client mod)

Adds two round buttons to the top left of the housing Decorate panel:

- **Search** (magnifying glass) opens a search box that filters the current tab's building parts by name as you type.
  Requested in the LAN playtest of 2026-10-07; verified in game by Nate.
- **Colours** (four squares) opens a box of the game's 38 dye colours, plus "own colours" (white with a red slash),
  and a **Paint brush** switch. A colour recolours the tray's dyeable parts, so new parts come in it; with the brush
  on, a placed part you move or turn takes the colour too. A white dot on the button means the brush is on. The brush
  switches itself off when you leave Decorate.

This is a **client** mod. Players get it through the Evergrove launcher. Search works on its own; the colours need
the server (Evergrove's `HousingPalette`), since the server keeps the colour and paints the parts. Without the mod,
`!build` still searches and dyes the tray, and the bar follows a colour chosen that way.

## How the colours reach the server

A panel can only tell the game which tray item was clicked (`itemSelected(<id>)`), which reaches the server as a
placement request. So a swatch sends a made-up id, `0x3F000000 + 512 (brush on) + the dye's tint id` (0 for own
colours), and the server reads it as the choice (`HousingPalette.TryParseCommand`). The server then shows the tray
again, and every creative tray id carries the choice (`0x40000000 | brush << 29 | dye << 20 | item`), so the bar shows
what the server has, whoever set it. On the Wall, Floor and Roof tabs the game sends the click as a wallpaper
request instead, and the server reads it the same way. The swatch colours are the game's own (`Resources/Tints.xml`,
`dyetint-*`).

## How players get it

The launcher applies it on every Play, after the client passes its check (`ClientMods` in the launcher):
`ScriptsBase.bin` is patched in place (the check accepts the patched hash as a variant of the official one), and the
panel is rebuilt from the player's own verified `Assets_*.pack` with `housing-search.tag` inserted. The launcher
embeds `housing-search.tag`, which is only our compiled code; after changing `evg_search.as` or `evg_colours.as`, run
`build.py` (it compiles both as one script) and copy
the new `housing-search.tag` to `launcher/src/Launcher/Mods/`, then update the hashes in `ClientMods` if the originals
change.

## What changes

| File in `<client>\UI\` | Change |
|---|---|
| `housingEditPanel.gfx` and `.swf` (new loose files) | The Decorate panel with a second frame 1 script, our compiled `evg_search.as` and `evg_colours.as` (`housing-search.tag`), inserted after the panel's own: it runs second, so its `AddItem`, `ResetItems`, `DisplayItems` and `onClick` replace the panel's. The client loads loose UI files in preference to the packed ones. |
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
