using System.Collections.Generic;
using NUnit.Framework;

public class NormalCrapsRoundTests
{
    class FixedDiceSource : IRandomSource
    {
        readonly Queue<int> faces;
        public FixedDiceSource(params int[] dieFaces) => faces = new Queue<int>(dieFaces);
        public int Next(int min, int max) => faces.Dequeue();
    }

    // ── Come-out naturals ─────────────────────────────────────────────────────
    [TestCase(3, 4)] // 7
    [TestCase(5, 6)] // 11
    public void ComeOut_Natural_PassWins_DontPassLoses_RoundStaysOpen(int d1, int d2)
    {
        var round = new NormalCrapsRound(new FixedDiceSource(d1, d2));
        round.PlaceBet(NormalCrapsBetType.PassLine, 100);
        round.PlaceBet(NormalCrapsBetType.DontPass, 100);
        var r = round.Roll();

        Assert.AreEqual(200, r.PassReturn);
        Assert.IsTrue(r.PassResolved);
        Assert.AreEqual(0, r.DontPassReturn);
        Assert.IsTrue(r.DontPassResolved);
        Assert.IsFalse(r.RoundOver);
        Assert.AreEqual(NormalCrapsPhase.ComeOut, round.Phase);
    }

    // ── Come-out craps ────────────────────────────────────────────────────────
    [TestCase(1, 1)] // 2 — DP wins
    [TestCase(1, 2)] // 3 — DP wins
    public void ComeOut_Craps2or3_PassLoses_DontPassWins(int d1, int d2)
    {
        var round = new NormalCrapsRound(new FixedDiceSource(d1, d2));
        round.PlaceBet(NormalCrapsBetType.PassLine, 100);
        round.PlaceBet(NormalCrapsBetType.DontPass, 100);
        var r = round.Roll();

        Assert.AreEqual(0, r.PassReturn);
        Assert.IsTrue(r.PassResolved);
        Assert.AreEqual(200, r.DontPassReturn);
        Assert.IsTrue(r.DontPassResolved);
        Assert.IsFalse(r.RoundOver);
    }

    [Test]
    public void ComeOut_Craps12_PassLoses_DontPassPushes()
    {
        var round = new NormalCrapsRound(new FixedDiceSource(6, 6));
        round.PlaceBet(NormalCrapsBetType.PassLine, 100);
        round.PlaceBet(NormalCrapsBetType.DontPass, 100);
        var r = round.Roll();

        Assert.AreEqual(0, r.PassReturn);
        Assert.IsTrue(r.PassResolved);
        Assert.AreEqual(100, r.DontPassReturn); // stake returned on push
        Assert.IsTrue(r.DontPassPushed);
        Assert.IsTrue(r.DontPassResolved);
        Assert.IsFalse(r.RoundOver);
    }

    [Test]
    public void ComeOut_PointNumber_EstablishesPoint()
    {
        var round = new NormalCrapsRound(new FixedDiceSource(2, 4)); // 6
        round.PlaceBet(NormalCrapsBetType.PassLine, 100);
        var r = round.Roll();

        Assert.IsTrue(r.PointEstablishedThisRoll);
        Assert.AreEqual(6, r.NewPoint);
        Assert.IsFalse(r.PassResolved);
        Assert.AreEqual(NormalCrapsPhase.Point, round.Phase);
    }

    // ── Point phase ───────────────────────────────────────────────────────────
    [Test]
    public void PointPhase_PointRepeats_PassWins_DontPassLoses_NoRoundOver()
    {
        // Set up: come-out 6, then roll 6
        var round = new NormalCrapsRound(new FixedDiceSource(2, 4, 2, 4));
        round.PlaceBet(NormalCrapsBetType.PassLine, 100);
        round.PlaceBet(NormalCrapsBetType.DontPass, 100);
        round.Roll(); // establish point 6

        var r = round.Roll(); // 6 again

        Assert.AreEqual(200, r.PassReturn);
        Assert.IsTrue(r.PassResolved);
        Assert.IsTrue(r.DontPassResolved);
        Assert.AreEqual(0, r.DontPassReturn);
        Assert.IsFalse(r.RoundOver);
        Assert.AreEqual(NormalCrapsPhase.ComeOut, round.Phase);
    }

