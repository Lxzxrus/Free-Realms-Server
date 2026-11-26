using System;
using System.Collections.Generic;
using System.Text;

using Sanctuary.Game.ChatCommands;
using Sanctuary.Packet.Common.Chat;

namespace Sanctuary.Game.Reports;

/// <summary>One chat line the server accepted from a player.</summary>
public readonly record struct ChatEvidenceLine(ChatChannel Channel, string Text, DateTimeOffset Time);

/// <summary>
/// The last few chat lines the server itself accepted from one player, for staff to compare with what a report's
/// sender says the player wrote (the client's lines can be forged). Bounded: at most <see cref="MaxLines"/> lines of at
/// most <see cref="MaxTextLength"/> characters, none older than <see cref="MaxAge"/>. It lives on the player, so it is
/// gone when they log out.
/// </summary>
public sealed class ChatEvidence
{
    public const int MaxLines = 10;
    public const int MaxTextLength = 256;

    public static readonly TimeSpan MaxAge = TimeSpan.FromMinutes(30);

    private readonly object _lock = new();
    private readonly Queue<ChatEvidenceLine> _lines = new(MaxLines);
    private readonly TimeProvider _timeProvider;

    public ChatEvidence(TimeProvider? timeProvider = null)
    {
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    /// <summary>
    /// Remembers a line the server accepted (muted, rate-limited and command lines never get here). A private tell is
    /// never kept: the line would go to staff and to Discord on the word of anyone who reports the sender, and the record
    /// does not say who it was to.
    /// </summary>
    public void Add(ChatChannel channel, string? text)
    {
        if (channel == ChatChannel.Tell)
            return;

        var clean = ModArguments.CleanText(text, MaxTextLength);

        if (clean is null)
            return;

        var now = _timeProvider.GetUtcNow();

        lock (_lock)
        {
            Expire(now);

            while (_lines.Count >= MaxLines)
                _lines.Dequeue();

            _lines.Enqueue(new ChatEvidenceLine(channel, clean, now));
        }
    }

    /// <summary>The lines still in date, oldest first.</summary>
    public IReadOnlyList<ChatEvidenceLine> Snapshot()
    {
        lock (_lock)
        {
            Expire(_timeProvider.GetUtcNow());

            return [.. _lines];
        }
    }

    public void Clear()
    {
        lock (_lock)
            _lines.Clear();
    }

    private void Expire(DateTimeOffset now)
    {
        while (_lines.Count > 0 && now - _lines.Peek().Time > MaxAge)
            _lines.Dequeue();
    }

    /// <summary>
    /// The lines as one text, a line each, oldest first, for a report's server context. <paramref name="now"/> is the
    /// time they are shown relative to.
    /// </summary>
    public static string Format(IReadOnlyList<ChatEvidenceLine> lines, DateTimeOffset now, int maxLength)
    {
        if (lines.Count == 0)
            return "no chat from them in the last " + (int)MaxAge.TotalMinutes + " minutes";

        var builder = new StringBuilder();

        foreach (var line in lines)
        {
            var age = now - line.Time;
            var entry = $"[{line.Time.UtcDateTime:HH:mm:ss} UTC, {(int)Math.Max(0, age.TotalSeconds)}s ago, {line.Channel}] {line.Text}";

            if (builder.Length + entry.Length + 1 > maxLength)
                break;

            if (builder.Length > 0)
                builder.Append('\n');

            builder.Append(entry);
        }

        return builder.ToString();
    }
}
