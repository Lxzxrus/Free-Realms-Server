using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;

using Microsoft.Extensions.Logging;

namespace Sanctuary.Game.Resources;

/// <summary>
/// The words refused in names players choose: characters, guilds, pets and houses (<c>NameFilter.txt</c>).
/// <para>
/// A name is folded first: lowered, stripped of accents and apostrophes, with compatibility forms made plain (full-width
/// and other styled letters) and letters that only look Latin made Latin (ı, ø, ł; Cyrillic and Greek letters drawn
/// like Latin ones), so "Fück", "Ｆuck" and "Fцck"'s cousins count as "fuck". It is then split into words: anything
/// but a letter separates them, digits too, so "Ass69" holds the word "ass". Where digits and symbols stand for letters
/// ("Sh1t", "B@d"), the name is checked again with them read as letters. Each line of the file is one entry:
/// </para>
/// <list type="bullet">
/// <item><c>word</c>: refused wherever it appears in a word of the name.</item>
/// <item><c>=word</c>: refused only as a whole word. For words too short to search for inside others ("ass").</item>
/// <item><c>word|cover|cover</c>: refused wherever it appears, except inside one of the cover words, which are innocent
/// words that contain it ("rape|grape": "Grapejuice" passes, "Rapeking" doesn't).</item>
/// </list>
/// Blank lines and lines starting with <c>#</c> are skipped. Matching used to be plain "contains", which with a real
/// list refuses Cassandra, Grape and Peacock (Evergrove launch prep).
/// </summary>
public class NameFilterCollection : List<string>
{
    private readonly ILogger _logger;

    private List<Entry> _entries = [];

    public NameFilterCollection(ILogger logger)
    {
        _logger = logger;
    }

    private sealed record Entry(string Line, string Word, bool WholeWordOnly, string[] Covers);

