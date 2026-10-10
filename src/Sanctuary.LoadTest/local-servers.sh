#!/usr/bin/env bash
# Starts or stops WebAPI, Login and Gateway (Release builds) on this machine for a load test, against a MariaDB
# database of their own, so the bot accounts never touch a real database. Used for the cloud baseline; see
# README.md in this folder.
#
#   ./local-servers.sh start [log-dir]    build, create the database if needed, start all three
#   ./local-servers.sh stop               stop them
#
# Needs: the .NET 10 SDK and a MariaDB on 127.0.0.1:3306 where
# `mariadb` can connect as an administrator (as root in a cloud session). Database, user and password can be
# changed with LOADTEST_DB, LOADTEST_DB_USER and LOADTEST_DB_PASSWORD.
set -euo pipefail

src="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
db="${LOADTEST_DB:-sanctuary_loadtest}"
db_user="${LOADTEST_DB_USER:-user}"
db_password="${LOADTEST_DB_PASSWORD:-password}"

stop() {
    if ! pkill -f 'Sanctuary\.(WebAPI|Login|Gateway)\.dll'; then
        echo "Nothing was running."
        return
    fi

    # The Gateway warns players and waits 5 s before it exits.
    for _ in $(seq 30); do
        pgrep -f 'Sanctuary\.(WebAPI|Login|Gateway)\.dll' >/dev/null || { echo "Stopped."; return; }
        sleep 1
    done

    echo "Still running after 30 s." >&2
    exit 1
}

start() {
    local logs="${1:-$src/Sanctuary.LoadTest/logs}"
    mkdir -p "$logs"

    if pgrep -f 'Sanctuary\.(WebAPI|Login|Gateway)\.dll' >/dev/null; then
        echo "Servers are already running; stop them first." >&2
        exit 1
    fi

    (cd "$src" && dotnet build Sanctuary.slnx -c Release --nologo -v quiet)

    mariadb -e "CREATE DATABASE IF NOT EXISTS \`$db\`;
        CREATE USER IF NOT EXISTS '$db_user'@'localhost' IDENTIFIED BY '$db_password';
        GRANT ALL ON \`$db\`.* TO '$db_user'@'localhost';"

    # Production settings (no DOTNET_ENVIRONMENT), with the database passed in the environment instead of a
    # database.json, so nothing is written into the build folders.
    export Database__Provider=MySql
    export Database__ConnectionString="server=127.0.0.1;port=3306;uid=$db_user;pwd=$db_password;database=$db"
    export Database__VersionString="$(mariadb -N -e 'SELECT VERSION();' | sed 's/-.*//')-MariaDB"
    # A fresh Login-Gateway challenge for each run; the servers refuse to start without one.
    export Server__LoginGatewayChallenge="$(openssl rand -base64 32)"

    # Every bot connects from this machine's address, so lift the per-address limits a public server keeps
    # (Udp section, see PlayerUdpOptions; WebAPI's RateLimits, see docs/webapi.md). Everything else stays at the
    # launch defaults.
    export Udp__MaxConnectionsPerIp=0
    export Udp__ConnectRatePerIp=0
    export Udp__UnverifiedReplyRatePerIp=0
    export WebAPI__RateLimits__LoginPerMinute=100000
    export WebAPI__RateLimits__RegisterPerHour=100000

    # Login applies the migrations, so it goes first.
    launch Login "$logs/login.log"
    wait_for "$logs/login.log" "LoginServer started"

    launch Gateway "$logs/gateway.log"
    launch WebAPI "$logs/webapi.log"
    wait_for "$logs/gateway.log" "connected"
    wait_for "$logs/webapi.log" "Application started"

    echo "WebAPI, Login and Gateway are running. Logs are in $logs."
}

launch() {
    (cd "$src/Sanctuary.$1/bin/Release/net10.0" && exec nohup dotnet "Sanctuary.$1.dll" >"$2" 2>&1 </dev/null) &
}

wait_for() {
    for _ in $(seq 60); do
        grep -q "$2" "$1" 2>/dev/null && return 0
        sleep 1
    done
    echo "Timed out waiting for \"$2\" in $1." >&2
    exit 1
}

case "${1:-}" in
    start) start "${2:-}" ;;
    stop) stop ;;
    *) echo "Usage: $0 start [log-dir] | stop" >&2; exit 2 ;;
esac
