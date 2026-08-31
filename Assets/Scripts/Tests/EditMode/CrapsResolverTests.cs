using NUnit.Framework;

public class CrapsResolverTests
{
    [TestCase(7, false)]
    [TestCase(2, true)]
    [TestCase(3, true)]
    [TestCase(11, true)]
    [TestCase(12, true)]
    [TestCase(6, true)]
    public void IsPointNumber_EverythingExceptSevenIsValidInCrapless(int total, bool expected)
    {
        Assert.AreEqual(expected, CrapsResolver.IsPointNumber(total));
    }

    [TestCase(5, 0)]
    [TestCase(6, 0)]
    [TestCase(7, 0)]
    [TestCase(8, 0)]
    [TestCase(3, 200)]
    [TestCase(4, 200)]
    [TestCase(9, 200)]
    [TestCase(10, 200)]
    [TestCase(11, 200)]
    [TestCase(2, 300)]
    [TestCase(12, 400)]
    public void FieldPayout_MatchesPayoutTable(int total, long expectedReturn)
    {
        Assert.AreEqual(expectedReturn, CrapsResolver.FieldPayout(100, total));
    }

    [TestCase(4, 180)]   // 9:5 on $100 — winnings only, stake stays on table
    [TestCase(10, 180)]
    [TestCase(5, 140)]   // 7:5
    [TestCase(9, 140)]
    [TestCase(6, 116)]   // 7:6 (integer: 100*7/6=116)
    [TestCase(8, 116)]
    [TestCase(2, 550)]   // 11:2
    [TestCase(12, 550)]
    [TestCase(3, 275)]   // 11:4
    [TestCase(11, 275)]
    public void PlacePayout_MatchesHouseAdjustedOdds(int number, long expectedReturn)
    {
        Assert.AreEqual(expectedReturn, CrapsResolver.PlacePayout(100, number));
    }

    [TestCase(4, 800)]
    [TestCase(10, 800)]
    [TestCase(6, 1000)]
    [TestCase(8, 1000)]
    public void HardwayPayout_SevenToOneOrNineToOne(int number, long expectedReturn)
    {
        Assert.AreEqual(expectedReturn, CrapsResolver.HardwayPayout(100, number));
    }

    [TestCase(2, 6, 1)]
    [TestCase(12, 6, 1)]
    [TestCase(3, 3, 1)]
    [TestCase(11, 3, 1)]
    [TestCase(4, 2, 1)]
    [TestCase(10, 2, 1)]
    [TestCase(5, 3, 2)]
    [TestCase(9, 3, 2)]
    [TestCase(6, 6, 5)]
    [TestCase(8, 6, 5)]
    public void TrueOdds_MatchesProbabilityRatio(int number, int expectedNum, int expectedDen)
    {
        var (num, den) = CrapsResolver.TrueOdds(number);
        Assert.AreEqual(expectedNum, num);
        Assert.AreEqual(expectedDen, den);
    }

    [Test]
    public void OddsPayout_PaysTrueOdds()
    {
        // Point 4: true odds 2:1 -> stake 100 returns 100 + 200 = 300.
        Assert.AreEqual(300, CrapsResolver.OddsPayout(100, 4));
    }

    [Test]
    public void LayOddsPayout_PaysInverseOfTrueOdds()
    {
        // Point 4: true odds 2:1, so laying against it risks 2 to win 1 -> stake 200
        // returns 200 + 100 = 300.
        Assert.AreEqual(300, CrapsResolver.LayOddsPayout(200, 4));
    }

    [TestCase(2, 800)]
    [TestCase(3, 800)]
    [TestCase(12, 800)]
    [TestCase(4, 0)]
    [TestCase(7, 0)]
    public void AnyCrapsPayout_WinsOnTwoThreeTwelve_SevenToOne(int total, long expected)
    {
        Assert.AreEqual(expected, CrapsResolver.AnyCrapsPayout(100, total));
    }

    [TestCase(7, 500)]
    [TestCase(6, 0)]
    public void AnySevenPayout_WinsOnSeven_FourToOne(int total, long expected)
    {
        Assert.AreEqual(expected, CrapsResolver.AnySevenPayout(100, total));
    }

    [TestCase(11, 1600)]
    [TestCase(10, 0)]
    public void AnyElevenPayout_WinsOnEleven_FifteenToOne(int total, long expected)
    {
        Assert.AreEqual(expected, CrapsResolver.AnyElevenPayout(100, total));
    }

    [TestCase(2, 31 * 25)]
    [TestCase(12, 31 * 25)]
    [TestCase(3, 16 * 25)]
    [TestCase(11, 16 * 25)]
    [TestCase(7, 0)]
    public void HornPayout_SplitsFourWays_PaysMatchingQuarter(int total, long expected)
    {
        // Stake 100 -> quarter = 25.
        Assert.AreEqual(expected, CrapsResolver.HornPayout(100, total));
    }
}
