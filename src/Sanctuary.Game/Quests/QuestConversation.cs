using System;
using System.Collections.Generic;

using Sanctuary.Game.Resources.Definitions;

namespace Sanctuary.Game.Quests;

public enum QuestConversationStep
{
    /// <summary>There is a next line to show.</summary>
    Next,

    /// <summary>The last line was answered: the conversation is over.</summary>
    Finished,

    /// <summary>A question got an answer it doesn't offer (the window was closed, say): stop without finishing.</summary>
    Abandoned
}

/// <summary>
/// A quest conversation in progress: the line on screen and the lines still to come. A line with
/// <see cref="QuestDialogueLine.Choices"/> is a question; the answer picked plays its own lines next, then (for a
/// <see cref="QuestDialogueChoice.Retry"/> answer) asks the question again.
/// </summary>
public sealed class QuestConversation
{
    private readonly List<QuestDialogueLine> _remaining;

    public QuestConversation(IReadOnlyList<QuestDialogueLine> lines, ulong npcGuid, Action? onFinished = null)
    {
        if (lines.Count == 0)
            throw new ArgumentException("A conversation needs at least one line.", nameof(lines));

        Current = lines[0];
        _remaining = [.. lines];
        _remaining.RemoveAt(0);

        NpcGuid = npcGuid;
        OnFinished = onFinished;
    }

    public QuestDialogueLine Current { get; private set; }

    public ulong NpcGuid { get; }

    /// <summary>Runs once the last line is answered, and never if the conversation is abandoned.</summary>
    public Action? OnFinished { get; }

    /// <summary>Whether answering the current line ends the conversation, so it gets the "leave" button.</summary>
    public bool IsLastLine => Current.Choices.Count == 0 && _remaining.Count == 0;

    /// <summary>
    /// Moves on after the player answers the current line. A line without choices shows one response, so any answer
    /// moves on; a question's responses are numbered from 1 in <see cref="QuestDialogueLine.Choices"/> order.
    /// </summary>
    public QuestConversationStep Answer(int responseId)
    {
        if (Current.Choices.Count > 0)
        {
            var index = responseId - 1;
            if (index < 0 || index >= Current.Choices.Count)
                return QuestConversationStep.Abandoned;

            var choice = Current.Choices[index];

            if (choice.Retry)
                _remaining.Insert(0, Current);

            _remaining.InsertRange(0, choice.Then);
        }

        if (_remaining.Count == 0)
            return QuestConversationStep.Finished;

        Current = _remaining[0];
        _remaining.RemoveAt(0);

        return QuestConversationStep.Next;
    }
}
