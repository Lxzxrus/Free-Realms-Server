using System.Text.Json;

using Microsoft.VisualStudio.TestTools.UnitTesting;

using Sanctuary.Packet.Common;

namespace Sanctuary.Game.Tests;

/// <summary>
/// An item stat's value is a float or an int in the resource files. Writing one threw after every write, so no item
/// definition with a stat could be saved as JSON (audit triage, QW-24).
/// </summary>
[TestClass]
public sealed class StatConverterTests
{
    [TestMethod]
    [DataRow("1.5")]
    [DataRow("7")]
    public void StatValueSurvivesARoundTrip(string value)
    {
        var json = $$"""{"Value":{{value}},"Unknown":false}""";

        var stat = JsonSerializer.Deserialize<ClientItemStatDefinition>(json);
        Assert.IsNotNull(stat);

        var written = JsonSerializer.Serialize(stat);
        var reread = JsonSerializer.Deserialize<ClientItemStatDefinition>(written);

        Assert.IsNotNull(reread);
        Assert.AreEqual(stat.Value, reread.Value);
        StringAssert.Contains(written, $"\"Value\":{value}");
    }
}
