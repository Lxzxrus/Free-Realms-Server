#!/bin/bash
set -e

# Find the absolute path of the script
SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"

# Check if version parameter is provided
if [ "$#" -ne 1 ]; then
    echo "Version number is required."
    echo "Usage: ./build.sh [version]"
    exit 1
fi

BUILD_VERSION="$1"
RELEASE_DIR="$SCRIPT_DIR/releases"
PUBLISH_DIR="$SCRIPT_DIR/publish"

echo ""
echo "Compiling Launcher with dotnet..."
dotnet publish ./src/Launcher/Launcher.csproj -c Release --self-contained -r linux-x64 --property:PublishDir="$PUBLISH_DIR"

# Name, authors and app id come from src/Directory.Build.props.
prop() { dotnet msbuild ./src/Launcher/Launcher.csproj -getProperty:"$1"; }
TITLE="$(prop LauncherTitle)"
AUTHORS="$(prop LauncherAuthors)"
APP_ID="$(prop LauncherId)"

echo ""
echo "Building Velopack Release v$BUILD_VERSION"
vpk pack --packTitle "$TITLE" --packAuthors "$AUTHORS" -u "$APP_ID" -e Launcher -o "$RELEASE_DIR" -p "$PUBLISH_DIR" -i ./assets/icon.png -v $BUILD_VERSION