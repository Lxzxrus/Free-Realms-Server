using System.Collections.Generic;

using Sanctuary.Core.IO;

namespace Sanctuary.Packet.Common;

/// <summary>
/// One collection as the client receives it in the login burst and in <c>ClientUpdatePacketCollectionStart</c>.
/// </summary>
/// <remarks>
/// Layout from the 253 collections in the 2014 <c>SendSelf</c> capture (<c>possible_character_data.pcapng</c>):
/// the header, one <see cref="RewardBundleBase"/> (the same payload as the reward packets, with per-entry tails that
/// depend on the entry type), one more int, then the entries. <see cref="RewardBundleBase.Coins"/> is the coin
/// reward there and <see cref="RewardBundleBase.Unknown3"/> is 15 in every row.
/// </remarks>
public sealed class ClientCollection : ISerializableType
{
    public int Id;
    public int NameId;
    public int DescriptionId;
    public int CategoryId;
    public int IconId;
    public int IconTintId;

    public int Unknown1 = 1;

    public RewardBundleBase Reward { get; } = new()
    {
        Success = true,
        Unknown3 = 15
    };

    /// <summary>
    /// Follows the reward bundle in every captured row (5604 for the Briarwood mushrooms, 5301 to 5314 for the Rare
    /// Exploration collections). Probably a string id for the reward line; the client shows the window either way.
    /// It is the bundle's own trailing <see cref="RewardBundleBase.DescriptionId"/>, which the bundle writes.
    /// </summary>
    public int RewardTextId
    {
        get => Reward.DescriptionId;
        set => Reward.DescriptionId = value;
    }

    public List<ClientCollectionEntry> Entries = [];

    public ulong PlayerGuid
    {
        get => Reward.PlayerGuid;
        set => Reward.PlayerGuid = value;
    }

    public void Serialize(PacketWriter writer)
    {
        writer.Write(Id);
        writer.Write(NameId);
        writer.Write(DescriptionId);
        writer.Write(CategoryId);
        writer.Write(IconId);
        writer.Write(IconTintId);

        writer.Write(Unknown1);

        // Writes RewardTextId as the bundle's trailing DescriptionId.
        Reward.Serialize(writer);

        writer.Write(Entries);
    }
}
