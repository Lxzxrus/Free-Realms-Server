using System.Collections.Generic;
using System.Linq;

using Microsoft.VisualStudio.TestTools.UnitTesting;

using Sanctuary.Game.Quests;
using Sanctuary.Game.Resources.Definitions;

namespace Sanctuary.Game.Tests;

[TestClass]
public sealed class QuestConversationTests
{
    private const int Ready = 1;
    private const int Question = 2;
    private const int TryAgain = 3;
    private const int WellDone = 4;

    private static QuestDialogueLine Line(int textId) => new() { TextId = textId };

    // "Ready?", then a question whose second answer is right; a wrong answer says "try again?" and asks again.
    private static List<QuestDialogueLine> Quiz() =>
    [
        Line(Ready),
        new QuestDialogueLine
        {
            TextId = Question,
            Choices =
            [
                new QuestDialogueChoice { ResponseTextId = 10, Retry = true, Then = [Line(TryAgain)] },
                new QuestDialogueChoice { ResponseTextId = 11, Then = [Line(WellDone)] },
            ]
        }
    ];

    [TestMethod]
    public void RightAnswer_PlaysItsLinesThenFinishes()
    {
        var conversation = new QuestConversation(Quiz(), npcGuid: 7);

        Assert.AreEqual(QuestConversationStep.Next, conversation.Answer(1));
        Assert.AreEqual(Question, conversation.Current.TextId);

        Assert.AreEqual(QuestConversationStep.Next, conversation.Answer(2));
        Assert.AreEqual(WellDone, conversation.Current.TextId);
        Assert.IsTrue(conversation.IsLastLine);

        Assert.AreEqual(QuestConversationStep.Finished, conversation.Answer(1));
    }

    [TestMethod]
    public void WrongAnswer_AsksTheQuestionAgain()
    {
        var conversation = new QuestConversation(Quiz(), npcGuid: 7);
        conversation.Answer(1);

        Assert.AreEqual(QuestConversationStep.Next, conversation.Answer(1));
        Assert.AreEqual(TryAgain, conversation.Current.TextId);
        Assert.IsFalse(conversation.IsLastLine);

        Assert.AreEqual(QuestConversationStep.Next, conversation.Answer(1));
        Assert.AreEqual(Question, conversation.Current.TextId);

        conversation.Answer(2);
        Assert.AreEqual(WellDone, conversation.Current.TextId);
    }

    [TestMethod]
    public void AnswerTheQuestionDoesNotOffer_AbandonsWithoutFinishing()
    {
        // A closed window, or a made-up response, must not count as passing the quiz.
        var conversation = new QuestConversation(Quiz(), npcGuid: 7);
        conversation.Answer(1);

        Assert.AreEqual(QuestConversationStep.Abandoned, conversation.Answer(0));
        Assert.AreEqual(QuestConversationStep.Abandoned, new QuestConversation(Quiz()[1..], 7).Answer(3));
    }

    [TestMethod]
    public void QuestionNeverShowsTheLeaveButton()
    {
        var conversation = new QuestConversation([Quiz()[1]], npcGuid: 7);

        Assert.IsFalse(conversation.IsLastLine);
    }

    [TestMethod]
    public void ConversationProblems_CatchAQuestionThatCanNeverBePassed()
    {
        var goal = new QuestGoal
        {
            Dialogue =
            [
                new QuestDialogueLine
                {
                    TextId = Question,
                    Choices = [new QuestDialogueChoice { ResponseTextId = 10, Retry = true }]
                }
            ]
        };

        Assert.AreEqual(1, goal.ConversationProblems().Count());
        Assert.AreEqual(0, new QuestGoal { Dialogue = Quiz() }.ConversationProblems().Count());
    }

    private static QuestDefinition Tour() => new()
    {
        QuestId = 1,
        GiverGuid = 100,
        TargetGuid = 100,
        Goals =
        [
            new QuestGoal { Type = QuestGoalType.ReachLocation },
            new QuestGoal { TargetGuid = 200 },
            new QuestGoal { TargetGuid = 300 },
            new QuestGoal()
        ]
    };

    [TestMethod]
    public void OnlyTheCurrentStepsNpc_WaitsForThePlayer()
    {
        var quest = Tour();
        var talked = new HashSet<ulong>();

        Assert.IsFalse(quest.WaitsOn(0, 200, talked), "Nobody waits during a reach step.");

        Assert.IsTrue(quest.WaitsOn(1, 200, talked));
        Assert.IsFalse(quest.WaitsOn(1, 300, talked), "A later step's NPC shows nothing yet.");

        Assert.IsFalse(quest.WaitsOn(2, 200, talked), "An earlier step's NPC loses its marker.");
        Assert.IsTrue(quest.WaitsOn(2, 300, talked));

        Assert.IsTrue(quest.WaitsOn(3, 100, talked), "A step naming nobody is with the quest's target.");
        Assert.IsFalse(quest.WaitsOn(4, 100, talked), "Nothing waits once every step is done.");
    }

    [TestMethod]
    public void CountedTalk_WaitsOnlyOnNpcsNotYetTalkedTo()
    {
        var quest = new QuestDefinition
        {
            Goals = [new QuestGoal { TargetGuids = [200, 300], RequiredCount = 2 }]
        };

        Assert.IsTrue(quest.WaitsOn(0, 200, new HashSet<ulong>()));
        Assert.IsFalse(quest.WaitsOn(0, 200, new HashSet<ulong> { 200 }));
        Assert.IsTrue(quest.WaitsOn(0, 300, new HashSet<ulong> { 200 }));
    }

    [TestMethod]
    public void NpcGuids_ListEveryNpcOnce()
    {
        CollectionAssert.AreEqual(new ulong[] { 100, 200, 300 }, Tour().NpcGuids().ToArray());
    }
}
