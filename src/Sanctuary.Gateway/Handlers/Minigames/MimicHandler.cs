using System;
using System.Collections.Generic;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

using Sanctuary.Database;
using Sanctuary.Game;
using Sanctuary.Game.Minigames;
using Sanctuary.Game.Minigames.Mimic;
using Sanctuary.Game.Quests;
using Sanctuary.Packet.Common.Attributes;

namespace Sanctuary.Gateway.Handlers;

/// <summary>
/// The Mimic minigames (Cooking): which Games menu entries are recipes in <c>Resources/Mimic.json</c>, the server's
/// side of each game (<see cref="MimicGame"/>), and the reward for a win (<see cref="MinigameReward.Pay"/>, as the
/// match-three games pay), which a quest step that is the game counts too (<see cref="ActivityWonEvent"/>). The Flash
/// plays every round itself, so a win is the client's report, paid when it is plausible: the recipe sent first, every
/// round reported, at least <see cref="MimicGameDefinition.MinSecondsPerPhase"/> a round. A modified client can still
/// report one without playing (<see cref="ActivityWins.ReportedByClient"/>). <see cref="FlashMinigameHandler"/> opens,
/// starts and closes them like any Flash minigame. See docs/research/mimic.md.
/// </summary>
[PacketHandler]
public static class MimicHandler
{
    private static ILogger _logger = null!;
    private static ILogger _gameLogger = null!;
    private static IResourceManager _resourceManager = null!;
    private static MinigameSettlement _settlement = null!;

    public static void ConfigureServices(IServiceProvider serviceProvider)
    {
        var loggerFactory = serviceProvider.GetRequiredService<ILoggerFactory>();
        _logger = loggerFactory.CreateLogger(nameof(MimicHandler));
        _gameLogger = loggerFactory.CreateLogger(nameof(MimicGame));

        _resourceManager = serviceProvider.GetRequiredService<IResourceManager>();
        _settlement = MinigameSettlement.Create(_resourceManager, serviceProvider.GetRequiredService<IDbContextFactory<DatabaseContext>>(),
            serviceProvider.GetRequiredService<IQuestManager>(), _logger);
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

    /// <summary>
    /// Logs a game's result once and pays a win, which is also the win a quest step that is this game counts
    /// (<see cref="ActivityWonEvent"/>): the Flash's report, accepted as plausible, and settled once per game. A win the
    /// server refused pays nothing and passes no step. Settling, paying and telling the quests are
    /// <see cref="MinigameSettlement.SettleMimic"/>'s, which the tests run too.
    /// </summary>
    private static void Settle(GatewayConnection connection, FlashMinigameSession session, MimicGame game)
    {
        var player = connection.Player;
        var result = _settlement.SettleMimic(session, player);

        if (result == MimicResult.None)
            return;

        if (result == MimicResult.Refused)
        {
            _logger.LogWarning("Minigame: {player} reported a win of {recipe} with {score} points that was refused: {reason}.",
                player.Guid, game.Recipe.Name, game.Score, game.RefusedBecause);
            return;
        }

        _logger.LogInformation("Minigame: {player} {result} {recipe} with {score} points ({seconds} s by the client's clock).",
            player.Guid, result, game.Recipe.Name, game.Score, game.ReportedSeconds);
    }
}
