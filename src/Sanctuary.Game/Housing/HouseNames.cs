using System;
using System.Collections.Generic;
using System.Linq;

namespace Sanctuary.Game.Housing;

public enum HouseNameResult
{
    Valid,
    TooShort,
    TooLong,
    IllegalCharacters,

    /// <summary>It contains a word from the server's name filter (<c>NameFilter.txt</c>).</summary>
    Filtered
}

/// <summary>
/// The names an owner gives a house, from the client's name picker or typed. The rules follow guild names
/// (<c>GuildHelper</c>), with a little more punctuation, and use the same name filter as character and guild names.
/// </summary>
public static class HouseNames
{
    public const int MinLength = 3;
    public const int MaxLength = 32;

    /// <summary>The punctuation a house name may use, besides letters, digits and single spaces.</summary>
    public const string AllowedPunctuation = "'-!&,.";

    /// <summary>Trims the name and turns each run of spaces into one.</summary>
    public static string Normalize(string? name)
    {
        return string.Join(' ', (name ?? string.Empty).Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
    }

    /// <param name="name">Already <see cref="Normalize">normalized</see>.</param>
    public static HouseNameResult Validate(string name, IEnumerable<string> nameFilter)
    {
        if (name.Length < MinLength)
            return HouseNameResult.TooShort;

        if (name.Length > MaxLength)
            return HouseNameResult.TooLong;

        if (!name.All(c => char.IsLetterOrDigit(c) || c == ' ' || AllowedPunctuation.Contains(c)))
            return HouseNameResult.IllegalCharacters;

        if (nameFilter.Any(token => name.Contains(token, StringComparison.OrdinalIgnoreCase)))
            return HouseNameResult.Filtered;

        return HouseNameResult.Valid;
    }

    /// <summary>What to tell the owner when a name is refused.</summary>
    public static string Describe(HouseNameResult result) => result switch
    {
        HouseNameResult.TooShort => $"A house name needs at least {MinLength} characters.",
        HouseNameResult.TooLong => $"A house name can be at most {MaxLength} characters.",
        HouseNameResult.IllegalCharacters => $"A house name can use letters, numbers, spaces and {string.Join(' ', AllowedPunctuation.ToCharArray())}",
        HouseNameResult.Filtered => "That house name isn't allowed. Please choose another.",
        _ => string.Empty
    };
}
