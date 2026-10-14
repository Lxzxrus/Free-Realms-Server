using System;

using Sanctuary.Core.IO;

namespace Sanctuary.Packet;

/// <summary>
/// Sent by <c>MiniGameFlashC.StartWheel()</c> (FreeRealms.exe 0x9bdbd0) when the player opens the daily wheel and the
/// client believes a spin is left. It carries nothing the server needs.
/// </summary>
public class CommandPacketInteractionStartWheel : BaseCommandPacket, IDeserializable<CommandPacketInteractionStartWheel>
{
    public new const short OpCode = 11;

    public CommandPacketInteractionStartWheel() : base(OpCode)
    {
    }

    public static bool TryDeserialize(ReadOnlySpan<byte> data, out CommandPacketInteractionStartWheel value)
    {
        value = new CommandPacketInteractionStartWheel();

        var reader = new PacketReader(data);

        return value.TryRead(ref reader);
    }
}
