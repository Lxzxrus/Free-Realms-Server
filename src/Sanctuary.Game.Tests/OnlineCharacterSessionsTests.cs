using System.Linq;

using Microsoft.VisualStudio.TestTools.UnitTesting;

using Sanctuary.Game.Helpers;
using Sanctuary.Packet;

namespace Sanctuary.Game.Tests;

/// <summary>
/// The Login server's online list for one Gateway, and the packets the two servers use to keep it right after a
/// crash or restart (upstream OSFR PR #190).
/// </summary>
[TestClass]
public sealed class OnlineCharacterSessionsTests
{
    private const ulong Character = 7;

    [TestMethod]
    public void Logout_CountsOnlyForTheSessionThatIsOnline()
    {
        var online = new OnlineCharacterSessions();

        online.MarkOnline(Character, session: 1);
        online.MarkOnline(Character, session: 2);

        Assert.IsFalse(online.TryLogout(Character, session: 1), "The old session's late logout.");
        Assert.IsTrue(online.IsOnline(Character), "The newer login stays online.");

        Assert.IsTrue(online.TryLogout(Character, session: 2));
        Assert.IsFalse(online.IsOnline(Character));
        Assert.AreEqual(0, online.Count);
    }

    [TestMethod]
    public void Release_ClearsWhateverSessionWasOnline()
    {
        var online = new OnlineCharacterSessions();

        online.MarkOnline(Character, session: 5);
        online.MarkReleased(Character);

        Assert.IsFalse(online.IsOnline(Character));
        Assert.IsFalse(online.TryLogout(Character, session: 5), "Its logout after the release changes nothing.");
    }

    [TestMethod]
    public void LoginAndLogoutPackets_CarryTheSession()
    {
        var login = new GatewayCharacterLogin { Id = Character, Session = ulong.MaxValue - 1 };
        Assert.IsTrue(GatewayCharacterLogin.TryDeserialize(login.Serialize(), out var readLogin));
        Assert.AreEqual(Character, readLogin.Id);
        Assert.AreEqual(ulong.MaxValue - 1, readLogin.Session);

        var logout = new GatewayCharacterLogout { id = Character, Session = 3 };
        Assert.IsTrue(GatewayCharacterLogout.TryDeserialize(logout.Serialize(), out var readLogout));
        Assert.AreEqual(Character, readLogout.id);
        Assert.AreEqual(3UL, readLogout.Session);

        // A Gateway from before the session field sends only the id; it is refused, not read as session 0.
        var oldLogout = logout.Serialize()[..^8];
        Assert.IsFalse(GatewayCharacterLogout.TryDeserialize(oldLogout, out _));
    }

    [TestMethod]
    public void ReleasePackets_RoundTripAndRefuseExtraBytes()
    {
        var request = new GatewayCharacterReleaseRequest { RequestId = 9, Id = Character, IdleThresholdMilliseconds = 5000 };
        Assert.IsTrue(GatewayCharacterReleaseRequest.TryDeserialize(request.Serialize(), out var readRequest));
        Assert.AreEqual(9U, readRequest.RequestId);
        Assert.AreEqual(Character, readRequest.Id);
        Assert.AreEqual(5000U, readRequest.IdleThresholdMilliseconds);

        var reply = new GatewayCharacterReleaseReply { RequestId = 9, Released = true };
        Assert.IsTrue(GatewayCharacterReleaseReply.TryDeserialize(reply.Serialize(), out var readReply));
        Assert.AreEqual(9U, readReply.RequestId);
        Assert.IsTrue(readReply.Released);

        Assert.IsFalse(GatewayCharacterReleaseReply.TryDeserialize(reply.Serialize().Append((byte)0).ToArray(), out _));
        Assert.IsFalse(GatewayCharacterReleaseRequest.TryDeserialize(reply.Serialize(), out _), "Wrong opcode.");
    }
}
