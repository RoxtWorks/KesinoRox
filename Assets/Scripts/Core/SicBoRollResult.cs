using System.Collections.Generic;

// Per-roll result from SicBoRound.Roll(). Returns dict for all winning bets
// (stake+winnings). Missing key = bet lost or wasn't placed.
public class SicBoRollResult
{
    public int Die1 { get; set; }
    public int Die2 { get; set; }
    public int Die3 { get; set; }
    public int Total => Die1 + Die2 + Die3;
    public bool IsTriple => Die1 == Die2 && Die2 == Die3;

    // Every stake on the table this roll — all Sic Bo bets are one-roll, so net = TotalReturned - TotalStaked
    public long TotalStaked { get; set; }

    // winning bets only: value is total returned (stake + winnings)
    public Dictionary<SicBoBetType, long> Returns { get; } = new Dictionary<SicBoBetType, long>();

    public long TotalReturned
    {
        get
        {
            long sum = 0;
            foreach (var v in Returns.Values) sum += v;
            return sum;
        }
    }
}
