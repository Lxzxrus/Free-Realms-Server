using System;

using Sanctuary.Core.IO;

namespace Sanctuary.Packet;

public class QuestReplyPacket : BaseQuestPacket, IDeserializable<QuestReplyPacket>
{
    public const int SubOpCode = 2;

    public int QuestId;
    public bool Accepted;

    public QuestReplyPacket() : base(SubOpCode)
    {
    }

    public static bool TryDeserialize(ReadOnlySpan<byte> data, out QuestReplyPacket value)
    {
        value = new QuestReplyPacket();

        var reader = new PacketReader(data);

        if (!value.TryRead(ref reader))
            return false;

        if (!reader.TryRead(out value.QuestId))
            return false;

        if (!reader.TryRead(out value.Accepted))
            return false;

        return true;
    }
}
