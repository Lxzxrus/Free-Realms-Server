using System.Globalization;
using System.Numerics;

using Sanctuary.Game.Entities;
using Sanctuary.Game.Helpers;

using Sanctuary.Game;
using Sanctuary.Packet;
using Sanctuary.Packet.Common.Attributes;

namespace Sanctuary.Game.ChatCommands;

public class TeleportCommand : IChatCommand
{
    public string KeyWord => "teleport";
    public string Usage => "<x> <y> <z>";
    public string Description => "Teleports you to the specified coordinates.";
    public ChatCommandRole RequiredRole => ChatCommandRole.Mod;

    public bool Handle(Player invoker, string[] args)
    {
        if (args.Length != 3)
        {
            ChatHelper.SendSystemMessage(invoker, "Invalid usage. Use: !teleport <x> <y> <z>");
            return false;
        }

        if (!TryGetTarget(args, invoker.Position, out var position))
        {
            ChatHelper.SendSystemMessage(invoker, "Invalid coordinates. Please enter valid numbers.");
            return false;
        }

        // The server moves the player first, as the other teleports do. It used to pass the old position here, so
        // until the player next moved, who they could see and the position saved on logout were where they had left.
        var rotation = invoker.Rotation;

        invoker.UpdatePosition(position, rotation, false);

        var clientUpdatePacketUpdateLocation = new ClientUpdatePacketUpdateLocation
        {
            Position = position,
            Rotation = rotation,
            Teleport = true
        };
        invoker.SendTunneled(clientUpdatePacketUpdateLocation);
        ChatHelper.SendSystemMessage(invoker, $"Teleported to ({position.X:F2}, {position.Y:F2}, {position.Z:F2})");
        return true;
    }

    /// <summary>
    /// The position <c>!teleport x y z</c> names, keeping <paramref name="current"/>'s W. Numbers are read with a dot,
    /// whatever the server's culture, as <c>/whereami</c> prints them.
    /// </summary>
    public static bool TryGetTarget(string[] args, Vector4 current, out Vector4 target)
    {
        target = current;

        if (args.Length != 3 ||
            !float.TryParse(args[0], NumberStyles.Float, CultureInfo.InvariantCulture, out var x) || !float.IsFinite(x) ||
            !float.TryParse(args[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var y) || !float.IsFinite(y) ||
            !float.TryParse(args[2], NumberStyles.Float, CultureInfo.InvariantCulture, out var z) || !float.IsFinite(z))
        {
            return false;
        }

        target = new Vector4(x, y, z, current.W);
        return true;
    }
}
