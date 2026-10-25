using Microsoft.VisualStudio.TestTools.UnitTesting;

using Sanctuary.Game.Housing;

namespace Sanctuary.Game.Tests;

[TestClass]
public sealed class HouseNamesTests
{
    [TestMethod]
    public void Normalize_TrimAndCollapseSpaces()
    {
        Assert.AreEqual("The Cozy Cottage", HouseNames.Normalize("  The   Cozy  Cottage  "));
        Assert.AreEqual(string.Empty, HouseNames.Normalize(null));
    }

    [TestMethod]
    public void Validate_ValidNames()
    {
        string[] filter = [];
        Assert.AreEqual(HouseNameResult.Valid, HouseNames.Validate("The Cozy Cottage", filter));
        Assert.AreEqual(HouseNameResult.Valid, HouseNames.Validate("Nate's Treehouse!", filter));
    }

    [TestMethod]
    public void Validate_TooShort()
    {
        string[] filter = [];
        Assert.AreEqual(HouseNameResult.TooShort, HouseNames.Validate("ab", filter));
    }

    [TestMethod]
    public void Validate_BoundaryLengths()
    {
        string[] filter = [];
        string minName = new('a', HouseNames.MinLength);
        string maxName = new('a', HouseNames.MaxLength);
        string tooLongName = new('a', HouseNames.MaxLength + 1);

        Assert.AreEqual(HouseNameResult.Valid, HouseNames.Validate(minName, filter));
        Assert.AreEqual(HouseNameResult.Valid, HouseNames.Validate(maxName, filter));
        Assert.AreEqual(HouseNameResult.TooLong, HouseNames.Validate(tooLongName, filter));
    }

    [TestMethod]
    public void Validate_IllegalCharacters()
    {
        string[] filter = [];
        Assert.AreEqual(HouseNameResult.IllegalCharacters, HouseNames.Validate("Home<3", filter));
        Assert.AreEqual(HouseNameResult.IllegalCharacters, HouseNames.Validate("a_b_c", filter));
    }

    [TestMethod]
    public void Validate_Filtered()
    {
        string[] filter = ["badword"];
        Assert.AreEqual(HouseNameResult.Filtered, HouseNames.Validate("My BADWORD Place", filter));
    }

    [TestMethod]
    public void Describe_ReturnsEmptyForValid()
    {
        Assert.AreEqual(string.Empty, HouseNames.Describe(HouseNameResult.Valid));
    }

    [TestMethod]
    public void Describe_ReturnsNonEmptyForRefusals()
    {
        Assert.IsTrue(HouseNames.Describe(HouseNameResult.TooShort).Length > 0);
        Assert.IsTrue(HouseNames.Describe(HouseNameResult.TooLong).Length > 0);
        Assert.IsTrue(HouseNames.Describe(HouseNameResult.IllegalCharacters).Length > 0);
        Assert.IsTrue(HouseNames.Describe(HouseNameResult.Filtered).Length > 0);
    }
}
