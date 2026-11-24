using System;
using System.IO;

using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.VisualStudio.TestTools.UnitTesting;

using Sanctuary.Game.Helpers;
using Sanctuary.Game.Resources;

namespace Sanctuary.Game.Tests;

/// <summary>
/// The name filter's three kinds of entries (<see cref="NameFilterCollection"/>), the shipped list, and the character
/// name rules that creation, the name check and renames share (<see cref="CharacterNameRules"/>).
/// </summary>
[TestClass]
public sealed class NameFilterTests
{
    private static readonly string[] Filter = ["badword", "=ass", "rape|grape|scrape"];

    [TestMethod]
    [DataRow("Badword")]
    [DataRow("Mister Badwordy", DisplayName = "inside a word")]
    [DataRow("BÄDWORD", DisplayName = "accents and case don't hide it")]
    [DataRow("Bad'word", DisplayName = "nor apostrophes")]
    [DataRow("Ass")]
    [DataRow("The Ass Club", DisplayName = "a whole word among others")]
    [DataRow("Rapeking")]
    [DataRow("Grape Rapeking", DisplayName = "a cover elsewhere in the name doesn't excuse it")]
    [DataRow("Grapeape Rape", DisplayName = "nor in the same word")]
    public void Refused(string name) => Assert.IsTrue(NameFilterCollection.Refuses(Filter, name), name);

    [TestMethod]
    [DataRow("Cassandra", DisplayName = "a whole-word entry inside a word")]
    [DataRow("Bass")]
    [DataRow("Grapejuice", DisplayName = "inside a cover word")]
    [DataRow("Scraper")]
    [DataRow("Bad Word", DisplayName = "split across words")]
    public void Allowed(string name) => Assert.IsFalse(NameFilterCollection.Refuses(Filter, name), name);

    [TestMethod]
    public void TheRefusingEntryIsNamed()
    {
        Assert.AreEqual("rape|grape|scrape", NameFilterCollection.FindRefusedEntry(Filter, "Rapeking"));
        Assert.IsNull(NameFilterCollection.FindRefusedEntry(Filter, "Grapejuice"));
        Assert.IsNull(NameFilterCollection.FindRefusedEntry([], "Badword"), "an empty filter refuses nothing");
    }

    [TestMethod]
    [DataRow("Cassandra")]
    [DataRow("Peacock")]
    [DataRow("Grapejuice")]
    [DataRow("Spicy Butterbutton")]
    [DataRow("Analyst")]
    [DataRow("Basement")]
    [DataRow("Scatterbrain")]
    [DataRow("Sparkle Moonbeam")]
    public void TheShippedListAllowsInnocentNames(string name) => Assert.IsFalse(NameFilterCollection.Refuses(Shipped(), name), name);

    [TestMethod]
    [DataRow("Ass")]
    [DataRow("Rapeking")]
    [DataRow("Pornking")]
    [DataRow("Buttface")]
    [DataRow("Cockroach", DisplayName = "no cover for this one: a cost of covers being few")]
    public void TheShippedListRefusesThese(string name) => Assert.IsTrue(NameFilterCollection.Refuses(Shipped(), name), name);

    [TestMethod]
    public void TheShippedListSkipsItsComments()
    {
        var shipped = Shipped();

        Assert.IsTrue(shipped.Count > 300);
        Assert.IsFalse(shipped.Exists(line => line.StartsWith('#')));
        Assert.IsFalse(NameFilterCollection.Refuses(shipped, "Words refused in names players choose"), "the header isn't an entry");
    }

    // ---- character name rules ----

    [TestMethod]
    [DataRow("Abby", "", CharacterNameProblem.None)]
    [DataRow("Abby", null, CharacterNameProblem.None)]
    [DataRow("O'Malley", "Thunderpaw", CharacterNameProblem.None)]
    [DataRow("Zoë", "Brightwater", CharacterNameProblem.None)]
    [DataRow("", "", CharacterNameProblem.Invalid)]
    [DataRow("Abby", "   ", CharacterNameProblem.Invalid)]
    [DataRow("Al", "", CharacterNameProblem.FirstNameTooShort)]
    [DataRow("Abcdefghijklmno", "", CharacterNameProblem.FirstNameTooLong)]
    [DataRow("Abby", "Ox", CharacterNameProblem.LastNameTooShort)]
    [DataRow("Abby", "Imperialantlers", CharacterNameProblem.None, DisplayName = "15 letters, as the picker makes")]
    [DataRow("Abby", "Abcdefghijklmnopq", CharacterNameProblem.LastNameTooLong)]
    [DataRow("Ab[]y", "", CharacterNameProblem.IllegalCharacters)]
    [DataRow("Abby", "Two Words", CharacterNameProblem.IllegalCharacters)]
    [DataRow("'Abby", "", CharacterNameProblem.IllegalCharacters)]
    [DataRow("Abby9", "", CharacterNameProblem.IllegalCharacters)]
    [DataRow("Abby", "Badwordson", CharacterNameProblem.Profane)]
    public void CharacterNames(string first, string? last, CharacterNameProblem expected) =>
        Assert.AreEqual(expected, CharacterNameRules.Check(first, last, Filter));

    // ---- digits, symbols and look-alike letters (the review of 24 November) ----

