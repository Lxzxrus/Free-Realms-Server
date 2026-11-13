using System.Collections.Generic;

namespace Sanctuary.Game.Helpers;

/// <summary>
/// The Login server's list of who is online on one Gateway: character id to the Gateway login session that announced
/// it (from upstream OSFR PR #190 by GekkoQuest). A logout only counts for the session it names, so a dropped
/// session's late logout can't mark a newer login of the same character offline. Kept here, beside
/// <see cref="LoginAdmission"/>, so it can be tested without a network.
/// </summary>
/// <remarks>Only touched from the Login server's single loop thread, so it has no lock.</remarks>
public sealed class OnlineCharacterSessions
{
    private readonly Dictionary<ulong, ulong> _sessions = new();

    public int Count => _sessions.Count;

    public bool IsOnline(ulong characterId) => _sessions.ContainsKey(characterId);

    /// <summary>The Gateway let the character in under <paramref name="session"/>. A newer session replaces an older one.</summary>
    public void MarkOnline(ulong characterId, ulong session) => _sessions[characterId] = session;

    /// <summary>The Gateway dropped the character on Login's request, whatever session it had.</summary>
    public void MarkReleased(ulong characterId) => _sessions.Remove(characterId);

    /// <summary>Marks the character offline if <paramref name="session"/> is the one Login knows; otherwise ignores it.</summary>
    public bool TryLogout(ulong characterId, ulong session)
    {
        if (!_sessions.TryGetValue(characterId, out var current) || current != session)
            return false;

        _sessions.Remove(characterId);
        return true;
    }
}