    public bool Load(string filePath)
    {
        if (!File.Exists(filePath))
        {
            _logger.LogError("Failed to find file \"{file}\"", filePath);
            return false;
        }

        try
        {
            var lines = File.ReadAllLines(filePath)
                .Select(x => x.Trim())
                .Where(x => !string.IsNullOrWhiteSpace(x) && !x.StartsWith('#'))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            var entries = Compile(lines);

            if (entries.Count == 0)
            {
                // A file with no entries refuses nothing. On a reload that is most likely a file caught half written
                // (a deploy, an edit): keep the list there is.
                if (_entries.Count > 0)
                {
                    _logger.LogWarning("\"{file}\" has no entries now; keeping the {count} it had. Names are still filtered.", filePath, _entries.Count);
                    return true;
                }

                _logger.LogWarning("\"{file}\" has no entries: no names are filtered.", filePath);
            }

            Clear();
            AddRange(lines);
            _entries = entries;

            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to parse file \"{file}\".", filePath);
            return false;
        }
    }

    /// <summary>Whether <paramref name="name"/> contains a word <paramref name="filter"/> refuses.</summary>
    public static bool Refuses(IEnumerable<string> filter, string name) => FindRefusedEntry(filter, name) is not null;

    /// <summary>
    /// The first line of <paramref name="filter"/> that refuses <paramref name="name"/>, or <c>null</c>.
    /// <paramref name="filter"/> is the server's <see cref="NameFilterCollection"/>, or lines in its format (tests).
    /// </summary>
    public static string? FindRefusedEntry(IEnumerable<string> filter, string name)
    {
        var entries = filter is NameFilterCollection collection ? collection._entries : Compile(filter);

        if (entries.Count == 0)
            return null;

        var folded = FoldText(name);

        foreach (var words in WordSets(folded))
        {
            foreach (var entry in entries)
            {
                foreach (var word in words)
                {
                    if (Matches(entry, word))
                        return entry.Line;
                }
            }
        }

        return null;
    }

    /// <summary>
    /// The words of <paramref name="name"/> as the filter sees them: folded (see the class), split at anything but a
    /// letter. For checks of whole words, such as the reserved staff words (CharacterNameRules).
    /// </summary>
    public static IReadOnlyList<string> WordsOf(string name) => Words(FoldText(name), leet: null);

    /// <summary>
    /// Whether <paramref name="character"/> folds to plain letters a to z (é to e, ø to o, ß to ss). A character's name
    /// may only use these, so every name the filter checks is one it can read (CharacterNameRules).
    /// </summary>
    public static bool FoldsToBasicLatin(char character)
    {
        var folded = FoldText(character.ToString());

        return folded.Length > 0 && folded.All(c => c is >= 'a' and <= 'z');
    }

    private static bool Matches(Entry entry, string word)
    {
        if (entry.WholeWordOnly)
            return word == entry.Word;

        for (var index = word.IndexOf(entry.Word, StringComparison.Ordinal); index >= 0;
             index = word.IndexOf(entry.Word, index + 1, StringComparison.Ordinal))
        {
            if (!IsCovered(entry, word, index))
                return true;
        }

        return false;
    }

    // Whether the match at index lies inside an occurrence of one of the entry's cover words.
    private static bool IsCovered(Entry entry, string word, int index)
    {
        foreach (var cover in entry.Covers)
        {
            var offset = cover.IndexOf(entry.Word, StringComparison.Ordinal);

            while (offset >= 0)
            {
                var start = index - offset;

                if (start >= 0 && start + cover.Length <= word.Length
                    && string.CompareOrdinal(word, start, cover, 0, cover.Length) == 0)
                {
                    return true;
                }

                offset = cover.IndexOf(entry.Word, offset + 1, StringComparison.Ordinal);
            }
        }

        return false;
    }

    private static List<Entry> Compile(IEnumerable<string> lines)
    {
        var entries = new List<Entry>();

        foreach (var raw in lines)
        {
            var line = raw.Trim();

            if (line.Length == 0 || line.StartsWith('#'))
                continue;

            var wholeWordOnly = line.StartsWith('=');
            var parts = (wholeWordOnly ? line[1..] : line).Split('|');
            var word = LettersOnly(parts[0]);

            if (word.Length == 0)
                continue;

            var covers = parts.Skip(1)
                .Select(LettersOnly)
                .Where(cover => cover.Length > word.Length && cover.Contains(word, StringComparison.Ordinal))
                .ToArray();

            entries.Add(new Entry(line, word, wholeWordOnly, covers));
        }

        return entries;
    }

    private static string LettersOnly(string text) => string.Concat(Words(FoldText(text), leet: null));

    // The name's words as written, then (if it has any digits or symbols that stand for letters) with them read as
    // letters: once with 1 as i, once with 1 as l.
    private static IEnumerable<List<string>> WordSets(string folded)
    {
        yield return Words(folded, leet: null);

        if (folded.Any(LeetWithI.ContainsKey))
        {
            yield return Words(folded, LeetWithI);
            yield return Words(folded, LeetWithL);
        }
    }

    private static List<string> Words(string folded, IReadOnlyDictionary<char, char>? leet)
    {
        var words = new List<string>();
        var current = new StringBuilder();

        foreach (var character in folded)
        {
            if (char.IsLetter(character))
            {
                current.Append(character);
            }
            else if (leet is not null && leet.TryGetValue(character, out var letter))
            {
                current.Append(letter);
            }
            else if (current.Length > 0)
            {
                words.Add(current.ToString());
                current.Clear();
            }
        }

        if (current.Length > 0)
            words.Add(current.ToString());

        return words;
    }

    /// <summary>
    /// Lower case, without accents or apostrophes; compatibility forms made plain (FormKD: "Ｆ" is "F", "ﬁ" is "fi");
    /// letters that only look Latin made Latin. Anything else (digits, spaces, symbols) is left as it is, for
    /// <see cref="Words"/> to split at or read as letters.
    /// </summary>
    private static string FoldText(string text)
    {
        var builder = new StringBuilder(text.Length);

        foreach (var character in text.Normalize(NormalizationForm.FormKD))
        {
            if (CharUnicodeInfo.GetUnicodeCategory(character) == UnicodeCategory.NonSpacingMark || character is '\'' or '’')
                continue;

            // the original case first: some letters look Latin only as capitals (Greek Η is H, its η isn't)
            if (LookAlikes.TryGetValue(character, out var latin))
            {
                builder.Append(latin);
                continue;
            }

            if (!char.IsLetter(character))
            {
                builder.Append(character);
                continue;
            }

            var lower = char.ToLowerInvariant(character);

            builder.Append(LookAlikes.TryGetValue(lower, out latin) ? latin : lower.ToString());
        }

        return builder.ToString();
    }

    // Letters FormKD leaves alone that read as Latin ones. Latin: with a stroke, dotless, ligatures and the like.
    // Cyrillic and Greek: those drawn the same as a Latin letter, capitals and small letters each as they look.
    private static readonly Dictionary<char, string> LookAlikes = new()
    {
        // Latin
        ['ı'] = "i", ['ł'] = "l", ['ø'] = "o", ['đ'] = "d", ['ħ'] = "h", ['ŧ'] = "t", ['ŋ'] = "n", ['ĸ'] = "k",
        ['ƒ'] = "f", ['ß'] = "ss", ['æ'] = "ae", ['œ'] = "oe", ['þ'] = "th", ['ð'] = "d",

        // Cyrillic capitals
        ['А'] = "a", ['В'] = "b", ['Е'] = "e", ['К'] = "k", ['М'] = "m", ['Н'] = "h", ['О'] = "o", ['Р'] = "p",
        ['С'] = "c", ['Т'] = "t", ['Х'] = "x", ['У'] = "y", ['Ѕ'] = "s", ['І'] = "i", ['Ј'] = "j", ['Ү'] = "y",

        // Cyrillic small letters
        ['а'] = "a", ['е'] = "e", ['о'] = "o", ['р'] = "p", ['с'] = "c", ['у'] = "y", ['х'] = "x", ['ѕ'] = "s",
        ['і'] = "i", ['ј'] = "j", ['һ'] = "h", ['ԁ'] = "d", ['ԛ'] = "q", ['ԝ'] = "w", ['ӏ'] = "l", ['ү'] = "y",
        ['к'] = "k", ['м'] = "m", ['н'] = "h", ['т'] = "t", ['в'] = "b",

        // Greek capitals
        ['Α'] = "a", ['Β'] = "b", ['Ε'] = "e", ['Ζ'] = "z", ['Η'] = "h", ['Ι'] = "i", ['Κ'] = "k", ['Μ'] = "m",
        ['Ν'] = "n", ['Ο'] = "o", ['Ρ'] = "p", ['Τ'] = "t", ['Υ'] = "y", ['Χ'] = "x",

        // Greek small letters
        ['α'] = "a", ['ε'] = "e", ['ι'] = "i", ['κ'] = "k", ['ν'] = "v", ['ο'] = "o", ['ρ'] = "p", ['τ'] = "t",
        ['υ'] = "u", ['χ'] = "x",
    };

    // Digits and symbols written for letters. The 1 is either i or l, so a name with one is read both ways.
    private static readonly Dictionary<char, char> LeetWithI = new()
    {
        ['0'] = 'o', ['1'] = 'i', ['3'] = 'e', ['4'] = 'a', ['5'] = 's', ['7'] = 't', ['8'] = 'b',
        ['@'] = 'a', ['$'] = 's', ['!'] = 'i', ['|'] = 'l',
    };

    private static readonly Dictionary<char, char> LeetWithL = new(LeetWithI) { ['1'] = 'l' };
}
