using System;
using System.Collections.Generic;

namespace Sanctuary.Game.Quests;

/// <summary>
/// Quests whose turn-in window is open and waiting for the player to accept it. A queue rather
/// than a single slot, so quests that finish in the same tick are all handed in.
/// </summary>
public sealed class PendingQuestTurnIns
{
    private readonly object _lock = new();
    private readonly Queue<int> _questIds = new();

    public int Count
    {
        get
        {
            lock (_lock)
                return _questIds.Count;
        }
    }

    public bool Enqueue(int questId)
    {
        lock (_lock)
        {
            if (_questIds.Contains(questId))
                return false;

            _questIds.Enqueue(questId);
            return true;
        }
    }

    /// <summary>
    /// Takes the oldest pending quest that is still active, dropping any that were abandoned or
    /// completed since their turn-in window opened.
    /// </summary>
    public bool TryDequeue(Func<int, bool> isStillActive, out int questId)
    {
        lock (_lock)
        {
            while (_questIds.TryDequeue(out questId))
            {
                if (isStillActive(questId))
                    return true;
            }

            return false;
        }
    }
}
