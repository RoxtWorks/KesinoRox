using NUnit.Framework;

// Comprehensive net P/L tests covering every bet type — wins and losses.
// Verifies TotalReturned, TotalStaked, and net = TotalReturned - TotalStaked,
// which drive the history-strip color and win-streak logic.
//
// TotalStaked = ALL resolved stakes this roll (wins + losses).
// Place/Lay wins: stake stays on table → TotalStaked stays 0 for that bet.
// All other bets: stake consumed on win or loss → TotalStaked += stake.
public class NormalCrapsNetPLTests
{
    static NormalCrapsRound Round(params int[] dice) =>
        new NormalCrapsRound(new FixedDiceSource(dice));

    static NormalCrapsRound RoundWithPoint(int pointDie1, int pointDie2, params int[] rest)
    {
        var allDice = new int[2 + rest.Length];
        allDice[0] = pointDie1; allDice[1] = pointDie2;
        for (int i = 0; i < rest.Length; i++) allDice[2 + i] = rest[i];
        var r = new NormalCrapsRound(new FixedDiceSource(allDice));
        r.Roll(); // establishes point
        return r;
    }

    // ── 1. PASS LINE ─────────────────────────────────────────────────────────

    [Test]
    public void PassLine_ComeOut7_Win()
    {
        var r = Round(3, 4); // 7
        r.PlaceBet(NormalCrapsBetType.PassLine, 100);
        var res = r.Roll();
        Assert.AreEqual(200, res.TotalReturned, "PassReturn");
        Assert.AreEqual(100, res.TotalStaked,   "stake consumed");
        Assert.AreEqual(100, res.TotalReturned - res.TotalStaked, "net +100");
    }

    [Test]
    public void PassLine_ComeOut11_Win()
    {
        var r = Round(5, 6); // 11
        r.PlaceBet(NormalCrapsBetType.PassLine, 100);
        var res = r.Roll();
        Assert.AreEqual(200, res.TotalReturned);
        Assert.AreEqual(100, res.TotalStaked);
    }

    [Test]
    public void PassLine_ComeOut2_Lose()
    {
        var r = Round(1, 1); // 2
        r.PlaceBet(NormalCrapsBetType.PassLine, 100);
        var res = r.Roll();
        Assert.AreEqual(0,    res.TotalReturned);
        Assert.AreEqual(100,  res.TotalStaked);
        Assert.AreEqual(-100, res.TotalReturned - res.TotalStaked, "net -100");
    }

    [Test]
    public void PassLine_ComeOut3_Lose()
    {
        var r = Round(1, 2);
        r.PlaceBet(NormalCrapsBetType.PassLine, 100);
        var res = r.Roll();
        Assert.AreEqual(0,   res.TotalReturned);
        Assert.AreEqual(100, res.TotalStaked);
    }

    [Test]
    public void PassLine_PointMade_Win()
    {
        var r = RoundWithPoint(2, 2, 1, 3); // point=4, roll 4 again
        r.PlaceBet(NormalCrapsBetType.PassLine, 100);
        var res = r.Roll();
        Assert.AreEqual(200, res.TotalReturned);
        Assert.AreEqual(100, res.TotalStaked);
    }

    [Test]
    public void PassLine_WithOdds_PointMade_Win()
    {
        var r = RoundWithPoint(3, 3, 1, 5); // point=6, roll 6
        r.PlaceBet(NormalCrapsBetType.PassLine, 100);
        r.PlaceBet(NormalCrapsBetType.PassOdds, 500); // 5x odds on 6/8
        var res = r.Roll();
        // PassReturn = 200 + OddsPayout(500,6) = 200 + (500 + 500*6/5) = 200 + 1100 = 1300
        Assert.AreEqual(1300, res.TotalReturned);
        Assert.AreEqual(600,  res.TotalStaked,   "pass 100 + odds 500");
        Assert.AreEqual(700,  res.TotalReturned - res.TotalStaked, "net +700");
    }

    [Test]
    public void PassLine_SevenOut_Lose()
    {
        var r = RoundWithPoint(3, 3, 1, 6); // point=6, roll 7
        r.PlaceBet(NormalCrapsBetType.PassLine, 100);
        var res = r.Roll();
        Assert.AreEqual(0,   res.TotalReturned);
        Assert.AreEqual(100, res.TotalStaked);
        Assert.IsTrue(res.RoundOver);
    }

    [Test]
    public void PassLine_WithOdds_SevenOut_Lose()
    {
        var r = RoundWithPoint(2, 2, 1, 6); // point=4, roll 7
        r.PlaceBet(NormalCrapsBetType.PassLine, 100);
        r.PlaceBet(NormalCrapsBetType.PassOdds, 300);
        var res = r.Roll();
        Assert.AreEqual(0,   res.TotalReturned);
        Assert.AreEqual(400, res.TotalStaked, "pass + odds lost");
    }

    // ── 2. DON'T PASS ────────────────────────────────────────────────────────

    [Test]
    public void DontPass_ComeOut2_Win()
    {
        var r = Round(1, 1);
        r.PlaceBet(NormalCrapsBetType.DontPass, 100);
        var res = r.Roll();
        Assert.AreEqual(200, res.TotalReturned);
        Assert.AreEqual(100, res.TotalStaked);
    }

    [Test]
    public void DontPass_ComeOut12_Push()
    {
        var r = Round(6, 6);
        r.PlaceBet(NormalCrapsBetType.DontPass, 100);
        var res = r.Roll();
        Assert.AreEqual(100, res.TotalReturned, "push — stake returned");
        Assert.AreEqual(100, res.TotalStaked,   "stake consumed then returned");
        Assert.AreEqual(0,   res.TotalReturned - res.TotalStaked, "net zero");
    }