    [TestMethod]
    [DataRow("Ass69", DisplayName = "digits end a word, so the whole word is still there")]
    [DataRow("Badw0rd", DisplayName = "a digit for a letter")]
    [DataRow("B@dword", DisplayName = "a symbol for a letter")]
    [DataRow("Вadword", DisplayName = "a Cyrillic capital that looks Latin")]
    [DataRow("Bаdword", DisplayName = "a Cyrillic small letter that looks Latin")]
    [DataRow("Ｂadword", DisplayName = "a full-width letter")]
    [DataRow("Bαdword", DisplayName = "a Greek small letter that looks Latin")]
    public void LookAlikesAreReadAsTheLettersTheyLookLike(string name) =>
        Assert.IsTrue(NameFilterCollection.Refuses(Filter, name), name);

    [TestMethod]
    [DataRow("Class2", DisplayName = "a whole-word entry inside a word, beside a digit")]
    [DataRow("Area51")]
    [DataRow("Grape4Life", DisplayName = "a cover word beside a digit")]
    public void DigitsDoNotMakeNewWords(string name) =>
        Assert.IsFalse(NameFilterCollection.Refuses(Filter, name), name);

    [TestMethod]
    [DataRow("Shıt", DisplayName = "a dotless i")]
    [DataRow("Ѕhit", DisplayName = "a Cyrillic S")]
    [DataRow("Sh1t")]
    [DataRow("B1tch")]
    [DataRow("Ass2")]
    [DataRow("Mongoloid", DisplayName = "no longer let through by mong's cover, mongo")]
    [DataRow("Hitlerfan")]
    [DataRow("Kkkclub")]
    public void TheShippedListRefusesTheseToo(string name) => Assert.IsTrue(NameFilterCollection.Refuses(Shipped(), name), name);

    [TestMethod]
    [DataRow("Bjørn")]
    [DataRow("Łukasz")]
    [DataRow("Team42")]
    [DataRow("Fire Retardant Crew")]
    public void TheShippedListAllowsTheseToo(string name) => Assert.IsFalse(NameFilterCollection.Refuses(Shipped(), name), name);

    [TestMethod]
    [DataRow("Admin", "", CharacterNameProblem.Reserved)]
    [DataRow("Âdmin", "", CharacterNameProblem.Reserved, DisplayName = "an accent doesn't hide it")]
    [DataRow("Abby", "Moderator", CharacterNameProblem.Reserved)]
    [DataRow("Evergrove", "", CharacterNameProblem.Reserved)]
    [DataRow("Abby", "Staffordshire", CharacterNameProblem.None, DisplayName = "only a whole part is reserved")]
    [DataRow("Abe", "Lee", CharacterNameProblem.None, DisplayName = "three letters each")]
    [DataRow("Abcdefghijklmn", "", CharacterNameProblem.None, DisplayName = "a 14-letter first name")]
    [DataRow("Abby", "Abcdefghijklmnop", CharacterNameProblem.None, DisplayName = "a 16-letter last name")]
    [DataRow("Bjørn", "", CharacterNameProblem.None)]
    [DataRow("Aƀby", "", CharacterNameProblem.IllegalCharacters, DisplayName = "a Latin letter that folds to nothing plain")]
    [DataRow("Аbby", "", CharacterNameProblem.IllegalCharacters, DisplayName = "a Cyrillic A")]
    [DataRow("Shıt", "", CharacterNameProblem.Profane, DisplayName = "an allowed letter, still read by the filter")]
    public void CharacterNamesToo(string first, string? last, CharacterNameProblem expected) =>
        Assert.AreEqual(expected, CharacterNameRules.Check(first, last, Shipped()));

    [TestMethod]
    [DataRow("The Admin Guild", true)]
    [DataRow("Official Builders", true)]
    [DataRow("GM Team", true)]
    [DataRow("Evergrove Builders", false, DisplayName = "the server's name is fine in a guild's")]
    [DataRow("Staffordshire Bulls", false)]
    public void GuildNamesMayNotUseStaffWords(string name, bool refused) =>
        Assert.AreEqual(refused, CharacterNameRules.UsesAny(name, CharacterNameRules.StaffWords), name);

    [TestMethod]
    public void AReloadOfAnEmptyFileKeepsTheList()
    {
        var file = Path.Combine(Path.GetTempPath(), $"namefilter-{Guid.NewGuid():N}.txt");

        try
        {
            File.WriteAllLines(file, ["badword"]);

            var filter = new NameFilterCollection(NullLogger.Instance);
            Assert.IsTrue(filter.Load(file));
            Assert.IsTrue(NameFilterCollection.Refuses(filter, "Badword"));

            // a deploy or an edit caught half way: no entries, for a moment
            File.WriteAllLines(file, ["# Words refused in names"]);
            Assert.IsTrue(filter.Load(file));
            Assert.IsTrue(NameFilterCollection.Refuses(filter, "Badword"), "the list it had is kept");
        }
        finally
        {
            File.Delete(file);
        }
    }

    private static NameFilterCollection Shipped()
    {
        var nameFilter = new NameFilterCollection(NullLogger.Instance);

        Assert.IsTrue(nameFilter.Load(PetNamePickerTests.FindShippedNameFilter()));

        return nameFilter;
    }
}
