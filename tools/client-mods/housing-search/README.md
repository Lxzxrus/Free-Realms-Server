# Housing search and colours (client mod)

Adds two round buttons to the top left of the housing Decorate panel, colours first, then search:

- **Search** (magnifying glass) opens a search box that filters the current tab's building parts by name as you type.
  Requested in the LAN playtest of 2026-10-07; verified in game by Nate.
- **Colours** (four squares) opens a box of the game's 38 dye colours, plus "own colours" (white with a red slash).
  A colour recolours the tray's dyeable parts, so new parts come in it.
- **Paint** in a placed part's menu (the one with Up, Rotate and Remove) paints the part in the colour chosen in the
  box, or gives it back its own colour if "own colours" is chosen.

This is a **client** mod. Players get it through the Evergrove launcher. Search works on its own; the colours need
the server (Evergrove's `HousingPalette`), since the server keeps the colour and paints the parts. Without the mod,
`!build` still searches and dyes the tray, and the bar follows a colour chosen that way.

## How Paint reaches the server

The part's menu is built by `Housing.lua` (`MakeHousingRadialMenu`) and its clicks handled by `OnRadialMenuClick`;
`lua_patch.py` adds a fifth button and its handler, which calls `House.PlaceFixture("-" .. currentItemGuid)`. That
sends the server a placement request for the negated fixture guid, unchecked by the client, and the server reads a
negative id as "paint this part" (`HousingPalette.TryParsePaintCommand`). The patch adds code at the end of each
function and changes one jump to reach it, so no existing jump moves; every byte it carries is ours. It was checked by
loading the patched chunk in Lua 5.1 (whose loader verifies the bytecode) and running both functions against stubs:
`check_scripts.py` does both (needs `pip install lupa`).

## How the colours reach the server

A panel can only tell the game which tray item was clicked (`itemSelected(<id>)`), which reaches the server as a
placement request. So a swatch sends a made-up id, `0x3F000000 + the dye's tint id` (0 for own colours), and the server reads it as the choice (`HousingPalette.TryParseCommand`). The server then shows the tray
again, and every creative tray id carries the choice (`0x40000000 | dye << 20 | item`), so the bar shows
what the server has, whoever set it. On the Wall, Floor and Roof tabs the game sends the click as a wallpaper
request instead, and the server reads it the same way. The swatch colours are the game's own (`Resources/Tints.xml`,
`dyetint-*`).

## How players get it

The launcher applies it on every Play, after the client passes its check (`ClientMods` in the launcher):
`ScriptsBase.bin` is patched in place with `scripts.patch` (the check accepts the patched hash as a variant of the
official one), and the panel is rebuilt from the player's own verified `Assets_*.pack` with `housing-search.tag`
inserted. The launcher embeds both, which are only our code. After changing `evg_search.as`, `evg_colours.as` or
`lua_patch.py`, run `build.py` (it compiles the two scripts as one), copy `housing-search.tag` and `scripts.patch` to
`launcher/src/Launcher/Mods/`, and set `ClientMods.ScriptsModdedSha256` to the hash build.py prints.

## What changes

| File in `<client>\UI\` | Change |
|---|---|
| `housingEditPanel.gfx` and `.swf` (new loose files) | The Decorate panel with a second frame 1 script, our compiled `evg_search.as` and `evg_colours.as` (`housing-search.tag`), inserted after the panel's own: it runs second, so its `AddItem`, `ResetItems`, `DisplayItems` and `onClick` replace the panel's. The client loads loose UI files in preference to the packed ones. |
| `ScriptsBase.bin` (patched) | `lua_patch.py`'s edits. One instruction in `Housing.lua`'s `Main_wndHousingEditPanel_swfHousingEditPanel_OnFocus` becomes `RETURN`, so the panel no longer hands keyboard focus straight back to the game and the search box can be typed in (side effect: after clicking in the panel, keys go to the panel until the player clicks in the world). `MakeHousingRadialMenu` and `OnRadialMenuClick` get the Paint button. |

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