    [Test]
    public void DontPass_ComeOut7_Lose()
    {
        var r = Round(3, 4);
        r.PlaceBet(NormalCrapsBetType.DontPass, 100);
        var res = r.Roll();
        Assert.AreEqual(0,   res.TotalReturned);
        Assert.AreEqual(100, res.TotalStaked);
    }

    [Test]
    public void DontPass_SevenOut_Win()
    {
        var r = RoundWithPoint(3, 3, 1, 6); // point=6, roll 7
        r.PlaceBet(NormalCrapsBetType.DontPass, 100);
        var res = r.Roll();
        Assert.AreEqual(200, res.TotalReturned);
        Assert.AreEqual(100, res.TotalStaked);
        Assert.IsTrue(res.RoundOver);
    }

    [Test]
    public void DontPass_WithLayOdds_SevenOut_Win()
    {
        var r = RoundWithPoint(3, 3, 1, 6); // point=6, roll 7
        r.PlaceBet(NormalCrapsBetType.DontPass, 100);
        r.PlaceBet(NormalCrapsBetType.DontPassOdds, 600); // 6x lay odds against 6/8
        var res = r.Roll();
        // DontPass 200 + LayOddsPayout(600,6) = 200 + (600 + 600*5/6) = 200 + 1100 = 1300
        Assert.AreEqual(1300, res.TotalReturned);
        Assert.AreEqual(700,  res.TotalStaked, "dont pass + lay odds both consumed");
    }

    [Test]
    public void DontPass_PointMade_Lose()
    {
        var r = RoundWithPoint(3, 3, 1, 5); // point=6, roll 6
        r.PlaceBet(NormalCrapsBetType.DontPass, 100);
        var res = r.Roll();
        Assert.AreEqual(0,   res.TotalReturned);
        Assert.AreEqual(100, res.TotalStaked);
    }

    [Test]
    public void DontPass_WithLayOdds_PointMade_Lose()
    {
        var r = RoundWithPoint(3, 3, 1, 5); // point=6, roll 6
        r.PlaceBet(NormalCrapsBetType.DontPass, 100);
        r.PlaceBet(NormalCrapsBetType.DontPassOdds, 600);
        var res = r.Roll();
        Assert.AreEqual(0,   res.TotalReturned);
        Assert.AreEqual(700, res.TotalStaked, "dont pass + lay odds both lost");
    }

    // ── 3. FIELD ─────────────────────────────────────────────────────────────

    [Test] public void Field_2_Win()   { var r = Round(1,1); r.PlaceBet(NormalCrapsBetType.Field,100); var res=r.Roll(); Assert.AreEqual(300,res.TotalReturned); Assert.AreEqual(100,res.TotalStaked); Assert.AreEqual(200,res.TotalReturned-res.TotalStaked,"net +200"); }
    [Test] public void Field_12_Win()  { var r = Round(6,6); r.PlaceBet(NormalCrapsBetType.Field,100); var res=r.Roll(); Assert.AreEqual(400,res.TotalReturned); Assert.AreEqual(100,res.TotalStaked); Assert.AreEqual(300,res.TotalReturned-res.TotalStaked,"net +300"); }
    [Test] public void Field_3_Win()   { var r = Round(1,2); r.PlaceBet(NormalCrapsBetType.Field,100); var res=r.Roll(); Assert.AreEqual(200,res.TotalReturned); Assert.AreEqual(100,res.TotalStaked); }
    [Test] public void Field_11_Win()  { var r = Round(5,6); r.PlaceBet(NormalCrapsBetType.Field,100); var res=r.Roll(); Assert.AreEqual(200,res.TotalReturned); Assert.AreEqual(100,res.TotalStaked); }
    [Test] public void Field_7_Lose()  { var r = Round(3,4); r.PlaceBet(NormalCrapsBetType.Field,100); var res=r.Roll(); Assert.AreEqual(0,res.TotalReturned); Assert.AreEqual(100,res.TotalStaked); Assert.AreEqual(-100,res.TotalReturned-res.TotalStaked,"net -100"); }
    [Test] public void Field_5_Lose()  { var r = Round(2,3); r.PlaceBet(NormalCrapsBetType.Field,100); var res=r.Roll(); Assert.AreEqual(0,res.TotalReturned); Assert.AreEqual(100,res.TotalStaked); }
    [Test] public void Field_6_Lose()  { var r = Round(1,5); r.PlaceBet(NormalCrapsBetType.Field,100); var res=r.Roll(); Assert.AreEqual(0,res.TotalReturned); Assert.AreEqual(100,res.TotalStaked); }
    [Test] public void Field_8_Lose()  { var r = Round(2,6); r.PlaceBet(NormalCrapsBetType.Field,100); var res=r.Roll(); Assert.AreEqual(0,res.TotalReturned); Assert.AreEqual(100,res.TotalStaked); }

    // ── 4. PROPS ─────────────────────────────────────────────────────────────

