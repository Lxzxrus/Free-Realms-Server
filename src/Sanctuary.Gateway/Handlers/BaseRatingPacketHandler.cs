using System;
using System.Data;
using System.Globalization;
using System.Linq;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

using Sanctuary.Core.Helpers;
using Sanctuary.Core.IO;
using Sanctuary.Database;
using Sanctuary.Database.Entities;
using Sanctuary.Game;
using Sanctuary.Game.Helpers;
using Sanctuary.Game.Housing;
using Sanctuary.Game.Resources.Definitions.Zones;
using Sanctuary.Game.Zones;
using Sanctuary.Packet;
using Sanctuary.Packet.Common.Attributes;

namespace Sanctuary.Gateway.Handlers;

[PacketHandler]
public static class BaseRatingPacketHandler
{
    private const string HousingSystem = "Housing";
    private const int MaxDirectoryEntries = 50;

    private static ILogger _logger = null!;
    private static IDbContextFactory<DatabaseContext> _dbContextFactory = null!;
    private static IResourceManager _resourceManager = null!;
    private static CompetitionOptions _competitionOptions = null!;

    public static void ConfigureServices(IServiceProvider serviceProvider)
    {
        var loggerFactory = serviceProvider.GetRequiredService<ILoggerFactory>();
        _logger = loggerFactory.CreateLogger(nameof(BaseRatingPacketHandler));
        _dbContextFactory = serviceProvider.GetRequiredService<IDbContextFactory<DatabaseContext>>();
        _resourceManager = serviceProvider.GetRequiredService<IResourceManager>();
        _competitionOptions = serviceProvider.GetRequiredService<IOptions<CompetitionOptions>>().Value;
    }

    public static bool HandlePacket(GatewayConnection connection, PacketReader reader)
    {
        if (!reader.TryRead(out byte subOpCode))
            return false;

        var payload = reader.RemainingSpan;
        return subOpCode switch
        {
            3 => HandleDataRequest(connection, payload),
            6 => HandlePublish(connection),
            7 => HandleUnpublish(connection),
            8 => HandleVote(connection, payload),
            12 => HandleSearch(connection, payload),
            16 => HandleCandidateInfo(connection, payload),
            20 => HandleFeatured(connection, payload),
            _ => false
        };
    }

    private static bool HandleDataRequest(GatewayConnection connection, ReadOnlySpan<byte> payload)
    {
        var reader = new PacketReader(payload);
        if (!reader.TryRead(out string system) || !reader.TryRead(out int mode))
        {
            LogMalformed(connection, 3, payload);
            return false;
        }

        if (!IsHousingSystem(system))
            return false;

        using var dbContext = _dbContextFactory.CreateDbContext();
        var characterId = GuidHelper.GetPlayerId(connection.Player.Guid);
        var query = CreateDirectoryQuery(dbContext, connection.Player.MembershipStatus != 0);

        if (mode == 2)
        {
            query = query.Where(house => dbContext.Friends.Any(friend =>
                friend.CharacterId == characterId &&
                friend.FriendCharacterId == house.CharacterId));
        }

        var totalCount = query.Count();
        var standings = LoadStandings(dbContext);
        var selected = SortDirectoryQuery(query, mode, standings)
            .Take(MaxDirectoryEntries)
            .ToList();
        var response = new RatingPacketDataReply
        {
            Correlation = connection.Player.Guid,
            System = HousingSystem,
            TotalCount = totalCount
        };

        for (int index = 0; index < selected.Count; index++)
            response.Entries[index] = ToRatingEntry(selected[index], standings);

        connection.SendTunneled(response);
        return true;
    }

