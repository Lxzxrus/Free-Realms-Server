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

        // Evergrove answers with an empty list, not upstream's Marketplace announcement (PR #118). The welcome window
        // still shows its built-in "Hot New Items!" ad: it reads the list before this answer arrives and draws that ad
        // when the list is empty.
        connection.SendTunneled(WelcomeScreenHelper.GetAnnouncementsPacket([]));
        return true;
    }
}
