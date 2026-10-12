#!/usr/bin/env python3
"""Look up and search the game client's text by string id.

The protocol sends text as numeric string ids (quest titles, objectives, dialogue, item names). The client's
locale/en_us_data.dat keeps each string under a hash of "Global.Text.<id>": Bob Jenkins' lookup2 hash (his 1996
hash(), initial value 0), the scheme PlanetSide 2 uses too. This reads the client you point it at; nothing from the
client is stored in this repository.

    python strings.py <client folder> lookup 93420 93421
    python strings.py <client folder> search "Touring Seaside" [--around 10]

Searching hashes every id in the ranges real ids use (0 to 1,000,000 and 4,000,000 to 6,000,000), about ten seconds.
Elsewhere roughly 26 ids per million match some row by chance (32-bit hashes), so the search skips those ranges.
"""
import argparse
import os
import sys

MASK = 0xFFFFFFFF
ID_RANGES = (range(0, 1_000_000), range(4_000_000, 6_000_000))


def _mix(a, b, c):
    a = (a - b - c) & MASK; a ^= c >> 13
    b = (b - c - a) & MASK; b ^= (a << 8) & MASK
    c = (c - a - b) & MASK; c ^= b >> 13
    a = (a - b - c) & MASK; a ^= c >> 12
    b = (b - c - a) & MASK; b ^= (a << 16) & MASK
    c = (c - a - b) & MASK; c ^= b >> 5
    a = (a - b - c) & MASK; a ^= c >> 3
    b = (b - c - a) & MASK; b ^= (a << 10) & MASK
    c = (c - a - b) & MASK; c ^= b >> 15
    return a, b, c


def lookup2(key, initval=0):
    """Bob Jenkins' lookup2 hash of an ASCII string."""
    k = key.encode("ascii")
    a = b = 0x9E3779B9
    c = initval
    i, n = 0, len(k)
    while n >= 12:
        a = (a + int.from_bytes(k[i:i + 4], "little")) & MASK
        b = (b + int.from_bytes(k[i + 4:i + 8], "little")) & MASK
        c = (c + int.from_bytes(k[i + 8:i + 12], "little")) & MASK
        a, b, c = _mix(a, b, c)
        i += 12
        n -= 12
    rest = k[i:] + bytes(12)
    c = (c + len(k) + (int.from_bytes(rest[8:11], "little") << 8 if n > 8 else 0)) & MASK
    b = (b + (int.from_bytes(rest[4:8], "little") & ((1 << (8 * max(0, min(n, 8) - 4))) - 1))) & MASK
    a = (a + (int.from_bytes(rest[0:4], "little") & ((1 << (8 * min(n, 4))) - 1))) & MASK
    return _mix(a, b, c)[2]


def text_key(string_id):
    return lookup2(f"Global.Text.{string_id}")


def load_rows(client):
    path = os.path.join(client, "locale", "en_us_data.dat")
    rows = {}
    with open(path, encoding="utf-8", errors="replace") as f:
        for line in f:
            parts = line.rstrip("\r\n").split("\t", 2)
            if len(parts) == 3 and parts[0].isdigit():
                rows[int(parts[0])] = parts[2]
    return rows


def main():
    parser = argparse.ArgumentParser(description=__doc__.split("\n\n")[0])
    parser.add_argument("client", help="the game client folder (the one with FreeRealms.exe)")
    sub = parser.add_subparsers(dest="command", required=True)
    look = sub.add_parser("lookup", help="print the text of string ids")
    look.add_argument("ids", nargs="+", type=int)
    find = sub.add_parser("search", help="find string ids whose text contains the words")
    find.add_argument("text")
    find.add_argument("--around", type=int, default=0, help="also print rows this many ids either side")
    args = parser.parse_args()

    rows = load_rows(args.client)

    if args.command == "lookup":
        for string_id in args.ids:
            print(f"{string_id}\t{rows.get(text_key(string_id), '<not in this client>')}")
        return

    index = {}
    for id_range in ID_RANGES:
        for string_id in id_range:
            key = text_key(string_id)
            if key in rows:
                index[string_id] = rows[key]

    needle = args.text.lower()
    hits = sorted(i for i, t in index.items() if needle in t.lower())
    if not hits:
        sys.exit("no match")
    shown = set()
    for hit in hits:
        for string_id in range(hit - args.around, hit + args.around + 1):
            if string_id in index and string_id not in shown:
                shown.add(string_id)
                marker = "*" if string_id == hit else " "
                print(f"{marker}{string_id}\t{index[string_id]}")
        if args.around:
            print()


if __name__ == "__main__":
    main()
