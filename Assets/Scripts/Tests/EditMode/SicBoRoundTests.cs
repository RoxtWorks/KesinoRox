using NUnit.Framework;
using System.Collections.Generic;

[TestFixture]
public class SicBoRoundTests
{
    class FixedDiceSource : IRandomSource
    {
        readonly Queue<int> faces;
        public FixedDiceSource(params int[] dieFaces) => faces = new Queue<int>(dieFaces);
        public int Next(int minInclusive, int maxExclusive) => faces.Dequeue();
    }

    // Helper: create a round with a fixed 3-die roll sequence
    static SicBoRound MakeRound(params int[] dice)
    {
        var rng = new FixedDiceSource(dice);
        return new SicBoRound(rng);
    }

    [Test]
    public void Big_Bet_Wins_On_11()
    {
        // 4+3+4=11, Big wins
        var round = MakeRound(4, 3, 4);
        round.PlaceBet(SicBoBetType.Big, 100);
        var result = round.Roll();
        Assert.AreEqual(200, result.TotalReturned);
        Assert.IsTrue(result.Returns.ContainsKey(SicBoBetType.Big));
    }

    [Test]
    public void Small_Bet_Loses_On_11()
    {
        var round = MakeRound(4, 3, 4);
        round.PlaceBet(SicBoBetType.Small, 100);
        var result = round.Roll();
        Assert.AreEqual(0, result.TotalReturned);
    }

    [Test]
    public void Big_Loses_On_Triple_Even_If_High()
    {
        // 4+4+4=12, isTriple=true, Big should lose
        var round = MakeRound(4, 4, 4);
        round.PlaceBet(SicBoBetType.Big, 100);
        var result = round.Roll();
        Assert.AreEqual(0, result.TotalReturned);
    }

    [Test]
    public void AnyTriple_Wins_On_Three_Of_A_Kind()
    {
        var round = MakeRound(5, 5, 5);
        round.PlaceBet(SicBoBetType.AnyTriple, 100);
        var result = round.Roll();
        Assert.AreEqual(2500, result.TotalReturned);
    }

    [Test]
    public void SpecificTriple_Wins_On_Exact_Face()
    {
        var round = MakeRound(3, 3, 3);
        round.PlaceBet(SicBoBetType.Triple3, 100);
        var result = round.Roll();
        Assert.AreEqual(15100, result.TotalReturned);
    }

    [Test]
    public void Total_Bet_Wins_On_Matching_Sum()
    {
        // 2+2+3=7
        var round = MakeRound(2, 2, 3);
        round.PlaceBet(SicBoBetType.Total7, 100);
        var result = round.Roll();
        Assert.AreEqual(1300, result.TotalReturned); // 12:1 → stake×13
    }

    [Test]
    public void Single_Bet_Wins_Proportionally()
    {
        // 1+1+3 → two 1s
        var round = MakeRound(1, 1, 3);
        round.PlaceBet(SicBoBetType.Single1, 100);
        var result = round.Roll();
        Assert.AreEqual(300, result.TotalReturned); // 2:1 for 2 matches
    }

    [Test]
    public void Combo_Bet_Wins_When_Both_Faces_Present()
    {
        // 1,2,4 → Combo12 wins
        var round = MakeRound(1, 2, 4);
        round.PlaceBet(SicBoBetType.Combo12, 100);
        var result = round.Roll();
        Assert.AreEqual(600, result.TotalReturned);
    }

    [Test]
    public void Multiple_Bets_Can_Win_Same_Roll()
    {
        // 3+4+4 = 11: Big wins, Single4 wins (two 4s), Double4 wins
        var round = MakeRound(3, 4, 4);
        round.PlaceBet(SicBoBetType.Big,     100);
        round.PlaceBet(SicBoBetType.Single4, 100);
        round.PlaceBet(SicBoBetType.Double4, 100);
        var result = round.Roll();
        // Big: 200, Single4 2matches: 300, Double4: 900
        Assert.AreEqual(1400, result.TotalReturned);
        Assert.AreEqual(300, result.TotalStaked);
    }

    [Test]
    public void Bets_Cleared_After_Roll()
    {
        var round = MakeRound(1, 2, 3, 4, 5, 6);
        round.PlaceBet(SicBoBetType.Big, 100);
        round.Roll();
        // second roll — no bets active
        var result2 = round.Roll();
        Assert.AreEqual(0, result2.TotalReturned);
    }
}
