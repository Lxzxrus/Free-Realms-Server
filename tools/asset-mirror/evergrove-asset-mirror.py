#!/usr/bin/env python3
"""
Makes a frozen copy of the game's streamed assets, so players' clients download them from our server instead of a
third party's.

The client (AssetDeliveryIndirect) streams assets that aren't in its packs from AssetDelivery:IndirectServerAddress:
  <address>/manifest.crc        "<crc32 of the manifest text>,<its length>"
  <address>/manifest.txt.z      the manifest: lines "name,crc32,size"
  <address>/NNN/<name>?<crc32>  one asset, NNN = FolderOf(name), see below
A .z file is an 8-byte header (a1b2c3d4, big-endian unpacked length) followed by a zlib stream, and its crc32 in the
manifest is of the unpacked data. Other files (.gfx, .mp3, .png...) are stored as is; their crc32 is of the file.

Each file is checked against the manifest (size, header, unpacked length and crc32) before it is kept, and written
atomically, so a rerun resumes and never keeps a bad file. The source manifest is the trust anchor: once copied,
nothing changes unless this is run again on purpose. SHA256SUMS lists every kept file, to notice any later change.

Usage: evergrove-asset-mirror.py <source address> <destination folder> [--workers N] [--limit N]
"""

import argparse
import concurrent.futures
import hashlib
import os
import sys
import threading
import time
import urllib.error
import urllib.request
import zlib

USER_AGENT = "EvergroveAssetMirror/1.0 (+https://play.evergrove.fyi)"
MAGIC = bytes.fromhex("a1b2c3d4")


def s32(value):
    value &= 0xFFFFFFFF
    return value - (1 << 32) if value & 0x80000000 else value


def folder_of(name):
    """The client's folder for an asset: its hash of the upper-cased name, modulo 1000 (FreeRealms.exe 0x705b00)."""
    h = 0
    for ch in name.upper():
        h = s32(ord(ch) + h)
        h = s32(h * 0x401)
        h = s32(h ^ (h >> 6))
    c = s32(h * 9)
    a = s32((c >> 11) ^ c)
    return (s32(a * 0x8001) & 0xFFFFFFFF) % 1000


def unpack(data):
    """The unpacked content of a .z file, or None when it isn't one."""
    if len(data) < 8 or data[:4] != MAGIC:
        return None
    try:
        content = zlib.decompress(data[8:])
    except zlib.error:
        return None
    return content if len(content) == int.from_bytes(data[4:8], "big") else None


def fetch(url, attempts=4):
    """The body, or None on 404. Other failures are retried, then raised."""
    for attempt in range(attempts):
        try:
            request = urllib.request.Request(url, headers={"User-Agent": USER_AGENT, "Accept": "*/*"})
            with urllib.request.urlopen(request, timeout=60) as response:
                return response.read()
        except urllib.error.HTTPError as error:
            if error.code == 404:
                return None
            if attempt == attempts - 1:
                raise
        except (urllib.error.URLError, TimeoutError, ConnectionError):
            if attempt == attempts - 1:
                raise
        time.sleep(2 ** attempt)


def write_atomically(path, data):
    os.makedirs(os.path.dirname(path), exist_ok=True)
    temporary = path + ".part"
    with open(temporary, "wb") as file:
        file.write(data)
    os.replace(temporary, path)


def is_valid(name, data, crc, size):
    """A .z file's crc32 is of its unpacked content; any other file is stored as is and its crc32 is of the file."""
    if len(data) != size:
        return False
    content = unpack(data) if name.endswith(".z") else data
    return content is not None and zlib.crc32(content) == crc


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("source")
    parser.add_argument("destination")
    parser.add_argument("--workers", type=int, default=4)
    parser.add_argument("--limit", type=int, help="only the first N assets, for a trial run")
    args = parser.parse_args()

    source = args.source.rstrip("/")
    destination = os.path.abspath(args.destination)

    manifest_crc = fetch(source + "/manifest.crc")
    manifest_z = fetch(source + "/manifest.txt.z")
    if manifest_crc is None or manifest_z is None:
        sys.exit("The source has no manifest.")

    manifest_text = unpack(manifest_z)
    want_crc, want_length = (int(x) for x in manifest_crc.decode("ascii").strip().split(","))
    if manifest_text is None or zlib.crc32(manifest_text) != want_crc or len(manifest_text) != want_length:
        sys.exit("The manifest doesn't match manifest.crc.")

    entries = []
    for line in manifest_text.decode("latin-1").splitlines():
        if not line.strip():
            continue
        name, crc, size = line.rsplit(",", 2)
        # Names become file names: nothing that could leave the folder. (".dma.z" is a real asset.)
        if not name or name in (".", "..") or any(c in name for c in "/\\:\0") or name.endswith(".part"):
            sys.exit(f"Refusing an unsafe asset name: {name!r}")
        entries.append((name, int(crc), int(size)))

    if args.limit:
        entries = entries[:args.limit]

    print(f"{len(entries)} assets, {sum(e[2] for e in entries) / 1e9:.2f} GB", flush=True)

    lock = threading.Lock()
    counts = {"kept": 0, "downloaded": 0, "missing": 0, "bad": 0}
    missing, bad = [], []
    started = time.time()

    def mirror(entry):
        name, crc, size = entry
        path = os.path.join(destination, "%03u" % folder_of(name), name)

        if os.path.exists(path) and os.path.getsize(path) == size:
            with open(path, "rb") as file:
                if is_valid(name, file.read(), crc, size):
                    return "kept", name

        data = fetch("%s/%03u/%s?%u" % (source, folder_of(name), name, crc))
        if data is None:
            return "missing", name
        if not is_valid(name, data, crc, size):
            return "bad", name

        write_atomically(path, data)
        return "downloaded", name

    with concurrent.futures.ThreadPoolExecutor(max_workers=args.workers) as pool:
        for done, (result, name) in enumerate(pool.map(mirror, entries), 1):
            with lock:
                counts[result] += 1
                if result == "missing":
                    missing.append(name)
                elif result == "bad":
                    bad.append(name)
            if done % 2000 == 0 or done == len(entries):
                print(f"{done}/{len(entries)} {counts} {time.time() - started:.0f}s", flush=True)

    # The manifest last, so a client never sees a manifest for files that aren't there yet.
    write_atomically(os.path.join(destination, "manifest.txt.z"), manifest_z)
    write_atomically(os.path.join(destination, "manifest.crc"), manifest_crc)

    with open(os.path.join(destination, "MISSING.txt"), "w") as file:
        file.write("".join(n + "\n" for n in sorted(missing)))
    with open(os.path.join(destination, "BAD.txt"), "w") as file:
        file.write("".join(n + "\n" for n in sorted(bad)))

    sums = []
    for root, _, files in os.walk(destination):
        for file_name in files:
            if file_name in ("SHA256SUMS", "MISSING.txt", "BAD.txt") or file_name.endswith(".part"):
                continue
            full = os.path.join(root, file_name)
            with open(full, "rb") as file:
                sums.append(f"{hashlib.sha256(file.read()).hexdigest()}  {os.path.relpath(full, destination).replace(os.sep, "/")}\n")
    write_atomically(os.path.join(destination, "SHA256SUMS"), "".join(sorted(sums, key=lambda s: s[66:])).encode())

    print(f"Done: {counts}. Missing at the source: {len(missing)}, failed checks: {len(bad)}.", flush=True)


if __name__ == "__main__":
    main()