    private static bool HandleSearch(GatewayConnection connection, ReadOnlySpan<byte> payload)
    {
        var reader = new PacketReader(payload);
        if (!reader.TryRead(out ulong correlation) ||
            !reader.TryRead(out string system) ||
            !reader.TryRead(out string searchText))
        {
            LogMalformed(connection, 12, payload);
            return false;
        }

        if (!IsHousingSystem(system))
            return false;

        using var dbContext = _dbContextFactory.CreateDbContext();
        var query = CreateDirectoryQuery(dbContext, connection.Player.MembershipStatus != 0);
        var normalizedSearch = searchText.Trim().ToLowerInvariant();

        if (normalizedSearch.Length > 0)
        {
            var matchingDefinitionIds = GetSupportedDefinitions()
                .Where(definition => definition.DisplayName.Contains(normalizedSearch, StringComparison.OrdinalIgnoreCase))
                .Select(definition => definition.Id)
                .ToArray();

            query = query.Where(house =>
                matchingDefinitionIds.Contains(house.ZoneDefinitionId) ||
                house.Name != null && house.Name.ToLower().Contains(normalizedSearch) ||
                house.Description.ToLower().Contains(normalizedSearch) ||
                house.KeywordList.ToLower().Contains(normalizedSearch) ||
                house.Character.FullName != null && house.Character.FullName.ToLower().Contains(normalizedSearch) ||
                house.Character.FirstName.ToLower().Contains(normalizedSearch) ||
                house.Character.LastName != null && house.Character.LastName.ToLower().Contains(normalizedSearch));
        }

        var standings = LoadStandings(dbContext);
        var houses = SortDirectoryQuery(query, 0, standings)
            .Take(MaxDirectoryEntries)
            .ToList();

        connection.SendTunneled(new RatingPacketSearchReply
        {
            Correlation = correlation,
            Query = searchText,
            Entries = houses.Select(house => ToRatingEntry(house, standings)).ToList()
        });
        return true;
    }

    private static bool HandleFeatured(GatewayConnection connection, ReadOnlySpan<byte> payload)
    {
        var reader = new PacketReader(payload);
        if (!reader.TryRead(out string system) || !reader.TryRead(out ulong correlation))
        {
            LogMalformed(connection, 20, payload);
            return false;
        }

        if (!IsHousingSystem(system))
            return false;

        using var dbContext = _dbContextFactory.CreateDbContext();
        var standings = LoadStandings(dbContext);
        var house = SortDirectoryQuery(
            CreateDirectoryQuery(dbContext, connection.Player.MembershipStatus != 0),
            0,
            standings).FirstOrDefault();

        connection.SendTunneled(new RatingPacketSendFeatured
        {
            Correlation = correlation,
            System = HousingSystem,
            Entry = house is null ? new RatingDataEntry() : ToRatingEntry(house, standings)
        });
        return true;
    }

    private static bool HandleCandidateInfo(GatewayConnection connection, ReadOnlySpan<byte> payload)
    {
        var reader = new PacketReader(payload);
        if (!reader.TryRead(out string system) ||
            !reader.TryRead(out string candidateId) ||
            !reader.TryRead(out ulong ownerGuid) ||
            !reader.TryRead(out ulong correlation))
        {
            LogMalformed(connection, 16, payload);
            return false;
        }

        if (!IsHousingSystem(system))
            return false;

        using var dbContext = _dbContextFactory.CreateDbContext();
        var house = FindCandidateHouse(dbContext, connection, candidateId, ownerGuid);
        SendCandidateInfo(connection, dbContext, house, correlation);
        return true;
    }

    private static bool HandlePublish(GatewayConnection connection)
    {
        using var dbContext = _dbContextFactory.CreateDbContext();
        var house = FindCurrentOwnedHouse(dbContext, connection);
        if (house is null)
            return true;

        house.IsPublished = true;
        dbContext.SaveChanges();
        SendCandidateInfo(connection, dbContext, house, connection.Player.Guid);

        var message = (house.Plot, house.IsLocked) switch
        {
            (HousePlot.Ranked, false) => "Your ranked plot is published: it's in this week's competition, and visitors can vote for it.",
            (HousePlot.Ranked, true) => "Your ranked plot is published, but it's locked, so it isn't in the directory or the competition until you unlock it.",
            (_, false) => "This personal plot is published, so anyone can visit it, but it isn't in the weekly competition. To enter this house instead, say !house ranked here.",
            (_, true) => $"This personal plot is published, but it's locked, so only {OwnersFriends(connection, house)} can visit until you unlock it."
        };

        ChatHelper.SendSystemMessage(connection.Player, message);
        return true;
    }