    [Test] public void AnyCraps_2_Win()    { var r=Round(1,1); r.PlaceBet(NormalCrapsBetType.AnyCraps,100); var res=r.Roll(); Assert.AreEqual(800,res.TotalReturned); Assert.AreEqual(100,res.TotalStaked); Assert.AreEqual(700,res.TotalReturned-res.TotalStaked,"net +700"); }
    [Test] public void AnyCraps_3_Win()    { var r=Round(1,2); r.PlaceBet(NormalCrapsBetType.AnyCraps,100); var res=r.Roll(); Assert.AreEqual(800,res.TotalReturned); Assert.AreEqual(100,res.TotalStaked); }
    [Test] public void AnyCraps_12_Win()   { var r=Round(6,6); r.PlaceBet(NormalCrapsBetType.AnyCraps,100); var res=r.Roll(); Assert.AreEqual(800,res.TotalReturned); Assert.AreEqual(100,res.TotalStaked); }
    [Test] public void AnyCraps_7_Lose()   { var r=Round(3,4); r.PlaceBet(NormalCrapsBetType.AnyCraps,100); var res=r.Roll(); Assert.AreEqual(0,res.TotalReturned); Assert.AreEqual(100,res.TotalStaked); Assert.AreEqual(-100,res.TotalReturned-res.TotalStaked,"net -100"); }
    [Test] public void AnySeven_Win()      { var r=Round(3,4); r.PlaceBet(NormalCrapsBetType.AnySeven,100); var res=r.Roll(); Assert.AreEqual(500,res.TotalReturned); Assert.AreEqual(100,res.TotalStaked); Assert.AreEqual(400,res.TotalReturned-res.TotalStaked,"net +400"); }
    [Test] public void AnySeven_Lose()     { var r=Round(1,1); r.PlaceBet(NormalCrapsBetType.AnySeven,100); var res=r.Roll(); Assert.AreEqual(0,res.TotalReturned); Assert.AreEqual(100,res.TotalStaked); }
    [Test] public void AnyEleven_Win()     { var r=Round(5,6); r.PlaceBet(NormalCrapsBetType.AnyEleven,100); var res=r.Roll(); Assert.AreEqual(1600,res.TotalReturned); Assert.AreEqual(100,res.TotalStaked); Assert.AreEqual(1500,res.TotalReturned-res.TotalStaked,"net +1500"); }
    [Test] public void AnyEleven_Lose()    { var r=Round(3,4); r.PlaceBet(NormalCrapsBetType.AnyEleven,100); var res=r.Roll(); Assert.AreEqual(0,res.TotalReturned); Assert.AreEqual(100,res.TotalStaked); }
    [Test] public void Horn_2_Win()        { var r=Round(1,1); r.PlaceBet(NormalCrapsBetType.Horn,100); var res=r.Roll(); Assert.AreEqual(775,res.TotalReturned); Assert.AreEqual(100,res.TotalStaked); Assert.AreEqual(675,res.TotalReturned-res.TotalStaked,"net +675"); }
    [Test] public void Horn_11_Win()       { var r=Round(5,6); r.PlaceBet(NormalCrapsBetType.Horn,100); var res=r.Roll(); Assert.AreEqual(400,res.TotalReturned); Assert.AreEqual(100,res.TotalStaked); } // 25*16=400
    [Test] public void Horn_8_Lose()       { var r=Round(2,6); r.PlaceBet(NormalCrapsBetType.Horn,100); var res=r.Roll(); Assert.AreEqual(0,res.TotalReturned); Assert.AreEqual(100,res.TotalStaked); Assert.AreEqual(-100,res.TotalReturned-res.TotalStaked,"net -100"); }

    [Test] public void CAndE_Craps_Win()   { var r=Round(1,2); r.PlaceBet(NormalCrapsBetType.CAndE,100); var res=r.Roll(); Assert.AreEqual(400,res.TotalReturned); Assert.AreEqual(100,res.TotalStaked); Assert.AreEqual(300,res.TotalReturned-res.TotalStaked,"net +300 (3:1)"); }
    [Test] public void CAndE_12_Win()      { var r=Round(6,6); r.PlaceBet(NormalCrapsBetType.CAndE,100); var res=r.Roll(); Assert.AreEqual(300,res.TotalReturned-res.TotalStaked,"net +300 (3:1)"); }
    [Test] public void CAndE_11_Win()      { var r=Round(5,6); r.PlaceBet(NormalCrapsBetType.CAndE,100); var res=r.Roll(); Assert.AreEqual(800,res.TotalReturned); Assert.AreEqual(700,res.TotalReturned-res.TotalStaked,"net +700 (7:1)"); }
    [Test] public void CAndE_7_Lose()      { var r=Round(3,4); r.PlaceBet(NormalCrapsBetType.CAndE,100); var res=r.Roll(); Assert.AreEqual(0,res.TotalReturned); Assert.AreEqual(-100,res.TotalReturned-res.TotalStaked,"net -100"); Assert.AreEqual(0,r.GetBet(NormalCrapsBetType.CAndE),"one-roll bet cleared"); }

    // ── 5. HARDWAYS (follow BETS ON/OFF like Place/Lay) ──────────────────────

    [Test]
    public void Hard_BetsOff_NoAction_OnHardOr7()
    {
        var r = Round(2, 2, 3, 4); // hard 4, then 7 — bets OFF by default
        r.PlaceBet(NormalCrapsBetType.Hard4, 100);
        var hard = r.Roll();
        Assert.AreEqual(0, hard.TotalReturned, "off: hard 4 doesn't pay");
        Assert.AreEqual(0, hard.TotalStaked,   "off: nothing resolved");
        var seven = r.Roll();
        Assert.AreEqual(0,   seven.TotalStaked, "off: 7 doesn't take it");
        Assert.AreEqual(100, r.GetBet(NormalCrapsBetType.Hard4), "stake still on table");
    }

