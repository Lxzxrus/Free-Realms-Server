#!/usr/bin/env bash
# Fetches the zone geometry upstream OSFR converted from the game client: FabledRealms' navmesh and terrain, about
# 60 MB, which this repository doesn't keep. The Gateway uses them for NPC movement (wandering and fleeing NPCs,
# pets, the client's path requests). Without them it still runs: it logs one warning at start, wandering NPCs stand
# at their spawn points, and paths follow the roads.
#
# Run it on the server after unpacking a release and before `docker compose build`, with sudo, because the unpacked
# tree (/opt/evergrove/app) belongs to root:
#
#   sudo bash /opt/evergrove/app/tools/deploy/fetch-zone-geometry.sh
#
# Each file is downloaded over HTTPS from upstream's GitHub at the pinned commit below, its SHA-256 is checked, and
# only then is it moved into src/Resources, which Compose mounts into every container (docker-compose.yml). A file
# already there with the right hash is left alone, so running it on every deploy costs nothing. When a download
# fails or a hash doesn't match, it stops with an error and leaves any file already in place untouched.
#
# To take a newer version from upstream: change UPSTREAM_COMMIT, and each file's hash and size to the new file's
# (`sha256sum`, `stat -c %s`), computed from a copy you trust, such as `git show <commit>:<path>` from a clone.
set -euo pipefail

UPSTREAM_REPOSITORY="Open-Source-Free-Realms/Sanctuary"

# Upstream catch-up 1 merged OSFR up to this commit. Both files are unchanged from the commit that added them
# (e3deee7, Groaxy's pets and NPC movement) to OSFR main at 3b3aec3.
UPSTREAM_COMMIT="1dfe4b980ff01e87512f90350fd6c8758e928ecf"

# Path under src/Resources, SHA-256, size in bytes.
FILES=(
    "NavMeshes/FabledRealms.navmesh 81dc045233bb39a41804a99bfdb6ab3a9e56fe7f0f615d38b146af72d6e27586 31839576"
    "Terrain/FabledRealms.terrain 5341e052e4c710b9db01150bd13fed7bc3a97ee2f31452dd394b3fb257d6b45c 27944457"
)

resources_dir="$(cd "$(dirname "${BASH_SOURCE[0]}")/../../src/Resources" && pwd)"

has_hash() {
    printf '%s  %s\n' "$2" "$1" | sha256sum --check --status
}

tmp=""
trap 'if [ -n "$tmp" ]; then rm -f "$tmp"; fi' EXIT

for entry in "${FILES[@]}"; do
    read -r path sha256 size <<<"$entry"
    target="$resources_dir/$path"

    if [ -f "$target" ] && has_hash "$target" "$sha256"; then
        echo "$path: already in place"
        continue
    fi

    mkdir -p "$(dirname "$target")"
    tmp="$(mktemp "$target.XXXXXX")"

    url="https://raw.githubusercontent.com/$UPSTREAM_REPOSITORY/$UPSTREAM_COMMIT/src/Resources/$path"
    echo "$path: downloading $url"
    curl --fail --silent --show-error --proto '=https' --tlsv1.2 --retry 3 --max-filesize "$size" \
        --output "$tmp" "$url"

    if ! has_hash "$tmp" "$sha256"; then
        echo "$path: the download's SHA-256 isn't $sha256, so it was not installed." >&2
        exit 1
    fi

    chmod 644 "$tmp"
    mv -f "$tmp" "$target"
    tmp=""
    echo "$path: installed"
done
