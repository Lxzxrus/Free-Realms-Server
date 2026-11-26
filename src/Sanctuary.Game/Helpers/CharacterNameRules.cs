using System.Collections.Generic;
using System.Linq;

using Sanctuary.Game.Resources;

namespace Sanctuary.Game.Helpers;

public enum CharacterNameProblem
{
    None,
    Invalid,
    IllegalCharacters,
    Profane,
    FirstNameTooShort,
    FirstNameTooLong,
    LastNameTooShort,
    LastNameTooLong,

    /// <summary>A staff or server word (<see cref="CharacterNameRules.ReservedWords"/>): the client is told it's taken.</summary>
    Reserved
}

/// <summary>
/// The rules for a character's name, the same at creation (Login), in the name check the creation screen runs (Login)
/// and on a rename (Gateway). Creation checked only that the name was free: any length, any character and any word went
/// through (Evergrove launch prep). The rename rules were already these, but for a last name's length: 14 refused 437
/// of the last names the client's own picker makes (CharacterNamePickerTests).
/// </summary>
public static class CharacterNameRules
{
    public const int MinLength = 3;
    public const int FirstNameMaxLength = 14;

    /// <summary>The client's name picker joins last names of up to 16 letters ("Imperialantlers" is 15).</summary>
    public const int LastNameMaxLength = 16;

    /// <summary>
    /// Words no part of a character's name may be (after folding: "Âdmin" is "admin"), so nobody passes for staff or
    /// for the server in chat, trades or the friends list. Guild names may not use them either, apart from the server's
    /// own name (<see cref="StaffWords"/>; "Evergrove Builders" is fine).
    /// </summary>
    public static readonly IReadOnlySet<string> ReservedWords = new HashSet<string>
    {
        "admin", "administrator", "moderator", "mod", "gm", "gamemaster", "staff", "system", "server", "official",
        "developer", "sysop", "soe", "evergrove",
        // whole words are compared, so the plurals too ("dev" stays allowed: it's a real first name)
        "admins", "administrators", "moderators", "mods", "gms", "gamemasters", "officials", "developers", "devs",
        "sysops",
    };

    /// <summary><see cref="ReservedWords"/> without the server's name: the words a guild name may not use.</summary>
    public static readonly IReadOnlySet<string> StaffWords = ReservedWords.Where(word => word != "evergrove").ToHashSet();

    /// <param name="lastName">Empty or null for none: a last name is optional.</param>
    public static CharacterNameProblem Check(string? firstName, string? lastName, IEnumerable<string> nameFilter)
    {
        lastName ??= string.Empty;

        if (string.IsNullOrWhiteSpace(firstName) || lastName.Length > 0 && string.IsNullOrWhiteSpace(lastName))
            return CharacterNameProblem.Invalid;

        if (firstName.Length < MinLength)
            return CharacterNameProblem.FirstNameTooShort;

        if (firstName.Length > FirstNameMaxLength)
            return CharacterNameProblem.FirstNameTooLong;

        if (lastName.Length > 0 && lastName.Length < MinLength)
            return CharacterNameProblem.LastNameTooShort;

        if (lastName.Length > LastNameMaxLength)
            return CharacterNameProblem.LastNameTooLong;

        if (!IsNamePart(firstName) || lastName.Length > 0 && !IsNamePart(lastName))
            return CharacterNameProblem.IllegalCharacters;

        if (UsesAny(firstName, ReservedWords) || UsesAny(lastName, ReservedWords))
            return CharacterNameProblem.Reserved;

        if (NameFilterCollection.Refuses(nameFilter, $"{firstName} {lastName}"))
            return CharacterNameProblem.Profane;

        return CharacterNameProblem.None;
    }

    /// <summary>Whether a word of <paramref name="name"/>, folded as the name filter folds it, is one of <paramref name="words"/>.</summary>
    public static bool UsesAny(string name, IReadOnlySet<string> words) =>
        NameFilterCollection.WordsOf(name).Any(words.Contains);

    // Latin letters (accents too) and apostrophes, as the client's name box allows; at least one letter, and no
    // apostrophe at the start. Every letter has to fold to plain a to z (é is e, ø is o, ß is ss), so the name filter
    // can read the whole name: a letter that doesn't (such as ƀ) would hide a word from it. The old check
    // (CharacterNameHelper) also let through the symbols between Z and a and in Latin-1 (such as [ ] ^ _ and ×).
    private static bool IsNamePart(string part)
    {
        if (part[0] == '\'')
            return false;

        foreach (var character in part)
        {
            if (character == '\'')
                continue;

            if (character > 'ɏ' || !char.IsLetter(character) || !NameFilterCollection.FoldsToBasicLatin(character))
                return false;
        }

        return true;
    }
}
