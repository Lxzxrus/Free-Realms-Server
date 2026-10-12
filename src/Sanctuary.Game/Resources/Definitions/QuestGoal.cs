using System.Collections.Generic;

namespace Sanctuary.Game.Resources.Definitions;

public enum QuestGoalType
{
    TalkToNpc = 0,
    ReachLocation = 1,
    Collect = 2
}

public sealed class QuestDialogueLine
{
    public int TextId { get; set; }

    public int ResponseTextId { get; set; }

    /// <summary>When set, the line is a question with one response per choice, instead of ResponseTextId.</summary>
    public List<QuestDialogueChoice> Choices { get; set; } = [];
}

public sealed class QuestDialogueChoice
{
    public int ResponseTextId { get; set; }

    /// <summary>The lines the NPC says next when this answer is picked.</summary>
    public List<QuestDialogueLine> Then { get; set; } = [];

    /// <summary>A wrong answer: after <see cref="Then"/>, the question is asked again.</summary>
    public bool Retry { get; set; }
}

public sealed class QuestGoal
{
    public int NameId { get; set; }

    public int DescriptionId { get; set; }

    public int DialogueId { get; set; }
    public List<QuestDialogueLine> Dialogue { get; set; } = [];

    /// <summary>
    /// The step completes when its conversation ends, rather than when it starts, so a quiz has to be answered
    /// right. For a goal with one NPC (not a counted talk).
    /// </summary>
    public bool CompleteAfterDialogue { get; set; }

    public QuestGoalType Type { get; set; } = QuestGoalType.TalkToNpc;

    public ulong TargetGuid { get; set; }

    public List<ulong> TargetGuids { get; set; } = [];

    public List<int> TargetDialogueIds { get; set; } = [];
    public List<int> TargetResponseIds { get; set; } = [];

    public int RequiredCount { get; set; }

    public string CollectNodeType { get; set; } = string.Empty;

    public float[] ReachPosition { get; set; } = [];

    public float ReachRadius { get; set; }

    public byte CursorId { get; set; } = 17;
    public int InteractRange { get; set; } = 12;

    public bool IsCountedTalk => Type == QuestGoalType.TalkToNpc && RequiredCount > 1;

    public IEnumerable<ulong> AllTalkTargetGuids()
    {
        if (TargetGuid != 0)
            yield return TargetGuid;

        foreach (var guid in TargetGuids)
            if (guid != 0 && guid != TargetGuid)
                yield return guid;
    }

    public IReadOnlyList<QuestDialogueLine> ConversationFor(ulong npcGuid)
    {
        if (Dialogue.Count > 0)
            return Dialogue;

        var index = 0;

        foreach (var guid in AllTalkTargetGuids())
        {
            if (guid == npcGuid && index < TargetDialogueIds.Count && TargetDialogueIds[index] != 0)
            {
                return [new QuestDialogueLine
                {
                    TextId = TargetDialogueIds[index],
                    ResponseTextId = index < TargetResponseIds.Count ? TargetResponseIds[index] : 0
                }];
            }

            index++;
        }

        return DialogueId != 0 ? [new QuestDialogueLine { TextId = DialogueId }] : [];
    }

    /// <summary>Mistakes in the goal's conversation that would show a blank line or trap the player in it.</summary>
    public IEnumerable<string> ConversationProblems()
    {
        if (CompleteAfterDialogue && IsCountedTalk)
            yield return "CompleteAfterDialogue is set on a counted talk goal, where it does nothing";

        if (CompleteAfterDialogue && Dialogue.Count == 0)
            yield return "CompleteAfterDialogue is set without a Dialogue";

        foreach (var problem in LineProblems(Dialogue))
            yield return problem;
    }

    private static IEnumerable<string> LineProblems(List<QuestDialogueLine> lines)
    {
        foreach (var line in lines)
        {
            if (line.TextId == 0)
                yield return "a Dialogue line has no TextId";

            if (line.Choices.Count == 0)
                continue;

            if (line.Choices.TrueForAll(choice => choice.Retry))
                yield return $"every answer to line {line.TextId} is a Retry, so it can never be passed";

            foreach (var choice in line.Choices)
            {
                if (choice.ResponseTextId == 0)
                    yield return $"an answer to line {line.TextId} has no ResponseTextId";

                foreach (var problem in LineProblems(choice.Then))
                    yield return problem;
            }
        }
    }
}