    [Test]
    public void Hard_BetsOff_CarriesOverOnSevenOut()
    {
        var r = RoundWithPoint(3, 3, 3, 4); // point 6, then seven-out, bets OFF
        r.PlaceBet(NormalCrapsBetType.Hard8, 100);
        var res = r.Roll();
        Assert.IsTrue(res.RoundOver);
        Assert.IsTrue(res.PlaceBetsCarriedOver, "hardway counts as carried bet");
        Assert.AreEqual(100, r.GetBet(NormalCrapsBetType.Hard8));
    }

    [Test]
    public void Hard4_Win_NoPriorPoint()
    {
        var r = PlaceRound(2, 2); // 4 hard on come-out, bets working
        r.PlaceBet(NormalCrapsBetType.Hard4, 100);
        var res = r.Roll();
        Assert.AreEqual(800, res.TotalReturned, "7:1 → 800");
        Assert.AreEqual(100, res.TotalStaked);
        Assert.AreEqual(700, res.TotalReturned - res.TotalStaked, "net +700");
    }

    [Test]
    public void Hard4_Win_WithPoint()
    {
        var r = RoundWithPoint(3, 3, 2, 2); // point=6, then roll hard 4
        r.PlaceBetsWorking = true;
        r.PlaceBet(NormalCrapsBetType.Hard4, 100);
        var res = r.Roll();
        Assert.AreEqual(800, res.TotalReturned);
        Assert.AreEqual(100, res.TotalStaked);
    }

    [Test]
    public void Hard6_Win()
    {
        var r = PlaceRound(3, 3); // hard 6
        r.PlaceBet(NormalCrapsBetType.Hard6, 100);
        var res = r.Roll();
        Assert.AreEqual(1000, res.TotalReturned, "9:1 → 1000");
        Assert.AreEqual(100,  res.TotalStaked);
        Assert.AreEqual(900,  res.TotalReturned - res.TotalStaked, "net +900");
    }

    [Test]
    public void Hard8_Win()
    {
        var r = PlaceRound(4, 4);
        r.PlaceBet(NormalCrapsBetType.Hard8, 100);
        var res = r.Roll();
        Assert.AreEqual(1000, res.TotalReturned);
        Assert.AreEqual(100,  res.TotalStaked);
    }

    [Test]
    public void Hard10_Win()
    {
        var r = PlaceRound(5, 5);
        r.PlaceBet(NormalCrapsBetType.Hard10, 100);
        var res = r.Roll();
        Assert.AreEqual(800, res.TotalReturned);
        Assert.AreEqual(100, res.TotalStaked);
    }

    [Test]
    public void Hard4_Lose_On_7()
    {
        var r = PlaceRound(3, 4);
        r.PlaceBet(NormalCrapsBetType.Hard4, 100);
        var res = r.Roll();
        Assert.AreEqual(0,    res.TotalReturned);
        Assert.AreEqual(100,  res.TotalStaked);
        Assert.AreEqual(-100, res.TotalReturned - res.TotalStaked, "net -100");
    }

    [Test]
    public void Hard6_Lose_On_Easy6()
    {
        var r = PlaceRound(4, 2); // easy 6
        r.PlaceBet(NormalCrapsBetType.Hard6, 100);
        var res = r.Roll();
        Assert.AreEqual(0,   res.TotalReturned);
        Assert.AreEqual(100, res.TotalStaked, "easy 6 kills Hard6");
    }

    [Test]
    public void Hard6_Lose_On_SevenOut()
    {
        var r = RoundWithPoint(2, 2, 1, 6); // point=4, roll 7
        r.PlaceBetsWorking = true;
        r.PlaceBet(NormalCrapsBetType.Hard6, 100);
        var res = r.Roll();
        Assert.AreEqual(0,   res.TotalReturned);
        Assert.AreEqual(100, res.TotalStaked);
    }

    // ── 6. PLACE BETS (PlaceBetsWorking = true) ───────────────────────────────
    // Stake stays on table on win — TotalStaked = 0 for winning Place bets.

    static NormalCrapsRound PlaceRound(params int[] dice)
    {
        var r = new NormalCrapsRound(new FixedDiceSource(dice));
        r.PlaceBetsWorking = true;
        return r;
    }

    [Test] public void Place4_Win()  { var r=PlaceRound(2,2); r.PlaceBet(NormalCrapsBetType.Place4,100); var res=r.Roll(); Assert.AreEqual(180,res.TotalReturned); Assert.AreEqual(0,res.TotalStaked); Assert.AreEqual(180,res.TotalReturned-res.TotalStaked,"net +180"); }
    [Test] public void Place5_Win()  { var r=PlaceRound(2,3); r.PlaceBet(NormalCrapsBetType.Place5,100); var res=r.Roll(); Assert.AreEqual(140,res.TotalReturned); Assert.AreEqual(0,res.TotalStaked); }
    [Test] public void Place6_Win()  { var r=PlaceRound(1,5); r.PlaceBet(NormalCrapsBetType.Place6,100); var res=r.Roll(); Assert.AreEqual(116,res.TotalReturned); Assert.AreEqual(0,res.TotalStaked); }
    [Test] public void Place8_Win()  { var r=PlaceRound(2,6); r.PlaceBet(NormalCrapsBetType.Place8,100); var res=r.Roll(); Assert.AreEqual(116,res.TotalReturned); Assert.AreEqual(0,res.TotalStaked); }
    [Test] public void Place9_Win()  { var r=PlaceRound(4,5); r.PlaceBet(NormalCrapsBetType.Place9,100); var res=r.Roll(); Assert.AreEqual(140,res.TotalReturned); Assert.AreEqual(0,res.TotalStaked); }
    [Test] public void Place10_Win() { var r=PlaceRound(5,5); r.PlaceBet(NormalCrapsBetType.Place10,100); var res=r.Roll(); Assert.AreEqual(180,res.TotalReturned); Assert.AreEqual(0,res.TotalStaked); }