    [Test]
    public void PointPhase_SevenOut_PassLoses_DontPassWins_RoundOver()
    {
        var round = new NormalCrapsRound(new FixedDiceSource(2, 4, 3, 4)); // come-out 6, then 7
        round.PlaceBet(NormalCrapsBetType.PassLine, 100);
        round.PlaceBet(NormalCrapsBetType.DontPass, 100);
        round.Roll(); // establish 6

        var r = round.Roll(); // 7 — seven-out

        Assert.AreEqual(0, r.PassReturn);
        Assert.IsTrue(r.PassResolved);
        Assert.AreEqual(200, r.DontPassReturn);
        Assert.IsTrue(r.DontPassResolved);
        Assert.IsTrue(r.RoundOver);
        Assert.IsTrue(round.RoundOver);
    }

    // ── Come bet: craps out ───────────────────────────────────────────────────
    [Test]
    public void ComeBet_Unparked_CrapsOut_On2()
    {
        // Establish point first, then Come bets can be placed
        var round = new NormalCrapsRound(new FixedDiceSource(2, 4, 1, 1)); // 6, then 2
        round.PlaceBet(NormalCrapsBetType.PassLine, 100);
        round.Roll(); // establish point 6

        var come = round.PlaceComeBet(50);
        var r = round.Roll(); // 2 — Come craps out

        Assert.Contains(come, r.ComeCrapsOut);
        Assert.IsFalse(r.ComeReturns.ContainsKey(come));
    }

    [Test]
    public void ComeBet_Unparked_Natural_Wins()
    {
        var round = new NormalCrapsRound(new FixedDiceSource(2, 4, 3, 4)); // 6, then 7
        round.PlaceBet(NormalCrapsBetType.PassLine, 100);
        round.Roll(); // establish 6

        var come = round.PlaceComeBet(50);
        var r = round.Roll(); // 7 — Come natural wins, but Pass point-phase 7 = seven-out

        Assert.IsTrue(r.ComeReturns.ContainsKey(come));
        Assert.AreEqual(100L, r.ComeReturns[come]);
        Assert.IsTrue(r.RoundOver); // seven-out still ends the round
    }

    // ── Don't Pass odds ───────────────────────────────────────────────────────
    [Test]
    public void DontPassOdds_SevenOut_PaysLayOdds()
    {
        // Point = 4, DP lay odds behind
        var round = new NormalCrapsRound(new FixedDiceSource(2, 2, 3, 4)); // 4, then 7
        round.PlaceBet(NormalCrapsBetType.PassLine, 100);
        round.PlaceBet(NormalCrapsBetType.DontPass, 100);
        round.Roll(); // establish 4

        round.PlaceBet(NormalCrapsBetType.DontPassOdds, 200); // lay $200 against point 4
        var r = round.Roll(); // 7 — seven-out, DP wins + lay odds pay

        // Lay $200 against 4 (true odds 2:1): win = 200 * 1/2 = 100 → returned 300
        // DP: 100*2 = 200. Total = 200 + 300 = 500
        Assert.AreEqual(500, r.DontPassReturn);
    }

    // ── Lay bets: win on 7 ────────────────────────────────────────────────────
    // Each number: establish point, place lay bet, roll 7 — lay should pay
    [TestCase(2, 2, NormalCrapsBetType.Lay4,  4,   48)] // 4 point, lay 4, 7 → winnings 50 less 5% vig (stake stays)
    [TestCase(2, 3, NormalCrapsBetType.Lay5,  5,   63)] // 5 point
    [TestCase(3, 3, NormalCrapsBetType.Lay6,  6,   79)] // 6 point
    [TestCase(4, 4, NormalCrapsBetType.Lay8,  8,   79)] // 8 point
    [TestCase(4, 5, NormalCrapsBetType.Lay9,  9,   63)] // 9 point
    [TestCase(4, 6, NormalCrapsBetType.Lay10, 10,  48)] // 10 point
    public void Lay_WinsOn7_CorrectPayout(int pd1, int pd2, NormalCrapsBetType layType, int layNum, long expected)
    {
        // Sequence: come-out die pair → point established, then 3+4=7
        var round = new NormalCrapsRound(new FixedDiceSource(pd1, pd2,  3, 4));
        round.PlaceBet(NormalCrapsBetType.PassLine, 25);
        round.PlaceBetsWorking = true;
        round.Roll(); // establish point

        round.PlaceBet(layType, 100);
        var r = round.Roll(); // 7 — lay wins

        Assert.IsTrue(r.LayHits.ContainsKey(layNum), $"LayHits missing {layNum}");
        Assert.AreEqual(expected, r.LayHits[layNum]);
        Assert.AreEqual(0, r.LayLosses.Count);
        Assert.IsTrue(r.RoundOver);
        // Stake auto-persists — Lay bet remains active for next shooter
        Assert.AreEqual(100, round.GetBet(layType));
    }

