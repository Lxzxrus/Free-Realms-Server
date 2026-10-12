using System.Collections.Generic;

using Microsoft.VisualStudio.TestTools.UnitTesting;

using Sanctuary.Game.Resources.Definitions;

namespace Sanctuary.Game.Tests;

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

    [TestMethod]
    public void ExclusiveBranchesSharingAPrerequisite_OfferOnlyOne()
    {
        // The player finished the shared prerequisite and took branch A; branch B must not be offered too.
        var playerQuests = new Dictionary<int, bool> { [Shared] = true, [BranchA] = false };

        Assert.IsFalse(Branch(BranchB, excludes: BranchA).IsOfferableFor(playerQuests));
    }

    [TestMethod]
    public void BothBranches_AreOfferedUntilOneIsTaken()
    {
        var playerQuests = new Dictionary<int, bool> { [Shared] = true };

        Assert.IsTrue(Branch(BranchA, excludes: BranchB).IsOfferableFor(playerQuests));
        Assert.IsTrue(Branch(BranchB, excludes: BranchA).IsOfferableFor(playerQuests));
    }

    [TestMethod]
    public void Prerequisite_StillHasToBeFinished()
    {
        Assert.IsFalse(Branch(BranchA, excludes: BranchB).IsOfferableFor(new Dictionary<int, bool>()));
        Assert.IsFalse(Branch(BranchA, excludes: BranchB).IsOfferableFor(new Dictionary<int, bool> { [Shared] = false }));
    }
}