    [Test] public void Place4_Lose_SevenOut()  { var r=PlaceRound(3,4); r.PlaceBet(NormalCrapsBetType.Place4,100); var res=r.Roll(); Assert.AreEqual(0,res.TotalReturned); Assert.AreEqual(100,res.TotalStaked); Assert.AreEqual(-100,res.TotalReturned-res.TotalStaked,"net -100"); }
    [Test] public void Place6_Lose_SevenOut()  { var r=PlaceRound(3,4); r.PlaceBet(NormalCrapsBetType.Place6,100); var res=r.Roll(); Assert.AreEqual(0,res.TotalReturned); Assert.AreEqual(100,res.TotalStaked); }
    [Test]
    public void AllPlace_Lose_SevenOut()
    {
        var r = PlaceRound(3, 4); // 7
        r.PlaceBet(NormalCrapsBetType.Place4,  100);
        r.PlaceBet(NormalCrapsBetType.Place5,  100);
        r.PlaceBet(NormalCrapsBetType.Place6,  100);
        r.PlaceBet(NormalCrapsBetType.Place8,  100);
        r.PlaceBet(NormalCrapsBetType.Place9,  100);
        r.PlaceBet(NormalCrapsBetType.Place10, 100);
        var res = r.Roll();
        Assert.AreEqual(0,    res.TotalReturned);
        Assert.AreEqual(600,  res.TotalStaked, "all 6 place bets lost");
        Assert.AreEqual(-600, res.TotalReturned - res.TotalStaked, "net -600");
    }

    // ── 7. LAY BETS ──────────────────────────────────────────────────────────
    // Stake stays on table on 7-win — TotalStaked = 0 for winning Lay bets.
    // Vegas 5% commission taken from winnings: $100 win → $95.

    [Test] public void Lay4_Win_On7()   { var r=PlaceRound(3,4); r.PlaceBet(NormalCrapsBetType.Lay4,200); var res=r.Roll(); Assert.AreEqual(95,res.TotalReturned,"100 win - 5 vig"); Assert.AreEqual(0,res.TotalStaked); Assert.AreEqual(95,res.TotalReturned-res.TotalStaked,"net +95"); }
    [Test] public void Lay5_Win_On7()   { var r=PlaceRound(3,4); r.PlaceBet(NormalCrapsBetType.Lay5,300); var res=r.Roll(); Assert.AreEqual(190,res.TotalReturned,"200 win - 10 vig"); Assert.AreEqual(0,res.TotalStaked); }
    [Test] public void Lay6_Win_On7()   { var r=PlaceRound(3,4); r.PlaceBet(NormalCrapsBetType.Lay6,300); var res=r.Roll(); Assert.AreEqual(238,res.TotalReturned,"250 win - 12 vig"); Assert.AreEqual(0,res.TotalStaked); }
    [Test] public void Lay8_Win_On7()   { var r=PlaceRound(3,4); r.PlaceBet(NormalCrapsBetType.Lay8,300); var res=r.Roll(); Assert.AreEqual(238,res.TotalReturned); Assert.AreEqual(0,res.TotalStaked); }
    [Test] public void Lay9_Win_On7()   { var r=PlaceRound(3,4); r.PlaceBet(NormalCrapsBetType.Lay9,300); var res=r.Roll(); Assert.AreEqual(190,res.TotalReturned); Assert.AreEqual(0,res.TotalStaked); }
    [Test] public void Lay10_Win_On7()  { var r=PlaceRound(3,4); r.PlaceBet(NormalCrapsBetType.Lay10,200); var res=r.Roll(); Assert.AreEqual(95,res.TotalReturned); Assert.AreEqual(0,res.TotalStaked); }

    [Test] public void Lay4_Lose_On4()  { var r=PlaceRound(2,2); r.PlaceBet(NormalCrapsBetType.Lay4,200); var res=r.Roll(); Assert.AreEqual(0,res.TotalReturned); Assert.AreEqual(200,res.TotalStaked); Assert.AreEqual(-200,res.TotalReturned-res.TotalStaked,"net -200"); }
    [Test] public void Lay6_Lose_On6()  { var r=PlaceRound(3,3); r.PlaceBet(NormalCrapsBetType.Lay6,300); var res=r.Roll(); Assert.AreEqual(0,res.TotalReturned); Assert.AreEqual(300,res.TotalStaked); }
    [Test] public void Lay10_Lose_On10(){ var r=PlaceRound(5,5); r.PlaceBet(NormalCrapsBetType.Lay10,200); var res=r.Roll(); Assert.AreEqual(0,res.TotalReturned); Assert.AreEqual(200,res.TotalStaked); }

    // ── 8. COME BETS ──────────────────────────────────────────────────────────

    [Test]
    public void Come_Natural7_Win()
    {
        // point=4, unparked Come bet, roll 7 → Come natural win (seven-out too)
        var r = RoundWithPoint(2, 2, 3, 4); // point=4, roll 7
        var w = r.PlaceComeBet(100);
        var res = r.Roll(); // 7: unparked Come wins, round over (seven-out)
        Assert.AreEqual(100, w.Amount);
        Assert.IsTrue(res.ComeReturns.ContainsKey(w), "Come won on natural 7");
        Assert.AreEqual(200, res.ComeReturns[w], "stake + win");
        Assert.AreEqual(100, res.TotalStaked, "Come stake consumed");
    }

