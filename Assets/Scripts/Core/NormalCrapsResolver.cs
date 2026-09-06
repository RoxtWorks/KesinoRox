// Pure payout math for standard (non-crapless) craps. Same "return total amount
// returned (stake + winnings), 0 on loss" convention as every other resolver.
// Integer math throughout.
public static class NormalCrapsResolver
{
    // Standard craps point numbers — 2/3/11/12 are NOT points; they crap out.
    public static bool IsPointNumber(int total) => total is 4 or 5 or 6 or 8 or 9 or 10;

    // Field: wins on 2(2:1), 3/4/9/10/11(1:1), 12(2:1). Loses 5/6/7/8.
    // Standard casino field; some pay 12 at 3:1 but 2:1 is most common.
    public static long FieldPayout(long stake, int total) => total switch
    {
        2 or 12 => stake * 3,
        3 or 4 or 9 or 10 or 11 => stake * 2,
        _ => 0
    };

    // Standard place payouts — valid only on 4/5/6/8/9/10.
    // Returns WINNINGS ONLY (stake stays on table, same as crapless version).
    public static long PlacePayout(long stake, int number) => number switch
    {
        4 or 10 => stake * 9 / 5,
        5 or 9  => stake * 7 / 5,
        6 or 8  => stake * 7 / 6,
        _ => 0
    };

    // Hardway payouts: 4/10 pay 7:1, 6/8 pay 9:1.
    public static long HardwayPayout(long stake, int number) => number switch
    {
        4 or 10 => stake * 8,
        6 or 8  => stake * 10,
        _ => 0
    };

    // True odds behind Pass/Come. Standard 3-4-5x odds convention:
    // 4/10 pay 2:1, 5/9 pay 3:2, 6/8 pay 6:5.
    public static (int num, int den) TrueOdds(int number) => number switch
    {
        4 or 10 => (2, 1),
        5 or 9  => (3, 2),
        6 or 8  => (6, 5),
        _ => (0, 1)
    };

    public static int MaxOddsMultiplier(int point) => point switch
    {
        4 or 10 => 3,
        5 or 9  => 4,
        6 or 8  => 5,
        _ => 0
    };

    public static long OddsPayout(long oddsStake, int number)
    {
        var (num, den) = TrueOdds(number);
        return oddsStake + oddsStake * num / den;
    }

    // Lay odds behind Don't Pass / Don't Come: risk more to win less (inverse).
    // E.g. against point 4 (true 2:1): lay $2 to win $1.
    public static long MaxLayOddsMultiplier(int point) => point switch
    {
        4 or 10 => 6,   // risk 6 to win 3 (ratio 2:1 inverted)
        5 or 9  => 6,   // risk 6 to win 4
        6 or 8  => 6,   // risk 6 to win 5
        _ => 0
    };

    // Lay bet against a point number — win when 7 comes first, lose when number hits first.
    // Stake auto-persists on the table after a win; only WINNINGS are paid to bankroll.
    public static long LayBetPayout(long stake, int number)
    {
        var (num, den) = TrueOdds(number);
        return stake * den / num; // winnings only — stake stays at risk
    }

    public static long LayOddsPayout(long oddsStake, int number)
    {
        var (num, den) = TrueOdds(number);
        // Laid odds: win is oddsStake * den/num (plus stake back).
        return oddsStake + oddsStake * den / num;
    }

    // One-roll props — same as crapless.
    public static long AnyCrapsPayout(long stake, int total) => total is 2 or 3 or 12 ? stake * 8 : 0;
    public static long AnySevenPayout(long stake, int total) => total == 7 ? stake * 5 : 0;
    public static long AnyElevenPayout(long stake, int total) => total == 11 ? stake * 16 : 0;

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
}
