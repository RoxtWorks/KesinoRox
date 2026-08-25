using System.Linq;

public class Bet
{
    public BetType Type { get; }
    public int[] Numbers { get; } // populated for Straight/Split/Street/Corner/SixLine only
    public long Amount { get; }

    public Bet(BetType type, long amount, int[] numbers = null)
    {
        Type = type;
        Amount = amount;
        Numbers = numbers ?? System.Array.Empty<int>();
    }

    public static Bet Straight(int number, long amount) => new Bet(BetType.Straight, amount, new[] { number });

    // Stable identity for a bet target, independent of amount — used to key/merge
    // pending bets on the same spot (e.g. clicking a number twice adds chips rather
    // than creating two separate bets on it).
    public string TargetKey() => Numbers.Length == 0
        ? Type.ToString()
        : $"{Type}:{string.Join(",", Numbers.OrderBy(n => n))}";
}
