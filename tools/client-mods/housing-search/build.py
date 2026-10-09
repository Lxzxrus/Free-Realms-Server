#!/usr/bin/env python3
r"""Builds the Evergrove housing Decorate mod (search and colour bar) from a player's own Free Realms client.

No game file is stored in this repository: the script reads the original panel and UI scripts from the client
folder, applies Evergrove's changes and writes the modified files to an output folder.

  python build.py --client <FreeRealms client folder> --ffdec <ffdec-cli.jar> --out <folder>
                  [--scripts <original UI\ScriptsBase.bin, if the client's copy is already patched>]

Output (the launcher applies the mod itself from housing-search.tag; for testing by hand, copy the rest into
<client>\UI\ and keep a backup of the original ScriptsBase.bin):
  housing-search.tag                          our compiled script block only (no game code); the launcher embeds it
  housingEditPanel.gfx, housingEditPanel.swf  the Decorate panel with search and colour buttons (loose UI files
                                              override the packs)
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
import zlib

PANEL = "housingEditPanel.gfx"
TAG = "housing-search.tag"
DO_ACTION = 12
PANEL_SHA1 = "477f1a674d90947fa88d8d5512694bc95dd587c3"
SCRIPTS_SHA1 = "18593c20029d96820bc75438777099131de6af46"

# Housing.lua's Main_wndHousingEditPanel_swfHousingEditPanel_OnFocus hands keyboard focus back to the game window
# whenever the panel gets it. Replacing its first instruction with its own final RETURN makes it do nothing.
FOCUS_PATCH_OFFSET = 1642906
LUA_ORIGINAL_FIRST = 0x000000C5
LUA_RETURN = 0x0080001E

HERE = os.path.dirname(os.path.abspath(__file__))

# Compiled as one frame script, in this order: evg_colours.as places its button beside evg_search.as's.
SOURCES = ("evg_search.as", "evg_colours.as")


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


def swf_tags(body):
    """Yields (offset, code, header length, data length) for each top-level tag of an uncompressed SWF body."""
    rect_bits = body[0] >> 3
    offset = (5 + 4 * rect_bits + 7) // 8 + 4
    while offset < len(body):
        header = struct.unpack_from("<H", body, offset)[0]
        code, length, header_length = header >> 6, header & 0x3F, 2
        if length == 0x3F:
            length, header_length = struct.unpack_from("<I", body, offset + 2)[0], 6
        yield offset, code, header_length, length
        offset += header_length + length
        if code == 0:
            return


def insert_after_frame_script(gfx, tag):
    """The original panel with <tag> (a DoAction tag) inserted straight after its own frame 1 DoAction, which is what
    the launcher does too: both run in frame 1, ours second, so our functions replace the panel's."""
    body = zlib.decompress(gfx[8:])
    end = next(offset + header + length for offset, code, header, length in swf_tags(body) if code == DO_ACTION)
    length = struct.unpack_from("<I", gfx, 4)[0] + len(tag)
    return gfx[:4] + struct.pack("<I", length) + zlib.compress(body[:end] + tag + body[end:], 9)


def build_panel(client, jar, out):
    original = read_pack_file(client, PANEL)
    if sha1(original) != PANEL_SHA1:
        sys.exit(f"{PANEL} differs from the version this mod was made for; refusing to patch it")

    # Compile our scripts on their own, as the panel's only frame script, and keep just that DoAction tag: it holds
    # our code and nothing of the game's.
    with tempfile.TemporaryDirectory() as tmp:
        source = os.path.join(tmp, PANEL)
        compiled = os.path.join(tmp, "compiled.gfx")
        script = os.path.join(tmp, "evg.as")
        with open(source, "wb") as f:
            f.write(original)
        with open(script, "w", encoding="utf-8") as f:
            for name in SOURCES:
                with open(os.path.join(HERE, name), encoding="utf-8") as part:
                    f.write(part.read())
        ffdec(jar, "-replace", source, compiled, r"\frame_1\DoAction", script)
        with open(compiled, "rb") as f:
            compiled_gfx = f.read()

    body = zlib.decompress(compiled_gfx[8:])
    offset, _, header, length = next(t for t in swf_tags(body) if t[1] == DO_ACTION)
    tag = body[offset:offset + header + length]

    with open(os.path.join(out, TAG), "wb") as f:
        f.write(tag)

    panel = insert_after_frame_script(original, tag)
    for name in ("housingEditPanel.gfx", "housingEditPanel.swf"):
        with open(os.path.join(out, name), "wb") as f:
            f.write(panel)


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
    print(f"Built the housing Decorate mod in {args.out}")


if __name__ == "__main__":
    main()