    private static bool HandleUnpublish(GatewayConnection connection)
    {
        using var dbContext = _dbContextFactory.CreateDbContext();
        var house = FindCurrentOwnedHouse(dbContext, connection);
        if (house is null)
            return true;

        house.IsPublished = false;
        dbContext.SaveChanges();
        SendCandidateInfo(connection, dbContext, house, connection.Player.Guid);

        if (house.Plot == HousePlot.Ranked)
        {
            ChatHelper.SendSystemMessage(connection.Player,
                "Your ranked plot is unpublished, so it has left the weekly competition. Publish it again before the week ends and this week's votes count again.");
        }

        return true;
    }

    private static bool HandleVote(GatewayConnection connection, ReadOnlySpan<byte> payload)
    {
        var reader = new PacketReader(payload);
        var values = new string[4];

        for (int index = 0; index < values.Length; index++)
        {
            if (!reader.TryRead(out values[index]))
            {
                LogMalformed(connection, 8, payload);
                return false;
            }
        }

        var stars = ParseVote(values);
        var outcome = TryVote(connection, stars, out var houseId);

        connection.SendTunneled(new RatingPacketVoteReply());

        if (outcome is not null)
            SendVoteMessage(connection, outcome.Value, stars);

        if (houseId is not null)
        {
            using var dbContext = _dbContextFactory.CreateDbContext();
            var house = dbContext.Houses
                .AsNoTracking()
                .Include(entry => entry.Character)
                .SingleOrDefault(entry => entry.Id == houseId.Value);
            SendCandidateInfo(connection, dbContext, house, connection.Player.Guid);
        }

        return true;
    }

    /// <summary>
    /// Records a vote for the house the player is standing in: the client's vote packet doesn't say which house.
    /// </summary>
    /// <returns>What happened, or <c>null</c> if the player isn't in a house or the vote couldn't be saved.</returns>
    private static PlotVoteOutcome? TryVote(GatewayConnection connection, int stars, out ulong? votedHouseId)
    {
        votedHouseId = null;

        if (!TryGetActiveHouseKey(connection, out var houseId, out var ownerId, out var zoneDefinitionId))
        {
            ChatHelper.SendSystemMessage(connection.Player, "Stand in a ranked plot to vote for it.");
            return null;
        }

        votedHouseId = houseId;

        var characterId = GuidHelper.GetPlayerId(connection.Player.Guid);
        var now = DateTimeOffset.UtcNow;

        using var strategyContext = _dbContextFactory.CreateDbContext();
        var strategy = strategyContext.Database.CreateExecutionStrategy();

        try
        {
            // Serializable, so the checks and the write see the same votes.
            return strategy.Execute(() =>
            {
                using var dbContext = _dbContextFactory.CreateDbContext();
                using var transaction = dbContext.Database.BeginTransaction(IsolationLevel.Serializable);

                if (!dbContext.Houses.Any(house =>
                        house.Id == houseId &&
                        house.CharacterId == ownerId &&
                        house.ZoneDefinitionId == zoneDefinitionId))
                {
                    return new PlotVoteOutcome(PlotVoteResult.NotFound);
                }

                var outcome = PlotVoting.Cast(dbContext, characterId, houseId, stars, now, _competitionOptions);
                transaction.Commit();
                return outcome;
            });
        }
        catch (Exception exception) when (exception is DbUpdateException or RetryLimitExceededException)
        {
            _logger.LogWarning(exception, "Failed to add a rating for the active house.");
            ChatHelper.SendSystemMessage(connection.Player, "Your vote couldn't be saved. Please try again.");
            return null;
        }
    }

