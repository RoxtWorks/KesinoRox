using System.Collections.Generic;
using NUnit.Framework;

public class CrapsRoundTests
{
    // Feeds a scripted sequence of die faces (consumed two at a time, one pair per
    // Roll() call) instead of depending on real randomness — same "force specific
    // outcomes deterministically" approach used for Blackjack/Baccarat's Shoe tests.
    class FixedDiceSource : IRandomSource
    {
        readonly Queue<int> faces;
        public FixedDiceSource(params int[] dieFaces) => faces = new Queue<int>(dieFaces);
        public int Next(int minInclusive, int maxExclusive) => faces.Dequeue();
    }

    [Test]
    public void ComeOut_Seven_ResolvesPassImmediately_RoundStaysOpen()
    {
        var round = new CrapsRound(new FixedDiceSource(3, 4)); // 7
        round.PlaceBet(CrapsBetType.PassLine, 100);
        var result = round.Roll();

        Assert.AreEqual(200, result.PassReturn);
        Assert.IsTrue(result.PassResolved);
        Assert.IsFalse(result.RoundOver);
        Assert.IsFalse(round.RoundOver);
        Assert.AreEqual(CrapsPhase.ComeOut, round.Phase);
    }

    [TestCase(1, 1)] // 2
    [TestCase(1, 2)] // 3
    [TestCase(5, 6)] // 11
    [TestCase(6, 6)] // 12
    public void ComeOut_NonSeven_EstablishesPointWithoutResolvingAnything(int d1, int d2)
    {
        var round = new CrapsRound(new FixedDiceSource(d1, d2));
        round.PlaceBet(CrapsBetType.PassLine, 100);
        var result = round.Roll();

        Assert.IsTrue(result.PointEstablishedThisRoll);
        Assert.AreEqual(d1 + d2, result.NewPoint);
        Assert.IsFalse(result.PassResolved);
        Assert.AreEqual(CrapsPhase.Point, round.Phase);
        Assert.AreEqual(d1 + d2, round.Point);
    }

    [Test]
    public void PointRepeat_ResolvesPassWin_PaysOdds()
    {
        var round = new CrapsRound(new FixedDiceSource(2, 2, 2, 2)); // establish 4, then repeat 4
        round.PlaceBet(CrapsBetType.PassLine, 100);
        round.Roll(); // establishes point 4
        round.PlaceBet(CrapsBetType.PassOdds, 50);
        var result = round.Roll(); // point repeats

        // Pass line 100 -> 200, odds 50 at true odds 2:1 -> 150. Total 350.
        Assert.AreEqual(350, result.PassReturn);
        Assert.IsFalse(result.RoundOver);
        Assert.AreEqual(CrapsPhase.ComeOut, round.Phase);
    }

    [Test]
    public void SevenOut_ResolvesDontPassWin_ClearsPlaceAndHardwayAndCome()
    {
        var round = new CrapsRound(new FixedDiceSource(3, 3, 3, 4)); // establish 6, then seven-out
        round.PlaceBet(CrapsBetType.DontPass, 100);
        round.Roll(); // establishes point 6

        round.PlaceBet(CrapsBetType.Place8, 60);
        round.PlaceBet(CrapsBetType.Hard4, 25);
        round.PlaceComeBet(isDontCome: false, amount: 40);

        var result = round.Roll(); // seven-out

        Assert.AreEqual(200, result.DontPassReturn);
        Assert.IsTrue(result.RoundOver);
        Assert.IsTrue(round.RoundOver);
        Assert.AreEqual(0, round.GetBet(CrapsBetType.Place8));
        Assert.AreEqual(0, round.GetBet(CrapsBetType.Hard4));
    }

    [Test]
    public void PlaceBet_HitsMidPoint_PaysAndStaysWorking()
    {
        // Main point established at 6 (via 3,3); Place8 bet hits twice via repeated
        // 8s (4,4) — deliberately a different number than the main point, so hitting
        // it doesn't also resolve the point (point-made would flip Place bets back
        // off until the next point, which is a separate, correctly-tested scenario).
        var round = new CrapsRound(new FixedDiceSource(3, 3, 4, 4, 4, 4));
        round.PlaceBet(CrapsBetType.PassLine, 100);
        round.Roll(); // establishes point 6
        round.PlaceBet(CrapsBetType.Place8, 60);

        var first = round.Roll(); // place 8 hits
        Assert.AreEqual(60 + 60 * 7 / 6, first.PlaceHits[8]);
        Assert.AreEqual(60, round.GetBet(CrapsBetType.Place8)); // still working
        Assert.IsFalse(first.RoundOver);
        Assert.AreEqual(CrapsPhase.Point, round.Phase); // point 6 untouched

        var second = round.Roll(); // hits again — repeatable
        Assert.IsTrue(second.PlaceHits.ContainsKey(8));
    }

