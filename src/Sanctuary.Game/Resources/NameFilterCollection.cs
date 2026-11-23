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
/// A name is split into words (anything but a letter or a digit separates them, and apostrophes are dropped), lowered
/// and stripped of accents, so "Fück" counts as "fuck". Each line of the file is one entry:
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

        var words = Words(name);

        foreach (var entry in entries)
        {
            foreach (var word in words)
            {
                if (Matches(entry, word))
                    return entry.Line;
            }
        }

        return null;
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
            var word = Fold(parts[0]);

            if (word.Length == 0)
                continue;

            var covers = parts.Skip(1)
                .Select(Fold)
                .Where(cover => cover.Length > word.Length && cover.Contains(word, StringComparison.Ordinal))
                .ToArray();

            entries.Add(new Entry(line, word, wholeWordOnly, covers));
        }

        return entries;
    }

    private static List<string> Words(string name)
    {
        var words = new List<string>();
        var current = new StringBuilder();

        foreach (var character in Fold(name, keepSeparators: true))
        {
            if (char.IsLetterOrDigit(character))
            {
                current.Append(character);
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

    // Lower case, without accents or apostrophes; other separators become spaces when they're kept.
    private static string Fold(string text) => Fold(text, keepSeparators: false);

    private static string Fold(string text, bool keepSeparators)
    {
        var builder = new StringBuilder(text.Length);

        foreach (var character in text.Normalize(NormalizationForm.FormD))
        {
            if (CharUnicodeInfo.GetUnicodeCategory(character) == UnicodeCategory.NonSpacingMark || character is '\'' or '’')
                continue;

            if (char.IsLetterOrDigit(character))
                builder.Append(char.ToLowerInvariant(character));
            else if (keepSeparators)
                builder.Append(' ');
        }

        return builder.ToString();
    }
}
