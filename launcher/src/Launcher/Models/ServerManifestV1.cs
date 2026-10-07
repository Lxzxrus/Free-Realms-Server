using System.Xml.Serialization;

using Launcher.Helpers;

namespace Launcher.Models;

[XmlRoot("ServerManifest")]
public sealed class ServerManifestV1
{
    public const int ManifestVersion = 1;

    public const string FileName = $"servermanifest.xml";
    public const string SchemaName = $"{nameof(ServerManifestV1)}.xsd";

    [XmlAttribute("version")]
    public int Version { get; set; }

    public required string Name { get; set; }
    public required string Description { get; set; }

    public required string LoginApiUrl { get; set; }
    public required string RegisterUrl { get; set; }
    public required string LoginServer { get; set; }

    public static ServerManifest ToServerManifest(ServerManifestV1 v1) => new()
    {
        Version = v1.Version,
        Name = v1.Name,
        Description = v1.Description,
        WebApiUrl = UriHelper.StripLeaf(v1.LoginApiUrl),
        LoginServer = v1.LoginServer
    };
}