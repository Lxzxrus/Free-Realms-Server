# Launcher

The game launcher for our Free Realms server. It registers an account, logs in with the password the player
types, checks the game files and starts the game.

It is a fork of the **Open Source Free Realms launcher** (https://github.com/Open-Source-Free-Realms/Launcher,
imported at commit `e6a2674`), by the OSFR team and its contributors. Like the original, it is licensed under the
GNU AGPL v3: see [LICENSE](LICENSE).

## Placeholders to decide before release

| What | Placeholder | Where to change it |
|---|---|---|
| Launcher name (window title, installer) | `ServerName Launcher` | `LauncherTitle` in [src/Directory.Build.props](src/Directory.Build.props) |
| Authors (installer) | `ServerName team` | `LauncherAuthors`, same file |
| App id: the settings folder and the installer id. **Can't change once players have it installed** | `ServerNameLauncher` | `LauncherId`, same file |
| Server address | `https://play.example.com` | `DefaultServerUrl`, same file |
| Server name and description shown in the launcher | `ServerName` | [server/servermanifest.xml](server/servermanifest.xml), on the server |
| Where players download the game client | `https://files.example.com` | `<ClientUrl>` in [server/servermanifest.xml](server/servermanifest.xml) |

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
- **HTTPS outside the local network.** Every HTTP request the launcher makes (server manifest, login, register,
  game files, launcher updates) must be `https://`, unless the host is this machine or a private network address
  (`10/8`, `172.16/12`, `192.168/16`, link-local, IPv6 unique-local, or a `.local` name). Anything else is refused
  before it's sent: over plain HTTP, anyone on the path could read the password or swap `FreeRealms.exe`. Local
  plain HTTP shows a warning on the login and register forms.
- **Files stay in the client folder.** A server's file list can't write outside the client folder, and a server's
  name can't escape the servers folder.
- **Updates come from our server only**, over HTTPS (see below).

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

**Updates.** The launcher looks for updates under `<DefaultServerUrl>/launcher/`. To publish one, upload the
contents of `releases/` there (the Caddy example in [server/Caddyfile](server/Caddyfile) serves that folder). Only
installed copies update; a copy run from a plain `publish` folder doesn't.

## Changes from OSFR's launcher

- **Ours, not OSFR's:** our name, id, icon and default server, all set in `src/Directory.Build.props`. OSFR's
  tree logo is replaced by a plain icon. The settings folder is our own, so it never mixes with an OSFR install.
- **Updates from our server** (Velopack `SimpleWebSource` on `<DefaultServerUrl>/launcher/`) instead of OSFR's
  GitHub releases, which would have replaced our launcher with theirs.
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
- **Font:** OSFR bundled Maiandra GD, a commercial Microsoft font that can't be redistributed. The launcher now
  uses Inter, which Avalonia ships.
- Error notifications stay 8 seconds instead of 3, and the missing `Text.Main.Info` text that info notifications
  need was added.
- Tests in `src/Launcher.Tests` for the WebAPI contract, the HTTPS rule, the launch arguments and the path checks.
