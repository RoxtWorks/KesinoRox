using System;

// Pure payout math for Sic Bo. Macau / Asian pay table and bet layout.
// All methods return total amount back (stake + winnings), 0 on loss.
public static class SicBoResolver
{
    // Big: total 11-17, not triple. Small: total 4-10, not triple.
    public static long BigSmallPayout(long stake, int total, bool isTriple, bool isBig)
    {
        if (isTriple) return 0; // any triple kills Big/Small
        if (isBig   && total >= 11 && total <= 17) return stake * 2;
        if (!isBig  && total >= 4  && total <= 10) return stake * 2;
        return 0;
    }

    // Odd / Even total, 1:1. Any triple loses.
    public static long OddEvenPayout(long stake, int total, bool isTriple, bool isOdd)
    {
        if (isTriple) return 0;
        return (total % 2 == 1) == isOdd ? stake * 2 : 0;
    }

    // Specific total payout.
    public static long TotalPayout(long stake, int betTotal, int rollTotal) =>
        betTotal != rollTotal ? 0 : stake * (1 + TotalMultiplier(betTotal));

    public static int TotalMultiplier(int total) => total switch
    {
        4  or 17 => 50,
        5  or 16 => 30,
        6  or 15 => 18,
        7  or 14 => 12,
        8  or 13 => 8,
        9  or 12 => 6,
        10 or 11 => 6,
        _ => 0
    };

    // Single number: 1:1 on one die, 2:1 on two dice, 12:1 on three dice.
    public static long SinglePayout(long stake, int betNumber, int d1, int d2, int d3)
    {
        int matches = Count(betNumber, d1, d2, d3);
        return matches switch { 1 => stake * 2, 2 => stake * 3, 3 => stake * 13, _ => 0 };
    }

    // Specific double (pair): at least two dice show the face. Pays 8:1.
    public static long DoublePayout(long stake, int betFace, int d1, int d2, int d3) =>
        Count(betFace, d1, d2, d3) >= 2 ? stake * 9 : 0;

    // Two-number combination: both faces appear on any two of the three dice. Pays 5:1.
    public static long ComboPayout(long stake, int faceA, int faceB, int d1, int d2, int d3) =>
        Count(faceA, d1, d2, d3) > 0 && Count(faceB, d1, d2, d3) > 0 ? stake * 6 : 0;

    // Any triple: all three dice match each other. Pays 24:1.
    public static long AnyTriplePayout(long stake, bool isTriple) =>
        isTriple ? stake * 25 : 0;

    // Specific triple: all three dice show the exact face. Pays 150:1.
    public static long SpecificTriplePayout(long stake, int betFace, int d1, int d2, int d3) =>
        Count(betFace, d1, d2, d3) == 3 ? stake * 151 : 0;

    // Three single number combination: three different faces, all showing. Pays 30:1.
    public static long ThreeNumberPayout(long stake, int a, int b, int c, int d1, int d2, int d3) =>
        Count(a, d1, d2, d3) == 1 && Count(b, d1, d2, d3) == 1 && Count(c, d1, d2, d3) == 1 ? stake * 31 : 0;

    // Specific double and single: the pair face on two dice, the single face on the third. Pays 50:1.
    public static long PairSinglePayout(long stake, int pair, int single, int d1, int d2, int d3) =>
        Count(pair, d1, d2, d3) == 2 && Count(single, d1, d2, d3) == 1 ? stake * 51 : 0;

    // Four number combination: three different faces showing, all from the four. Pays 7:1.
    public static long FourNumberPayout(long stake, int[] set, int d1, int d2, int d3)
    {
        if (d1 == d2 || d2 == d3 || d1 == d3) return 0;
        return Array.IndexOf(set, d1) >= 0 && Array.IndexOf(set, d2) >= 0 && Array.IndexOf(set, d3) >= 0 ? stake * 8 : 0;
    }

    static int Count(int face, int d1, int d2, int d3) =>
        (d1 == face ? 1 : 0) + (d2 == face ? 1 : 0) + (d3 == face ? 1 : 0);

