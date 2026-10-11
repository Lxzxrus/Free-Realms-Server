#!/bin/sh
# Lets a character rename again now, by clearing its rename cooldown (RenameCooldown in Sanctuary.Game), for example
# after a typo in a new name. The same as `/mod resetrename <name>` in game.
#
# On the server:  sudo sh /opt/evergrove/app/tools/ops/reset-rename-cooldown.sh "First Last"
#
# Takes the character's full name exactly as shown in game. Only letters, spaces and apostrophes are accepted; for
# a name with other letters (accents), use the chat command. Prints how many characters have that name (1 when it
# worked, 0 when there's no such character). The database password never leaves the container.
set -eu

usage() {
    echo "usage: $0 \"First Last\"  (letters, spaces and apostrophes only)" >&2
    exit 2
}

# A `case` pattern, not grep: grep checks line by line, so a name with a line break could slip through.
name="${1:-}"
case "$name" in
    *[!A-Za-z\'\ ]*) usage ;;
esac
[ "${#name}" -ge 3 ] && [ "${#name}" -le 33 ] || usage

# The name is checked above; doubling apostrophes keeps it a single SQL string.
sql_name=$(printf '%s' "$name" | sed "s/'/''/g")

cd "$(dirname "$0")/../../src/Docker"
printf "UPDATE Characters SET LastRenamed = NULL WHERE FullName = '%s';\nSELECT COUNT(*) FROM Characters WHERE FullName = '%s';\n" "$sql_name" "$sql_name" |
    docker compose exec -T sanctuary.mysql sh -c 'exec mariadb -uroot -p"$MYSQL_ROOT_PASSWORD" -N "$MYSQL_DATABASE"' |
    sed 's/^/characters with that name: /'
