# What the server hosts for the launcher

The launcher needs these from the server side. None of them is a change to the server code; they're
files and web server configuration.

| URL | What | Required |
|---|---|---|
| `<server URL>/servermanifest.xml` | Name, description, WebAPI address, Login server address | Yes |
| `<WebApiUrl>/login`, `/register` | WebAPI, normally behind Caddy (see `docs/webapi.md`) | Yes |
| UDP `<Login server>` (port 20042) | Status ping and login | Yes |
| `<ClientUrl or server URL>/clientmanifest.xml` and `/client/...` | The game files, so the launcher can download and check them | No: without it players supply their own client |

Launcher updates are not hosted here. They come from the repository's GitHub Releases (see
[Publishing a release](../README.md#publishing-a-release)), so a break-in on the VPS can't push a launcher to
players. If an older setup still serves `/srv/launcher/` releases under `/launcher/`, remove that.

## On the VPS

1. Follow **HTTPS on the VPS with Caddy** in `docs/webapi.md`.
2. Copy [servermanifest.xml](servermanifest.xml) to `/srv/launcher/`, and fill in the real name, domain and
   description. Remove `<ClientUrl>` if the game files aren't hosted anywhere yet.
3. Use [Caddyfile](Caddyfile) instead of the one in `docs/webapi.md`: it's the same, plus `servermanifest.xml`.
   Then `systemctl reload caddy`.
4. Check from any machine: `curl https://play.evergrove.fyi/servermanifest.xml` shows the file, and
   `curl -X POST https://play.evergrove.fyi/login -H 'content-type: application/json' -d '{"username":"x","password":"y"}'`
   answers `401`.

## Testing on the local network (the Optiplex)

Plain HTTP is allowed to private addresses, so no certificate is needed. Say the server's address is
`192.168.1.20`:

1. Let WebAPI listen on the network, and hand out a portrait address the other PC can reach:
   `Urls=http://0.0.0.0:20040` and `WebAPI__PortraitUploadUrl=http://192.168.1.20:20040/image` (environment
   variables, or `appsettings.local.json`). Before task 15 is merged, the portrait address is the
   `Portrait:UploadUrl=` part of `WebAPI:LaunchArguments` instead.
2. Make a folder with a `servermanifest.xml` like this, and serve it with `python3 -m http.server 8080` from
   that folder:

   ```xml
   <?xml version="1.0" encoding="utf-8"?>
   <ServerManifest version="2">
     <Name>Staging</Name>
     <Description>The Optiplex.</Description>
     <WebApiUrl>http://192.168.1.20:20040/</WebApiUrl>
     <LoginServer>192.168.1.20:20042</LoginServer>
   </ServerManifest>
   ```
3. In the launcher, **Add Server** `http://192.168.1.20:8080`, or build it with that as the default:
   `-p:DefaultServerUrl=http://192.168.1.20:8080`.
4. Open the server's client folder with the folder button and copy the game client into it (the folder with
   `FreeRealms.exe`), since there's no `clientmanifest.xml` to download from. The launcher only starts a
   `FreeRealms.exe` whose SHA-256 matches `ClientExecutableSha256` (see
   [the client pin](../README.md#the-client-pin)).
5. Open UDP 20042 and 20260 and TCP 20040 and 8080 in the Optiplex's firewall, for the local network only.

## Hosting the game files

`clientmanifest.xml` lists every file of the client with its size and its XXH64 hash:

```xml
<ClientManifest version="1" languages="en_US">
  <Folder>
    <File name="FreeRealms.exe" size="1234" hash="1234567890" />
    <Folder name="Resources">
      <File name="Assets_000.pack" size="5678" hash="9876543210" />
    </Folder>
  </Folder>
</ClientManifest>
```

Files are fetched from `<base>/client/<folder>/<name>`. The launcher never saves a `FreeRealms.exe` from the server
that fails [the client pin](../README.md#the-client-pin), whatever hash the manifest lists. No tool in this repo generates the manifest yet. Two things
to decide before hosting the client: the bandwidth (every new player downloads the whole client), and the fact
that the client belongs to Daybreak.
