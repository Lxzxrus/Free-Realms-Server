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

    private static NameFilterCollection Shipped()
    {
        var nameFilter = new NameFilterCollection(NullLogger.Instance);

        Assert.IsTrue(nameFilter.Load(PetNamePickerTests.FindShippedNameFilter()));

        return nameFilter;
    }
}
