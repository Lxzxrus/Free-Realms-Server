#!/usr/bin/env bash
# Installs what the self-check needs in a Claude Code cloud session: the .NET 10 SDK (src/global.json) and the
# .NET 9 runtime (every project targets net9.0). Runs from the SessionStart hook in .claude/settings.json and
# does nothing outside cloud sessions.
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