    private static void SendVoteMessage(GatewayConnection connection, PlotVoteOutcome outcome, int stars)
    {
        var message = outcome.Result switch
        {
            PlotVoteResult.Cast =>
                $"You gave this plot {Stars(stars)} this week. You can change your vote until the week ends ({DescribeWeekStart()}).",
            PlotVoteResult.Changed =>
                $"You changed your vote for this plot from {Stars(outcome.PreviousStars)} to {Stars(stars)}.",
            PlotVoteResult.Unchanged =>
                $"You've already given this plot {Stars(stars)} this week.",
            PlotVoteResult.InvalidStars =>
                $"A vote is {PlotVoting.MinStars} to {PlotVoting.MaxStars} stars.",
            PlotVoteResult.NotEntered =>
                "This house isn't in the weekly competition, so it can't take votes. Only a published, unlocked ranked plot can.",
            PlotVoteResult.NotFound =>
                "This house can't take votes right now.",
            PlotVoteResult.OwnAccount =>
                "You can't vote for a plot your own account owns.",
            PlotVoteResult.AccountTooNew =>
                $"New accounts can't vote yet. Yours can from {outcome.EligibleFrom.UtcDateTime:yyyy-MM-dd HH:mm} UTC.",
            PlotVoteResult.NotEnoughPlaytime =>
                $"Your account can vote once its characters have played {_competitionOptions.MinPlaytimeMinutes} minutes between them. So far: {outcome.PlaytimeMinutes}.",
            _ => null
        };

        if (message is not null)
            ChatHelper.SendSystemMessage(connection.Player, message);
    }

    private static string Stars(int stars) => stars == 1 ? "1 star" : $"{stars} stars";

    /// <summary>When votes reset, for chat: "Monday 00:00 UTC".</summary>
    private static string DescribeWeekStart()
    {
        return string.Create(
            CultureInfo.InvariantCulture,
            $"votes reset {_competitionOptions.WeekStartDay} {_competitionOptions.WeekStartTime:hh\\:mm} {_competitionOptions.TimeZone}");
    }

    private static WeeklyStandings LoadStandings(DatabaseContext dbContext)
    {
        return PlotVoting.Standings(
            dbContext,
            _competitionOptions.WeekContaining(DateTimeOffset.UtcNow),
            _competitionOptions.MinimumVotes);
    }

    private static IQueryable<DbHouse> CreateDirectoryQuery(DatabaseContext dbContext, bool isMember)
    {
        var supportedZoneDefinitionIds = GetSupportedDefinitions()
            .Select(definition => definition.Id)
            .ToArray();

        return dbContext.Houses
            .AsNoTracking()
            .Include(house => house.Character)
            .Where(house =>
                supportedZoneDefinitionIds.Contains(house.ZoneDefinitionId) &&
                house.IsPublished &&
                !house.IsLocked &&
                (!house.IsMembersOnly || isMember));
    }

    // SQLite can't sort by ulong (the house id) in SQL. The query has already filtered in the database, so the
    // sort runs on the loaded houses; that works the same on every provider.
    // Top rated: the plots with votes this week by score, then the rest of the competition, then published personal
    // plots, which have no score.
    private static IOrderedEnumerable<DbHouse> SortDirectoryQuery(IQueryable<DbHouse> query, int mode, WeeklyStandings standings)
    {
        var houses = query.AsEnumerable();

        return mode == 3
            ? houses.OrderByDescending(house => house.Id)
            : houses
                .OrderByDescending(house => standings.For(house.Id).Votes > 0)
                .ThenByDescending(house => standings.For(house.Id).Score)
                .ThenByDescending(house => standings.For(house.Id).Votes)
                .ThenByDescending(house => house.Plot == HousePlot.Ranked)
                .ThenByDescending(house => house.Id);
    }

    private static DbHouse? FindCandidateHouse(
        DatabaseContext dbContext,
        GatewayConnection connection,
        string candidateId,
        ulong ownerGuid)
    {
        var supportedZoneDefinitionIds = GetSupportedDefinitions()
            .Select(definition => definition.Id)
            .ToArray();
        var query = dbContext.Houses
            .AsNoTracking()
            .Include(house => house.Character)
            .Where(house => supportedZoneDefinitionIds.Contains(house.ZoneDefinitionId));

        if (TryParseHouseId(candidateId, out var houseId))
        {
            var candidate = query.SingleOrDefault(house => house.Id == houseId);
            if (candidate is not null && CanViewCandidate(connection, candidate))
                return candidate;
        }

        if (TryGetActiveHouseKey(
            connection,
            out var activeHouseId,
            out var activeOwnerId,
            out var activeZoneDefinitionId))
        {
            var current = query.SingleOrDefault(house =>
                house.Id == activeHouseId &&
                house.CharacterId == activeOwnerId &&
                house.ZoneDefinitionId == activeZoneDefinitionId);
            if (current is not null)
                return current;
        }

        if (!TryGetPlayerId(ownerGuid, out var ownerId))
            return null;

        var ownedCandidate = query
            .Where(house => house.CharacterId == ownerId)
            .AsEnumerable()
            // SQLite can't sort by ulong in SQL, so sort after loading. The owner's ranked plot comes first.
            .OrderByDescending(house => house.Plot == HousePlot.Ranked)
            .ThenBy(house => house.Id)
            .FirstOrDefault();
        return ownedCandidate is not null && CanViewCandidate(connection, ownedCandidate)
            ? ownedCandidate
            : null;
    }

