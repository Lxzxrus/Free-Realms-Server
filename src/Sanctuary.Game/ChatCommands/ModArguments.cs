using System;
using System.Globalization;
using System.Text;

using Sanctuary.Database.Entities;

namespace Sanctuary.Game.ChatCommands;

/// <summary>What a <c>!mod</c> subcommand was asked to act on.</summary>
/// <param name="Name">The character's full name. It can have a space: "First Last".</param>
/// <param name="Minutes">How long, when the command takes a duration and one was given.</param>
/// <param name="Reason">The reason or warning text after the separator, cleaned. Null when there is none.</param>
/// <param name="Text">Everything the staff member typed after the subcommand, before the separator was taken out.</param>
/// <param name="HasSeparator">Whether the text had a separator in it.</param>
internal sealed record ModTarget(string Name, int? Minutes, string? Reason, string Text, bool HasSeparator);

/// <summary>
/// Reads the arguments of <c>!mod</c>: <c>&lt;player&gt; [minutes] [| reason]</c>.
/// </summary>
/// <remarks>
/// The chat path splits a command on spaces, and a name can have one ("First Last"), so the reason cannot be told
/// from the name by position. It starts at the first <c>|</c>: everything before it is the player (and minutes),
/// everything after it is the reason, and a <c>|</c> inside the reason is kept.
/// </remarks>
internal static class ModArguments
{
    public const char ReasonSeparator = '|';

    /// <summary>The longest reason, ban or mute: what <see cref="DbUser.BanReason"/> holds.</summary>
    public const int MaxReasonLength = DbUser.MaxReasonLength;

    /// <summary>The longest warning text. It is sent to the player's chat, so it stays short.</summary>
    public const int MaxWarnLength = 200;

    /// <summary>
    /// Reads <c>&lt;player&gt; [minutes] [| text]</c>. Returns false when the player is missing, or, with
    /// <paramref name="error"/> set, when the minutes cannot be used. A false with no error means "show the usage".
    /// </summary>
    /// <param name="args">What followed the subcommand.</param>
    /// <param name="allowMinutes">Whether a trailing number is a duration (ban, mute) or part of the name.</param>
    /// <param name="maxTextLength">The longest the text after the separator may be.</param>
    public static bool TryParse(string[] args, bool allowMinutes, int maxTextLength, out ModTarget target, out string? error)
    {
        target = new ModTarget(string.Empty, null, null, string.Empty, false);
        error = null;

        var text = string.Join(' ', args);
        var left = text;
        string? rawReason = null;

        var separator = text.IndexOf(ReasonSeparator);

        if (separator >= 0)
        {
            left = text[..separator];
            rawReason = text[(separator + 1)..];
        }

        var tokens = left.Split(' ', StringSplitOptions.RemoveEmptyEntries);

        if (tokens.Length == 0)
            return false;

        int? minutes = null;

        if (allowMinutes && tokens.Length > 1 && int.TryParse(tokens[^1], NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsedMinutes))
        {
            if (parsedMinutes <= 0)
            {
                error = "Duration must be a positive number of minutes.";
                return false;
            }

            minutes = parsedMinutes;
            tokens = tokens[..^1];
        }

        target = new ModTarget(string.Join(' ', tokens), minutes, CleanText(rawReason, maxTextLength), text, separator >= 0);
        return true;
    }

    /// <summary>
    /// Makes text from a staff member fit for a database column and for other players' chat: control and invisible
    /// formatting characters are dropped (a line break or a right-to-left override could fake a second line), runs of
    /// whitespace become one space, the ends are trimmed, and it is cut to <paramref name="maxLength"/> characters.
    /// Returns null when nothing is left.
    /// </summary>
    public static string? CleanText(string? text, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(text))
            return null;

        var builder = new StringBuilder(Math.Min(text.Length, maxLength));
        var pendingSpace = false;

        foreach (var rune in text.EnumerateRunes())
        {
            // Any kind of whitespace, line breaks included, becomes a plain space.
            if (Rune.IsWhiteSpace(rune))
            {
                pendingSpace = builder.Length > 0;
                continue;
            }

            if (IsDropped(rune) || rune == Rune.ReplacementChar)
                continue;

            var needed = rune.Utf16SequenceLength + (pendingSpace ? 1 : 0);

            if (builder.Length + needed > maxLength)
                break;

            if (pendingSpace)
                builder.Append(' ');

            pendingSpace = false;
            builder.Append(rune.ToString());
        }

        return builder.Length == 0 ? null : builder.ToString();
    }

    private static bool IsDropped(Rune rune)
    {
        switch (Rune.GetUnicodeCategory(rune))
        {
            case UnicodeCategory.Control:
            case UnicodeCategory.Format:
            case UnicodeCategory.LineSeparator:
            case UnicodeCategory.ParagraphSeparator:
            case UnicodeCategory.PrivateUse:
            case UnicodeCategory.OtherNotAssigned:
                return true;
            default:
                return false;
        }
    }
}
