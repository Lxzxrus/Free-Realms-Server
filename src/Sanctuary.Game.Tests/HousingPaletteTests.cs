using Microsoft.VisualStudio.TestTools.UnitTesting;

using Sanctuary.Game.Housing;

namespace Sanctuary.Game.Tests;

[TestClass]
public sealed class HousingPaletteTests
{
    private const int Rubyburst = 227;
    private const int Darkmatter = 264;

    [TestMethod]
    public void CommandIds_RoundTrip()
    {
        foreach (var palette in new[]
                 {
                     HousingPalette.None,
                     new HousingPalette(Rubyburst, false),
                     new HousingPalette(Darkmatter, true),
                     new HousingPalette(0, true)
                 })
        {
            Assert.IsTrue(HousingPalette.TryParseCommand(palette.CommandId, out var parsed));
            Assert.AreEqual(palette, parsed);
        }
    }

    [TestMethod]
    public void CommandIds_MatchTheClientMod()
    {
        // tools/client-mods/housing-search/evg_colours.as builds these numbers itself.
        Assert.AreEqual(1056964608, HousingPalette.None.CommandId);
        Assert.AreEqual(1056964608 + 512 + Rubyburst, new HousingPalette(Rubyburst, true).CommandId);
    }

    [TestMethod]
    public void OnlyTheGamesDyesAreColours()
    {
        Assert.IsFalse(HousingPalette.TryParseCommand(HousingPalette.CommandIdBase + 100, out _), "an eye tint");
        Assert.IsFalse(HousingPalette.TryParseCommand(HousingPalette.CommandIdBase + 511, out _));
    }

    [TestMethod]
    public void CommandIds_StayClearOfItemsAndCatalogEntries()
    {
        Assert.IsFalse(HousingPalette.TryParseCommand(1, out _), "an inventory record id");
        Assert.IsFalse(HousingPalette.TryParseCommand(HousingPalette.CommandIdBase - 1, out _));
        Assert.IsFalse(HousingPalette.TryParseCommand(HousingPalette.CommandIdBase + 1024, out _));
        Assert.IsFalse(HousingPalette.TryParseCommand(HousingCreativeCatalog.RecordIdBase, out _));
        Assert.IsFalse(HousingPalette.TryParseCommand(-1, out _));

        Assert.IsTrue(new HousingPalette(Darkmatter, true).CommandId < HousingCreativeCatalog.RecordIdBase);
        Assert.IsFalse(HousingCreativeCatalog.IsCreativeRecordId(new HousingPalette(Darkmatter, true).CommandId));
    }

    [TestMethod]
    public void ColourNames_ReadAsTheGameWritesThem()
    {
        Assert.AreEqual("Rubyburst", new HousingPalette(Rubyburst, false).ColourName);
        Assert.AreEqual("each part's own colour", HousingPalette.None.ColourName);
    }
}
