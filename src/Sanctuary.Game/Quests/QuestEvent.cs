using System.Numerics;

using Sanctuary.Game.Entities;

namespace Sanctuary.Game.Quests;

public abstract record QuestEvent
{
    public virtual bool CreditsOneQuest => false;
}

public sealed record NpcTalkedEvent(Npc Npc) : QuestEvent;

public sealed record NodeGatheredEvent(CollectionNode Node) : QuestEvent
{
    public override bool CreditsOneQuest => true;
}

public sealed record PlayerMovedEvent(Vector4 Position, int ZoneDefinitionId) : QuestEvent;

/// <summary>
/// The player won <paramref name="ActivityId"/>, a Games menu entry (ClientActivityDefinitions.json), and was paid for
/// it: a match-three win the server's own board decided (FlashMinigameHandler), or a Cooking win the client's Flash
/// reported and the server accepted as plausible (MimicHandler; <see cref="Minigames.ActivityWins.ReportedByClient"/>).
/// Never raised for a win the server refused (too fast, rounds missing), and once per game, as the session settles each
/// game's result once.
/// </summary>
public sealed record ActivityWonEvent(int ActivityId) : QuestEvent
{
    // One game won is one win, like one pickup: it counts toward one quest's step, never two.
    public override bool CreditsOneQuest => true;
}
