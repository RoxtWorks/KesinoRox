// Pure payout math for crapless craps — no state, no dice-rolling. CrapsRound owns
// the actual roll sequence and state machine; this just answers "given this total,
// what does a bet of this size pay." Mirrors BaccaratResolver's shape (a Payout-style
// method per bet family, returning the total amount returned — stake + winnings, 0
// on a loss). Integer math throughout, same convention as every other resolver here.
public static class CrapsResolver
{
    // Crapless craps: every total except 7 can be a point — 2, 3, 11, 12 included.
    public static bool IsPointNumber(int total) => total != 7;

    // One-roll bet, resolves every roll regardless of come-out/point phase.
    // Wins 3/4/9/10/11 at 1:1, 2 at 2:1, 12 at 3:1. Loses on 5, 6, 7, 8.
    public static long FieldPayout(long stake, int total) => total switch
    {
        2 => stake * 3,
        12 => stake * 4,
        3 or 4 or 9 or 10 or 11 => stake * 2,
        _ => 0
    };

    // House-adjusted place-bet odds (not true odds) — standard 9:5/7:5/7:6 on
    // 4-10/5-9/6-8, plus the crapless-specific 11:2/11:4 on 2-12/3-11 (those two
    // numbers only exist as place bets because crapless makes them valid points).
    // Returns WINNINGS ONLY (not stake+winnings): Place bets stay on the table after
    // a hit and are only cleared on a seven-out, so the stake is NOT returned here.
    public static long PlacePayout(long stake, int number) => number switch
    {
        4 or 10 => stake * 9 / 5,
        5 or 9 => stake * 7 / 5,
        6 or 8 => stake * 7 / 6,
        2 or 12 => stake * 11 / 2,
        3 or 11 => stake * 11 / 4,
        _ => 0
    };

    // Hard 4/10 pays 7:1, hard 6/8 pays 9:1. Caller is responsible for confirming
    // the roll was actually the matching double, not just the right total.
    public static long HardwayPayout(long stake, int number) => number switch
    {
        4 or 10 => stake * 8,
        6 or 8 => stake * 10,
        _ => 0
    };

    // True odds (no house edge) behind Pass/Don't Pass/Come/Don't Come, once a point
    // exists — probability of that point repeating before a 7.
    public static (int num, int den) TrueOdds(int number) => number switch
    {
        2 or 12 => (6, 1),
        3 or 11 => (3, 1),
        4 or 10 => (2, 1),
        5 or 9 => (3, 2),
        6 or 8 => (6, 5),
        _ => (0, 1)
    };

    // Max odds allowed behind the line, as a multiplier of the base bet — the real
    // "3-4-5x odds" convention most US casinos use for standard craps: the
    // multiplier is chosen per point so the max total payout works out to roughly
    // the same 6x the line bet regardless of which point it is (mult * trueOdds
    // ≈ 6 for every point). Crapless craps adds four point numbers standard craps
    // never has (2, 3, 11, 12) with no single settled real-world convention for
    // them, so this extends that exact same "constant ~6x max payout" formula to
    // them instead of guessing — 1x on 2/12, 2x on 3/11, matching what a real
    // casino's own logic would produce if it had those numbers as points.
    public static int MaxOddsMultiplier(int point) => point switch
    {
        2 or 12 => 1,
        3 or 11 => 2,
        4 or 10 => 3,
        5 or 9 => 4,
        6 or 8 => 5,
        _ => 0
    };

    public static long OddsPayout(long oddsStake, int number)
    {
        var (num, den) = TrueOdds(number);
        return oddsStake + oddsStake * num / den;
    }

    // One-roll proposition bets — resolve every single roll regardless of phase,
    // exactly like Field, just with their own numbers/odds.
    public static long AnyCrapsPayout(long stake, int total) => total is 2 or 3 or 12 ? stake * 8 : 0;
    public static long AnySevenPayout(long stake, int total) => total == 7 ? stake * 5 : 0;
    public static long AnyElevenPayout(long stake, int total) => total == 11 ? stake * 16 : 0;

    // Splits the stake 4 ways across 2/3/11/12 (integer division — chip denominations
    // of 25/100/500 don't split perfectly by 4; the remainder is a small, documented
    // rounding loss, same "integer math throughout" convention every resolver here
    // already uses). Only the matching quarter pays; the other three lose.
    public static long HornPayout(long stake, int total)
    {
        long quarter = stake / 4;
        return total switch
        {
            2 or 12 => quarter * 31,
            3 or 11 => quarter * 16,
            _ => 0
        };
    }

    // Don't-side odds are "laid," not taken — you risk more to win less, the mirror
    // image of TrueOdds (inverse ratio). E.g. laying odds against point 4 (true odds
    // 2:1) risks $2 to win $1.
    public static long LayOddsPayout(long oddsStake, int number)
    {
        var (num, den) = TrueOdds(number);
        return oddsStake + oddsStake * den / num;
    }
}