    [Test]
    public void PlaceBet_OffDuringComeOut_DoesNotResolve()
    {
        var round = new CrapsRound(new FixedDiceSource(1, 5)); // 6, but still come-out
        round.PlaceBet(CrapsBetType.Place6, 60);
        var result = round.Roll();

        Assert.IsFalse(result.PlaceHits.ContainsKey(6));
        Assert.AreEqual(60, round.GetBet(CrapsBetType.Place6));
    }

    [Test]
    public void ComeBet_TravelsThenParksThenResolvesOnRepeatHit()
    {
        // Establish main point 6, then come bet travels on next roll (lands on 5,
        // parks there), then 5 repeats to win it.
        var round = new CrapsRound(new FixedDiceSource(3, 3, 2, 3, 2, 3));
        round.PlaceBet(CrapsBetType.PassLine, 100);
        round.Roll(); // main point 6

        var come = round.PlaceComeBet(isDontCome: false, amount: 50);
        var travel = round.Roll(); // 5 -> come bet parks at 5
        Assert.AreEqual(5, come.Point);
        Assert.Contains(come, (System.Collections.ICollection)travel.ComeParked);

        var hit = round.Roll(); // 5 again -> come bet wins
        Assert.IsTrue(hit.ComeReturns.ContainsKey(come));
        Assert.AreEqual(100, hit.ComeReturns[come]);
    }

    [Test]
    public void PlaceBetsWorking_PaysOnComeOutRoll()
    {
        // Bets forced "on" via the BETS ON/OFF toggle — a place bet can win on a
        // come-out roll instead of being dormant, matching the reference machine.
        var round = new CrapsRound(new FixedDiceSource(4, 4)); // 8, on come-out
        round.PlaceBetsWorking = true;
        round.PlaceBet(CrapsBetType.Place8, 60);

        var result = round.Roll();

        Assert.AreEqual(60 + 60 * 7 / 6, result.PlaceHits[8]);
    }

    [Test]
    public void PlaceBetsWorking_ComeOutSevenDoesNotClearForcedOnPlaceBets()
    {
        // A come-out 7 resolves the line but is NOT a seven-out (the shooter's turn
        // isn't over) — a place bet forced "on" must survive it.
        var round = new CrapsRound(new FixedDiceSource(3, 4)); // 7, on come-out
        round.PlaceBetsWorking = true;
        round.PlaceBet(CrapsBetType.Place8, 60);

        var result = round.Roll();

        Assert.IsFalse(result.RoundOver);
        Assert.AreEqual(60, round.GetBet(CrapsBetType.Place8));
    }

    [Test]
    public void OneRollProps_ResolveIndependentlyOfLineAndPlaceState()
    {
        // Any Craps + Horn both placed; roll a 3 — Any Craps wins outright, Horn's
        // "3" quarter wins, the other three quarters lose. Line/Place untouched.
        var round = new CrapsRound(new FixedDiceSource(1, 2)); // 3
        round.PlaceBet(CrapsBetType.AnyCraps, 100);
        round.PlaceBet(CrapsBetType.Horn, 100);

        var result = round.Roll();

        Assert.AreEqual(800, result.AnyCrapsReturn);
        Assert.AreEqual(16 * 25, result.HornReturn);
        // Crapless: 3 is a valid point number, so it still establishes the point on
        // come-out — the prop bets resolving doesn't change that main-line behavior.
        Assert.IsTrue(result.PointEstablishedThisRoll);
        Assert.AreEqual(3, result.NewPoint);
    }

    [Test]
    public void Hardway_WinsOnMatchingDouble_LosesOnEasyWay()
    {
        var winRound = new CrapsRound(new FixedDiceSource(2, 2)); // hard 4
        winRound.PlaceBet(CrapsBetType.Hard4, 25);
        var winResult = winRound.Roll();
        Assert.AreEqual(25 * 8, winResult.HardwayHits[4]);
        Assert.AreEqual(0, winRound.GetBet(CrapsBetType.Hard4));

        var loseRound = new CrapsRound(new FixedDiceSource(1, 3)); // easy 4
        loseRound.PlaceBet(CrapsBetType.Hard4, 25);
        var loseResult = loseRound.Roll();
        Assert.IsFalse(loseResult.HardwayHits.ContainsKey(4));
        Assert.AreEqual(0, loseRound.GetBet(CrapsBetType.Hard4));
    }
}
