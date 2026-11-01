using Microsoft.VisualStudio.TestTools.UnitTesting;

using Sanctuary.Game.Helpers;

namespace Sanctuary.Game.Tests;

/// <summary>
/// A pet's name is shown to everyone near the pet, so it follows the character name rules: the same letters and the same
/// name filter, with the '.' and '-' of the client's pet name picker (<see cref="PetHelper.IsValidName"/>).
/// </summary>
[TestClass]
public sealed class PetNameTests
{
    private static readonly string[] NoFilter = [];

    // The client names a pet only with its name picker, which joins a title and a name from its Resources/PetNames.txt
    // (in German, French or Spanish, from its string table) with the template "[*pet_first*] [*pet_second*]". These are
    // the parts holding anything but letters (the blank title is one space, "Colonel " ends in one and the French
    // "Mlle.  " in two) and the longest of each. PetNamePickerTests checks every name the picker makes, given the client.
    private static readonly string[] PickerTitles =
        [" ", "Baron von", "Colonel ", "Dr.", "Guv'nor", "Lil'", "Lt.", "Mlle.  ", "Mr.", "Mrs.", "Ms.", "President"];
    private static readonly string[] PickerNames = ["Emo-tor", "June Bug", "She-Rex", "Magnificent"];

    [TestMethod]
    public void EveryNameThePetNamePickerMakes_IsAllowed()
    {
        foreach (var title in PickerTitles)
        {
            foreach (var name in PickerNames)
                Assert.IsTrue(PetHelper.IsValidName($"{title} {name}", NoFilter), $"\"{title} {name}\"");
        }
    }

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

    /// <summary>Character names allow U+0041 to U+024F, which holds symbols and invisible marks as well as letters.</summary>
    [TestMethod]
    public void SymbolsAndInvisibleMarksAmongTheLetters_AreRefused()
    {
        Assert.IsFalse(PetHelper.IsValidName("Re\u00ADx", NoFilter), "Soft hyphen.");
        Assert.IsFalse(PetHelper.IsValidName("Re\u00A0x", NoFilter), "No-break space.");
        Assert.IsFalse(PetHelper.IsValidName("Re_x", NoFilter));
        Assert.IsFalse(PetHelper.IsValidName("Re|x", NoFilter));
        Assert.IsFalse(PetHelper.IsValidName("Rex~", NoFilter));
        Assert.IsFalse(PetHelper.IsValidName("Rex\u00A9", NoFilter), "Copyright sign.");
        Assert.IsFalse(PetHelper.IsValidName("Re\u00D7x", NoFilter), "Multiplication sign.");
    }

    [TestMethod]
    public void ASoftHyphen_CantHideAWordFromTheNameFilter()
    {
        string[] filter = ["badword"];

        Assert.IsFalse(PetHelper.IsValidName("Bad\u00ADword", filter));
    }

    /// <summary>The picker's '.' only ends a title such as "Mr.", so it may only follow a letter and come before a space or the end.</summary>
    [TestMethod]
    public void ADot_OnlyEndsAWord()
    {
        Assert.IsTrue(PetHelper.IsValidName("Mr. Rex", NoFilter));
        Assert.IsTrue(PetHelper.IsValidName("Rex Jr.", NoFilter));

        Assert.IsFalse(PetHelper.IsValidName("freesc.com", NoFilter));
        Assert.IsFalse(PetHelper.IsValidName("Mr.Rex", NoFilter));
        Assert.IsFalse(PetHelper.IsValidName(".Rex", NoFilter));
        Assert.IsFalse(PetHelper.IsValidName("Mr . Rex", NoFilter));
        Assert.IsFalse(PetHelper.IsValidName("Mr.. Rex", NoFilter));
        Assert.IsFalse(PetHelper.IsValidName("Lil'. Rex", NoFilter));
    }

    /// <summary>The picker's '-' only joins two words, as in "She-Rex", so it must stand between two letters.</summary>
    [TestMethod]
    public void AHyphen_OnlyJoinsTwoWords()
    {
        Assert.IsTrue(PetHelper.IsValidName("She-Rex", NoFilter));
        Assert.IsTrue(PetHelper.IsValidName("Emo-tor", NoFilter));

        Assert.IsFalse(PetHelper.IsValidName("kaines-realm.net", NoFilter));
        Assert.IsFalse(PetHelper.IsValidName("-Rex", NoFilter));
        Assert.IsFalse(PetHelper.IsValidName("Rex-", NoFilter));
        Assert.IsFalse(PetHelper.IsValidName("She--Rex", NoFilter));
        Assert.IsFalse(PetHelper.IsValidName("She -Rex", NoFilter));
        Assert.IsFalse(PetHelper.IsValidName("She- Rex", NoFilter));
    }

    [TestMethod]
    public void ANameWithoutALetter_IsRefused()
    {
        Assert.IsFalse(PetHelper.IsValidName(".", NoFilter));
        Assert.IsFalse(PetHelper.IsValidName("----", NoFilter));
        Assert.IsFalse(PetHelper.IsValidName("'''", NoFilter));
        Assert.IsFalse(PetHelper.IsValidName("' '", NoFilter));
    }
}
