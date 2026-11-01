using Microsoft.VisualStudio.TestTools.UnitTesting;

using Sanctuary.Game.Helpers;
using Sanctuary.Packet.Common.Chat;

namespace Sanctuary.Game.Tests;

[TestClass]
public sealed class ChatLimitsTests
{
    [TestMethod]
    public void IsClientChannel_RefusesSystemAndNonChannels()
    {
        Assert.IsFalse(ChatLimits.IsClientChannel(ChatChannel.System), "A player's System message shows as the server's.");
        Assert.IsFalse(ChatLimits.IsClientChannel((ChatChannel)11));
        Assert.IsFalse(ChatLimits.IsClientChannel((ChatChannel)(-1)));

        foreach (var channel in new[]
                 {
                     ChatChannel.WorldSay, ChatChannel.Tell, ChatChannel.Whisper, ChatChannel.GroupSay, ChatChannel.WorldShout,
                     ChatChannel.WorldTrade, ChatChannel.WorldLfg, ChatChannel.WorldArea, ChatChannel.GuildSay,
                     ChatChannel.WorldMembersOnly
                 })
        {
            Assert.IsTrue(ChatLimits.IsClientChannel(channel), channel.ToString());
        }
    }

    [TestMethod]
    public void Allowance_LetsABurstThroughThenOneARefill()
    {
        var allowance = new Allowance(burst: 3, refillMs: 1000, now: 0);

        Assert.IsTrue(allowance.TryUse(0));
        Assert.IsTrue(allowance.TryUse(0));
        Assert.IsTrue(allowance.TryUse(0));
        Assert.IsFalse(allowance.TryUse(999), "The burst is used up and a second hasn't passed.");

        Assert.IsTrue(allowance.TryUse(1000));
        Assert.IsFalse(allowance.TryUse(1500));
        Assert.IsTrue(allowance.TryUse(2000));
    }

    [TestMethod]
    public void Allowance_RefillsNoFurtherThanTheBurst()
    {
        var allowance = new Allowance(burst: 2, refillMs: 1000, now: 0);

        Assert.IsTrue(allowance.TryUse(0));
        Assert.IsTrue(allowance.TryUse(0));

        // A long quiet spell gives back the burst, not one per second of it.
        Assert.IsTrue(allowance.TryUse(60_000));
        Assert.IsTrue(allowance.TryUse(60_000));
        Assert.IsFalse(allowance.TryUse(60_000));
    }
}
