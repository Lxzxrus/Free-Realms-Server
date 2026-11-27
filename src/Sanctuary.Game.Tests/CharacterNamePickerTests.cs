using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.VisualStudio.TestTools.UnitTesting;

using Sanctuary.Game.Helpers;
using Sanctuary.Game.Resources;

namespace Sanctuary.Game.Tests;

/// <summary>
/// Checks every name the client's character name picker makes against the character name rules and the name filter
/// (<see cref="CharacterNameRules"/>): each first name, and each last name the picker joins from a prefix and a suffix.
/// Like <see cref="PetNamePickerTests"/>, it needs the game client, which isn't in the repository, so it runs only when
/// EVERGROVE_CLIENT names the client's folder, and checks EVERGROVE_NAME_FILTER or the repository's NameFilter.txt:
/// <code>
/// $env:EVERGROVE_CLIENT = "C:\Games\Free Realms"
/// dotnet test Sanctuary.Game.Tests -c Release --filter "FullyQualifiedName~NamePickerTests"
/// </code>
/// </summary>
[TestClass]
public sealed class CharacterNamePickerTests
{
    [TestMethod]
    public void EveryNameThePickerMakes_IsAllowed()
    {
        var client = Environment.GetEnvironmentVariable("EVERGROVE_CLIENT");

        if (string.IsNullOrEmpty(client))
            Assert.Inconclusive("Set EVERGROVE_CLIENT to the game client's folder to check the names the character name picker makes.");

        var nameFilterFile = Environment.GetEnvironmentVariable("EVERGROVE_NAME_FILTER") is { Length: > 0 } file
            ? file
            : PetNamePickerTests.FindShippedNameFilter();
        var nameFilter = new NameFilterCollection(NullLogger.Instance);

        Assert.IsTrue(nameFilter.Load(nameFilterFile), $"Can't read {nameFilterFile}.");

        var (firstNames, lastNames) = PickerNames(Path.Combine(client, "Resources", "CharacterNames.txt"));

        Assert.IsTrue(firstNames.Count > 100 && lastNames.Count > 1000, "The picker's lists weren't read.");

        // Each first name with no last name, and each last name after one first name: the rules check each part alone,
        // and the filter checks each word of the name alone.
        var refused = firstNames.Select(first => (First: first, Last: ""))
            .Concat(lastNames.Select(last => (First: "Abby", Last: last)))
            .Select(name => (Name: name, Problem: CharacterNameRules.Check(name.First, name.Last, nameFilter)))
            .Where(result => result.Problem != CharacterNameProblem.None)
            .ToList();

        var reasons = refused
            .GroupBy(result => result.Problem == CharacterNameProblem.Profane
                ? $"\"{NameFilterCollection.FindRefusedEntry(nameFilter, $"{result.Name.First} {result.Name.Last}")}\""
                : result.Problem.ToString())
            .Select(group => $"{group.Key}: {group.Count()}, such as "
                + string.Join(", ", group.Take(3).Select(result => $"\"{result.Name.First} {result.Name.Last}\"")));

        Assert.AreEqual(0, refused.Count, $"{refused.Count} names the character name picker makes are refused with "
            + $"{nameFilterFile}:{Environment.NewLine}{string.Join(Environment.NewLine, reasons)}");
    }

    // Resources/CharacterNames.txt holds "key^text" lines: male.first.name.N, female.first.name.N, and
    // last.name.prefix.N and last.name.suffix.N, which the picker joins with nothing between them.
    private static (List<string> FirstNames, List<string> LastNames) PickerNames(string characterNamesFile)
    {
        var entries = File.ReadAllLines(characterNamesFile)
            .Where(line => !line.StartsWith('#'))
            .Select(line => line.Split('^'))
            .Where(fields => fields.Length >= 2)
            .Select(fields => (Key: fields[0], Text: fields[1].Trim()))
            .ToList();

        List<string> Part(string key) => [.. entries.Where(entry => entry.Key.StartsWith(key, StringComparison.Ordinal)).Select(entry => entry.Text)];

        var firstNames = Part("male.first.name.").Concat(Part("female.first.name.")).Distinct().ToList();
        var lastNames = (from prefix in Part("last.name.prefix.")
                         from suffix in Part("last.name.suffix.")
                         select prefix + suffix).Distinct().ToList();

        return (firstNames, lastNames);
    }
}
