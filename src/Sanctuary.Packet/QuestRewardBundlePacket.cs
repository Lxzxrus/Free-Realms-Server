using Sanctuary.Core.IO;
using Sanctuary.Packet.Common;

namespace Sanctuary.Packet;

public class QuestRewardBundlePacket : RewardBasePacket, ISerializablePacket
{
    public const byte SubOpCode = 1;

    public RewardBundleBase RewardBundle { get; } = new();

    public QuestRewardBundlePacket() : base(SubOpCode)
    {
    }

    public byte[] Serialize()
    {
        using var writer = new PacketWriter();

        Write(writer);

        // The trailing int (formerly Unknown15) is RewardBundle.DescriptionId, written by the bundle itself.
        RewardBundle.Serialize(writer);

        return writer.Buffer;
    }
}
