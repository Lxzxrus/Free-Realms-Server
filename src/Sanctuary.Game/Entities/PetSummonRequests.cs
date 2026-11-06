namespace Sanctuary.Game.Entities;

/// <summary>
/// Evergrove: the pet a player last asked to have out, or none, until the zone carries it out on its tick
/// (<see cref="PlayerPet"/>). Upstream queued every summon, and each one rebuilds the pet for everyone nearby, so a client
/// sending summons as fast as it could held the zone's tick for every player in it. Summons are now spaced
/// <see cref="SummonIntervalMs"/> apart. A request that comes sooner waits instead of being dropped, and a newer one
/// replaces it, so the latest always wins: the activity portal puts one pet away and summons another in the same moment.
/// Putting the pet away is cheap and is carried out at once.
/// </summary>
public sealed class PetSummonRequests
{
    public const int SummonIntervalMs = 1000;

    /// <summary>The pet id <see cref="TryTake"/> gives for putting the pet away.</summary>
    public const int NoPetId = 0;

    private readonly object _lock = new();
    private bool _requested;
    private int _petId;
    private long _nextSummonAt;

    public void Summon(int petId) => Request(petId);

    public void Recall() => Request(NoPetId);

    /// <summary>
    /// Takes the latest request if it may be carried out <paramref name="now"/> (an <see cref="System.Environment.TickCount64"/>):
    /// putting the pet away at once, a summon once <see cref="SummonIntervalMs"/> have passed since the last summon taken.
    /// </summary>
    /// <param name="petId">The pet to summon, or <see cref="NoPetId"/> to put the pet away.</param>
    public bool TryTake(long now, out int petId)
    {
        lock (_lock)
        {
            petId = NoPetId;

            if (!_requested || _petId != NoPetId && now < _nextSummonAt)
                return false;

            _requested = false;
            petId = _petId;

            if (petId != NoPetId)
                _nextSummonAt = now + SummonIntervalMs;

            return true;
        }
    }

    private void Request(int petId)
    {
        lock (_lock)
        {
            _requested = true;
            _petId = petId;
        }
    }
}
