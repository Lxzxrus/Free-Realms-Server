# Launcher

The game launcher for our Free Realms server. It registers an account, logs in with the password the player
types, checks the game files and starts the game.

It is a fork of the **Open Source Free Realms launcher** (https://github.com/Open-Source-Free-Realms/Launcher,
imported at commit `e6a2674`), by the OSFR team and its contributors. Like the original, it is licensed under the
GNU AGPL v3: see [LICENSE](LICENSE).

## Release settings

| What | Value | Where to change it |
|---|---|---|
| Launcher name (window title, installer) | `Free Realms Evergrove` | `LauncherTitle` in [src/Directory.Build.props](src/Directory.Build.props) |
| Authors (installer) | `Evergrove team` | `LauncherAuthors`, same file |
| App id: the settings folder and the installer id. **Can't change once players have it installed** | `EvergroveLauncher` | `LauncherId`, same file |
| Server address | `https://play.evergrove.fyi` | `DefaultServerUrl`, same file |
| SHA-256 of `FreeRealms.exe`. **Empty: the launcher starts no game until it's set** | the official client's, `26cc1c52…` (must match `ClientPins.txt`) | `ClientExecutableSha256`, same file (see [the client pin](#the-client-pin)) |
| Where launcher updates come from | `https://github.com/Lxzxrus/Free-Realms-Server` | `UpdateRepositoryUrl`, same file. The release workflow sets it to the repository it runs in |
| Server name and description shown in the launcher | `Free Realms Evergrove` | [server/servermanifest.xml](server/servermanifest.xml), on the server |
| Where players download the game client | The official OSFR client, repaired against the pins (no `<ClientUrl>`) | `OfficialClientUrl` in `Helpers/Constants.cs` |

Any of the build properties can be overridden for one build, on the command line or as an environment variable:

```bash
dotnet publish src/Launcher/Launcher.csproj -c Release -r win-x64 --no-self-contained -p:DefaultServerUrl=http://192.168.1.20:8080
DefaultServerUrl=http://192.168.1.20:8080 ./build_linux-x64.sh 1.0.0
```

## How it finds the server and logs in

1. On first run it adds `DefaultServerUrl` and fetches `<DefaultServerUrl>/servermanifest.xml`, which names the
   WebAPI address, the Login server (`host:port`) and, optionally, where the game files are. Players can add other
   servers with **Add Server**, but the default is the only one it adds by itself.
2. It pings the Login server over UDP for its status. It refuses to start the game while the server says offline. The
   request is padded to the size of the reply (6 bytes), because the server doesn't answer a smaller one (see
   [docs/udp-limits.md](../docs/udp-limits.md#replies-to-forged-addresses)).
3. **Play** checks the game files against `clientmanifest.xml` and downloads what's missing. If the server doesn't
   publish one (a `404`), it uses whatever the player put in the client folder (the folder button opens it).
4. **Login** posts `{"username", "password"}` to `<WebApiUrl>/login` and starts the client with
   `Server=<Login server> SessionId=<session> Internationalization:Locale=<language>` plus WebAPI's
   `launchArguments`, passed through unchanged. **Register** posts to `<WebApiUrl>/register`. Both follow the
   contract in [docs/webapi.md](../docs/webapi.md): every status code has its own message, a `429` shows how long
   to wait from `Retry-After`, and nothing is ever retried automatically.

What the server must host for this is in [server/README.md](server/README.md).

## Security

- **Passwords are never stored.** The password is held in memory for one request and cleared. Only the username
  is remembered, and only if the player ticks "Remember Username". The session id isn't stored either: it goes
  straight to the game's command line and works once, within 5 minutes.
- **HTTPS outside the local network.** Every request the launcher makes to a game server (server manifest, login,
  register, game files) must be `https://`, unless the host is this machine or a private network address
  (`10/8`, `172.16/12`, `192.168/16`, link-local, IPv6 unique-local, or a `.local` name). Anything else is refused
  before it's sent: over plain HTTP, anyone on the path could read the password or swap `FreeRealms.exe`. Local
  plain HTTP shows a warning on the login and register forms. Updates are always HTTPS, to GitHub only.
- **Files stay in the client folder.** A server's file list can't write outside the client folder, and a server's
  name can't escape the servers folder.
- **Updates come from this repository's GitHub Releases, never from the game server**, so someone who breaks into
  the VPS can't push a launcher to players. The update address must be `https://github.com/{owner}/{repository}`;
  anything else turns updates off. Releases are built by a workflow that runs only on `main` and only when
  started by hand (see [Publishing a release](#publishing-a-release)).
- **The game's executable is pinned.** The launcher only starts a `FreeRealms.exe` whose SHA-256 matches the one
  it was built with, and never saves one from a server that doesn't match (see [the client pin](#the-client-pin)).

## Building

Needs the .NET 10 SDK.

```bash
cd launcher/src
dotnet build Launcher.slnx
dotnet test Launcher.slnx --no-build
dotnet publish Launcher/Launcher.csproj -c Release -r win-x64 --no-self-contained   # the Windows build
```

The `build_*.sh` and `build_*.bat` scripts make installers and update packages with
[Velopack](https://velopack.io) (`dotnet tool install -g vpk`), for example `./build_linux-x64.sh 1.0.0` or
`build_win-x64.bat 1.0.0`. They put the release in `releases/`.

Only installed copies update; a copy run from a plain `publish` folder doesn't.

## Publishing a release

Installed launchers update from this repository's GitHub Releases, through Velopack's GitHub source. They look
for a release when the player clicks the arrows button at the top of the window.

1. Merge everything the release needs into `main`, including the real `ClientExecutableSha256`.
2. On GitHub: **Actions** > **Launcher release** > **Run workflow**, leave the branch on `main`, and type the
   version, such as `1.0.0`. It must be higher than the last launcher release, or no launcher will update to it.
3. The workflow refuses to run from any other branch, refuses a version that isn't `x.y.z`, and refuses to build
   while `ClientExecutableSha256` is empty. It runs the launcher's tests, builds Windows (x64) and Linux (x64)
   with the `build_*` scripts, and publishes the GitHub release `launcher-v1.0.0` holding both.
4. Players download the installer from that release page: `EvergroveLauncher-win-Setup.exe` on Windows,
   `EvergroveLauncher.AppImage` on Linux (both named after `LauncherId`).

The launcher reads releases without a token, so **updates only work once the repository is public**. While it's
private, installed launchers can't see the releases: the update button finds nothing or reports an error, and a
new version has to be installed by hand from the release page or a local `build_*` script. Don't put a token in the launcher to get around
this: every player could read it.

The workflow doesn't build macOS or ARM; use the matching `build_*` script and attach its `releases/` files to the
same GitHub release by hand if that's ever needed. The installers aren't code-signed, so Windows SmartScreen warns
players the first time.

To do it all by hand instead: on `main`, set `UpdateRepositoryUrl`, run `build_win-x64.bat 1.0.0` on Windows and
`./build_linux-x64.sh 1.0.0` on Linux, then create a GitHub release tagged `launcher-v1.0.0` and attach every file
from both `releases/` folders.

## The client pin

`ClientExecutableSha256` in [src/Directory.Build.props](src/Directory.Build.props) is the SHA-256 of the game
client's `FreeRealms.exe`, the one every player already has. Get it from a copy you trust:

```bash
sha256sum FreeRealms.exe                    # Linux
Get-FileHash FreeRealms.exe                 # Windows PowerShell
```

Paste the 64 hex digits in (case doesn't matter). It isn't a secret. What the launcher does with it:

- **Play** and the launch itself both hash `FreeRealms.exe` in the server's client folder and refuse to start it if
  it doesn't match. The player sees: *"FreeRealms.exe isn't the Free Realms game this launcher was made for, so it
  won't be started. It may have been changed or replaced. Put the original FreeRealms.exe back in:"* and the
  folder.
- A server's `clientmanifest.xml` may download any other game file, but a `FreeRealms.exe` it sends (under any
  case, such as `freerealms.exe`) is hashed before it's written, and thrown away if it fails: *"The server sent a
  FreeRealms.exe that isn't the Free Realms game this launcher was made for. It wasn't saved, and the game won't
  be started."* A copy already on disk that passes is never replaced, whatever the manifest says.
- While the setting is empty, no game starts: *"This copy of the launcher was built without the game's
  fingerprint, so it can't check FreeRealms.exe and won't start the game."* For a local test build, pass it on the
  command line instead: `-p:ClientExecutableSha256=<hash>`.

## The whole client is pinned

Many players reuse a game folder that came from someone else's distribution, and `FreeRealms.exe` loads DLLs and
sound plugins from its own folder, so pinning the executable alone isn't enough. The launcher carries the SHA-256
and size of **every file of the official Open Source Free Realms client** ([src/Launcher/ClientPins.txt](src/Launcher/ClientPins.txt),
embedded in the launcher, 1,788 files).

What happens when a player presses **Play** (see `ClientVerifier`):

1. Every pinned file is checked (*"Checking game files... 120/1788"*). Code (`.exe`, `.dll`, the Miles plugins
   `.asi` and `.flt`, scripts) is hashed every time; the big data files only when their size or write time changed
   since they last passed. A full first check of the 852 MB client takes about 2 seconds, later ones about 0.1.
2. Code files that aren't part of the official client (a proxy `dinput8.dll`, an extra plugin) are moved to
   `<launcher data>/<server>/Quarantine/<date>/`, keeping their paths, and the player is told where. If one can't be
   moved, the game isn't started.
3. Missing or changed files are downloaded from the official client (`https://opensourcefreerealms.com/client/`,
   *"Downloading official game files..."*), written beside the target and moved into place only when they match their
   pin. If any can't be repaired, the game isn't started.
4. Only then is the server's own `clientmanifest.xml` used, and it can only **add data files**: it can't replace a
   pinned file, and it can't add code (*"The server offered program files for your game folder..."*).
5. Right before the game starts, `FreeRealms.exe` and every code file are hashed again.

Downloads send `Accept: */*`: without it, Cloudflare in front of the official download injects its analytics script
into HTML files (the client's `loading.html`), which then fail their pins.

**Returning players** can point the launcher at the game folder they already have (the **+** button beside the
folder button) instead of downloading 852 MB. That folder gets the same check and repair.

**Refreshing the list** when OSFR changes its client: `cd launcher/tools`, then
`dotnet run make-client-pins.cs -- --client <a client folder>`. Every file is first confirmed against OSFR's
`clientmanifest.xml` (size and XXHash64); local copies that match are reused, anything else is downloaded. Update
`ClientExecutableSha256` to the new `FreeRealms.exe` line (a test checks they agree).

## Changes from OSFR's launcher

- **Ours, not OSFR's:** our name, id, icon and default server, all set in `src/Directory.Build.props`. OSFR's
  tree logo is replaced by a plain icon. The settings folder is our own, so it never mixes with an OSFR install.
- **Updates from this repository's GitHub Releases** instead of OSFR's, which would have replaced our launcher
  with theirs, and only from an `https://github.com/` address. A release workflow publishes them from `main`.
- **The game's executable is pinned** to one SHA-256 (`ClientExecutableSha256`).
- **Discord removed:** the Discord Game SDK (27 MB of proprietary binaries) and the rich-presence integration,
  which used OSFR's own Discord application.
- **"Remember Password" removed.** OSFR kept passwords encrypted with ASP.NET Data Protection, whose keys sit
  unprotected next to the passwords on Linux and macOS.
- **WebAPI contract:** login and register go through `Helpers/WebApiClient.cs`, with a clear message for each
  answer: wrong username or password, banned, name taken, too many attempts (with the wait), validation errors
  from the server, server down, and not HTTPS. `launchArguments` are passed through unchanged, split on spaces, so
  the portrait upload token survives. The game is started without a shell, with each argument passed separately.
- **HTTPS rule** (`Helpers/TransportPolicy.cs`) instead of a warning.
- **Path checks** on the client manifest's file and folder names, and on server names.
- **No client manifest means "use the local client"** instead of an error, so a server can work without hosting
  the game files.
- **Optional `<ClientUrl>`** in the server manifest, to serve the game files from somewhere other than the server
  URL. Older manifests without it still work.
- **Settings folder fix:** if the local app-data folder doesn't exist yet (a fresh Linux account has no
  `~/.local/share`), OSFR's launcher wrote its settings and the whole client into the current directory.
- **Data apart from the install:** settings, servers, game folders and the log live in
  `%LocalAppData%\<LauncherId>Data` (`~/.local/share/...` on Linux), not in `%LocalAppData%\<LauncherId>`, which
  is Velopack's install folder. OSFR's launcher kept them in the install folder: the installer took a launcher
  that had only been run for an existing install, and uninstalling removes that folder, with every downloaded game in it. The
  first start moves an old folder's settings and servers over (`Helpers/DataFolder.cs`).
- **Font:** OSFR bundled Maiandra GD, a commercial Microsoft font that can't be redistributed. The launcher now
  uses Inter, which Avalonia ships.
- Error notifications stay 8 seconds instead of 3, and the missing `Text.Main.Info` text that info notifications
  need was added.
- Tests in `src/Launcher.Tests` for the WebAPI contract, the HTTPS rule, the launch arguments, the path checks, the
  update source and the client pin.
