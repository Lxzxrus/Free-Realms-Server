using Sanctuary.Core.IO;

namespace Sanctuary.Packet.Common;

/// <summary>
/// Experience for one job. <see cref="RewardBundleEntryBase.Quantity"/> is the amount and
/// <see cref="RewardBundleEntryBase.DefinitionId"/> the profile it goes to.
/// </summary>
public sealed class RewardBundleEntryExperience : RewardBundleEntryBase
{
    public override RewardBundleEntryType Type => RewardBundleEntryType.Experience;

    protected override void SerializeData(PacketWriter writer)
    {
        // The 2014 captures' collection rows carry an experience entry with no bytes after the common
        // fields, while item and collection entries in the same bundle have their 4-byte tails.
    }
}
