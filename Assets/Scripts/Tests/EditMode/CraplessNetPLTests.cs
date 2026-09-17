using NUnit.Framework;

// Per-roll net P/L for CRAPLESS craps: net = TotalReturned - TotalStaked.
// TotalStaked = every stake resolved this roll (wins + losses).
// Place wins leave the stake on the table, so they add nothing to TotalStaked.
public class CraplessNetPLTests
{
    static CrapsRound Round(params int[] dice) => new CrapsRound(new FixedDiceSource(dice));

    static CrapsRound Working(params int[] dice)
    {
        var r = Round(dice);
        r.PlaceBetsWorking = true;
        return r;
    }

    static long Net(CrapsRollResult res) => res.TotalReturned - res.TotalStaked;

    // ── Pass Line ────────────────────────────────────────────────────────────

    [Test]
    public void Pass_ComeOut7_Wins()
    {
        var r = Round(3, 4);
        r.PlaceBet(CrapsBetType.PassLine, 100);
        var res = r.Roll();
        Assert.AreEqual(200, res.TotalReturned);
        Assert.AreEqual(100, res.TotalStaked);
        Assert.AreEqual(100, Net(res));
    }

    [TestCase(1, 1)] // 2
    [TestCase(1, 2)] // 3
    [TestCase(5, 6)] // 11
    [TestCase(6, 6)] // 12
    public void Pass_ComeOut_CrapsNumbersBecomePoints_NothingResolves(int d1, int d2)
    {
        var r = Round(d1, d2);
        r.PlaceBet(CrapsBetType.PassLine, 100);
        var res = r.Roll();
        Assert.IsTrue(res.PointEstablishedThisRoll);
        Assert.AreEqual(0, res.TotalStaked, "crapless: no craps loss on come-out");
        Assert.AreEqual(0, Net(res));
    }

    [Test]
    public void Pass_Point2_MadeWithOdds_PaysSixToOne()
    {
        var r = Round(1, 1, 1, 1); // point 2, then 2 again
        r.PlaceBet(CrapsBetType.PassLine, 100);
        r.Roll();
        r.PlaceBet(CrapsBetType.PassOdds, 100);
        var res = r.Roll();
        Assert.AreEqual(200 + 700, res.TotalReturned, "line 1:1 + odds 6:1");
        Assert.AreEqual(200, res.TotalStaked);
        Assert.AreEqual(700, Net(res));
    }

    [Test]
    public void Pass_SevenOut_LosesLineAndOdds()
    {
        var r = Round(2, 2, 3, 4); // point 4, seven-out
        r.PlaceBet(CrapsBetType.PassLine, 100);
        r.Roll();
        r.PlaceBet(CrapsBetType.PassOdds, 300);
        var res = r.Roll();
        Assert.IsTrue(res.RoundOver);
        Assert.AreEqual(0, res.TotalReturned);
        Assert.AreEqual(-400, Net(res));
    }

    // ── Place (2-12, winnings only) ──────────────────────────────────────────

    [TestCase(1, 1, CrapsBetType.Place2, 110)]  // 11:2
    [TestCase(1, 2, CrapsBetType.Place3, 55)]   // 11:4
    [TestCase(5, 6, CrapsBetType.Place11, 55)]  // 11:4
    [TestCase(6, 6, CrapsBetType.Place12, 110)] // 11:2
    [TestCase(2, 2, CrapsBetType.Place4, 36)]   // 9:5
    [TestCase(3, 3, CrapsBetType.Place6, 23)]   // 7:6
    public void Place_Win_StakeStays(int d1, int d2, CrapsBetType type, long winnings)
    {
        var r = Working(d1, d2);
        r.PlaceBet(type, 20);
        var res = r.Roll();
        Assert.AreEqual(winnings, res.TotalReturned);
        Assert.AreEqual(0, res.TotalStaked, "stake stays on table");
        Assert.AreEqual(20, r.GetBet(type));
    }

    [Test]
    public void Place_Working_Lost_On7()
    {
        var r = Working(3, 4);
        r.PlaceBet(CrapsBetType.Place12, 100);
        r.PlaceBet(CrapsBetType.Place5, 100);
        var res = r.Roll();
        Assert.AreEqual(200, res.TotalStaked);
        Assert.AreEqual(-200, Net(res));
    }

    [Test]
    public void Place_Off_SevenOut_CarriesOver()
    {
        var r = Round(3, 3, 3, 4); // point 6, seven-out, bets OFF
        r.Roll();
        r.PlaceBet(CrapsBetType.Place3, 100);
        var res = r.Roll();
        Assert.IsTrue(res.RoundOver);
        Assert.IsTrue(res.PlaceBetsCarriedOver);
        Assert.AreEqual(0, res.TotalStaked);
        Assert.AreEqual(100, r.GetBet(CrapsBetType.Place3));
    }

    // ── Hardways (follow BETS ON/OFF) ────────────────────────────────────────

    [Test]
    public void Hard_Off_NoAction()
    {
        var r = Round(2, 2, 3, 4);
        r.PlaceBet(CrapsBetType.Hard4, 50);
        Assert.AreEqual(0, r.Roll().TotalStaked, "off: hard 4 doesn't pay");
        Assert.AreEqual(0, r.Roll().TotalStaked, "off: 7 doesn't take it");
        Assert.AreEqual(50, r.GetBet(CrapsBetType.Hard4));
    }

    [Test]
    public void Hard_Working_Win_And_EasyLoss()
    {
        var r = Working(4, 4, 5, 3);
        r.PlaceBet(CrapsBetType.Hard8, 10);
        var win = r.Roll();
        Assert.AreEqual(100, win.TotalReturned);
        Assert.AreEqual(90, Net(win));
        r.PlaceBet(CrapsBetType.Hard8, 10);
        var easy = r.Roll();
        Assert.AreEqual(-10, Net(easy));
    }

