using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

using Sanctuary.Game.Quests;

namespace Sanctuary.Game.Tests;

/// <summary>
/// The quests of a player in a minigame test that has no quest world (<see cref="QuestWorld"/>): every event the game
/// tells them (<see cref="IQuestManager.OnEvent"/>), recorded. Anything else asked of them throws.
/// </summary>
public class ToldQuests : DispatchProxy
{
    public List<QuestEvent> Events { get; } = [];

    /// <summary>The games whose wins they were told of, one entry per win.</summary>
    public IEnumerable<int> Wins => Events.OfType<ActivityWonEvent>().Select(won => won.ActivityId);

    /// <summary>A quest manager that records what it is told, and the record.</summary>
    public static (IQuestManager Quests, ToldQuests Told) New()
    {
        var quests = Create<IQuestManager, ToldQuests>();

        return (quests, (ToldQuests)(object)quests);
    }

    protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
    {
        if (targetMethod?.Name != nameof(IQuestManager.OnEvent))
            throw new NotSupportedException(targetMethod?.Name);

        Events.Add((QuestEvent)args![1]!);
        return null;
    }
}
