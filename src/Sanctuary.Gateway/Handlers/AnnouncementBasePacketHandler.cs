using Sanctuary.Core.IO;
using Sanctuary.Game.Helpers;
using Sanctuary.Packet.Common.Attributes;

namespace Sanctuary.Gateway.Handlers;

[PacketHandler]
public static class AnnouncementBasePacketHandler
{
    public static bool HandlePacket(GatewayConnection connection, PacketReader reader)
    {
        if (!reader.TryRead(out byte subOpCode) || subOpCode != 1)
            return false;

        // Evergrove: an empty list, not upstream OSFR's "Hot New Items!" Marketplace ad (PR #118 took store ads out of
        // the welcome window; WorldZone doesn't send it at login either).
        connection.SendTunneled(WelcomeScreenHelper.GetAnnouncementsPacket([]));
        return true;
    }
}
