#:package HashDepot@3.2.0

// Makes launcher/src/Launcher/ClientPins.txt: the SHA-256 and size of every file in the official OSFR game
// client, which the launcher checks the player's client against (see ClientVerifier).
//
//   dotnet run make-client-pins.cs -- [--client <folder>] [--manifest <url or file>] [--out <file>]
//
// Each file is first confirmed against the official client manifest (size and XXHash64): a copy in --client is used
// when it matches, and anything else is downloaded from the official client download. Only files that match the
// manifest are pinned.

using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Xml.Linq;

using HashDepot;

const string OfficialManifestUrl = "https://opensourcefreerealms.com/clientmanifest.xml";
const string OfficialClientUrl = "https://opensourcefreerealms.com/client/";

string manifestSource = OfficialManifestUrl;
string? clientDirectory = null;
string output = Path.Combine("..", "src", "Launcher", "ClientPins.txt");

for (var i = 0; i < args.Length; i++)
{
    switch (args[i])
    {
        case "--manifest": manifestSource = args[++i]; break;
        case "--client": clientDirectory = args[++i]; break;
        case "--out": output = args[++i]; break;
        default: Console.Error.WriteLine($"Unknown argument {args[i]}"); return 2;
    }
}

using var http = new HttpClient { Timeout = TimeSpan.FromMinutes(10) };
http.DefaultRequestHeaders.UserAgent.ParseAdd("Evergrove-client-pins/1.0");
// Without it Cloudflare, in front of the official download, injects its analytics script into HTML files.
http.DefaultRequestHeaders.Accept.ParseAdd("*/*");

var manifestBytes = manifestSource.StartsWith("https://", StringComparison.OrdinalIgnoreCase)
    ? await http.GetByteArrayAsync(manifestSource)
    : await File.ReadAllBytesAsync(manifestSource);

var entries = new List<(string Path, long Size, ulong XxHash)>();
void Visit(XElement folder, string parent)
{
    foreach (var element in folder.Elements())
    {
        var name = (string?)element.Attribute("name") ?? string.Empty;
        var path = parent.Length == 0 ? name : $"{parent}/{name}";
        if (element.Name == "Folder")
            Visit(element, path);
        else if (element.Name == "File")
        {
            if (path.Split('/').Any(part => part is "" or "." or ".."))
                throw new InvalidDataException($"Unsafe path in the manifest: {path}");
            entries.Add((path, (long)element.Attribute("size")!, ulong.Parse((string)element.Attribute("hash")!)));
        }
    }
}

using (var stream = new MemoryStream(manifestBytes))
    Visit(XDocument.Load(stream).Root!.Element("Folder")!, string.Empty);

var lines = new List<string>();
int reused = 0, downloaded = 0;
foreach (var (path, size, xxHash) in entries.OrderBy(entry => entry.Path, StringComparer.Ordinal))
{
    byte[]? content = null;
    if (clientDirectory is not null)
    {
        var local = Path.Combine(clientDirectory, path.Replace('/', Path.DirectorySeparatorChar));
        if (File.Exists(local) && new FileInfo(local).Length == size)
        {
            var bytes = await File.ReadAllBytesAsync(local);
            if (XXHash.Hash64(bytes) == xxHash)
            {
                content = bytes;
                reused++;
            }
        }
    }

    if (content is null)
    {
        var url = OfficialClientUrl + string.Join('/', path.Split('/').Select(Uri.EscapeDataString));
        content = await http.GetByteArrayAsync(url);
        if (content.LongLength != size || XXHash.Hash64(content) != xxHash)
            throw new InvalidDataException($"{path} from {url} doesn't match the manifest");
        downloaded++;
    }

    lines.Add($"{Convert.ToHexStringLower(SHA256.HashData(content))} {size} {path}");
}

var header = new StringBuilder()
    .AppendLine("# Every file of the official Open Source Free Realms game client: SHA-256, size in bytes, path.")
    .AppendLine("# Made by launcher/tools/make-client-pins.cs; don't edit by hand.")
    .AppendLine($"# Manifest: {manifestSource} (SHA-256 {Convert.ToHexStringLower(SHA256.HashData(manifestBytes))})");

Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(output))!);
await File.WriteAllTextAsync(output, header + string.Join('\n', lines) + "\n");
Console.WriteLine($"Pinned {lines.Count} files ({reused} from the local client, {downloaded} downloaded) to {output}");
return 0;
