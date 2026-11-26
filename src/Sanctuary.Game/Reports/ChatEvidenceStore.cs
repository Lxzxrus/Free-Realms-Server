using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;

namespace Sanctuary.Game.Reports;

/// <summary>
/// The <see cref="ChatEvidence"/> of every character that has spoken lately, kept by character id outside the
/// <c>Player</c>. A record that lived on the player was empty again after a log out and in, so someone who had insulted
/// another player could clear the server's evidence in seconds. Records are dropped once they are empty and older than
/// <see cref="ChatEvidence.MaxAge"/>, so memory stays bounded by the players who spoke in the last half hour; the number of
/// records is also capped (<see cref="MaxCharacters"/>). It lives in memory: a server restart starts it empty
/// (<see cref="StartedAt"/> says when).
/// </summary>
public sealed class ChatEvidenceStore
{
    /// <summary>The most characters kept at once. Past it a new character's lines are not recorded until some records expire.</summary>
    public const int MaxCharacters = 5000;

    private static readonly TimeSpan SweepInterval = TimeSpan.FromMinutes(1);

    /// <summary>The store the game server uses.</summary>
    public static ChatEvidenceStore Shared { get; } = new();

    private readonly ConcurrentDictionary<ulong, ChatEvidence> _byCharacter = new();
    private readonly TimeProvider _timeProvider;
    private long _nextSweepTicks;

    public ChatEvidenceStore(TimeProvider? timeProvider = null)
    {
        _timeProvider = timeProvider ?? TimeProvider.System;
        StartedAt = _timeProvider.GetUtcNow();
        _nextSweepTicks = (StartedAt + SweepInterval).UtcTicks;
    }

    /// <summary>When the store began to keep lines.</summary>
    public DateTimeOffset StartedAt { get; }

    /// <summary>How many characters have a record.</summary>
    public int Count => _byCharacter.Count;

    /// <summary>The character's record, started if there is none. Over the cap a new character gets a record that is not kept.</summary>
    public ChatEvidence For(ulong characterId)
    {
        Sweep();

        if (_byCharacter.TryGetValue(characterId, out var existing))
            return existing;

        if (_byCharacter.Count >= MaxCharacters)
            return new ChatEvidence(_timeProvider);

        return _byCharacter.GetOrAdd(characterId, _ => new ChatEvidence(_timeProvider));
    }

    /// <summary>The lines still in date of a character, without starting a record. Empty for one that has said nothing.</summary>
    public IReadOnlyList<ChatEvidenceLine> Snapshot(ulong characterId) =>
        _byCharacter.TryGetValue(characterId, out var evidence) ? evidence.Snapshot() : [];

    /// <summary>Drops the records that hold nothing in date, at most once a minute (a record goes only if it is also older than the window).</summary>
    private void Sweep()
    {
        var now = _timeProvider.GetUtcNow();
        var next = Interlocked.Read(ref _nextSweepTicks);

        if (now.UtcTicks < next || Interlocked.CompareExchange(ref _nextSweepTicks, (now + SweepInterval).UtcTicks, next) != next)
            return;

        foreach (var (characterId, evidence) in _byCharacter.ToArray())
        {
            if (evidence.IsIdle(now))
                _byCharacter.TryRemove(new KeyValuePair<ulong, ChatEvidence>(characterId, evidence));
        }
    }
}