    [Test]
    public void Hard_Off_SevenOut_CarriesOver()
    {
        var r = Round(3, 3, 3, 4);
        r.Roll();
        r.PlaceBet(CrapsBetType.Hard10, 25);
        var res = r.Roll();
        Assert.IsTrue(res.PlaceBetsCarriedOver);
        Assert.AreEqual(25, r.GetBet(CrapsBetType.Hard10));
    }

    // ── Come bets ────────────────────────────────────────────────────────────

    [Test]
    public void Come_Traveling_7_Wins()
    {
        var r = Round(2, 2, 3, 4); // point 4, then 7 (seven-out)
        r.PlaceBet(CrapsBetType.PassLine, 100);
        r.Roll();
        r.PlaceComeBet(50);
        var res = r.Roll();
        Assert.AreEqual(100, res.TotalReturned, "come wins even money");
        Assert.AreEqual(150, res.TotalStaked, "pass 100 lost + come 50 resolved");
        Assert.AreEqual(-50, Net(res));
    }

    [Test]
    public void Come_Traveling_12_Parks_NoCrapsLoss()
    {
        var r = Round(2, 2, 6, 6); // point 4, then 12
        r.Roll();
        var w = r.PlaceComeBet(50);
        var res = r.Roll();
        Assert.AreEqual(12, w.Point, "crapless: 12 is a come point");
        Assert.AreEqual(0, res.TotalStaked);
    }

    [Test]
    public void Come_Parked_OwnPoint_WithOdds_Wins()
    {
        var r = Round(2, 2, 1, 2, 1, 2); // point 4, come parks on 3, 3 again
        r.Roll();
        var w = r.PlaceComeBet(50);
        r.Roll();
        r.AddComeOdds(w, 100);
        var res = r.Roll();
        Assert.AreEqual(100 + 400, res.ComeReturns[w], "base 1:1 + odds 3:1");
        Assert.AreEqual(150, res.TotalStaked);
        Assert.AreEqual(350, Net(res));
    }

    [Test]
    public void Come_Parked_ComeOut7_BaseLost_OddsReturned()
    {
        // point 4, come parks on 6, point made (4) → come-out; come-out 7
        var r = Round(2, 2, 3, 3, 2, 2, 3, 4);
        r.Roll();
        var w = r.PlaceComeBet(100);
        r.Roll();
        r.AddComeOdds(w, 100);
        r.Roll();
        var res = r.Roll();
        Assert.AreEqual(100, res.ComeReturns[w], "odds off on come-out: returned");
        Assert.AreEqual(200, res.TotalStaked);
        Assert.AreEqual(-100, Net(res), "only the base is lost");
    }

    // ── One-roll bets ────────────────────────────────────────────────────────

    [Test] public void Field_12_PaysTriple()  { var r = Round(6, 6); r.PlaceBet(CrapsBetType.Field, 100); var res = r.Roll(); Assert.AreEqual(300, Net(res)); }
    [Test] public void Field_7_Loses()        { var r = Round(3, 4); r.PlaceBet(CrapsBetType.Field, 100); Assert.AreEqual(-100, Net(r.Roll())); }
    [Test] public void Horn_12_Win()          { var r = Round(6, 6); r.PlaceBet(CrapsBetType.Horn, 100); Assert.AreEqual(675, Net(r.Roll())); }
    [Test] public void CAndE_Craps_Win()      { var r = Round(1, 2); r.PlaceBet(CrapsBetType.CAndE, 100); Assert.AreEqual(300, Net(r.Roll()), "3:1"); }
    [Test] public void CAndE_11_Win()         { var r = Round(5, 6); r.PlaceBet(CrapsBetType.CAndE, 100); Assert.AreEqual(700, Net(r.Roll()), "7:1"); }
    [Test] public void CAndE_7_Loses()        { var r = Round(3, 4); r.PlaceBet(CrapsBetType.CAndE, 100); Assert.AreEqual(-100, Net(r.Roll())); Assert.AreEqual(0, r.GetBet(CrapsBetType.CAndE)); }

    // ── Lucky Roller (Vegas Bonus Craps rules) ───────────────────────────────

    [Test]
    public void Ats_AnySeven_Loses_ThenReopens()
    {
        var r = Round(1, 1, 3, 4); // 2 counts (and becomes the point), then a 7
        Assert.IsTrue(r.CanPlaceAts);
        r.PlaceBet(CrapsBetType.AtsLows, 25);
        r.Roll();
        Assert.IsFalse(r.CanPlaceAts, "run in progress");
        var res = r.Roll();
        Assert.IsTrue(res.AtsSevenOut);
        Assert.AreEqual(25, res.TotalStaked);
        Assert.IsTrue(r.CanPlaceAts);
    }

    [Test]
    public void Ats_SmallWin_KeepsNumbers_AllStillCompletes()
    {
        // 2,3,4,5,6 then 8,9,10,11,12 — crapless makes every one of these a point, none craps out
        var r = Round(1,1, 1,2, 2,2, 2,3, 3,3, 4,4, 4,5, 5,5, 5,6, 6,6);
        r.PlaceBet(CrapsBetType.AtsLows, 10);
        r.PlaceBet(CrapsBetType.AtsAll, 10);
        CrapsRollResult res = null;
        for (int i = 0; i < 5; i++) res = r.Roll();
        Assert.AreEqual(310, res.AtsLowsReturn);
        for (int i = 0; i < 5; i++) res = r.Roll();
        Assert.AreEqual(1560, res.AtsAllReturn, "All pays 155:1");
    }
}
