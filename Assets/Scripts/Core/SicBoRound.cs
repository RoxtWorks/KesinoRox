using System;
using System.Collections.Generic;

// One roll of Sic Bo. All bets are placed before the roll and resolve in one
// step — there's no phase or player decision. Never touches Bankroll directly.
public class SicBoRound
{
    readonly IRandomSource rng;
    readonly Dictionary<SicBoBetType, long> bets = new Dictionary<SicBoBetType, long>();

    public SicBoRound(IRandomSource rng) { this.rng = rng; }

    public long GetBet(SicBoBetType type) => bets.TryGetValue(type, out var v) ? v : 0;
    public void PlaceBet(SicBoBetType type, long amount) => bets[type] = GetBet(type) + amount;
    public void ClearBet(SicBoBetType type) => bets[type] = 0;
    public void ClearAllBets() { bets.Clear(); }

    public long TotalOnTable()
    {
        long sum = 0;
        foreach (var v in bets.Values) sum += v;
        return sum;
    }

    public SicBoRollResult Roll()
    {
        int d1 = rng.Next(1, 7);
        int d2 = rng.Next(1, 7);
        int d3 = rng.Next(1, 7);

        var result = new SicBoRollResult { Die1 = d1, Die2 = d2, Die3 = d3 };

        foreach (var kvp in bets)
        {
            SicBoBetType t = kvp.Key;
            long stake = kvp.Value;
            if (stake <= 0) continue;
            result.TotalStaked += stake;

            long ret = SicBoResolver.Payout(t, stake, d1, d2, d3);

            if (ret > 0) result.Returns[t] = ret;
        }

        bets.Clear(); // Sic Bo: all bets are one-roll, cleared after each roll
        return result;
    }
}
