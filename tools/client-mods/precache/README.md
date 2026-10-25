# Pre-caching the files the game loads on the spot

Most of what the game streams while playing (models, textures) is fetched from the asset server in the background,
and a missing one only shows up late or invisible. A few files are different: the game needs them **at once**, and
for those it only looks in the packs on the player's disk. If a file like that exists only on the asset server, the
load fails and the client writes this to `Client\Logs\FailedSyncLoadAssets.txt`:

```
SynchronousGetAssetData failed, local version was not current
```

Found on 2026-10-19 and confirmed in game on Nate's PC:

| What failed | File the game wanted | What players saw |
|---|---|---|
| Mining | `game_mining.gfx`'s 75 pictures (`img<n>.dds`), `mining_tutorial.gfx` | No copper, no bin, no frame; blank tutorial |
| Fishing holes | `<zone>.gzne` and `<zone>Areas.xml` of `sg_`, `bw_` and `sh_fishing_*` | Crash while zoning in |

The official OSFR client doesn't carry these files; the asset server (`tools/asset-mirror`) does. With them in a
cache pack in the client folder, Mining draws fully and fishing works. The daily wheel is **not** this problem:
`game_wheel.gfx` and all 83 of its pictures are in the official packs.

## What the launcher does

After the client check (`ClientVerifier`) and the client mods, the launcher (`Helpers/AssetPrecache.cs`):

1. Reads its list, `launcher/src/Launcher/Mods/precache.txt`, embedded in the launcher: each file's name on the
   asset server, unpacked size, CRC-32 and SHA-256.
2. Looks for each file in the game's download cache packs (`AssetsW_*.pack`). A copy counts if its index entry has the
   list's CRC and size **and** its data still hashes to that CRC. A damaged copy is hidden (its name's first letter
   becomes `~`), so the game stops reading it.
3. Downloads what's missing from `<DefaultAssetUrl>/<folder>/<name>`, the address the game itself uses, unpacks `.z` files
   and checks size, CRC-32 and SHA-256. One mismatch and nothing is written.
4. Writes them as a new cache pack after the game's own (`AssetsW_<last + 1>.pack`), through a temporary file.

The official `Assets_*.pack` files are never touched. If anything fails, the player gets a notice and the game starts
anyway.

The asset server's address is a launcher build setting, `DefaultAssetUrl` in `launcher/src/Directory.Build.props`,
and it's used for the default server only: the list is that server's files. It isn't in `servermanifest.xml` because
launchers released before this one refuse a manifest with an element they don't know (the server would show as
offline). Empty: nothing is pre-cached.

### Why plain HTTP is allowed here

Everything else the launcher fetches from a game server must be HTTPS (`TransportPolicy`). The asset server is plain
HTTP because the game can't do HTTPS. The pre-cache can use it safely because it fetches **only** the files on its
own list, and each must match the SHA-256 built into the launcher; a server, or anyone in between, can't change what
gets written. It follows no redirects and caps each file at 16 MiB.

### The pack layout

A pack is a chain of blocks: `next block offset` (u32, big-endian, 0 for the last), `entry count` (u32), then each
entry's name length (u32), name, data offset, data length and CRC-32 of the data (u32 each). The game **keeps the
first 8192 bytes of each block for the index** and adds its own downloads to the last cache pack's index, so the
launcher puts its data after that area. A first test pack written without the gap had its first file overwritten
by the game.

## Refreshing the list

When the asset server's files change (a new mirror, or more minigames to cover), rebuild the list with
`build_precache_list.py`. Edit its `FLASH` and `ZONES` lists first: each Flash file brings its `img*.dds`
pictures, each zone its `.gzne` and `Areas.xml`. Anything already in the official packs is left out.

1. Get the asset index: `/srv/assets/manifest.txt.z` on the VPS, unpacked (`a1b2c3d4`, length, then zlib), so
   `manifest.txt` has lines `name,crc32,size`.
2. Run it once to learn which files it needs:

   ```bash
   python build_precache_list.py <client folder> manifest.txt precache.txt
   ```

   It stops and writes their server names to `precache.txt.fetch`.
3. Copy `precache.txt.fetch` to the VPS and pack those files up (folders are three digits, so `*/name` finds each):

   ```bash
   cd /srv/assets && tar -cf ~/precache.tar $(while read -r n; do ls -d */"$n"; done < ~/precache.txt.fetch)
   ```

   Or stream it, which leaves nothing on the VPS:

   ```bash
   ssh ubuntu@play.evergrove.fyi 'cd /srv/assets && while read -r n; do ls -d */"$n"; done | tar -cf - -T -' < precache.txt.fetch > precache.tar
   ```

   A Flash file that isn't in the official packs (`game_cooking.gfx`) has to be fetched first, the same way: the script
   reads it to learn its pictures, and stops with "not found" until it has it.
4. Copy `precache.tar` back and run again with it (several tars are fine):

   ```bash
   python build_precache_list.py <client folder> manifest.txt precache.tar precache.txt
   ```

   Every file is checked against the index's CRC before it's listed.
5. Copy `precache.txt` to `launcher/src/Launcher/Mods/precache.txt`, run the launcher tests, and publish a launcher
   release.

Don't take these files from old client archives: their copies are older versions with other CRCs, and the game
treats a CRC that doesn't match the server's as out of date.
