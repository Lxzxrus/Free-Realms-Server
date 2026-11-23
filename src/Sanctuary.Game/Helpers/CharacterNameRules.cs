using System.Collections.Generic;

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
    LastNameTooLong
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

        if (NameFilterCollection.Refuses(nameFilter, $"{firstName} {lastName}"))
            return CharacterNameProblem.Profane;

        return CharacterNameProblem.None;
    }

    // Latin letters (accents too) and apostrophes, as the client's name box allows; at least one letter, and no
    // apostrophe at the start. The old check (CharacterNameHelper) also let through the symbols between Z and a and in
    // Latin-1 (such as [ ] ^ _ and ×).
    private static bool IsNamePart(string part)
    {
        if (part[0] == '\'')
            return false;

        foreach (var character in part)
        {
            if (character == '\'')
                continue;

            if (character > 'ɏ' || !char.IsLetter(character))
                return false;
        }

        return true;
    }
}
