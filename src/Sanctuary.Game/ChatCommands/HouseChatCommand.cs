using System;
using System.Data;
using System.Globalization;
using System.Linq;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

using Sanctuary.Core.Helpers;
using Sanctuary.Database;
using Sanctuary.Database.Entities;
using Sanctuary.Game.Entities;
using Sanctuary.Game.Helpers;
using Sanctuary.Game.Housing;
using Sanctuary.Game.Resources.Definitions.Zones;
using Sanctuary.Game.Zones;

namespace Sanctuary.Game.ChatCommands;

public sealed class HouseChatCommand : IChatCommand
{
    private readonly IHouseManager _houseManager;
    private readonly IResourceManager _resourceManager;
    private readonly IDbContextFactory<DatabaseContext> _dbContextFactory;
    private readonly CompetitionOptions _competitionOptions;
    private readonly ILogger<HouseChatCommand> _logger;

    public string KeyWord => "house";
    public string Usage => "list | enter <seaside|blackspore> | visit <seaside|blackspore> <player> | leave | ranked | personal";
    public string Description => "List, enter, visit, or leave a house. In your own house, ranked makes it your account's ranked plot, the one in the weekly competition; personal takes it out.";
    public ChatCommandRole RequiredRole => ChatCommandRole.Player;

    public HouseChatCommand(
        IHouseManager houseManager,
        IResourceManager resourceManager,
        IDbContextFactory<DatabaseContext> dbContextFactory,
        IOptions<CompetitionOptions> competitionOptions,
        ILogger<HouseChatCommand> logger)
    {
        _houseManager = houseManager;
        _resourceManager = resourceManager;
        _dbContextFactory = dbContextFactory;
        _competitionOptions = competitionOptions.Value;
        _logger = logger;
    }

    public bool Handle(Player invoker, string[] args)
    {
        if (args.Length == 0)
            return false;

        return args[0].ToLowerInvariant() switch
        {
            "list" when args.Length == 1 => List(invoker),
            "enter" when args.Length == 2 => Enter(invoker, args[1]),
            "visit" when args.Length >= 3 => Visit(invoker, args[1], string.Join(' ', args[2..])),
            "leave" when args.Length == 1 => Leave(invoker),
            "ranked" when args.Length == 1 => MakeRanked(invoker),
            "personal" when args.Length == 1 => MakePersonal(invoker),
            _ => false
        };
    }

    private bool List(Player invoker)
    {
        var characterId = GuidHelper.GetPlayerId(invoker.Guid);
        var names = _houseManager.GetOwnedHouses(characterId)
            .Select(house => (Name: GetHouseName(house), house.Plot, BoughtBy: house.CharacterId == characterId ? null : CharacterName(house.Character)))
            .OrderBy(house => house.Name)
            .Select(house => (house.Plot, house.BoughtBy) switch
            {
                (HousePlot.Ranked, null) => $"{house.Name} (ranked plot)",
                (HousePlot.Ranked, var buyer) => $"{house.Name} (ranked plot, on {buyer})",
                (_, null) => house.Name,
                (_, var buyer) => $"{house.Name} (on {buyer})"
            })
            .ToList();

        ChatHelper.SendSystemMessage(invoker, names.Count == 0
            ? "Your account does not own a house."
            : $"Your account's houses: {string.Join(", ", names)}");

        ChatHelper.SendSystemMessage(invoker, DescribeRankedPlot(characterId));

        return true;
    }

