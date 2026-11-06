using Microsoft.VisualStudio.TestTools.UnitTesting;

using Sanctuary.Game.Quests;
using Sanctuary.Game.Resources.Definitions;

namespace Sanctuary.Game.Tests;

/// <summary>
/// Evergrove's exclusion-ordering test (found by ImAlko on Sulphural's upstream PR #130), on upstream's QuestLog.
/// </summary>
[TestClass]
public sealed class QuestOfferTests
{
    private const int Shared = 10;
    private const int BranchA = 11;
    private const int BranchB = 12;

    private static QuestDefinition Branch(int questId, int excludes) => new()
    {
        QuestId = questId,
        PrerequisiteQuestId = Shared,
        ExcludesQuestIds = [excludes]
    };

    private static QuestLog Log(int[]? completed = null, int[]? inProgress = null)
    {
        var log = new QuestLog();

        foreach (var questId in completed ?? [])
            log.AddCompleted(questId);

        foreach (var questId in inProgress ?? [])
            log.Add(new QuestProgress(questId));

        return log;
    }

    [TestMethod]
    public void ExclusiveBranchesSharingAPrerequisite_OfferOnlyOne()
    {
        // The player finished the shared prerequisite and took branch A; branch B must not be offered too.
        var playerQuests = Log(completed: [Shared], inProgress: [BranchA]);

        Assert.IsFalse(Branch(BranchB, excludes: BranchA).IsOfferableFor(playerQuests));
    }

    [TestMethod]
    public void BothBranches_AreOfferedUntilOneIsTaken()
    {
        var playerQuests = Log(completed: [Shared]);

        Assert.IsTrue(Branch(BranchA, excludes: BranchB).IsOfferableFor(playerQuests));
        Assert.IsTrue(Branch(BranchB, excludes: BranchA).IsOfferableFor(playerQuests));
    }

    [TestMethod]
    public void Prerequisite_StillHasToBeFinished()
    {
        Assert.IsFalse(Branch(BranchA, excludes: BranchB).IsOfferableFor(Log()));
        Assert.IsFalse(Branch(BranchA, excludes: BranchB).IsOfferableFor(Log(inProgress: [Shared])));
    }
}
