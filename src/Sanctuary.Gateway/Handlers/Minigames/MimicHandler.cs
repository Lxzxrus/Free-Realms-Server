using System;
using System.Collections.Generic;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

using Sanctuary.Database;
using Sanctuary.Game;
using Sanctuary.Game.Helpers;
using Sanctuary.Game.Minigames;
using Sanctuary.Game.Minigames.Mimic;
using Sanctuary.Game.Resources.Definitions.Rewards;
using Sanctuary.Packet.Common.Attributes;

namespace Sanctuary.Gateway.Handlers;

/// <summary>
/// The Mimic minigames (Cooking): which Games menu entries are recipes in <c>Resources/Mimic.json</c>, the server's
/// side of each game (<see cref="MimicGame"/>), and the reward for a win. <see cref="FlashMinigameHandler"/> opens,
/// starts and closes them like any Flash minigame. See docs/research/mimic.md.
/// </summary>
[PacketHandler]
public static class MimicHandler
{
    private static ILogger _logger = null!;
    private static ILogger _gameLogger = null!;
    private static IResourceManager _resourceManager = null!;
    private static IDbContextFactory<DatabaseContext> _dbContextFactory = null!;

    public static void ConfigureServices(IServiceProvider serviceProvider)
    {
        var loggerFactory = serviceProvider.GetRequiredService<ILoggerFactory>();
        _logger = loggerFactory.CreateLogger(nameof(MimicHandler));
        _gameLogger = loggerFactory.CreateLogger(nameof(MimicGame));

        _resourceManager = serviceProvider.GetRequiredService<IResourceManager>();
        _dbContextFactory = serviceProvider.GetRequiredService<IDbContextFactory<DatabaseContext>>();
    }

    /// <summary>The Flash minigame an activity opens, if it's a Mimic recipe.</summary>
    public static bool TryDefine(int activityId, out FlashMinigameDefinition definition)
    {
        if (!_resourceManager.Mimic.TryFindActivity(activityId, out var game, out var recipe))
        {
            definition = null!;
            return false;
        }

        definition = new FlashMinigameDefinition(activityId, recipe.MiniGameId, game.MiniGameType, game.Swf, MimicGame: game.Name);
        return true;
    }

    /// <summary>The player pressed Go: the server's side of the game, waiting for the Flash to ask for its data.</summary>
    public static void Start(GatewayConnection connection, FlashMinigameSession session, IMimicOutput output)
    {
        if (!_resourceManager.Mimic.TryFindActivity(session.Definition.ActivityId, out var game, out var recipe))
        {
            _logger.LogError("Minigame: activity {activity} is no longer a recipe in Mimic.json; {swf} will get no data.",
                session.Definition.ActivityId, session.Definition.Swf);
            return;
        }

        session.Mimic = new MimicGame(game, recipe, output, () => Environment.TickCount64, _gameLogger);

        _logger.LogInformation("Minigame: {player} is playing {recipe} (game id {gameId}, {phases} rounds).",
            connection.Player.Guid, recipe.Name, recipe.GameId, recipe.Phases.Count);
    }

    /// <summary>A message from the game's Flash.</summary>
    public static void Handle(GatewayConnection connection, FlashMinigameSession session, IReadOnlyList<string> message)
    {
        if (session.Mimic is not { } game)
            return;

        game.Handle(message);
        Settle(connection, session, game);
    }

    /// <summary>Logs a game's result once and pays a win.</summary>
    private static void Settle(GatewayConnection connection, FlashMinigameSession session, MimicGame game)
    {
        if (game.Result == MimicResult.None || session.SettledMimicResult != MimicResult.None)
            return;

        session.SettledMimicResult = game.Result;

        var player = connection.Player;

        if (game.Result == MimicResult.Refused)
        {
            _logger.LogWarning("Minigame: {player} reported a win of {recipe} with {score} points that was refused: {reason}.",
                player.Guid, game.Recipe.Name, game.Score, game.RefusedBecause);
            return;
        }

        _logger.LogInformation("Minigame: {player} {result} {recipe} with {score} points ({seconds} s by the client's clock).",
            player.Guid, game.Result, game.Recipe.Name, game.Score, game.ReportedSeconds);

        if (game.Result != MimicResult.Won)
            return;

        using var dbContext = _dbContextFactory.CreateDbContext();

        RewardHelper.TryGrantCurrency(dbContext, _logger, player, CurrencyType.Coins, game.Recipe.Coins);

        if (game.Game.RewardProfileId > 0)
            RewardHelper.TryGrantExperience(_resourceManager, dbContext, _logger, player, game.Game.RewardProfileId, game.Recipe.Experience);
    }
}
