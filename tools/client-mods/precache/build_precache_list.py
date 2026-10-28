# Builds the launcher's pre-cache list: files the game loads on the spot (minigame pictures, zone files) that the
# official client doesn't have, so they exist only on the asset server.
#
# python build_precache_list.py <client dir> <manifest.txt> [fetched .tar files...] <out precache.txt>
#
# See README.md beside it for where the inputs come from.
#
# Steps it takes:
#   1. The official packs (Assets_*.pack only; AssetsW_*.pack are a player's download cache) give the "already local" set.
#   2. Each listed Flash file contributes its img*.dds pictures; each listed zone contributes <zone>.gzne and
#      <zone>Areas.xml; the Flash files themselves are included too. Anything already local is dropped.
#   3. Every remaining file must be in the asset index (manifest) and among the fetched copies, unpacked, with the
#      index's CRC; the output line is: server file name, unpacked size, CRC-32, SHA-256 of the unpacked data.
import glob, hashlib, os, re, struct, sys, tarfile, zlib

# game_wheel.gfx and its pictures are all in the official packs, so it adds nothing; it stays as a check.
# A .lst beside a .gfx is its picture list; game_cooking.lst, unlike game_mining.lst, isn't in the official packs.
FLASH = ["game_mining.gfx", "mining_tutorial.gfx", "game_farming.gfx", "farming_tutorial.gfx", "game_forging.gfx",
         "forging_tutorial.gfx", "game_mailsorting.gfx", "mailsorting_tutorial.gfx", "game_wheel.gfx", "game_cooking.gfx",
         "game_cooking.lst", "tutorial_cooking.gfx"]
ZONES = ["sg_fishing_medpond", "sg_fishing_stream", "bw_fishing_medpond", "bw_fishing_stream", "sh_fishing_medpond",
         "sh_fishing_stream"]

client, manifest_path, out_path = sys.argv[1], sys.argv[2], sys.argv[-1]
sources = sys.argv[3:-1]


def pack_entries(pattern):
    for pack in sorted(glob.glob(os.path.join(client, pattern))):
        data = open(pack, "rb").read()
        off = 0
        while True:
            nxt, cnt = struct.unpack_from(">II", data, off)
            p = off + 8
            for _ in range(cnt):
                n = struct.unpack_from(">I", data, p)[0]; p += 4
                name = data[p:p + n].decode("latin1"); p += n
                s, l, _ = struct.unpack_from(">III", data, p); p += 12
                yield name, data, s, l
            if nxt == 0:
                break
            off = nxt


official = {name.lower() for name, *_ in pack_entries("Assets_*.pack")}

index = {}
for line in open(manifest_path, encoding="utf-8"):
    parts = line.strip().split(",")
    if len(parts) == 3:
        stored = parts[0]
        plain = stored[:-2] if stored.lower().endswith(".z") else stored
        index[plain.lower()] = (stored, int(parts[1]), int(parts[2]))

# Every fetched copy, keyed by plain name -> unpacked bytes.
fetched = {}
def add_fetched(name, raw):
    if name.lower().endswith(".z"):
        assert raw[:4] == b"\xa1\xb2\xc3\xd4", name
        data = zlib.decompress(raw[8:])
        assert len(data) == struct.unpack(">I", raw[4:8])[0], name
        name = name[:-2]
    else:
        data = raw
    fetched[name.lower()] = (name, data)

for src in sources:
    with tarfile.open(src) as t:
        for m in t.getmembers():
            add_fetched(os.path.basename(m.name), t.extractfile(m).read())


def flash_bytes(name):
    if name.lower() in fetched:
        return fetched[name.lower()][1]
    for n, data, s, l in pack_entries("Assets*_*.pack"):
        if n.lower() == name.lower():
            return data[s:s + l]
    raise SystemExit(f"{name}: not found locally or among the fetched files")


wanted = set()
for f in FLASH:
    data = flash_bytes(f)
    body = zlib.decompress(data[8:]) if data[:3] in (b"CWS", b"CFX") else data[8:]
    wanted.add(f.lower())
    wanted.update(m.decode().lower() for m in re.findall(rb"img\d+\.dds", body))
for z in ZONES:
    wanted.update({f"{z}.gzne".lower(), f"{z}areas.xml".lower()})

wanted -= official
missing = sorted(w for w in wanted if w not in index)
if missing:
    raise SystemExit(f"not in the asset index: {missing}")
unfetched = sorted(w for w in wanted if w not in fetched)
if unfetched:
    open(out_path + ".fetch", "w", encoding="utf-8", newline="\n").write("\n".join(index[w][0] for w in unfetched) + "\n")
    raise SystemExit(f"{len(unfetched)} files still to fetch; their server names are in {out_path}.fetch")

lines = []
for w in sorted(wanted):
    stored, crc, _ = index[w]
    name, data = fetched[w]
    if zlib.crc32(data) != crc:
        raise SystemExit(f"{name}: fetched copy's CRC doesn't match the asset index")
    lines.append(f"{stored},{len(data)},{crc},{hashlib.sha256(data).hexdigest()}")

header = ("# Files the launcher pre-caches: the game loads them on the spot (minigame pictures, zone files) and the\n"
          "# official client doesn't have them. One per line: server file name, unpacked size, CRC-32, SHA-256 of the\n"
          "# unpacked data. Made by tools/client-mods/precache/build_precache_list.py; see its README.\n")
open(out_path, "w", encoding="utf-8", newline="\n").write(header + "\n".join(lines) + "\n")
print(f"{out_path}: {len(lines)} files, {sum(len(fetched[w][1]) for w in wanted) / 1e6:.1f} MB unpacked")