    /// <summary>The account's ranked plot and how it stands this week, wherever on the account it is.</summary>
    private string DescribeRankedPlot(ulong characterId)
    {
        var now = DateTimeOffset.UtcNow;
        var week = _competitionOptions.WeekContaining(now);
        var weekText = string.Create(
            CultureInfo.InvariantCulture,
            $"This competition week runs from {week.Start.UtcDateTime:ddd d MMM HH:mm} to {week.End.UtcDateTime:ddd d MMM HH:mm} UTC.");

        using var dbContext = _dbContextFactory.CreateDbContext();

        var rankedPlot = HousePlots.FindUserId(dbContext, characterId) is ulong userId
            ? HousePlots.FindRankedPlot(dbContext, userId)
            : null;

        if (rankedPlot is null)
            return $"Your account has no ranked plot. Stand in one of your houses and say !house ranked to make it the one in the weekly competition. {weekText}";

        var owner = rankedPlot.CharacterId == characterId ? string.Empty : $", on {CharacterName(rankedPlot.Character)}";

        if (!rankedPlot.IsPublished)
            return $"Your account's ranked plot is {GetHouseName(rankedPlot)}{owner}. Publish it from the house directory to enter it in the weekly competition. {weekText}";

        if (rankedPlot.IsLocked)
            return $"Your account's ranked plot is {GetHouseName(rankedPlot)}{owner}. It's locked, so it isn't in the weekly competition until you unlock it. {weekText}";

        var standing = PlotVoting.Standings(dbContext, week, _competitionOptions.MinimumVotes).For(rankedPlot.Id);
        var votes = standing.Votes == 1 ? "1 vote" : $"{standing.Votes} votes";
        var score = standing.Votes == 0
            ? "no score yet"
            : string.Create(CultureInfo.InvariantCulture, $"averaging {standing.Average:0.00} stars, for a score of {standing.Score:0.00}");

        return $"Your account's ranked plot is {GetHouseName(rankedPlot)}{owner}. It is in this week's competition, with {votes} {score}. {weekText}";
    }

    private bool MakeRanked(Player invoker)
    {
        if (!TryGetOwnHouse(invoker, out var zone))
            return true;

        var characterId = GuidHelper.GetPlayerId(invoker.Guid);
        var now = DateTimeOffset.UtcNow;

        var result = InTransaction(invoker, dbContext =>
        {
            var change = HousePlots.MakeRanked(dbContext, zone.HouseId, characterId, now);
            var state = dbContext.Houses
                .Where(house => house.Id == zone.HouseId)
                .Select(house => new { house.IsPublished, house.IsLocked })
                .First();

            return (Change: change, state.IsPublished, state.IsLocked);
        });

        if (result is not { } saved)
            return true;

        var (change, isPublished, isLocked) = saved;

        switch (change.Result)
        {
            case PlotChangeResult.Unchanged:
                ChatHelper.SendSystemMessage(invoker, "This house is already your ranked plot.");
                break;

            case PlotChangeResult.Changed:
                var replaced = change.Replaced is DbHouse previous
                    ? $" {GetHouseName(previous)} (on {CharacterName(previous.Character)}) is a personal plot again and has left the competition, and its votes this week no longer count."
                    : string.Empty;

                var entered = (isPublished, isLocked) switch
                {
                    (true, false) => " It's in this week's competition, and votes for it count from now.",
                    (true, true) => " It's locked, so it isn't in the competition until you unlock it.",
                    _ => " Publish it from the house directory to enter it in this week's competition."
                };

                ChatHelper.SendSystemMessage(invoker, $"This house is now your account's ranked plot.{replaced}{entered}");

                _logger.LogInformation(
                    "{Player} made house {HouseId} their account's ranked plot, replacing {PreviousHouseId}.",
                    invoker.Name.FullName,
                    zone.HouseId,
                    change.Replaced?.Id);
                break;

            default:
                ChatHelper.SendSystemMessage(invoker, "Only the owner can do that.");
                break;
        }

        return true;
    }

    private bool MakePersonal(Player invoker)
    {
        if (!TryGetOwnHouse(invoker, out var zone))
            return true;

        var characterId = GuidHelper.GetPlayerId(invoker.Guid);

        if (InTransaction(invoker, dbContext => HousePlots.MakePersonal(dbContext, zone.HouseId, characterId)) is not PlotChange change)
            return true;

        switch (change.Result)
        {
            case PlotChangeResult.Unchanged:
                ChatHelper.SendSystemMessage(invoker, "This house is already a personal plot.");
                break;

            case PlotChangeResult.Changed:
                ChatHelper.SendSystemMessage(invoker,
                    "This house is a personal plot again and has left the weekly competition. Its votes this week no longer count, even if you make it your ranked plot again.");

                _logger.LogInformation(
                    "{Player} made house {HouseId} a personal plot.",
                    invoker.Name.FullName,
                    zone.HouseId);
                break;

            default:
                ChatHelper.SendSystemMessage(invoker, "Only the owner can do that.");
                break;
        }

        return true;
    }

