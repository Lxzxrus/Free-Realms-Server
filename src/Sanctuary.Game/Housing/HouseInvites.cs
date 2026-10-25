using System;
using System.Collections.Generic;
using System.Linq;

namespace Sanctuary.Game.Housing;

/// <summary>
/// Invites into a house that haven't been answered yet. An invite lets its player in for <see cref="Lifetime"/>, even
/// if the house is locked or unpublished: "you can lock your house. This prevents people from visiting your house
/// without an invitation from you", as the client's own tip says. Once the guest is in, the house remembers them
/// (<see cref="Zones.HousingZone.IsGuest"/>), so a lock doesn't send them back out.
/// </summary>
public sealed class HouseInvites
{
    public static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(5);

    private readonly record struct Key(ulong InviteeCharacterId, ulong HouseId);
    private readonly record struct Invite(ulong InviterCharacterId, DateTimeOffset Expires);

    private readonly Dictionary<Key, Invite> _invites = [];
    private readonly object _lock = new();

    /// <summary>Records an invite, or renews the player's invite into that house.</summary>
    public void Add(ulong inviteeCharacterId, ulong houseId, ulong inviterCharacterId, DateTimeOffset now)
    {
        lock (_lock)
        {
            foreach (var expired in _invites.Where(entry => entry.Value.Expires <= now).Select(entry => entry.Key).ToList())
                _invites.Remove(expired);

            _invites[new Key(inviteeCharacterId, houseId)] = new Invite(inviterCharacterId, now + Lifetime);
        }
    }

    /// <summary>Whether the player has an invite into the house that hasn't run out.</summary>
    public bool IsInvited(ulong inviteeCharacterId, ulong houseId, DateTimeOffset now)
    {
        lock (_lock)
        {
            return _invites.TryGetValue(new Key(inviteeCharacterId, houseId), out var invite) && invite.Expires > now;
        }
    }

    /// <summary>Uses up the player's invite into the house.</summary>
    /// <returns>Whether there was one that hadn't run out.</returns>
    public bool TryUse(ulong inviteeCharacterId, ulong houseId, DateTimeOffset now)
    {
        return TryRemove(inviteeCharacterId, houseId, now, out _);
    }

    /// <summary>Removes the player's invite into the house, as when they decline it.</summary>
    /// <param name="inviterCharacterId">Who sent it, to tell them.</param>
    /// <returns>Whether there was one that hadn't run out.</returns>
    public bool TryRemove(ulong inviteeCharacterId, ulong houseId, DateTimeOffset now, out ulong inviterCharacterId)
    {
        lock (_lock)
        {
            inviterCharacterId = 0;

            if (!_invites.Remove(new Key(inviteeCharacterId, houseId), out var invite) || invite.Expires <= now)
                return false;

            inviterCharacterId = invite.InviterCharacterId;
            return true;
        }
    }
}
