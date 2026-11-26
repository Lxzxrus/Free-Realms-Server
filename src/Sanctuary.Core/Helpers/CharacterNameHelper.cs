using System.Globalization;

namespace Sanctuary.Core.Helpers;

/// <summary>What is wrong with a character name, as <see cref="CharacterNameHelper.Check"/> finds it.</summary>
public enum CharacterNameProblem
{
    None,
    /// <summary>No first name, or a last name that is only blanks.</summary>
    Empty,
    FirstNameTooShort,
    FirstNameTooLong,
    LastNameTooShort,
    LastNameTooLong,
    IllegalCharacters
}

public static class CharacterNameHelper
{
    public const int MinPartLength = 3;
    public const int MaxPartLength = 14;

    /// <summary>
    /// The characters a name may hold: letters (and the symbols mixed in with them in that range), a space between words
    /// and an apostrophe. Control characters, invisible formatting characters and every kind of space but a plain one are
    /// left out even where they fall inside the range.
    /// </summary>
    public static bool ContainsIllegalCharacters(string name)
    {
        foreach (var character in name)
        {
            if (character is '\'' or ' ')
                continue;

            if (character >= '\u0041' && character <= '\u024F'
                && !char.IsControl(character)
                && !char.IsWhiteSpace(character)
                && char.GetUnicodeCategory(character) != UnicodeCategory.Format)
            {
                continue;
            }

            return true;
        }

        return false;
    }

    /// <summary>
    /// The full name the way the database computes it (<c>Characters.FullName</c>): the first name, and the last name after a
    /// space if there is one. Two different (first, last) pairs can make the same full name when a part has a space in it,
    /// so a name is "taken" when its full name is, not when the pair is. <see cref="Check"/> refuses a space inside a part.
    /// </summary>
    public static string Compose(string? firstName, string? lastName) =>
        string.IsNullOrEmpty(lastName) ? firstName ?? string.Empty : $"{firstName} {lastName}";

    /// <summary>
    /// Whether a first and last name may be given to a character: both the right length, and made of allowed characters. A
    /// last name is optional (null or empty). A part may not hold a space: "Alice Admin" as a first name would be the same
    /// full name as "Alice" "Admin", and every staff command and every report finds a player by that full name.
    /// </summary>
    public static CharacterNameProblem Check(string? firstName, string? lastName)
    {
        if (string.IsNullOrWhiteSpace(firstName) || (!string.IsNullOrEmpty(lastName) && string.IsNullOrWhiteSpace(lastName)))
            return CharacterNameProblem.Empty;

        if (firstName.Length < MinPartLength)
            return CharacterNameProblem.FirstNameTooShort;

        if (firstName.Length > MaxPartLength)
            return CharacterNameProblem.FirstNameTooLong;

        if (!string.IsNullOrEmpty(lastName))
        {
            if (lastName.Length < MinPartLength)
                return CharacterNameProblem.LastNameTooShort;

            if (lastName.Length > MaxPartLength)
                return CharacterNameProblem.LastNameTooLong;
        }

        if (firstName.Contains(' ') || (lastName?.Contains(' ') ?? false))
            return CharacterNameProblem.IllegalCharacters;

        if (ContainsIllegalCharacters(firstName) || (lastName is not null && ContainsIllegalCharacters(lastName)))
            return CharacterNameProblem.IllegalCharacters;

        return CharacterNameProblem.None;
    }
}
