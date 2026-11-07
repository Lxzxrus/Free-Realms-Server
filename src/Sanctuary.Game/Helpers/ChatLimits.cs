using System;

using Sanctuary.Game.Entities;
using Sanctuary.Packet;
using Sanctuary.Packet.Common.Chat;

namespace Sanctuary.Game.Helpers;

/// <summary>
/// What a player's chat may be. A modified client can send any channel, any length and any number of messages, and a
/// shout reaches every player online, so the server holds chat to what the real client sends.
/// </summary>
public static class ChatLimits
{
    /// <summary>
    /// The client's chat box takes 300 characters (chatWindowCtrl.gfx, <c>TextEntryBox.entry_txt.maxChars</c>); this
    /// leaves room for anything the client adds to a message before sending it.
    /// </summary>
    public const int MaxMessageLength = 512;

    /// <summary>
    /// The longest first or last name a chat packet may give for whom it is to. A character's names are at most 14
    /// characters each (CheckNamePacketHandler).
    /// </summary>
    public const int MaxNameLength = 64;

    /// <summary>Messages a player may send at once: typed chat, quick chat and chat commands alike...</summary>
    public const int Burst = 6;

    /// <summary>...and then one more every this many milliseconds.</summary>
    public const int RefillMs = 1000;

    private const int TooFastNoticeIntervalMs = 5000;

    /// <summary>
    /// Whether a client may send on this channel. System is the server's own: sent by a player, it reaches the players
    /// around them as a system message. Any other value isn't a channel at all.
    /// </summary>
    public static bool IsClientChannel(ChatChannel channel) => channel != ChatChannel.System && Enum.IsDefined(channel);

    /// <summary>
    /// Whether the message, and the name the client gave for whom it is to, are no longer than the real client sends.
    /// Each could otherwise be as long as a whole packet (64 KiB), and the packet is relayed as it came.
    /// </summary>
    public static bool IsClientSized(PacketChat packet) =>
        packet.Message is { Length: <= MaxMessageLength }
        && packet.ToName.FirstName.Length <= MaxNameLength
        && packet.ToName.LastName.Length <= MaxNameLength;

    /// <summary>
    /// Whether the player may send another message now. If not, they're told so, at most once every few seconds.
    /// Staff (admins and mods) always may: repeating a command with the chat box's up arrow and Enter ran past the
    /// allowance, and every command after it was lost.
    /// </summary>
    public static bool TryUseAllowance(Player player)
    {
        if (player.IsAdmin || player.IsMod)
            return true;

        var now = Environment.TickCount64;

        if (player.ChatAllowance.TryUse(now))
            return true;

        if (now >= player.NextChatTooFastNoticeAt)
        {
            player.NextChatTooFastNoticeAt = now + TooFastNoticeIntervalMs;
            ChatHelper.SendSystemMessage(player, "You're sending messages too fast. Wait a moment, then try again.");
        }

        return false;
    }
}