    /// <summary>Whoever may come into the house while it's locked: the friends of the character who bought it.</summary>
    private static string OwnersFriends(GatewayConnection connection, DbHouse house)
    {
        return house.CharacterId == GuidHelper.GetPlayerId(connection.Player.Guid)
            ? "your friends"
            : $"{GetCharacterName(house.Character)}'s friends";
    }

    /// <param name="house">With its <see cref="DbHouse.Character"/>.</param>
    private static bool CanViewCandidate(GatewayConnection connection, DbHouse house)
    {
        if (HouseAccess.IsOwner(connection.Player.UserId, house.Character.UserId))
            return true;

        if (TryGetActiveHouseKey(
                connection,
                out var activeHouseId,
                out var activeOwnerId,
                out var activeZoneDefinitionId) &&
            activeHouseId == house.Id &&
            activeOwnerId == house.CharacterId &&
            activeZoneDefinitionId == house.ZoneDefinitionId)
        {
            return true;
        }

        if (house.IsMembersOnly && connection.Player.MembershipStatus == 0)
            return false;

        var isFriend = connection.Player.Friends.Any(friend =>
            friend.Guid == GuidHelper.GetPlayerGuid(house.CharacterId));
        return isFriend || house.IsPublished && !house.IsLocked;
    }

    /// <summary>The house the player is standing in, if their account owns it.</summary>
    private static DbHouse? FindCurrentOwnedHouse(DatabaseContext dbContext, GatewayConnection connection)
    {
        if (!TryGetActiveHouseKey(connection, out var houseId, out var ownerId, out var zoneDefinitionId))
            return null;

        return HouseOwnership.OwnedByAccountOf(dbContext, GuidHelper.GetPlayerId(connection.Player.Guid))
            .Include(house => house.Character)
            .SingleOrDefault(house =>
                house.Id == houseId &&
                house.CharacterId == ownerId &&
                house.ZoneDefinitionId == zoneDefinitionId);
    }

    private static void SendCandidateInfo(
        GatewayConnection connection,
        DatabaseContext dbContext,
        DbHouse? house,
        ulong correlation)
    {
        var response = new RatingPacketCandidateInfoReply { Correlation = correlation };

        if (house is not null && IsSupportedZoneDefinition(house.ZoneDefinitionId))
        {
            var characterId = GuidHelper.GetPlayerId(connection.Player.Guid);
            var isActiveHouse = TryGetActiveHouseKey(
                    connection,
                    out var activeHouseId,
                    out var ownerId,
                    out var zoneDefinitionId) &&
                activeHouseId == house.Id &&
                ownerId == house.CharacterId &&
                zoneDefinitionId == house.ZoneDefinitionId;

            var standing = LoadStandings(dbContext).For(house.Id);

            // A vote can be changed all week, so CanVote stays on after voting. Eligibility isn't part of it: a
            // refused vote gets a chat message saying why, where a disabled button wouldn't.
            response.Candidates.Add(new CandidateRatingInfo
            {
                CandidateId = GetCandidateId(house),
                OwnerName = GetCharacterName(house.Character),
                Name = GetHouseName(house),
                Rating = (float)standing.Score,
                Votes = standing.Votes,
                HasRating = house.IsPublished,
                CanVote = isActiveHouse &&
                    PlotVoting.IsEntered(house.Plot, house.IsPublished, house.IsLocked) &&
                    HousePlots.FindUserId(dbContext, characterId) is ulong userId &&
                    userId != house.Character.UserId
            });
        }

        connection.SendTunneled(response);
    }

