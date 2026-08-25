using NUnit.Framework;

public class BaccaratResolverTests
{
    static Card C(Rank rank, Suit suit = Suit.Spades) => new Card(rank, suit);

    static BaccaratHand Hand(params Rank[] ranks)
    {
        var hand = new BaccaratHand();
        foreach (var r in ranks) hand.AddCard(C(r));
        return hand;
    }

    [TestCase(Rank.Ace, 1)]
    [TestCase(Rank.Nine, 9)]
    [TestCase(Rank.Ten, 0)]
    [TestCase(Rank.Jack, 0)]
    [TestCase(Rank.Queen, 0)]
    [TestCase(Rank.King, 0)]
    public void CardValue_MapsRankToBaccaratPoint(Rank rank, int expected)
    {
        Assert.AreEqual(expected, BaccaratResolver.CardValue(C(rank)));
    }

    [Test]
    public void Point_WrapsModTen()
    {
        var hand = Hand(Rank.King, Rank.Nine, Rank.Eight); // 0 + 9 + 8 = 17 -> 7
        Assert.AreEqual(7, hand.Point);
    }

    [Test]
    public void IsNatural_TrueOnlyForTwoCardEightOrNine()
    {
        Assert.IsTrue(Hand(Rank.Ace, Rank.Eight).IsNatural); // 9
        Assert.IsTrue(Hand(Rank.Nine, Rank.Nine).IsNatural); // 18 -> 8
        Assert.IsFalse(Hand(Rank.Seven, Rank.Nine).IsNatural); // 16 -> 6
        Assert.IsFalse(Hand(Rank.Ace, Rank.Three, Rank.Five).IsNatural); // 3 cards, never natural
    }

    [TestCase(5, true)]
    [TestCase(6, false)]
    [TestCase(7, false)]
    public void PlayerDraws_StandsOnSixOrSeven(int point, bool expectedDraws)
    {
        var hand = point == 5 ? Hand(Rank.Two, Rank.Three) : point == 6 ? Hand(Rank.Two, Rank.Four) : Hand(Rank.Three, Rank.Four);
        Assert.AreEqual(expectedDraws, BaccaratResolver.PlayerDraws(hand));
    }

    [Test]
    public void BankerDraws_MirrorsPlayerRuleWhenPlayerStood()
    {
        var bankerLow = Hand(Rank.Two, Rank.Three); // 5
        var bankerHigh = Hand(Rank.Three, Rank.Four); // 7
        Assert.IsTrue(BaccaratResolver.BankerDraws(bankerLow, playerDrew: false, playerThirdCardValue: 0));
        Assert.IsFalse(BaccaratResolver.BankerDraws(bankerHigh, playerDrew: false, playerThirdCardValue: 0));
    }

    [Test]
    public void BankerDraws_TotalTwoOrLess_AlwaysDraws()
    {
        var banker = Hand(Rank.Ace, Rank.Ace); // 2
        Assert.IsTrue(BaccaratResolver.BankerDraws(banker, playerDrew: true, playerThirdCardValue: 8));
    }

    [Test]
    public void BankerDraws_TotalThree_StandsOnlyIfPlayerThirdIsEight()
    {
        var banker = Hand(Rank.Ace, Rank.Two); // 3
        Assert.IsFalse(BaccaratResolver.BankerDraws(banker, playerDrew: true, playerThirdCardValue: 8));
        Assert.IsTrue(BaccaratResolver.BankerDraws(banker, playerDrew: true, playerThirdCardValue: 7));
    }

    [TestCase(2, true)]
    [TestCase(7, true)]
    [TestCase(1, false)]
    [TestCase(8, false)]
    public void BankerDraws_TotalFour_DrawsOnPlayerThirdTwoToSeven(int playerThird, bool expectedDraws)
    {
        var banker = Hand(Rank.Two, Rank.Two); // 4
        Assert.AreEqual(expectedDraws, BaccaratResolver.BankerDraws(banker, playerDrew: true, playerThirdCardValue: playerThird));
    }

    [TestCase(4, true)]
    [TestCase(7, true)]
    [TestCase(3, false)]
    [TestCase(8, false)]
    public void BankerDraws_TotalFive_DrawsOnPlayerThirdFourToSeven(int playerThird, bool expectedDraws)
    {
        var banker = Hand(Rank.Two, Rank.Three); // 5
        Assert.AreEqual(expectedDraws, BaccaratResolver.BankerDraws(banker, playerDrew: true, playerThirdCardValue: playerThird));
    }

    [TestCase(6, true)]
    [TestCase(7, true)]
    [TestCase(5, false)]
    public void BankerDraws_TotalSix_DrawsOnlyOnPlayerThirdSixOrSeven(int playerThird, bool expectedDraws)
    {
        var banker = Hand(Rank.Three, Rank.Three); // 6
        Assert.AreEqual(expectedDraws, BaccaratResolver.BankerDraws(banker, playerDrew: true, playerThirdCardValue: playerThird));
    }

    [Test]
    public void BankerDraws_TotalSeven_AlwaysStands()
    {
        var banker = Hand(Rank.Three, Rank.Four); // 7
        Assert.IsFalse(BaccaratResolver.BankerDraws(banker, playerDrew: true, playerThirdCardValue: 0));
    }

    [Test]
    public void Resolve_HigherPointWins()
    {
        Assert.AreEqual(BaccaratOutcome.PlayerWin, BaccaratResolver.Resolve(Hand(Rank.Nine, Rank.Nine), Hand(Rank.Five, Rank.Two)));
        Assert.AreEqual(BaccaratOutcome.BankerWin, BaccaratResolver.Resolve(Hand(Rank.Five, Rank.Two), Hand(Rank.Nine, Rank.Nine)));
        Assert.AreEqual(BaccaratOutcome.Tie, BaccaratResolver.Resolve(Hand(Rank.Five, Rank.Two), Hand(Rank.Four, Rank.Three)));
    }

    [Test]
    public void Payout_PlayerWin_PaysEvenMoney()
    {
        Assert.AreEqual(200, BaccaratResolver.Payout(BaccaratBetType.Player, 100, BaccaratOutcome.PlayerWin));
        Assert.AreEqual(0, BaccaratResolver.Payout(BaccaratBetType.Player, 100, BaccaratOutcome.BankerWin));
    }

    [Test]
    public void Payout_BankerWin_PaysMinusFivePercentCommission()
    {
        Assert.AreEqual(195, BaccaratResolver.Payout(BaccaratBetType.Banker, 100, BaccaratOutcome.BankerWin));
    }

    [Test]
    public void Payout_Tie_PaysEightToOneOnTieBet_PushesOtherBets()
    {
        Assert.AreEqual(900, BaccaratResolver.Payout(BaccaratBetType.Tie, 100, BaccaratOutcome.Tie));
        Assert.AreEqual(100, BaccaratResolver.Payout(BaccaratBetType.Player, 100, BaccaratOutcome.Tie));
        Assert.AreEqual(100, BaccaratResolver.Payout(BaccaratBetType.Banker, 100, BaccaratOutcome.Tie));
    }
}
