using Sanctuary.Packet.Common;

namespace Sanctuary.Game.Store;

/// <summary>Why a sale to the coin store is refused, with the result code the client's transaction window shows.</summary>
public enum CoinStoreSaleRefusal
{
    None = 0,

    /// <summary>The item can't be sold (<see cref="BaseItemDefinition.NoSale"/>): client result 3, invalid item.</summary>
    NoSale = 3,

    /// <summary>Not that many owned, or the quantity isn't a positive count: client result 4.</summary>
    Quantity = 4,

    /// <summary>The coin total would overflow: client result 8, general failure.</summary>
    Overflow = 8
}

/// <summary>
/// Selling items to the coin store (the client's "buy from client" request). The quantity is the client's word,
/// so it is checked here before anything is written: it used to go straight into <c>Count -= quantity</c> and
/// <c>Coins += ResellValue * quantity</c>, where a negative quantity grew the stack and overflowed the coins (task 30).
/// </summary>
public static class CoinStoreSale
{
    public readonly record struct Plan(int RemainingCount, int Coins);

    /// <param name="ownedCount">How many the character has, from the database row, not the client's copy.</param>
    /// <param name="quantity">How many the client asked to sell.</param>
    /// <param name="coins">The character's coins before the sale.</param>
    public static bool TryPlan(ClientItemDefinition definition, int ownedCount, int quantity, int coins,
        out Plan plan, out CoinStoreSaleRefusal refusal)
    {
        plan = default;

        if (definition.NoSale)
        {
            refusal = CoinStoreSaleRefusal.NoSale;
            return false;
        }

        if (quantity <= 0 || ownedCount < quantity)
        {
            refusal = CoinStoreSaleRefusal.Quantity;
            return false;
        }

        // Resell values and coins are ints; multiply and add in 64 bits so nothing wraps.
        var resellValue = definition.ResellValue < 0 ? 0 : definition.ResellValue;
        var total = coins + (long)resellValue * quantity;

        if (total > int.MaxValue)
        {
            refusal = CoinStoreSaleRefusal.Overflow;
            return false;
        }

        plan = new Plan(ownedCount - quantity, (int)total);
        refusal = CoinStoreSaleRefusal.None;
        return true;
    }
}