    [Test]
    public void Come_Natural11_Win()
    {
        var r = RoundWithPoint(2, 2, 5, 6); // point=4, roll 11
        var w = r.PlaceComeBet(100);
        var res = r.Roll();
        Assert.IsTrue(res.ComeReturns.ContainsKey(w));
        Assert.AreEqual(200, res.ComeReturns[w]);
        Assert.AreEqual(100, res.TotalStaked);
        Assert.AreEqual(100, res.TotalReturned - res.TotalStaked, "net +100");
    }

    [Test]
    public void Come_CrapsOut_Lose()
    {
        var r = RoundWithPoint(2, 2, 1, 1); // point=4, roll 2 (Come craps)
        var w = r.PlaceComeBet(100);
        var res = r.Roll();
        Assert.IsTrue(res.ComeCrapsOut.Contains(w), "Come crapped out");
        Assert.AreEqual(0,    res.TotalReturned);
        Assert.AreEqual(100,  res.TotalStaked, "Come stake lost");
        Assert.AreEqual(-100, res.TotalReturned - res.TotalStaked, "net -100");
    }

    [Test]
    public void Come_Parks_Then_Wins()
    {
        // point=4, Come placed, roll 6 (Come parks at 6), add odds, roll 6 (Come wins)
        var r = new NormalCrapsRound(new FixedDiceSource(2,2, 1,5, 1,5));
        r.Roll(); // point=4
        var w = r.PlaceComeBet(100);
        r.Roll();                   // 6: Come parks
        r.AddComeOdds(w, 100);
        var res = r.Roll();         // 6: Come wins
        // ComeReturn = stake*2 + OddsPayout(100,6) = 200 + (100 + 100*6/5) = 200 + 220 = 420
        Assert.IsTrue(res.ComeReturns.ContainsKey(w));
        Assert.AreEqual(420, res.ComeReturns[w]);
        Assert.AreEqual(200, res.TotalStaked, "base + odds consumed");
        Assert.AreEqual(220, res.TotalReturned - res.TotalStaked, "net +220");
    }

    [Test]
    public void Come_Parks_Then_SevenOut_Lose()
    {
        // point=4, Come placed, roll 6 (parks), roll 7 (seven-out: Come loses, odds lost)
        var r = new NormalCrapsRound(new FixedDiceSource(2,2, 1,5, 3,4));
        r.Roll(); // point=4
        var w = r.PlaceComeBet(100);
        r.Roll();          // 6: Come parks
        r.AddComeOdds(w, 100);
        var res = r.Roll(); // 7: Come (parked, odds working) loses base + odds
        Assert.AreEqual(0,   res.TotalReturned);
        Assert.AreEqual(200, res.TotalStaked, "Come base + odds lost");
        Assert.IsTrue(res.RoundOver);
    }

    [Test]
    public void Come_Parks_SevenOut_OddsOff_OddsReturned()
    {
        // point=4, Come parks at 6, point made (4) → come-out phase, odds now OFF
        // Then 7 come-out natural: Come parked at 6 loses base, odds returned
        var r = new NormalCrapsRound(new FixedDiceSource(2,2, 1,5, 2,2, 3,4));
        r.PlaceBet(NormalCrapsBetType.PassLine, 100);
        r.Roll();           // point=4
        var w = r.PlaceComeBet(100);
        r.Roll();           // 6: Come parks
        r.AddComeOdds(w, 100);
        r.Roll();           // 4: point made, come-out phase; PassLine wins
        var res = r.Roll(); // 7: come-out natural; parked Come loses base, odds RETURNED
        Assert.IsTrue(res.ComeReturns.ContainsKey(w), "odds returned on come-out 7");
        Assert.AreEqual(100, res.ComeReturns[w], "only odds returned, not base");
        Assert.AreEqual(200, res.TotalStaked, "Come base consumed + odds resolved");
        Assert.AreEqual(-100, res.TotalReturned - res.TotalStaked, "net -100: base lost, odds merely returned");
    }

    [Test]
    public void Come_Parks_OwnPoint_OnComeOut_OddsOff_OddsReturnedNotProfit()
    {
        // point=4, Come parks at 6 with odds, point made → come-out; come-out 6 hits Come point with odds OFF
        var r = new NormalCrapsRound(new FixedDiceSource(2,2, 1,5, 2,2, 3,3));
        r.PlaceBet(NormalCrapsBetType.PassLine, 100);
        r.Roll();           // point=4
        var w = r.PlaceComeBet(100);
        r.Roll();           // 6: Come parks
        r.AddComeOdds(w, 100);
        r.Roll();           // 4: point made
        var res = r.Roll(); // 6 on come-out: Come base wins even money, odds returned without payout
        Assert.AreEqual(300, res.ComeReturns[w], "base*2 + odds returned");
        Assert.AreEqual(200, res.TotalStaked, "base + odds resolved");
        Assert.AreEqual(100, res.TotalReturned - res.TotalStaked, "net +100: only the base won");
    }

    // ── 9. DON'T COME ────────────────────────────────────────────────────────

    [Test]
    public void DontCome_Unparked_2_Win()
    {
        var r = RoundWithPoint(2, 2, 1, 1); // point=4, roll 2
        var w = r.PlaceDontComeBet(100);
        var res = r.Roll();
        Assert.IsTrue(res.DontComeReturns.ContainsKey(w));
        Assert.AreEqual(200, res.DontComeReturns[w]);
        Assert.AreEqual(100, res.TotalStaked);
        Assert.AreEqual(100, res.TotalReturned - res.TotalStaked, "net +100");
    }