    private static RatingDataEntry ToRatingEntry(DbHouse house, WeeklyStandings standings)
    {
        var standing = standings.For(house.Id);
        var definition = GetSupportedDefinitions()
            .Single(entry => entry.Id == house.ZoneDefinitionId);

        return new RatingDataEntry
        {
            CandidateId = GetCandidateId(house),
            OwnerName = GetCharacterName(house.Character),
            Name = GetHouseName(house),
            OwnerGuid = GuidHelper.GetPlayerGuid(house.CharacterId),
            Snapshot = definition.DirectorySnapshot,
            Description = house.Description,
            Keywords = house.KeywordList,
            Rating = (float)standing.Score,
            Votes = standing.Votes
        };
    }

    private static string GetCandidateId(DbHouse house)
    {
        return GuidHelper.GetHouseGuid(house.Id).ToString(CultureInfo.InvariantCulture);
    }

    private static string GetHouseName(DbHouse house)
    {
        if (!string.IsNullOrWhiteSpace(house.Name))
            return house.Name;

        return GetSupportedDefinitions()
            .Single(definition => definition.Id == house.ZoneDefinitionId)
            .DisplayName;
    }

    private static string GetCharacterName(DbCharacter character)
    {
        if (!string.IsNullOrWhiteSpace(character.FullName))
            return character.FullName;

        return string.IsNullOrWhiteSpace(character.LastName)
            ? character.FirstName
            : $"{character.FirstName} {character.LastName}";
    }

    private static int ParseVote(string[] values)
    {
        for (int index = values.Length - 1; index >= 0; index--)
        {
            if (int.TryParse(values[index], NumberStyles.Integer, CultureInfo.InvariantCulture, out var rating) &&
                rating is >= 1 and <= 5)
            {
                return rating;
            }
        }

        return 0;
    }

    private static bool TryParseHouseId(string value, out ulong houseId)
    {
        houseId = 0;
        if (!ulong.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var houseGuid))
            return false;

        try
        {
            houseId = GuidHelper.GetHouseId(houseGuid);
            return houseId > 0;
        }
        catch (ArgumentException)
        {
            return false;
        }
    }

    private static bool TryGetPlayerId(ulong playerGuid, out ulong playerId)
    {
        playerId = 0;
        if (playerGuid == 0)
            return false;

        try
        {
            playerId = GuidHelper.GetPlayerId(playerGuid);
            return true;
        }
        catch (ArgumentException)
        {
            return false;
        }
    }

    private static bool TryGetActiveHouseKey(
        GatewayConnection connection,
        out ulong houseId,
        out ulong ownerId,
        out int zoneDefinitionId)
    {
        houseId = 0;
        ownerId = 0;
        zoneDefinitionId = 0;

        if (connection.Player.Zone is not HousingZone housingZone ||
            housingZone.OwnerId is not ulong activeOwnerId ||
            !IsSupportedZoneDefinition(housingZone.DefinitionId))
        {
            return false;
        }

        houseId = housingZone.HouseId;
        ownerId = activeOwnerId;
        zoneDefinitionId = housingZone.DefinitionId;
        return true;
    }

    private static bool IsHousingSystem(string system)
    {
        return string.IsNullOrWhiteSpace(system) ||
            string.Equals(system, HousingSystem, StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsSupportedZoneDefinition(int zoneDefinitionId)
    {
        return _resourceManager.Zones.TryGetValue(zoneDefinitionId, out var definition) &&
            definition is HousingZoneDefinition;
    }

    private static HousingZoneDefinition[] GetSupportedDefinitions()
    {
        return _resourceManager.Zones.Values
            .OfType<HousingZoneDefinition>()
            .OrderBy(definition => definition.Id)
            .ToArray();
    }

    private static void LogMalformed(
        GatewayConnection connection,
        byte subOpCode,
        ReadOnlySpan<byte> payload)
    {
        _logger.LogWarning(
            "Malformed rating packet {SubOpCode} from player {PlayerGuid}. Data={Data}",
            subOpCode,
            connection.Player.Guid,
            PacketLog.Hex(payload));
    }
}
