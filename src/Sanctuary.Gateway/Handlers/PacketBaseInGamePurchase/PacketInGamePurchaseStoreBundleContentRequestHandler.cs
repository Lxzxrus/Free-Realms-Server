using System;
using System.Collections.Generic;
using System.Linq;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

using Sanctuary.Core.IO;
using Sanctuary.Game;
using Sanctuary.Game.Helpers;
using Sanctuary.Packet;
using Sanctuary.Packet.Common;
using Sanctuary.Packet.Common.Attributes;

namespace Sanctuary.Gateway.Handlers;

[PacketHandler]
public static class PacketInGamePurchaseStoreBundleContentRequestHandler
{
    // The client asks for the contents of two bundles at a time at most, and no bundle holds more than 16 items
    // (StoreBundles.json), so a request naming more items than this is made up, to make the server look up and send a
    // definition for every id in it.
    private const int MaxMarketingItemIdsPerRequest = 256;

    private static ILogger _logger = null!;
    private static IResourceManager _resourceManager = null!;

    public static void ConfigureServices(IServiceProvider serviceProvider)
    {
        var loggerFactory = serviceProvider.GetRequiredService<ILoggerFactory>();
        _logger = loggerFactory.CreateLogger(nameof(PacketBaseInGamePurchaseHandler));

        _resourceManager = serviceProvider.GetRequiredService<IResourceManager>();
    }

    public static bool HandlePacket(GatewayConnection connection, ReadOnlySpan<byte> data)
    {
        if (!PacketInGamePurchaseStoreBundleContentRequest.TryDeserialize(data, out var packet))
        {
            _logger.LogError("Failed to deserialize {packet}.", nameof(PacketInGamePurchaseStoreBundleContentRequest));
            return false;
        }

        _logger.LogTrace("Received {name} packet. ( {packet} )", nameof(PacketInGamePurchaseStoreBundleContentRequest), packet);

        if (packet.Requests.Sum(request => request.MarketingItemIds.Count) > MaxMarketingItemIdsPerRequest)
            return true;

        var packetInGamePurchaseStoreBundleContentResponse = new PacketInGamePurchaseStoreBundleContentResponse();

        foreach (var request in packet.Requests)
        {
            packetInGamePurchaseStoreBundleContentResponse.Responses.Add(new PacketInGamePurchaseStoreBundleContentResponse.ResponseData
            {
                StoreId = request.StoreId,
                BundleId = request.BundleId,
            });
        }

        connection.SendTunneled(packetInGamePurchaseStoreBundleContentResponse);

        List<ClientItemDefinition> clientItemDefinitions = [];
        HashSet<int> marketingItemIds = [];

        foreach (var request in packet.Requests)
        {
            foreach (var marketingItemId in request.MarketingItemIds)
            {
                // An item two bundles share needs its definition once.
                if (!marketingItemIds.Add(marketingItemId))
                    continue;

                if (!_resourceManager.ClientItemDefinitions.TryGetValue(marketingItemId, out var clientItemDefinition))
                {
                    _logger.LogWarning("Received request for unknown item definition. Id: {id}", marketingItemId);
                    continue;
                }

                clientItemDefinitions.Add(ItemDefinitionHelper.GetPlayerItemDefinition(connection.Player, _resourceManager, clientItemDefinition));
            }
        }

        if (clientItemDefinitions.Count == 0)
            return true;

        using var writer = new PacketWriter();

        writer.Write(clientItemDefinitions);

        var playerUpdatePacketItemDefinitions = new PlayerUpdatePacketItemDefinitions();

        playerUpdatePacketItemDefinitions.Payload = writer.Buffer;

        connection.SendTunneled(playerUpdatePacketItemDefinitions);

        return true;
    }
}