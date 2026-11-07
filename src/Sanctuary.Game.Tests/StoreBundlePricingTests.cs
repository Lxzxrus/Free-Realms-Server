using System;
using System.IO;
using System.Linq;

using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.VisualStudio.TestTools.UnitTesting;

using Sanctuary.Game.Helpers;
using Sanctuary.Game.Resources;
using Sanctuary.Packet.Common.GameCommerce;

namespace Sanctuary.Game.Tests;

/// <summary>
/// Station Cash prices: members pay the listed price until the economy pass (Nate, 2026-10-29).
/// </summary>
[TestClass]
public sealed class StoreBundlePricingTests
{
    /// <summary>
    /// The 108 bundles upstream OSFR's pets and NPC movement merge added to StoreBundles.json (catch-up 1 of 3): pets,
    /// mounts and the Mystery Cake. Upstream listed members' prices of 1 (and 10 for the cake) whatever the list price.
    /// </summary>
    private static readonly int[] UpstreamAddedBundleIds =
    [
        2172, 2272, 2274, 2807, 3059, 7885, 7886, 7888, 7889, 7890,
        10353, 10366, 10383, 10387, 10407, 10408, 10417, 10421, 10424, 10469,
        10993, 10994, 10995, 10996, 11584, 11614, 11763, 11939, 14184, 14316,
        17179, 17514, 17517, 17520, 17521, 17522, 17529, 17536, 17554, 17556,
        17557, 23935, 23941, 23963, 23964, 24058, 30589, 38788, 69824, 76528,
        76535, 76613, 77461, 77471, 79413, 79417, 79433, 79434, 79435, 79436,
        79437, 79440, 79441, 79442, 79443, 79444, 79445, 79446, 79448, 79449,
        79450, 79451, 79452, 79453, 79455, 79456, 79457, 79459, 79461, 79462,
        79463, 79464, 900118, 950009, 950010, 950011, 950012, 950013, 950014, 950015,
        950016, 950017, 950018, 950019, 950020, 950021, 950022, 950023, 950024, 950025,
        950026, 950027, 950101, 950102, 950103, 950104, 950105, 950106
    ];

    private static string FindResourcesDirectory()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            var candidate = Path.Combine(directory.FullName, "Resources");
            if (File.Exists(Path.Combine(candidate, "StoreBundles.json")))
                return candidate;
        }

        throw new DirectoryNotFoundException("src/Resources not found above the test output.");
    }

    [TestMethod]
    public void UpstreamAddedBundles_ChargeMembersTheirListedPrice()
    {
        Assert.AreEqual(108, UpstreamAddedBundleIds.Distinct().Count());

        var resources = FindResourcesDirectory();
        var stores = new StoreDefinitionCollection(NullLogger.Instance);
        Assert.IsTrue(stores.Load(Path.Combine(resources, "Stores.json")), "Stores.json failed to load.");
        Assert.IsTrue(stores.LoadBundles(Path.Combine(resources, "StoreBundles.json")), "StoreBundles.json failed to load.");

        var bundles = stores.Values.SelectMany(store => store.Bundles.Values).ToDictionary(bundle => bundle.Id);

        foreach (var id in UpstreamAddedBundleIds)
        {
            Assert.IsTrue(bundles.TryGetValue(id, out var bundle), $"Bundle {id} is missing.");

            // None of them is housing inventory, so a member is charged the bundle's MemberDiscount.
            Assert.AreEqual(bundle.Price, StoreBundlePricing.GetCost(bundle, isMember: true, containsHousingInventory: false),
                $"Bundle {id} charges members something other than its listed price.");

            // The client shows members MembersOnlyPrice, so it must say the same.
            Assert.AreEqual(bundle.Price, bundle.MembersOnlyPrice, $"Bundle {id} shows members another price than it charges.");
        }
    }

    [TestMethod]
    public void GetCost_ChargesNonMembersTheListPrice_AndMembersTheirPrice()
    {
        var bundle = new AppStoreBundleDefinition { Price = 500, MemberDiscount = 400, MembersOnlyPrice = 300 };

        Assert.AreEqual(500, StoreBundlePricing.GetCost(bundle, isMember: false, containsHousingInventory: false));
        Assert.AreEqual(500, StoreBundlePricing.GetCost(bundle, isMember: false, containsHousingInventory: true));
        Assert.AreEqual(400, StoreBundlePricing.GetCost(bundle, isMember: true, containsHousingInventory: false));
        Assert.AreEqual(300, StoreBundlePricing.GetCost(bundle, isMember: true, containsHousingInventory: true));
    }
}
