using System;
using System.Collections.Generic;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

using Sanctuary.Database;
using Sanctuary.Game.Entities;
using Sanctuary.Game.Minigames.MatchThree;
using Sanctuary.Game.Minigames.Mimic;
using Sanctuary.Game.Quests;

namespace Sanctuary.Game.Minigames;

/// <summary>A match-three game's win as <see cref="MinigameSettlement.SettleMatchThree"/> settled it.</summary>
/// <param name="Payout">What the session made of the game: nothing to settle, a win paid, or one refused as too fast.</param>
/// <param name="Reward">The game's reward, for a win that was paid.</param>
/// <param name="Paid">Whether its coins and its stars were paid.</param>
public readonly record struct MatchThreeSettlement(MatchThreePayout Payout, MinigameReward Reward, (bool Coins, bool Stars) Paid);

/// <summary>
/// What a minigame's result does once its session settles it (<see cref="FlashMinigameSession"/>): a win is paid, then
/// the player's quests are told of it (<see cref="ActivityWonEvent"/>), so a quest step that is the game counts it. A win
/// the server refused (a match three won faster than its levels can be played, a Cooking report that couldn't have
/// been played) pays nothing and passes no step. Each message from a game's Flash, and each tick of a match three's
/// clock, goes to the game and is settled here (<see cref="HandleMatchThree"/>, <see cref="TickMatchThree"/>,
/// <see cref="HandleMimic"/>). The Gateway's FlashMinigameHandler and MimicHandler run this and add only their log
/// lines; the tests run the same code, with each payment recorded instead of saved.
/// </summary>
public sealed class MinigameSettlement
{
    private readonly IQuestManager _quests;
    private readonly Func<Player, MinigameReward, (bool Coins, bool Stars)> _pay;

    /// <param name="quests">The quests each paid win is told to.</param>
    /// <param name="pay">Pays a win's reward to the player, saying whether its coins and stars were paid.</param>
    public MinigameSettlement(IQuestManager quests, Func<Player, MinigameReward, (bool Coins, bool Stars)> pay)
    {
        _quests = quests;
        _pay = pay;
    }

    /// <summary>
    /// The Gateway's: each win paid the way every minigame pays (<see cref="MinigameReward.Pay"/>), saved before the
    /// client hears of it.
    /// </summary>
    public static MinigameSettlement Create(IResourceManager resourceManager, IDbContextFactory<DatabaseContext> dbContextFactory,
        IQuestManager quests, ILogger logger) =>
        new(quests, (player, reward) =>
        {
            using var dbContext = dbContextFactory.CreateDbContext();

            return reward.Pay(resourceManager, dbContext, logger, player);
        });

    /// <summary>
    /// A message from a match-three game's Flash: the server's board takes it (<see cref="MatchThreeGame.Handle"/>), then
    /// the game's win is settled (<see cref="SettleMatchThree"/>). Nothing for a session with no board.
    /// </summary>
    public MatchThreeSettlement HandleMatchThree(FlashMinigameSession session, Player player, IReadOnlyList<string> message)
    {
        if (session.MatchThree is not { } game)
            return default;

        game.Handle(message);
        return SettleMatchThree(session, player);
    }

    /// <summary>
    /// The match-three clock: the server's board moves on (<see cref="MatchThreeGame.Tick"/>, which can end a level),
    /// then the game's win is settled. Nothing for a session with no board.
    /// </summary>
    public MatchThreeSettlement TickMatchThree(FlashMinigameSession session, Player player)
    {
        if (session.MatchThree is not { } game)
            return default;

        game.Tick();
        return SettleMatchThree(session, player);
    }

    /// <summary>
    /// Settles a match-three game's win (<see cref="FlashMinigameSession.SettleMatchThreeWin"/>): the first time the
    /// server's board has the game won, its reward is paid and the quests are told. Nothing before then or after.
    /// </summary>
    public MatchThreeSettlement SettleMatchThree(FlashMinigameSession session, Player player)
    {
        var payout = session.SettleMatchThreeWin(out var reward);

        if (payout == MatchThreePayout.None)
            return default;

        // Won faster than its levels can be played: only a client that skips the Flash's own animations gets there.
        if (payout == MatchThreePayout.TooFast)
            return new(payout, reward, default);

        var paid = _pay(player, reward);

        _quests.OnEvent(player, new ActivityWonEvent(session.Definition.ActivityId));

        return new(payout, reward, paid);
    }

    /// <summary>
    /// A message from a Cooking game's Flash: the server's side of the game takes it (<see cref="MimicGame.Handle"/>),
    /// then the game's result is settled (<see cref="SettleMimic"/>). Nothing for a session with no Cooking game.
    /// </summary>
    public MimicResult HandleMimic(FlashMinigameSession session, Player player, IReadOnlyList<string> message)
    {
        if (session.Mimic is not { } game)
            return MimicResult.None;

        game.Handle(message);
        return SettleMimic(session, player);
    }

    /// <summary>
    /// Settles a Cooking game's result (<see cref="FlashMinigameSession.SettleMimicResult"/>), the first time the Flash
    /// reports one: a win the server accepted as plausible is paid and the quests are told. The result, for the log.
    /// </summary>
    public MimicResult SettleMimic(FlashMinigameSession session, Player player)
    {
        var result = session.SettleMimicResult();

        if (result == MimicResult.None)
            return result;

        // A win reported before the recipe was sent, with rounds missing or faster than its rounds can be played is
        // refused; a loss pays nothing either.
        if (result != MimicResult.Won || session.Mimic is not { } game)
            return result;

        _pay(player, game.Reward);

        _quests.OnEvent(player, new ActivityWonEvent(session.Definition.ActivityId));

        return result;
    }
}
