#!/usr/bin/env python3
r"""Builds the Evergrove housing search mod from a player's own Free Realms client.

No game file is stored in this repository: the script reads the original panel and UI scripts from the client
folder, applies Evergrove's changes and writes the modified files to an output folder.

  python build.py --client <FreeRealms client folder> --ffdec <ffdec-cli.jar> --out <folder>
                  [--scripts <original UI\ScriptsBase.bin, if the client's copy is already patched>]

Output (copy into <client>\UI\; keep a backup of the original ScriptsBase.bin):
  housingEditPanel.gfx, housingEditPanel.swf  the Decorate panel with a search button (loose UI files override the packs)
  ScriptsBase.bin                             the UI scripts with the Decorate panel's focus-steal disabled, so the
                                              search box can take keyboard input

Needs Java for JPEXS FFDec (https://github.com/jindrapetrik/jpexs-decompiler).
"""
import argparse
import glob
import hashlib
import os
import shutil
import struct
import subprocess
import sys
import tempfile

PANEL = "housingEditPanel.gfx"
PANEL_SHA1 = "477f1a674d90947fa88d8d5512694bc95dd587c3"
SCRIPTS_SHA1 = "18593c20029d96820bc75438777099131de6af46"

# Housing.lua's Main_wndHousingEditPanel_swfHousingEditPanel_OnFocus hands keyboard focus back to the game window
# whenever the panel gets it. Replacing its first instruction with its own final RETURN makes it do nothing.
FOCUS_PATCH_OFFSET = 1642906
LUA_ORIGINAL_FIRST = 0x000000C5
LUA_RETURN = 0x0080001E

HERE = os.path.dirname(os.path.abspath(__file__))


def sha1(data):
    return hashlib.sha1(data).hexdigest()


def read_pack_file(client, name):
    for pack in sorted(glob.glob(os.path.join(client, "Assets_*.pack"))):
        with open(pack, "rb") as f:
            data = f.read()
        offset = 0
        while True:
            next_offset, count = struct.unpack_from(">II", data, offset)
            p = offset + 8
            for _ in range(count):
                name_len = struct.unpack_from(">I", data, p)[0]
                p += 4
                entry = data[p:p + name_len].decode("latin1")
                p += name_len
                start, length, _crc = struct.unpack_from(">III", data, p)
                p += 12
                if entry == name:
                    return data[start:start + length]
            if next_offset == 0:
                break
            offset = next_offset
    sys.exit(f"{name} not found in " + os.path.join(client, "Assets_*.pack"))


def ffdec(jar, *args):
    result = subprocess.run(["java", "-jar", jar, *args], capture_output=True, text=True)
    if result.returncode != 0:
        sys.exit(f"FFDec failed: {result.stdout}{result.stderr}")


def build_panel(client, jar, out):
    original = read_pack_file(client, PANEL)
    if sha1(original) != PANEL_SHA1:
        sys.exit(f"{PANEL} differs from the version this mod was made for; refusing to patch it")

    with tempfile.TemporaryDirectory() as tmp:
        source = os.path.join(tmp, PANEL)
        with open(source, "wb") as f:
            f.write(original)
        ffdec(jar, "-export", "script", os.path.join(tmp, "scripts"), source)
        with open(os.path.join(tmp, "scripts", "scripts", "frame_1", "DoAction.as"), encoding="utf-8") as f:
            script = f.read()
        with open(os.path.join(HERE, "evg_search.as"), encoding="utf-8") as f:
            script += "\n" + f.read()
        combined = os.path.join(tmp, "DoAction.as")
        with open(combined, "w", encoding="utf-8") as f:
            f.write(script)
        ffdec(jar, "-replace", source, os.path.join(out, PANEL), r"\frame_1\DoAction", combined)

    shutil.copyfile(os.path.join(out, PANEL), os.path.join(out, "housingEditPanel.swf"))


def build_scripts(scripts_path, out):
    with open(scripts_path, "rb") as f:
        data = bytearray(f.read())
    if sha1(data) != SCRIPTS_SHA1:
        sys.exit("UI/ScriptsBase.bin differs from the version this mod was made for (already patched?); refusing")
    if struct.unpack_from("<I", data, FOCUS_PATCH_OFFSET)[0] != LUA_ORIGINAL_FIRST:
        sys.exit("unexpected bytecode at the focus patch offset; refusing")
    struct.pack_into("<I", data, FOCUS_PATCH_OFFSET, LUA_RETURN)
    with open(os.path.join(out, "ScriptsBase.bin"), "wb") as f:
        f.write(data)


def main():
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument("--client", required=True)
    parser.add_argument("--ffdec", required=True, help="path to ffdec-cli.jar")
    parser.add_argument("--out", required=True)
    parser.add_argument("--scripts", help="original UI/ScriptsBase.bin (default: the client's)")
    args = parser.parse_args()

    os.makedirs(args.out, exist_ok=True)
    build_panel(args.client, args.ffdec, args.out)
    build_scripts(args.scripts or os.path.join(args.client, "UI", "ScriptsBase.bin"), args.out)
    print(f"Built the housing search mod in {args.out}")


if __name__ == "__main__":
    main()
