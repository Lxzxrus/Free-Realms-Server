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
/// Checks every name the client's pet name picker makes against the pet name rules and a name filter
/// (<see cref="PetHelper.IsValidName"/>), so a NameFilter.txt can be checked before it is deployed. The filter refuses a
/// name that holds one of its words anywhere, and the picker's names include Scrappy, Spike and Muffin, so a word such
/// as "crap" refuses every title paired with one. The picker's parts are in the game client, which isn't in the
/// repository, so the check runs only when EVERGROVE_CLIENT names the client's folder (the one with FreeRealms.exe);
/// it checks the NameFilter.txt that EVERGROVE_NAME_FILTER names, or else the repository's src/Resources/NameFilter.txt:
/// <code>
/// $env:EVERGROVE_CLIENT = "C:\Games\Free Realms"; $env:EVERGROVE_NAME_FILTER = "C:\Temp\NameFilter.txt"
/// dotnet test Sanctuary.Game.Tests -c Release --filter "FullyQualifiedName~PetNamePickerTests"
/// </code>
/// </summary>
[TestClass]
public sealed class PetNamePickerTests
{
    // The picker's Resources/PetNames.txt holds "id^text^part^" lines: part 0 is the template that joins a title
    // (part 1) and a name (part 2), "[*pet_first*] [*pet_second*]". The parts' spaces count: one title is a space.
    private const string TemplatePart = "0";
    private const string TitlePart = "1";
    private const string NamePart = "2";

    [TestMethod]
    public void EveryNameThePickerMakes_IsAllowed()
    {
        var client = Environment.GetEnvironmentVariable("EVERGROVE_CLIENT");

        if (string.IsNullOrEmpty(client))
            Assert.Inconclusive("Set EVERGROVE_CLIENT to the game client's folder to check the names the pet name picker makes.");

        var nameFilterFile = Environment.GetEnvironmentVariable("EVERGROVE_NAME_FILTER") is { Length: > 0 } file
            ? file
            : FindShippedNameFilter();
        var nameFilter = new NameFilterCollection(NullLogger.Instance);

        Assert.IsTrue(nameFilter.Load(nameFilterFile), $"Can't read {nameFilterFile}.");

        var names = PickerNames(Path.Combine(client, "Resources", "PetNames.txt"));
        var refused = names.Where(name => !PetHelper.IsValidName(name, nameFilter)).ToList();

        // Grouped by the filter word that refuses each name, which is the word to remove or narrow.
        var reasons = refused
            .GroupBy(name => nameFilter.FirstOrDefault(token => name.Contains(token, StringComparison.OrdinalIgnoreCase)))
            .Select(group => $"{(group.Key is null ? "the name rules" : $"\"{group.Key}\"")}: {group.Count()}, such as "
                + string.Join(", ", group.Take(3).Select(name => $"\"{name}\"")));

        Assert.AreEqual(0, refused.Count, $"{refused.Count} of the {names.Count} names the pet name picker makes are refused with "
            + $"{nameFilterFile}:{Environment.NewLine}{string.Join(Environment.NewLine, reasons)}");
    }

    private static List<string> PickerNames(string petNamesFile)
    {
        var parts = File.ReadAllLines(petNamesFile)
            .Where(line => !line.StartsWith('#'))
            .Select(line => line.Split('^'))
            .Where(fields => fields.Length >= 3)
            .ToLookup(fields => fields[2], fields => fields[1]);

        var template = parts[TemplatePart].Single();

        return [.. from title in parts[TitlePart]
                   from name in parts[NamePart]
                   select template.Replace("[*pet_first*]", title).Replace("[*pet_second*]", name)];
    }

    private static string FindShippedNameFilter()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            var candidate = Path.Combine(directory.FullName, "Resources", "NameFilter.txt");

            if (File.Exists(candidate))
                return candidate;
        }

        throw new FileNotFoundException("src/Resources/NameFilter.txt not found above the test output.");
    }
}
