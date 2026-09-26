#!/usr/bin/env bash
# Installs what the self-check needs in a Claude Code cloud session: the .NET 10 SDK (src/global.json), the
# .NET 9 runtime (every project targets net9.0) and a MariaDB for the MySQL test. Runs from the SessionStart hook
# in .claude/settings.json and does nothing outside cloud sessions. See docs/cloud-environment.md.
set -u

[ "${CLAUDE_CODE_REMOTE:-}" = "true" ] || exit 0

DOTNET_ROOT="$HOME/.dotnet"
export DOTNET_ROOT PATH="$DOTNET_ROOT:$PATH" DOTNET_CLI_TELEMETRY_OPTOUT=1 DOTNET_NOLOGO=1

have_sdk() { dotnet --list-sdks 2>/dev/null | grep -q '^10\.'; }
have_runtime() { dotnet --list-runtimes 2>/dev/null | grep -q 'Microsoft.NETCore.App 9\.'; }

if ! have_sdk || ! have_runtime; then
    installer="$(mktemp)"
    if curl -fsSL https://dot.net/v1/dotnet-install.sh -o "$installer"; then
        have_sdk || bash "$installer" --channel 10.0 --install-dir "$DOTNET_ROOT" >/dev/null 2>&1
        have_runtime || bash "$installer" --channel 9.0 --runtime dotnet --install-dir "$DOTNET_ROOT" >/dev/null 2>&1
    fi
    rm -f "$installer"
fi

# Keep dotnet on PATH for the rest of the session.
if [ -n "${CLAUDE_ENV_FILE:-}" ]; then
    {
        echo "export DOTNET_ROOT=\"$DOTNET_ROOT\""
        echo "export PATH=\"$DOTNET_ROOT:\$PATH\""
        echo "export DOTNET_CLI_TELEMETRY_OPTOUT=1"
        echo "export DOTNET_NOLOGO=1"
    } >> "$CLAUDE_ENV_FILE"
fi

if have_sdk && have_runtime; then
    echo "cloud-setup: .NET ready ($(dotnet --version))."
else
    echo "cloud-setup: .NET is NOT fully installed (SDK 10: $(have_sdk && echo yes || echo no), runtime 9: $(have_runtime && echo yes || echo no))."
    echo "cloud-setup: the environment probably blocks dot.net downloads. Report this to Nate; don't work around it."
fi

# MySqlTest.IsValidAsync needs the MariaDB that CI runs as a service (build.yml): 127.0.0.1:3306, user/password,
# database sanctuary_test. Without it the test fails after about a minute of retries; it does not skip. The cloud
# container has no systemd, so the server is started with `service`.
db_alive() { mariadb-admin ping >/dev/null 2>&1; }

if [ "$(id -u)" -eq 0 ]; then
    if ! command -v mariadbd >/dev/null 2>&1; then
        { apt-get update && DEBIAN_FRONTEND=noninteractive apt-get install -y --no-install-recommends mariadb-server; } >/dev/null 2>&1
    fi
    if command -v mariadbd >/dev/null 2>&1 && ! db_alive; then
        service mariadb start >/dev/null 2>&1
    fi
    if db_alive; then
        mariadb -e "CREATE DATABASE IF NOT EXISTS sanctuary_test;
            CREATE USER IF NOT EXISTS 'user'@'localhost' IDENTIFIED BY 'password';
            CREATE USER IF NOT EXISTS 'user'@'%' IDENTIFIED BY 'password';
            GRANT ALL ON sanctuary_test.* TO 'user'@'localhost';
            GRANT ALL ON sanctuary_test.* TO 'user'@'%';"
    fi
fi

if db_alive; then
    echo "cloud-setup: MariaDB ready ($(mariadb -N -e 'SELECT VERSION();' 2>/dev/null))."
else
    echo "cloud-setup: MariaDB is NOT running, so MySqlTest.IsValidAsync will fail after about a minute."
    echo "cloud-setup: the environment probably blocks the Ubuntu archive. Report this to Nate; don't work around it."
fi
