using System;
using System.Collections.Generic;
using System.Linq;
using System.Xml.Serialization;

namespace Launcher.Models;

public sealed class ClientManifest
{
    public const int ManifestVersion = 1;

    public const string FileName = $"{nameof(ClientManifest)}.xml";
    public const string SchemaName = $"{nameof(ClientManifest)}.xsd";

    [XmlAttribute("version")]
    public int Version { get; set; }

    [XmlAttribute("languages")]
    public required string LanguagesString { get; set; }
    public IEnumerable<LocaleType> Languages
    {
        get
        {
            foreach (var language in LanguagesString.Split(',', StringSplitOptions.RemoveEmptyEntries))
            {
                if (Enum.TryParse<LocaleType>(language, true, out var locale))
                {
                    yield return locale;
                }
            }
        }
    }

    [XmlElement("Folder")]
    public required ClientFolder RootFolder { get; set; }
}
