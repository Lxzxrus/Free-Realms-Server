# Asset mirror

The game ships with only part of its content. Whatever isn't in its packs (about 162,000 textures, models, sounds and
UI files, 4.1 GB) it downloads while playing from `AssetDelivery:IndirectServerAddress`, a launch argument WebAPI
hands the client (`WebAPI:LaunchArguments`). The client keeps them in `AssetsW_*.pack` in the game folder.

Sanctuary's default points at OSFR's asset server, `http://osfr.editz.dev/assets`. Whoever runs that address decides
what every player's game downloads and opens, and the launcher's client pinning can't see any of it. So Evergrove
serves its own frozen copy, made once with `evergrove-asset-mirror.py`.

## What the client asks for

Found in `FreeRealms.exe` and its `Logs/FreeRealms.log`:

| URL | What |
|---|---|
| `<address>/manifest.crc` | `<crc32 of the manifest text>,<its length>` |
| `<address>/manifest.txt.z` | The manifest, lines `name,crc32,size`. Saved as `Assets_manifest.txt` |
| `<address>/NNN/<name>?<crc32>` | One asset. `NNN` is the client's hash of the upper-cased name, modulo 1000 (`0x705b00`, a one-at-a-time hash with signed shifts) |

A `.z` file is `a1b2c3d4`, the unpacked length (big-endian), then a zlib stream; its manifest crc32 is of the
unpacked data. Other files (`.gfx`, `.mp3`, `.png`...) are stored as is, and their crc32 is of the file.

## Making the copy

```bash
sudo install -m 755 evergrove-asset-mirror.py /usr/local/sbin/evergrove-asset-mirror
sudo useradd --system --no-create-home --shell /usr/sbin/nologin evg-assets
sudo install -d -m 755 -o evg-assets -g evg-assets /srv/assets
sudo systemd-run --unit=evergrove-asset-mirror -p User=evg-assets -p Group=evg-assets \
  -p ProtectSystem=strict -p ReadWritePaths=/srv/assets -p ProtectHome=yes -p PrivateTmp=yes -p NoNewPrivileges=yes \
  /usr/bin/python3 -I /usr/local/sbin/evergrove-asset-mirror http://osfr.editz.dev/assets /srv/assets --workers 4
journalctl -u evergrove-asset-mirror -f
```

Every file is checked against the manifest (size, and crc32 as above) before it's kept, and written atomically. A
rerun keeps the files that still check out and fetches the rest, so an interrupted copy resumes. The manifest is
written last. At the end:

- `SHA256SUMS`: every kept file, to notice any later change (`cd /srv/assets && sha256sum -c --quiet SHA256SUMS`).
- `MISSING.txt`: assets in the manifest the source doesn't have (404). The client asks for them anyway; OSFR's
  server lacks some housing pieces, for example `hsg_chair_throne_01.dds.z`.
- `BAD.txt`: assets that failed the check. They aren't kept.
- `FAILED.txt`: assets that couldn't be downloaded (network errors). A rerun retries them.
- `SKIPPED-CODE.txt`: programs in the manifest, never copied or served. OSFR's manifest lists `PlayClient.exe` and
  three installer DLLs, leftovers of SOE's.

Names are URL-encoded: 703 of them have spaces or characters like `^ & ( ) ' ! +` (`Bear Vinegolem.gfx`).

To run it again (to resume, or to retry `FAILED.txt`), clear the finished unit first:
`sudo systemctl reset-failed evergrove-asset-mirror`, then the same `systemd-run` line.

The copy trusts the source's manifest at the moment it's made. After that nothing changes unless it's run again on
purpose, and `SHA256SUMS` shows what changed if it is.

## Serving it

[launcher/server/Caddyfile](../../launcher/server/Caddyfile) serves `/srv/assets` at `http://<domain>/assets/` over
plain HTTP, because the client has no HTTPS, and redirects everything else on port 80 to HTTPS. Then point WebAPI
at it, for example in the VPS's `docker-compose.override.yml`:

```yaml
WebAPI__LaunchArguments: "AssetDelivery:IndirectServerAddress=http://play.evergrove.fyi/assets"
```

Players pick it up at their next login.
