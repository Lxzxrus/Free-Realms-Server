using System;

namespace Sanctuary.Game.Trading;

public sealed class TradeOptions
{
    /// <summary>
    /// How long a trade request waits for an answer. The client keeps the request in its action queue for 300 s
    /// (<c>Gaq:AddTradeInvite</c> in ScriptsBase.lua), so the server waits a little longer and never forgets a request
    /// the player can still click. Upstream OSFR uses the same 310 s (PR #193).
    /// </summary>
    public TimeSpan InviteTimeout { get; init; } = TimeSpan.FromSeconds(310);

    public TimeSpan SessionTimeout { get; init; } = TimeSpan.FromMinutes(5);

    public TimeSpan ConfirmationDelay { get; init; } = TimeSpan.FromSeconds(10);

    public int MaximumDistinctItemsPerPlayer { get; init; } = 50;

    /// <summary>The shortest time between two trade requests from the same player.</summary>
    public TimeSpan RequestInterval { get; init; } = TimeSpan.FromSeconds(3);

    /// <summary>How long a player waits to ask the same player again after that player said no.</summary>
    public TimeSpan DeclinedRequestCooldown { get; init; } = TimeSpan.FromSeconds(30);

    /// <summary>
    /// How many unanswered trade requests one player can have waiting at once, from different players. Each player
    /// sends at most one request at a time, so a single player can hold only one of these.
    /// </summary>
    public int MaximumPendingInvitesPerTarget { get; init; } = 3;
}
