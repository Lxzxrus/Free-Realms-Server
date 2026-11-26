using Sanctuary.Game.ChatCommands;
using Sanctuary.Game.Entities;
using Sanctuary.Packet;

namespace Sanctuary.Game.Helpers;

public static class ChatHelper
{
    public static void SendSystemMessage(Player player, string message, bool formatted = false)
    {
        player.SendTunneled(new ChatPacketDebugChat
        {
            PrintToChat = true,
            Message = formatted ? message : EscapeMarkup(message)
        });
    }

    private static string EscapeMarkup(string message)
    {
        return message
            .Replace("&", "&amp;")
            .Replace("<", "&lt;")
            .Replace(">", "&gt;");
    }

    /// <summary>
    /// The role an account's stored flags give it, with no Debug override: a plain account is a Player in every build.
    /// For data about accounts (who a report is about), not for what a logged-in player may run.
    /// </summary>
    public static ChatCommandRole GetStoredRole(bool isAdmin, bool isMod)
    {
        if (isAdmin)
            return ChatCommandRole.Admin;

        return isMod ? ChatCommandRole.Mod : ChatCommandRole.Player;
    }

    public static ChatCommandRole GetRoleFromFlags(bool isAdmin, bool isMod)
    {
        if (isAdmin)
            return ChatCommandRole.Admin;

        if (isMod)
            return ChatCommandRole.Mod;

#if DEBUG
        return ChatCommandRole.Admin;
#else
        return ChatCommandRole.Player;
#endif
    }
}
