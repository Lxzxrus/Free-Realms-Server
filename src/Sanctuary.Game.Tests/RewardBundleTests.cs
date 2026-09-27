using Microsoft.VisualStudio.TestTools.UnitTesting;

using Sanctuary.Core.IO;
using Sanctuary.Packet.Common;

namespace Sanctuary.Game.Tests;

[TestClass]
public sealed class RewardBundleTests
{
    private static int Length(bool success, int itemCount)
    {
        var bundle = new RewardBundleBase { Success = success };

        for (var i = 0; i < itemCount; i++)
            bundle.Entries.Add(new RewardBundleEntryItem { IconId = 1, NameId = 2, ItemGuid = 3 });

        using var writer = new PacketWriter();
        bundle.Serialize(writer);

        return writer.Buffer.Length;
    }

    /// <summary>
    /// A quest offer previews its rewards, and the client reads a preview's entries without their
    /// type-specific tail. Writing the tail anyway misaligns the whole offer, so the client drops it.
    /// </summary>
    [TestMethod]
    public void Preview_LeavesOutEachItemsGuid()
    {
        var itemInPreview = Length(success: false, itemCount: 1) - Length(success: false, itemCount: 0);
        var itemInGrant = Length(success: true, itemCount: 1) - Length(success: true, itemCount: 0);

        Assert.AreEqual(sizeof(int), itemInGrant - itemInPreview);
    }

    [TestMethod]
    public void Preview_LeavesOutTheTailOfEveryEntry()
    {
        var twoInPreview = Length(success: false, itemCount: 2) - Length(success: false, itemCount: 0);
        var twoInGrant = Length(success: true, itemCount: 2) - Length(success: true, itemCount: 0);

        Assert.AreEqual(2 * sizeof(int), twoInGrant - twoInPreview);
    }

    /// <summary>
    /// The collection toast never sets <see cref="RewardBundleBase.Success"/>, so it relies on a bundle
    /// being a grant, tail included, unless it is marked as a preview.
    /// </summary>
    [TestMethod]
    public void Bundle_IsAGrantUnlessMarkedAsAPreview()
    {
        Assert.IsTrue(new RewardBundleBase().Success);
    }
}
