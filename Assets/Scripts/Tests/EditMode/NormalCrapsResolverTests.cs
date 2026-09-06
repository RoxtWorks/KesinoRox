using NUnit.Framework;

public class NormalCrapsResolverTests
{
    // ── Field ────────────────────────────────────────────────────────────────
    [TestCase(2,  300)] // 2:1
    [TestCase(12, 300)] // 2:1
    [TestCase(3,  200)] // 1:1
    [TestCase(4,  200)]
    [TestCase(9,  200)]
    [TestCase(11, 200)]
    [TestCase(5,  0)]
    [TestCase(6,  0)]
    [TestCase(7,  0)]
    [TestCase(8,  0)]
    public void Field_PayoutsCorrect(int total, long expected)
        => Assert.AreEqual(expected, NormalCrapsResolver.FieldPayout(100, total));

    // ── Place ────────────────────────────────────────────────────────────────
    [TestCase(4,  180)] // 9:5 winnings only
    [TestCase(10, 180)]
    [TestCase(5,  140)] // 7:5
    [TestCase(9,  140)]
    [TestCase(6,  116)] // 7:6 (integer floor)
    [TestCase(8,  116)]
    public void Place_WinningsOnly_Correct(int number, long expected)
        => Assert.AreEqual(expected, NormalCrapsResolver.PlacePayout(100, number));

    // ── Hardways ─────────────────────────────────────────────────────────────
    [TestCase(4,  800)]  // 7:1
    [TestCase(10, 800)]
    [TestCase(6,  1000)] // 9:1
    [TestCase(8,  1000)]
    public void Hardway_PayoutsCorrect(int number, long expected)
        => Assert.AreEqual(expected, NormalCrapsResolver.HardwayPayout(100, number));

    // ── Odds ─────────────────────────────────────────────────────────────────
    [TestCase(4,  300)] // 2:1 → 100+200
    [TestCase(10, 300)]
    [TestCase(5,  250)] // 3:2 → 100+150
    [TestCase(9,  250)]
    [TestCase(6,  220)] // 6:5 → 100+120
    [TestCase(8,  220)]
    public void Odds_PayoutsCorrect(int number, long expected)
        => Assert.AreEqual(expected, NormalCrapsResolver.OddsPayout(100, number));

    // ── Lay Odds ─────────────────────────────────────────────────────────────
    [TestCase(4,  150)] // lay $100 against 4 (2:1): win $50 → returned $150
    [TestCase(10, 150)]
    [TestCase(5,  166)] // lay $100 against 5 (3:2): win $66 integer → returned $166
    [TestCase(9,  166)]
    [TestCase(6,  183)] // lay $100 against 6 (6:5): win $83 integer → returned $183
    [TestCase(8,  183)]
    public void LayOdds_PayoutsCorrect(int number, long expected)
        => Assert.AreEqual(expected, NormalCrapsResolver.LayOddsPayout(100, number));

    // ── Props ─────────────────────────────────────────────────────────────────
    [TestCase(2,  800)]
    [TestCase(3,  800)]
    [TestCase(12, 800)]
    [TestCase(7,  0)]
    public void AnyCraps_Correct(int total, long expected)
        => Assert.AreEqual(expected, NormalCrapsResolver.AnyCrapsPayout(100, total));

    [TestCase(7,  500)]
    [TestCase(11, 0)]
    public void AnySeven_Correct(int total, long expected)
        => Assert.AreEqual(expected, NormalCrapsResolver.AnySevenPayout(100, total));

    [TestCase(11, 1600)]
    [TestCase(7,  0)]
    public void AnyEleven_Correct(int total, long expected)
        => Assert.AreEqual(expected, NormalCrapsResolver.AnyElevenPayout(100, total));

    // ── Lay bet payout (stake stays, winnings returned with stake) ────────────
    // Against 4/10 (true odds 2:1): risk 100, win 50 → returned 150
    [TestCase(4,  150)]
    [TestCase(10, 150)]
    // Against 5/9 (true odds 3:2): risk 100, win 66 → returned 166
    [TestCase(5,  166)]
    [TestCase(9,  166)]
    // Against 6/8 (true odds 6:5): risk 100, win 83 → returned 183
    [TestCase(6,  183)]
    [TestCase(8,  183)]
    public void LayBet_Payout_Correct(int number, long expected)
        => Assert.AreEqual(expected, NormalCrapsResolver.LayBetPayout(100, number));
}
