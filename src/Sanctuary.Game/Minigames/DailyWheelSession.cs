using System;
using System.Globalization;
using System.Linq;
using System.Text;

using Sanctuary.Packet;

namespace Sanctuary.Game.Minigames;

/// <summary>
/// One opening of the daily wheel. The wheel's Flash talks in tab-separated messages carried by minigame payload
/// packets; the vocabulary and order are those of SOE's own test server inside <c>game_wheel.gfx</c>
/// (<c>Wheel.WheelGameServer</c>): wheel data, then spins left, then "server ready"; a spin request is answered with
/// the wedge to aim for; when the wheel stops, the prize.
/// </summary>
public sealed class DailyWheelSession
{
    private readonly object _lock = new();

    /// <summary>The minigame ids the Flash sends its messages with; replies carry the same ones.</summary>
    public int StateId { get; set; }
    public int GroupId { get; set; } = -1;
    public int GameId { get; set; } = -1;

    public bool DataSent { get; set; }

    /// <summary>The wedge won this opening, once the spin is paid for.</summary>
    public int? PrizeSlot { get; private set; }

    /// <summary>Whether the prize window has been shown and the client's coin total updated.</summary>
    public bool PrizeShown { get; set; }

    /// <summary>Claims this opening's single spin. False if a spin was already taken.</summary>
    public bool TryClaimSpin(int prizeSlot)
    {
        lock (_lock)
        {
            if (PrizeSlot is not null)
                return false;

            PrizeSlot = prizeSlot;
            return true;
        }
    }

    /// <summary>Gives back a claimed spin that couldn't be paid (the database refused it).</summary>
    public void ReleaseSpin()
    {
        lock (_lock)
            PrizeSlot = null;
    }

    /// <summary>The "wheel" repeating activity: one spin if today's is unused, else none until UTC midnight.</summary>
    public static RepeatingActivityAddPacket SpinsPacket(DailyWheel wheel, DateTimeOffset? lastSpin, DateTimeOffset now)
    {
        var seconds = (int)Math.Ceiling(DailyWheel.UntilNextSpin(lastSpin, now).TotalSeconds);

        return new RepeatingActivityAddPacket
        {
            Id = wheel.WheelId,
            Count = DailyWheel.CanSpin(lastSpin, now) ? 1 : 0,
            Consecutive = 1,
            SecondsUntilNext = seconds,
            SecondsUntilNext2 = seconds,
            Name = DailyWheel.RepeatingActivityName,
            Name2 = DailyWheel.RepeatingActivityName
        };
    }

    /// <summary>A message to the wheel's Flash: name and arguments, tab separated, NUL terminated.</summary>
    public MiniGamePayloadPacket Message(string name, params object[] args)
    {
        var text = string.Join('\t', new[] { name }.Concat(args.Select(arg => Convert.ToString(arg, CultureInfo.InvariantCulture))));

        var packet = new MiniGamePayloadPacket
        {
            StateId = StateId,
            GroupId = GroupId,
            GameId = GameId,
            Payload = Encoding.UTF8.GetBytes(text + "\0")
        };

        return packet;
    }

    /// <summary>Splits a message from the wheel's Flash into its name and arguments.</summary>
    public static string[] Parse(byte[] payload) => Encoding.UTF8.GetString(payload).TrimEnd('\0').Split('\t');
}