    [Test]
    public void DontCome_Unparked_12_Push()
    {
        var r = RoundWithPoint(2, 2, 6, 6); // point=4, roll 12
        var w = r.PlaceDontComeBet(100);
        var res = r.Roll();
        Assert.IsTrue(res.DontComePushed.Contains(w), "push on 12");
        Assert.AreEqual(100, res.TotalReturned, "stake returned as push");
        Assert.AreEqual(100, res.TotalStaked,   "stake consumed then returned");
        Assert.AreEqual(0,   res.TotalReturned - res.TotalStaked, "net zero");
    }

    [Test]
    public void DontCome_Unparked_7_Lose()
    {
        var r = RoundWithPoint(2, 2, 3, 4); // point=4, roll 7 (seven-out AND DC loses)
        var w = r.PlaceDontComeBet(100);
        var res = r.Roll();
        Assert.AreEqual(0,   res.TotalReturned);
        Assert.AreEqual(100, res.TotalStaked, "DC loses on 7/11 unparked");
        Assert.IsTrue(res.RoundOver);
    }

    [Test]
    public void DontCome_Parks_Then_7_Win()
    {
        // point=4, DC placed, roll 6 (DC parks at 6), roll 7 (DC wins)
        var r = new NormalCrapsRound(new FixedDiceSource(2,2, 1,5, 3,4));
        r.Roll(); // point=4
        var w = r.PlaceDontComeBet(100);
        r.Roll();          // 6: DC parks
        var res = r.Roll(); // 7: DC wins (seven-out, DC parked wins)
        Assert.IsTrue(res.DontComeReturns.ContainsKey(w));
        Assert.AreEqual(200, res.DontComeReturns[w]);
        Assert.AreEqual(100, res.TotalStaked);
        Assert.IsTrue(res.RoundOver);
    }

    [Test]
    public void DontCome_Parks_OwnPoint_Lose()
    {
        // point=4, DC placed, roll 6 (DC parks at 6), roll 6 (DC loses)
        var r = new NormalCrapsRound(new FixedDiceSource(2,2, 1,5, 1,5));
        r.Roll(); // point=4
        var w = r.PlaceDontComeBet(100);
        r.Roll();          // 6: DC parks
        var res = r.Roll(); // 6: DC own point — DC loses
        Assert.AreEqual(0,    res.TotalReturned);
        Assert.AreEqual(100,  res.TotalStaked, "DC lost when its point hit");
        Assert.AreEqual(-100, res.TotalReturned - res.TotalStaked, "net -100");
    }

    // ── 10. MIX: DontPass + Place bets on seven-out ──────────────────────────

    [Test]
    public void Mix_DontPass_Plus_Places_SevenOut_NetNegative()
    {
        // DontPass $100 wins $100 profit; 4 place bets ($100 each) lose $400.
        // TotalStaked = DontPass(100) + places(400) = 500. Net = 200 - 500 = -300.
        var r = new NormalCrapsRound(new FixedDiceSource(3,3, 3,4));
        r.Roll(); // point=6
        r.PlaceBet(NormalCrapsBetType.DontPass, 100);
        r.PlaceBet(NormalCrapsBetType.Place4,  100);
        r.PlaceBet(NormalCrapsBetType.Place5,  100);
        r.PlaceBet(NormalCrapsBetType.Place8,  100);
        r.PlaceBet(NormalCrapsBetType.Place9,  100);
        r.PlaceBetsWorking = true;
        var res = r.Roll(); // 7: DontPass wins, places lose
        Assert.AreEqual(200,  res.TotalReturned, "DontPass win");
        Assert.AreEqual(500,  res.TotalStaked,   "DontPass(100) + 4 places(400)");
        Assert.AreEqual(-300, res.TotalReturned - res.TotalStaked, "net -300 RED");
        Assert.IsTrue(res.RoundOver);
    }

    [Test]
    public void Mix_DontPass_Plus_Places_SevenOut_NetPositive()
    {
        // DontPass $500 wins $500 profit; Place4 $100 loses. Net = 500 - 100 = +400.
        // TotalStaked = DontPass(500) + place(100) = 600. TotalReturned = 1000. Net = 400.
        var r = new NormalCrapsRound(new FixedDiceSource(3,3, 3,4));
        r.Roll(); // point=6
        r.PlaceBet(NormalCrapsBetType.DontPass, 500);
        r.PlaceBet(NormalCrapsBetType.Place4,   100);
        r.PlaceBetsWorking = true;
        var res = r.Roll(); // 7
        Assert.AreEqual(1000, res.TotalReturned);
        Assert.AreEqual(600,  res.TotalStaked, "DontPass(500) + Place4(100)");
        Assert.AreEqual(400,  res.TotalReturned - res.TotalStaked, "net +400 GREEN even on seven-out");
    }

    // ── 11. MIX: PassLine + Lay bets ─────────────────────────────────────────

    [Test]
    public void Mix_PassLine_Plus_Lay_PointMade()
    {
        // PassLine $100 wins $100 profit; Lay6 $300 loses. Net = 100 - 300 = -200.
        // TotalStaked = PassLine(100) + Lay6(300) = 400. TotalReturned = 200. Net = -200.
        var r = new NormalCrapsRound(new FixedDiceSource(3,3, 1,5));
        r.Roll(); // point=6
        r.PlaceBet(NormalCrapsBetType.PassLine, 100);
        r.PlaceBet(NormalCrapsBetType.Lay6, 300);
        r.PlaceBetsWorking = true;
        var res = r.Roll(); // 6
        Assert.AreEqual(200,  res.TotalReturned, "PassLine win");
        Assert.AreEqual(400,  res.TotalStaked,   "PassLine(100) + Lay6 loss(300)");
        Assert.AreEqual(-200, res.TotalReturned - res.TotalStaked, "net -200 RED");
    }

