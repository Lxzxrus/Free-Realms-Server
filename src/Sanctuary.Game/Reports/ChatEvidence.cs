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
/// most <see cref="MaxTextLength"/> characters, none older than <see cref="MaxAge"/>. It belongs to a character, not to
/// the connection: <see cref="ChatEvidenceStore"/> keeps it, so logging out and in again does not clear it.
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
        CreatedAt = _timeProvider.GetUtcNow();
    }

    /// <summary>When this record was started.</summary>
    public DateTimeOffset CreatedAt { get; }

    /// <summary>Whether it holds nothing and is old enough that nothing could still be in date: the store may drop it.</summary>
    public bool IsIdle(DateTimeOffset now)
    {
        lock (_lock)
        {
            Expire(now);

            return _lines.Count == 0 && now - CreatedAt > MaxAge;
        }
    }

    /// <summary>
    /// The channels whose lines are kept: those that everyone in the area, or on the server, can read. Any other channel
    /// (a tell, a whisper, group or guild chat, or one added later) is private to some people: the line would go to staff
    /// and to Discord on the word of anyone who reports the sender, who need not have seen it. An allowlist, so a new
    /// channel stays out until someone decides otherwise.
    /// </summary>
    public static bool IsPublicChannel(ChatChannel channel) => channel is
        ChatChannel.WorldSay or ChatChannel.WorldShout or ChatChannel.WorldTrade or ChatChannel.WorldLfg
        or ChatChannel.WorldArea or ChatChannel.WorldMembersOnly;

    /// <summary>
    /// Remembers a line the server accepted (muted, rate-limited and command lines never get here), if it was said on a
    /// <see cref="IsPublicChannel">public channel</see>. Private lines are never kept.
    /// </summary>
    public void Add(ChatChannel channel, string? text)
    {
        if (!IsPublicChannel(channel))
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
    /// time they are shown relative to. <paramref name="recordingSince"/> is when the server began keeping lines (it starts
    /// empty at every restart): with no lines, the text says so when that is less than <see cref="MaxAge"/> ago, so an empty
    /// record is not read as proof that nothing was said.
    /// </summary>
    public static string Format(IReadOnlyList<ChatEvidenceLine> lines, DateTimeOffset now, int maxLength, DateTimeOffset? recordingSince = null)
    {
        if (lines.Count == 0)
        {
            var window = (int)MaxAge.TotalMinutes;

            if (recordingSince is { } since && now - since < MaxAge)
            {
                return $"no public chat from them recorded since the server started {(int)Math.Max(0, (now - since).TotalMinutes)} minutes ago " +
                    $"(the record is kept in memory and starts empty at every restart)";
            }

            return $"no public chat from them recorded in the last {window} minutes (tells, group and guild chat are never recorded)";
        }

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