    // ── Lay bets: lose when their number hits ─────────────────────────────────
    [TestCase(2, 2, NormalCrapsBetType.Lay4,  4)]
    [TestCase(3, 3, NormalCrapsBetType.Lay6,  6)]
    [TestCase(4, 4, NormalCrapsBetType.Lay8,  8)]
    public void Lay_LosesWhenNumberHits(int pd1, int pd2, NormalCrapsBetType layType, int layNum)
    {
        // Establish point, place lay, roll that same number again
        var round = new NormalCrapsRound(new FixedDiceSource(pd1, pd2,  pd1, pd2));
        round.PlaceBet(NormalCrapsBetType.PassLine, 25);
        round.PlaceBetsWorking = true;
        round.Roll(); // establish point

        round.PlaceBet(layType, 100);
        var r = round.Roll(); // number hits again — point made, lay loses

        Assert.IsTrue(r.LayLosses.ContainsKey(layNum), $"LayLosses missing {layNum}");
        Assert.AreEqual(100, r.LayLosses[layNum]);
        Assert.AreEqual(0, r.LayHits.Count);
        // Stake cleared by core on number hit
        Assert.AreEqual(0, round.GetBet(layType));
    }

    // ── Lay bets: BETS OFF — carry over on seven-out ──────────────────────────
    [Test]
    public void Lay_BetsOff_CarriesOverOnSevenOut()
    {
        var round = new NormalCrapsRound(new FixedDiceSource(2, 2,  3, 4)); // 4, then 7
        round.PlaceBet(NormalCrapsBetType.PassLine, 25);
        round.PlaceBetsWorking = false; // BETS OFF
        round.Roll(); // establish 4

        round.PlaceBet(NormalCrapsBetType.Lay4, 100);
        var r = round.Roll(); // 7 — BETS OFF, lay neither wins nor loses

        Assert.AreEqual(0, r.LayHits.Count,   "Lay should not win when BETS OFF");
        Assert.AreEqual(0, r.LayLosses.Count, "Lay should not lose when BETS OFF");
        Assert.IsTrue(r.PlaceBetsCarriedOver);
        Assert.AreEqual(100, round.GetBet(NormalCrapsBetType.Lay4), "Stake must survive seven-out when BETS OFF");
    }

    // ── Lay + Place on same number simultaneously ─────────────────────────────
    [Test]
    public void Lay_And_Place_SameNumber_IndependentResolution()
    {
        // Point = 6. Place6 wins when 6 hits. Lay6 loses when 6 hits.
        var round = new NormalCrapsRound(new FixedDiceSource(3, 3,  3, 3)); // 6, then 6
        round.PlaceBet(NormalCrapsBetType.PassLine, 25);
        round.PlaceBetsWorking = true;
        round.Roll(); // establish 6

        round.PlaceBet(NormalCrapsBetType.Place6, 120); // Place6: wins 7:6 → 140 winnings
        round.PlaceBet(NormalCrapsBetType.Lay6, 60);    // Lay6: loses when 6 hits
        var r = round.Roll(); // 6 — point made

        Assert.IsTrue(r.PlaceHits.ContainsKey(6),  "Place6 should win");
        Assert.AreEqual(140, r.PlaceHits[6]);        // 120 * 7/6 = 140
        Assert.IsTrue(r.LayLosses.ContainsKey(6),  "Lay6 should lose");
        Assert.AreEqual(60, r.LayLosses[6]);
    }
}