    /// <summary>
    /// Runs a plot change in a serializable transaction, so two of an account's characters changing its ranked plot at
    /// once can't both win: on MariaDB one deadlocks, and the execution strategy runs it again.
    /// </summary>
    /// <returns>The change's result, or <c>null</c> (after telling the player) if it couldn't be saved.</returns>
    private T? InTransaction<T>(Player invoker, Func<DatabaseContext, T> change) where T : struct
    {
        try
        {
            using var strategyContext = _dbContextFactory.CreateDbContext();

            return strategyContext.Database.CreateExecutionStrategy().Execute(() =>
            {
                using var dbContext = _dbContextFactory.CreateDbContext();
                using var transaction = dbContext.Database.BeginTransaction(IsolationLevel.Serializable);

                var result = change(dbContext);
                transaction.Commit();
                return result;
            });
        }
        catch (Exception exception) when (exception is DbUpdateException or RetryLimitExceededException)
        {
            _logger.LogWarning(exception, "Failed to change the plot of {Player}'s house.", invoker.Name.FullName);
            ChatHelper.SendSystemMessage(invoker, "That couldn't be saved. Please try again.");
            return null;
        }
    }

    /// <summary>The house the player is standing in, if their account owns it. Tells them otherwise.</summary>
    private static bool TryGetOwnHouse(Player invoker, out HousingZone zone)
    {
        if (invoker.Zone is HousingZone housingZone && housingZone.IsOwner(invoker))
        {
            zone = housingZone;
            return true;
        }

        zone = null!;
        ChatHelper.SendSystemMessage(invoker, "Stand in one of your own houses first.");
        return false;
    }

    private static string CharacterName(DbCharacter character) => character.FullName ?? character.FirstName;

    private string GetHouseName(DbHouse house)
    {
        if (!string.IsNullOrWhiteSpace(house.Name))
            return house.Name;

        return _resourceManager.Zones.TryGetValue(house.ZoneDefinitionId, out var definition) &&
            definition is HousingZoneDefinition housingDefinition
                ? housingDefinition.DisplayName
                : $"house {house.Id}";
    }

    private bool Enter(Player invoker, string commandName)
    {
        var definition = FindDefinition(commandName);

        if (definition is null)
        {
            ChatHelper.SendSystemMessage(invoker, $"Unknown house: {commandName}");
            return true;
        }

        SendEnterResult(invoker, _houseManager.EnterOwnedHouse(invoker, definition.Id));
        return true;
    }

    private bool Visit(Player invoker, string commandName, string ownerName)
    {
        var definition = FindDefinition(commandName);

        if (definition is null)
        {
            ChatHelper.SendSystemMessage(invoker, $"Unknown house: {commandName}");
            return true;
        }

        SendEnterResult(invoker, _houseManager.VisitHouse(invoker, definition.Id, ownerName));
        return true;
    }

    private bool Leave(Player invoker)
    {
        if (!_houseManager.LeaveHouse(invoker))
            ChatHelper.SendSystemMessage(invoker, "You are not in a house.");

        return true;
    }

    private HousingZoneDefinition? FindDefinition(string commandName)
    {
        return _resourceManager.Zones.Values
            .OfType<HousingZoneDefinition>()
            .FirstOrDefault(definition =>
                string.Equals(definition.CommandName, commandName, StringComparison.OrdinalIgnoreCase));
    }

    private static void SendEnterResult(Player player, EnterHouseResult result)
    {
        var message = result switch
        {
            EnterHouseResult.Success => null,
            EnterHouseResult.HouseNotFound => "That house is not available.",
            EnterHouseResult.NotAuthorized => "Only the owner and their friends can enter that house.",
            EnterHouseResult.UnsupportedSourceZone => "Return to the main world before entering a house.",
            EnterHouseResult.ZoneUnavailable => "The house instance could not be created.",
            EnterHouseResult.TransferFailed => "The house transfer failed.",
            _ => "The house could not be entered."
        };

        if (message is not null)
            ChatHelper.SendSystemMessage(player, message);
    }
}
