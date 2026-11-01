using System;
using System.Collections.Generic;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

using Sanctuary.Game;
using Sanctuary.Game.Entities;
using Sanctuary.Game.Helpers;
using Sanctuary.Core.IO;
using Sanctuary.Packet;
using Sanctuary.Packet.Common;
using Sanctuary.Packet.Common.Attributes;

namespace Sanctuary.Gateway.Handlers;

[PacketHandler]
public static class PlayerUpdatePacketItemDefinitionRequestHandler
{
    private const int CoinStoreItemDefinitionBatchSize = 100;

    private static ILogger _logger = null!;
    private static IResourceManager _resourceManager = null!;

    public static void ConfigureServices(IServiceProvider serviceProvider)
    {
        var loggerFactory = serviceProvider.GetRequiredService<ILoggerFactory>();
        _logger = loggerFactory.CreateLogger(nameof(PlayerUpdatePacketItemDefinitionRequestHandler));

        _resourceManager = serviceProvider.GetRequiredService<IResourceManager>();
    }

    public static bool HandlePacket(GatewayConnection connection, ReadOnlySpan<byte> data)
    {
        if (!PlayerUpdatePacketItemDefinitionRequest.TryDeserialize(data, out var playerUpdatePacketItemDefinitionRequest))
        {
            _logger.LogError("Failed to deserialize {packet}.", nameof(PlayerUpdatePacketItemDefinitionRequest));
            return false;
        }

        _logger.LogTrace("Received {name} packet. ( {packet} )", nameof(PlayerUpdatePacketItemDefinitionRequest), playerUpdatePacketItemDefinitionRequest);

        if (!_resourceManager.ClientItemDefinitions.TryGetValue(playerUpdatePacketItemDefinitionRequest.Id, out var clientItemDefinition))
        {
            _logger.LogWarning("Received request for unknown item definition. Id: {id}", playerUpdatePacketItemDefinitionRequest.Id);
            return true;
        }

        if (_resourceManager.CoinStoreItems.TryGetValue(clientItemDefinition.Id, out var itemDefinitionMetaData))
        {
            SendCoinStoreItemDefinitions(connection.Player, itemDefinitionMetaData);
            return true;
        }

        ItemDefinitionHelper.SendItemDefinition(connection.Player, _resourceManager, clientItemDefinition);

        return true;
    }

    private static void SendCoinStoreItemDefinitions(Player player, ItemDefinitionMetaData requestedItemDefinitionMetaData)
    {
        var clientItemDefinitions = new List<ClientItemDefinition>();

        // The requested item and the ones after it in its category, a batch at most, which the coin store indexed when it
        // loaded. A repeated request costs no more than a first one: this used to copy and walk all 17,000 coin store
        // items on every request, since items already sent didn't count towards the batch.
        foreach (var coinStoreItemId in _resourceManager.CoinStoreItems.GetCategoryItemIdsFrom(requestedItemDefinitionMetaData.Id,
            CoinStoreItemDefinitionBatchSize))
        {
            if (!player.SentCoinStoreItemDefinitionIds.TryAdd(coinStoreItemId) && coinStoreItemId != requestedItemDefinitionMetaData.Id)
                continue;

            if (_resourceManager.ClientItemDefinitions.TryGetValue(coinStoreItemId, out var clientItemDefinition))
                clientItemDefinitions.Add(clientItemDefinition);
        }

        using var writer = new PacketWriter();

        writer.Write(clientItemDefinitions);

        var playerUpdatePacketItemDefinitions = new PlayerUpdatePacketItemDefinitions();

        playerUpdatePacketItemDefinitions.Payload = writer.Buffer;

        player.SendTunneled(playerUpdatePacketItemDefinitions);
    }
}