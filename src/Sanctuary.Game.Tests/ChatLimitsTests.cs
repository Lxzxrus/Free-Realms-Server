using Microsoft.VisualStudio.TestTools.UnitTesting;

using Sanctuary.Game.Helpers;
using Sanctuary.Packet;
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
    public void IsClientSized_TakesWhatTheChatBoxSends()
    {
        // A full chat box, as a tell to the longest name a character can have.
        var packet = new PacketChat { Channel = ChatChannel.Tell, Message = new string('a', 300) };
        packet.ToName.FirstName = new string('b', 14);
        packet.ToName.LastName = new string('c', 14);

        Assert.IsTrue(ChatLimits.IsClientSized(packet));
        Assert.IsTrue(ChatLimits.IsClientSized(new PacketChat { Channel = ChatChannel.WorldShout, Message = string.Empty }));
    }

    [TestMethod]
    public void IsClientSized_RefusesALongMessageOrName()
    {
        Assert.IsFalse(ChatLimits.IsClientSized(new PacketChat { Message = new string('a', ChatLimits.MaxMessageLength + 1) }));
        Assert.IsFalse(ChatLimits.IsClientSized(new PacketChat { Message = null }));

        // A short shout carrying a long name went to every player online.
        var longFirstName = new PacketChat { Channel = ChatChannel.WorldShout, Message = "hi" };
        longFirstName.ToName.FirstName = new string('b', ChatLimits.MaxNameLength + 1);
        Assert.IsFalse(ChatLimits.IsClientSized(longFirstName));

        var longLastName = new PacketChat { Channel = ChatChannel.WorldShout, Message = "hi" };
        longLastName.ToName.LastName = new string('c', ChatLimits.MaxNameLength + 1);
        Assert.IsFalse(ChatLimits.IsClientSized(longLastName));
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