    // Every bet type in one place: total returned for this stake on these dice.
    public static long Payout(SicBoBetType t, long stake, int d1, int d2, int d3)
    {
        int total = d1 + d2 + d3;
        bool isTriple = d1 == d2 && d2 == d3;
        switch (t)
        {
            case SicBoBetType.Big:       return BigSmallPayout(stake, total, isTriple, true);
            case SicBoBetType.Small:     return BigSmallPayout(stake, total, isTriple, false);
            case SicBoBetType.Odd:       return OddEvenPayout(stake, total, isTriple, true);
            case SicBoBetType.Even:      return OddEvenPayout(stake, total, isTriple, false);
            case SicBoBetType.AnyTriple: return AnyTriplePayout(stake, isTriple);
        }
        int n = TotalForBet(t);
        if (n > 0) return TotalPayout(stake, n, total);
        if ((n = SingleFace(t)) > 0) return SinglePayout(stake, n, d1, d2, d3);
        if ((n = DoubleFace(t)) > 0) return DoublePayout(stake, n, d1, d2, d3);
        if ((n = TripleFace(t)) > 0) return SpecificTriplePayout(stake, n, d1, d2, d3);
        var (a, b) = ComboFaces(t);
        if (a > 0) return ComboPayout(stake, a, b, d1, d2, d3);
        var three = ThreeNumberFaces(t);
        if (three != null) return ThreeNumberPayout(stake, three[0], three[1], three[2], d1, d2, d3);
        var (pair, single) = PairSingleFaces(t);
        if (pair > 0) return PairSinglePayout(stake, pair, single, d1, d2, d3);
        var four = FourNumberFaces(t);
        if (four != null) return FourNumberPayout(stake, four, d1, d2, d3);
        return 0;
    }

    // Used by the UI to light up every winning area after a roll.
    public static bool WouldWin(SicBoBetType bet, int d1, int d2, int d3) => Payout(bet, 1, d1, d2, d3) > 0;

    // --- Bet type decoding ---

    public static (int a, int b) ComboFaces(SicBoBetType t)
    {
        int i = t - SicBoBetType.Combo12;
        if (i < 0 || i > 14) return (0, 0);
        for (int a = 1; a <= 6; a++)
            for (int b = a + 1; b <= 6; b++)
                if (i-- == 0) return (a, b);
        return (0, 0);
    }

    // Enum order is a<b<c ascending, same as these loops.
    public static int[] ThreeNumberFaces(SicBoBetType t)
    {
        int i = t - SicBoBetType.Three123;
        if (i < 0 || i > 19) return null;
        for (int a = 1; a <= 6; a++)
            for (int b = a + 1; b <= 6; b++)
                for (int c = b + 1; c <= 6; c++)
                    if (i-- == 0) return new[] { a, b, c };
        return null;
    }

    // Enum order is pair 1-6, then single ascending skipping the pair face.
    public static (int pair, int single) PairSingleFaces(SicBoBetType t)
    {
        int i = t - SicBoBetType.Pair1Single2;
        if (i < 0 || i > 29) return (0, 0);
        int pair = i / 5 + 1;
        int single = i % 5 + 1;
        if (single >= pair) single++;
        return (pair, single);
    }

    public static int[] FourNumberFaces(SicBoBetType t) => t switch
    {
        SicBoBetType.Four1234 => new[] { 1, 2, 3, 4 },
        SicBoBetType.Four2345 => new[] { 2, 3, 4, 5 },
        SicBoBetType.Four2356 => new[] { 2, 3, 5, 6 },
        SicBoBetType.Four3456 => new[] { 3, 4, 5, 6 },
        _ => null
    };

    public static int SingleFace(SicBoBetType t) =>
        t >= SicBoBetType.Single1 && t <= SicBoBetType.Single6 ? t - SicBoBetType.Single1 + 1 : 0;

    public static int DoubleFace(SicBoBetType t) =>
        t >= SicBoBetType.Double1 && t <= SicBoBetType.Double6 ? t - SicBoBetType.Double1 + 1 : 0;

    public static int TripleFace(SicBoBetType t) =>
        t >= SicBoBetType.Triple1 && t <= SicBoBetType.Triple6 ? t - SicBoBetType.Triple1 + 1 : 0;

    public static int TotalForBet(SicBoBetType t) =>
        t >= SicBoBetType.Total4 && t <= SicBoBetType.Total17 ? t - SicBoBetType.Total4 + 4 : 0;
}