    [Test]
    public void Mix_PassLine_Plus_Lay_SevenOut()
    {
        // PassLine $100 loses; Lay6 $300 wins $250 less 5% vig = $238 (stake stays). Net = 238 - 100 = +138.
        var r = new NormalCrapsRound(new FixedDiceSource(3,3, 3,4));
        r.Roll(); // point=6
        r.PlaceBet(NormalCrapsBetType.PassLine, 100);
        r.PlaceBet(NormalCrapsBetType.Lay6, 300);
        r.PlaceBetsWorking = true;
        var res = r.Roll(); // 7
        Assert.AreEqual(238, res.TotalReturned, "Lay6 wins (winnings less vig, stake stays)");
        Assert.AreEqual(100, res.TotalStaked,   "PassLine lost (Lay6 stake stays on table)");
        Assert.AreEqual(138, res.TotalReturned - res.TotalStaked, "net +138 GREEN on seven-out");
    }

    // ── 12. MIX: Hardways without point ──────────────────────────────────────

    [Test]
    public void Hard_Without_Point_7_Lose()
    {
        var r = PlaceRound(3, 4); // come-out 7 — PassLine natural, bets working
        r.PlaceBet(NormalCrapsBetType.Hard6, 100);
        r.PlaceBet(NormalCrapsBetType.Hard8, 100);
        var res = r.Roll();
        Assert.AreEqual(0,    res.TotalReturned);
        Assert.AreEqual(200,  res.TotalStaked, "both hardways lost to 7");
        Assert.AreEqual(-200, res.TotalReturned - res.TotalStaked, "net -200");
    }

    [Test]
    public void Hard_Without_Point_Win()
    {
        var r = PlaceRound(4, 4); // hard 8
        r.PlaceBet(NormalCrapsBetType.Hard8, 100);
        var res = r.Roll();
        Assert.AreEqual(1000, res.TotalReturned);
        Assert.AreEqual(100,  res.TotalStaked);
    }

    // ── 12b. ATS — Las Vegas Bonus Craps rules ───────────────────────────────

    [Test]
    public void Ats_ComeOut7_Loses()
    {
        var r = Round(1, 1, 3, 4); // come-out 2 (counts), then come-out natural 7
        Assert.IsTrue(r.CanPlaceAts);
        r.PlaceBet(NormalCrapsBetType.AtsLows, 25);
        r.Roll();
        Assert.IsFalse(r.CanPlaceAts, "run in progress after a number rolls");
        var res = r.Roll();
        Assert.IsTrue(res.AtsSevenOut, "any 7 ends the run");
        Assert.AreEqual(25, res.TotalStaked, "ATS lost on come-out 7");
        Assert.AreEqual(0, r.GetBet(NormalCrapsBetType.AtsLows));
        Assert.IsTrue(r.CanPlaceAts, "can re-bet after the 7");
    }

    [Test]
    public void Ats_CannotPlace_DuringPointPhase()
    {
        var r = Round(2, 2); // point 4
        r.Roll();
        Assert.IsFalse(r.CanPlaceAts);
    }

    [Test]
    public void Ats_SmallWin_KeepsNumbers_AllCanStillComplete()
    {
        // come-out 2 → 3 (craps) → 4 point → 5 → 6 (Small done) → 8 → 9 → 10 → 11 → 12 (All done)
        var r = Round(1,1, 1,2, 2,2, 2,3, 3,3, 4,4, 4,5, 5,5, 5,6, 6,6);
        r.PlaceBet(NormalCrapsBetType.AtsLows, 10);
        r.PlaceBet(NormalCrapsBetType.AtsAll, 10);
        NormalCrapsRollResult res = null;
        for (int i = 0; i < 5; i++) res = r.Roll();
        Assert.AreEqual(310, res.AtsLowsReturn, "Small pays 30:1 on the 6");
        Assert.AreEqual(0, res.AtsAllReturn, "All not done yet");
        for (int i = 0; i < 5; i++) res = r.Roll();
        Assert.AreEqual(1560, res.AtsAllReturn, "All pays 155:1 — Small win didn't wipe the low numbers");
    }

    // ── 13. MIX: Full board seven-out — all losses trackable ─────────────────

    [Test]
    public void FullBoard_SevenOut_AllLost()
    {
        var r = new NormalCrapsRound(new FixedDiceSource(3,3, 3,4));
        r.Roll(); // point=6
        r.PlaceBet(NormalCrapsBetType.PassLine,  100);
        r.PlaceBet(NormalCrapsBetType.Place4,    100);
        r.PlaceBet(NormalCrapsBetType.Place5,    100);
        r.PlaceBet(NormalCrapsBetType.Place8,    100);
        r.PlaceBet(NormalCrapsBetType.Place9,    100);
        r.PlaceBet(NormalCrapsBetType.Place10,   100);
        r.PlaceBet(NormalCrapsBetType.Hard4,     50);
        r.PlaceBet(NormalCrapsBetType.Hard8,     50);
        r.PlaceBetsWorking = true;
        var res = r.Roll(); // 7: everything loses
        Assert.AreEqual(0, res.TotalReturned);
        // PassLine=100 + Place4/5/8/9/10=500 + Hard4/8=100 = 700
        Assert.AreEqual(700,  res.TotalStaked, "all bets lost");
        Assert.AreEqual(-700, res.TotalReturned - res.TotalStaked, "net -700 RED");
        Assert.IsTrue(res.RoundOver);
    }
}
