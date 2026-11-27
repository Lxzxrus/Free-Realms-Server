using System;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

using Sanctuary.Packet;
using Sanctuary.Packet.Common.Attributes;

namespace Sanctuary.Gateway.Handlers;

[PacketHandler]
public static class PacketClientLogHandler
{
    private static ILogger _logger = null!;

    public static void ConfigureServices(IServiceProvider serviceProvider)
    {
        var loggerFactory = serviceProvider.GetRequiredService<ILoggerFactory>();
        _logger = loggerFactory.CreateLogger(nameof(PacketClientLogHandler));
    }

    public static bool HandlePacket(GatewayConnection connection, Span<byte> data)
    {
        if (!PacketClientLog.TryDeserialize(data, out var packet))
        {
            _logger.LogError("Failed to deserialize {packet}.", nameof(PacketClientLog));
            return false;
        }

        _logger.LogTrace("Received {name} packet. ( {packet} )", nameof(PacketClientLog), packet);

        // TODO: Save to a separate file based on packet value

        // The client reports each loading screen once it has dropped (MarketingData.log, "Viewed loading screen
        // <movie> for <n> seconds."). A fishing hole the map took the player to offers its start screen then.
        if (packet.Message?.StartsWith("Viewed loading screen", StringComparison.Ordinal) == true)
            FishingHandler.OnLoadingScreenDropped(connection);

        return true;
    }
}