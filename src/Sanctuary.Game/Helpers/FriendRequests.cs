using System.Linq;

using Sanctuary.Game.Entities;
using Sanctuary.Packet;
using Sanctuary.Packet.Common;

namespace Sanctuary.Game.Helpers;

public enum FriendRequestResult
{
    Sent,
    ToSelf,
    AlreadyFriends,
    AlreadyRequested,
    Ignored
}

/// <summary>
/// Sending a friend request, from the friend list's Add box or from another player's menu.
/// </summary>
public static class FriendRequests
{
    public static FriendRequestResult Send(Player from, Player to)
    {
        if (to.Guid == from.Guid)
            return Tell(from, FriendRequestResult.ToSelf, to);

        // Someone who ignores the sender never sees the request, but the sender is told it was sent, as for anyone
        // else, so a request can't be used to find out who ignores you.
        if (to.Ignores.Any(x => x.Guid == from.Guid))
        {
            SendRequested(from, to);
            return FriendRequestResult.Ignored;
        }

        if (to.Friends.Any(x => x.Guid == from.Guid))
            return Tell(from, FriendRequestResult.AlreadyFriends, to);

        if (!to.IncomingFriendRequests.TryAdd(from.Guid))
            return Tell(from, FriendRequestResult.AlreadyRequested, to);

        SendRequested(from, to);

        to.SendTunneled(new CommandPacketConfirmFriendRequest
        {
            Guid = from.Guid,
            Name = from.Name
        });

        return FriendRequestResult.Sent;
    }

    /// <summary>What the sender is told when a request isn't sent. The client has its own message for a sent one.</summary>
    public static string? Explain(FriendRequestResult result, string name) => result switch
    {
        FriendRequestResult.ToSelf => "You can't add yourself as a friend.",
        FriendRequestResult.AlreadyFriends => $"{name} is already your friend.",
        FriendRequestResult.AlreadyRequested => $"You've already sent {name} a friend request.",
        _ => null
    };

    public static string NoSuchPlayer(string name) => $"There's no player named {name}.";

    public static string Offline(string name) => $"{name} isn't online. Friend requests can only be sent to players who are online.";

    private static FriendRequestResult Tell(Player from, FriendRequestResult result, Player to)
    {
        if (Explain(result, to.Name.FullName) is string message)
            ChatHelper.SendSystemMessage(from, message);

        return result;
    }

    private static void SendRequested(Player from, Player to)
    {
        from.SendTunneled(new FriendMessagePacket
        {
            Type = FriendMessageType.FriendAddRequested,
            Guid = to.Guid,
            Name = to.Name
        });
    }
}
