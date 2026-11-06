using System;
using System.Reflection;
using System.Runtime.CompilerServices;

using Microsoft.VisualStudio.TestTools.UnitTesting;

using Sanctuary.Core.Helpers;
using Sanctuary.Game.Zones;

namespace Sanctuary.Game.Tests;

/// <summary>
/// The GUID layouts after upstream OSFR's #164. Players, houses and fixtures keep the layouts Evergrove's clients have
/// always seen (the id shifted over a type nibble: 1, 2, 3); NPCs and guilds take upstream's. A zone spawns a fixture's
/// actor under the fixture's own GUID: upstream re-encodes every GUID as an NPC's, which throws on a fixture's and would
/// leave every house empty.
/// </summary>
[TestClass]
public sealed class GuidLayoutTests
{
    [TestMethod]
    public void PlayersHousesAndFixtures_KeepTheirLayouts()
    {
        Assert.AreEqual(0x51UL, GuidHelper.GetPlayerGuid(5));
        Assert.AreEqual((7UL << 4) | 2, GuidHelper.GetHouseGuid(7));
        Assert.AreEqual((7UL << 4) | 3, GuidHelper.GetFixtureGuid(7));

        Assert.AreEqual(7UL, GuidHelper.GetHouseId(GuidHelper.GetHouseGuid(7)));
        Assert.AreEqual(7UL, GuidHelper.GetFixtureId(GuidHelper.GetFixtureGuid(7)));
        Assert.AreNotEqual(GuidHelper.GetHouseGuid(7), GuidHelper.GetFixtureGuid(7), "A house and a fixture with one id differ.");
        Assert.IsFalse(GuidHelper.IsNpcGuid(GuidHelper.GetFixtureGuid(7)));
    }

    [TestMethod]
    public void FixedNpcs_TakeTheirNpcIdAsTheCounter()
    {
        var guid = GuidHelper.GetNpcGuid(1201, GuidHelper.OpenWorldInstanceId);

        Assert.AreEqual((1201UL << 36) | (1UL << 4) | 2, guid);
        Assert.IsTrue(GuidHelper.IsFixedNpcGuid(guid));
        Assert.AreEqual(1201UL, GuidHelper.GetNpcCounter(guid));
    }

    [TestMethod]
    public void Guilds_TakeUpstreamsPrefix()
    {
        Assert.AreEqual((0x07DDUL << 48) | 3, GuidHelper.GetGuildGuid(3));
        Assert.AreEqual(3UL, GuidHelper.GetGuildId(GuidHelper.GetGuildGuid(3)));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => GuidHelper.GetGuildId(3));
    }

    [TestMethod]
    public void AHouseZone_KeepsAFixturesGuid_AndGivesAnNpcItsInstance()
    {
        // Only the zone's choice of GUID is exercised, and it reads nothing but the owner and the zone id.
        var zone = (BaseZone)RuntimeHelpers.GetUninitializedObject(typeof(HousingZone));
        typeof(BaseZone).GetProperty(nameof(BaseZone.OwnerId))!.SetValue(zone, 10UL);
        typeof(BaseZone).GetProperty(nameof(BaseZone.Id))!.SetValue(zone, 42);

        var getNpcGuid = typeof(BaseZone).GetMethod("GetNpcGuid", BindingFlags.Instance | BindingFlags.NonPublic)!;

        var fixtureGuid = GuidHelper.GetFixtureGuid(12345);
        Assert.AreEqual(fixtureGuid, (ulong)getNpcGuid.Invoke(zone, [fixtureGuid])!, "A fixture's actor keeps the fixture's GUID.");

        var openWorldGuid = GuidHelper.GetNpcGuid(1201, GuidHelper.OpenWorldInstanceId);
        var houseGuid = (ulong)getNpcGuid.Invoke(zone, [openWorldGuid])!;

        Assert.AreEqual(1201UL, GuidHelper.GetNpcCounter(houseGuid));
        Assert.AreNotEqual(openWorldGuid, houseGuid, "An NPC in a house gets the house's instance id.");
    }
}
