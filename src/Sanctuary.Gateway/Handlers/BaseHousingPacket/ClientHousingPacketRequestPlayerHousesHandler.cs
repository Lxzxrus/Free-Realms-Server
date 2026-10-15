using System;
using System.Collections.Generic;
using System.Linq;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

using Sanctuary.Core.Helpers;
using Sanctuary.Database;
using Sanctuary.Database.Entities;
using Sanctuary.Game;
using Sanctuary.Game.Helpers;
using Sanctuary.Game.Housing;
using Sanctuary.Game.Resources.Definitions.Zones;
using Sanctuary.Packet;
using Sanctuary.Packet.Common;
using Sanctuary.Packet.Common.Attributes;

namespace Sanctuary.Gateway.Handlers;

[PacketHandler]
public static class ClientHousingPacketRequestPlayerHousesHandler
{
    private static ILogger _logger = null!;
    private static IResourceManager _resourceManager = null!;
    private static IDbContextFactory<DatabaseContext> _dbContextFactory = null!;
    private static CompetitionOptions _competitionOptions = null!;

    public static void ConfigureServices(IServiceProvider serviceProvider)
    {
        var loggerFactory = serviceProvider.GetRequiredService<ILoggerFactory>();
        _logger = loggerFactory.CreateLogger(nameof(ClientHousingPacketRequestPlayerHousesHandler));
        _resourceManager = serviceProvider.GetRequiredService<IResourceManager>();
        _dbContextFactory = serviceProvider.GetRequiredService<IDbContextFactory<DatabaseContext>>();
        _competitionOptions = serviceProvider.GetRequiredService<IOptions<CompetitionOptions>>().Value;
    }

    public static bool HandlePacket(GatewayConnection connection, ReadOnlySpan<byte> data)
    {
        if (!ClientHousingPacketRequestPlayerHouses.TryDeserialize(data, out var packet))
        {
            _logger.LogError("Failed to deserialize {packet}. ( {data} )", nameof(ClientHousingPacketRequestPlayerHouses),
                PacketLog.Hex(data));
            return false;
        }

        _logger.LogTrace("Received {name} packet.", nameof(ClientHousingPacketRequestPlayerHouses));

        if (!packet.IsTargeted || packet.PlayerGuid == connection.Player.Guid)
            SendHouseList(connection);
        else
            SendTargetHouseList(connection, packet.PlayerGuid);

        return true;
    }

    /// <summary>Sends the player their own houses.</summary>
    public static void SendHouseList(GatewayConnection connection)
    {
        var characterId = GuidHelper.GetPlayerId(connection.Player.Guid);

        using var dbContext = _dbContextFactory.CreateDbContext();
        var houses = LoadHouses(dbContext, characterId);

        var packet = new HousingPacketInstanceList
        {
            PlayerGuid = connection.Player.Guid
        };

        AddInstances(packet, houses, connection.Player.Guid, connection.Player.Name.FullName, LoadStandings(dbContext));

        connection.SendTunneled(packet);
    }

    /// <summary>
    /// Answers "Go to House" on another player with that player's houses the requester may enter (see
    /// <see cref="HouseAccess.MayEnter"/>). Picking one sends <see cref="ClientHousingPacketEnterRequest"/>, which
    /// checks the same rule again.
    /// </summary>
    private static void SendTargetHouseList(GatewayConnection connection, ulong ownerGuid)
    {
        var player = connection.Player;

        ulong ownerId;

        try
        {
            ownerId = GuidHelper.GetPlayerId(ownerGuid);
        }
        catch (ArgumentException)
        {
            ChatHelper.SendSystemMessage(player, "That player has no houses you can visit.");
            return;
        }

        using var dbContext = _dbContextFactory.CreateDbContext();

        var owner = dbContext.Characters
            .AsNoTracking()
            .Where(character => character.Id == ownerId)
            .Select(character => new { character.FullName })
            .FirstOrDefault();

        var ownerName = owner?.FullName ?? string.Empty;

        var houses = owner is null
            ? []
            : LoadHouses(dbContext, ownerId)
                .Where(entry => HouseAccess.MayEnter(player.Guid, player.Friends, player.MembershipStatus != 0, entry.House))
                .ToList();

        if (houses.Count == 0)
        {
            var who = string.IsNullOrEmpty(ownerName) ? "That player" : ownerName;
            ChatHelper.SendSystemMessage(player, $"{who} has no houses you can visit.");
            return;
        }

        // The live server's answer to a targeted request (the 2014 capture named in
        // ClientHousingPacketRequestPlayerHouses) had a PlayerGuid of 0, where the player's own list carries their guid.
        var packet = new HousingPacketInstanceList
        {
            PlayerGuid = 0
        };

        AddInstances(packet, houses, ownerGuid, ownerName, LoadStandings(dbContext));

        connection.SendTunneled(packet);
    }

    private static List<(DbHouse House, int FixtureCount)> LoadHouses(DatabaseContext dbContext, ulong ownerId)
    {
        var supportedZoneDefinitionIds = _resourceManager.Zones.Values
            .OfType<HousingZoneDefinition>()
            .Select(definition => definition.Id)
            .ToArray();

        return dbContext.Houses
            .AsNoTracking()
            .Where(house =>
                house.CharacterId == ownerId &&
                supportedZoneDefinitionIds.Contains(house.ZoneDefinitionId))
            .Select(house => new
            {
                House = house,
                FixtureCount = house.Fixtures.Count
            })
            .ToList()
            // SQLite can't sort by ulong in SQL, so sort after loading.
            .OrderBy(entry => entry.House.Id)
            .Select(entry => (entry.House, entry.FixtureCount))
            .ToList();
    }

    private static WeeklyStandings LoadStandings(DatabaseContext dbContext)
    {
        return PlotVoting.Standings(
            dbContext,
            _competitionOptions.WeekContaining(DateTimeOffset.UtcNow),
            _competitionOptions.MinimumVotes);
    }

    private static void AddInstances(
        HousingPacketInstanceList packet,
        List<(DbHouse House, int FixtureCount)> houses,
        ulong ownerGuid,
        string ownerName,
        WeeklyStandings standings)
    {
        foreach (var entry in houses)
        {
            var house = entry.House;
            var standing = standings.For(house.Id);
            if (!_resourceManager.Zones.TryGetValue(house.ZoneDefinitionId, out var definition) ||
                definition is not HousingZoneDefinition housingDefinition)
            {
                continue;
            }

            packet.Instances.Add(new PlayerHousingInstanceInfo
            {
                OwnerGuid = ownerGuid,
                InstanceGuid = GuidHelper.GetHouseGuid(house.Id),
                NameId = housingDefinition.NameId,
                OwnerName = ownerName,
                HouseName = string.IsNullOrWhiteSpace(house.Name) ? housingDefinition.DisplayName : house.Name,
                IconId = housingDefinition.IconId,
                FixtureCount = entry.FixtureCount,
                FurnitureScore = house.FurnitureScore,
                LastVisited = house.LastVisited,
                IsLocked = house.IsLocked,
                IsMembersOnly = house.IsMembersOnly,
                IsFloraAllowed = house.IsFloraAllowed,
                Description = house.Description,
                KeywordList = house.KeywordList,
                Unknown21 = string.Empty,
                Rating = (float)standing.Score,
                Votes = standing.Votes,
                HasRating = house.IsPublished,
                CanVote = false,
                WhenCreated = house.Created
            });
        }
    }
}
