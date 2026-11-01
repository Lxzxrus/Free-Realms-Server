using Microsoft.VisualStudio.TestTools.UnitTesting;

using Sanctuary.Game.Helpers;

namespace Sanctuary.Game.Tests;

/// <summary>
/// A pet's name is shown to everyone near the pet, so it follows the character name rules: the same letters and the same
/// name filter (<see cref="PetHelper.IsValidName"/>).
/// </summary>
[TestClass]
public sealed class PetNameTests
{
    private static readonly string[] NoFilter = [];

    [TestMethod]
    public void OrdinaryNames_AreAllowed()
    {
        Assert.IsTrue(PetHelper.IsValidName("Rex", NoFilter));
        Assert.IsTrue(PetHelper.IsValidName("Sir Fluffington", NoFilter));
        Assert.IsTrue(PetHelper.IsValidName("O'Malley", NoFilter));
        Assert.IsTrue(PetHelper.IsValidName("Zoë", NoFilter), "Accented Latin letters, as in character names.");
    }

    [TestMethod]
    public void ShortNames_AreAllowed_AsThePetWindowTakesThem()
    {
        Assert.IsTrue(PetHelper.IsValidName("Bo", NoFilter));
    }

    [TestMethod]
    public void BlankAndLongNames_AreRefused()
    {
        Assert.IsFalse(PetHelper.IsValidName(string.Empty, NoFilter));
        Assert.IsFalse(PetHelper.IsValidName("   ", NoFilter));
        Assert.IsTrue(PetHelper.IsValidName(new string('a', PetHelper.NameMaxLength), NoFilter));
        Assert.IsFalse(PetHelper.IsValidName(new string('a', PetHelper.NameMaxLength + 1), NoFilter));
    }

    [TestMethod]
    public void AWordFromTheNameFilter_IsRefused_InAnyCase()
    {
        string[] filter = ["badword"];

        Assert.IsFalse(PetHelper.IsValidName("BadWord", filter));
        Assert.IsFalse(PetHelper.IsValidName("Mister BADWORDy", filter));
        Assert.IsTrue(PetHelper.IsValidName("Goodword", filter));
    }

    [TestMethod]
    public void CharactersACharacterNameCantHave_AreRefused()
    {
        Assert.IsFalse(PetHelper.IsValidName("Rex2", NoFilter));
        Assert.IsFalse(PetHelper.IsValidName("Rex!", NoFilter));
        Assert.IsFalse(PetHelper.IsValidName("Rex<3", NoFilter));
    }

    [TestMethod]
    public void InvisibleAndControlCharacters_AreRefused()
    {
        Assert.IsFalse(PetHelper.IsValidName("Re​x", NoFilter), "Zero-width space.");
        Assert.IsFalse(PetHelper.IsValidName("‮xer", NoFilter), "Right-to-left override.");
        Assert.IsFalse(PetHelper.IsValidName("Ré́́x", NoFilter), "Stacked combining marks.");
        Assert.IsFalse(PetHelper.IsValidName("Re\u0085x", NoFilter), "A control character inside the letters' range.");
        Assert.IsFalse(PetHelper.IsValidName("Re\tx", NoFilter));
    }
}
