using NUnit.Framework;

public class BlackjackResolverTests
{
    static Card C(Rank rank, Suit suit = Suit.Spades) => new Card(rank, suit);

    static BlackjackHand Hand(long bet, params Rank[] ranks)
    {
        var hand = new BlackjackHand { Bet = bet };
        foreach (var r in ranks) hand.AddCard(C(r));
        return hand;
    }

    [Test]
    public void NaturalBlackjack_PaysThreeToTwo()
    {
        var player = Hand(100, Rank.Ace, Rank.King);
        var dealer = Hand(0, Rank.Ten, Rank.Seven);
        var outcome = BlackjackResolver.Resolve(player, dealer);
        Assert.AreEqual(BlackjackOutcome.PlayerBlackjack, outcome);
        Assert.AreEqual(250, BlackjackResolver.Payout(player, outcome)); // 100 stake + 150 winnings
    }

    [Test]
    public void BothNatural_Pushes()
    {
        var player = Hand(100, Rank.Ace, Rank.Queen);
        var dealer = Hand(0, Rank.Ace, Rank.King);
        var outcome = BlackjackResolver.Resolve(player, dealer);
        Assert.AreEqual(BlackjackOutcome.Push, outcome);
        Assert.AreEqual(100, BlackjackResolver.Payout(player, outcome));
    }

    [Test]
    public void DealerNatural_BeatsNonNaturalPlayerTwentyOne()
    {
        var player = Hand(100, Rank.Seven, Rank.Seven, Rank.Seven); // 21 over 3 cards
        var dealer = Hand(0, Rank.Ace, Rank.King);
        var outcome = BlackjackResolver.Resolve(player, dealer);
        Assert.AreEqual(BlackjackOutcome.Lose, outcome);
        Assert.AreEqual(0, BlackjackResolver.Payout(player, outcome));
    }

    [Test]
    public void PlayerBust_AlwaysLosesRegardlessOfDealer()
    {
        var player = Hand(100, Rank.King, Rank.Queen, Rank.Two);
        var dealer = Hand(0, Rank.Ten, Rank.Ten, Rank.Five); // dealer also bust
        var outcome = BlackjackResolver.Resolve(player, dealer);
        Assert.AreEqual(BlackjackOutcome.Bust, outcome);
        Assert.AreEqual(0, BlackjackResolver.Payout(player, outcome));
    }

    [Test]
    public void DealerBust_PlayerWins()
    {
        var player = Hand(100, Rank.Ten, Rank.Eight);
        var dealer = Hand(0, Rank.Ten, Rank.Ten, Rank.Five);
        var outcome = BlackjackResolver.Resolve(player, dealer);
        Assert.AreEqual(BlackjackOutcome.Win, outcome);
        Assert.AreEqual(200, BlackjackResolver.Payout(player, outcome));
    }

    [Test]
    public void HigherTotal_Wins_LowerTotal_Loses()
    {
        var player = Hand(100, Rank.Ten, Rank.Nine); // 19
        var dealer = Hand(0, Rank.Ten, Rank.Seven); // 17
        Assert.AreEqual(BlackjackOutcome.Win, BlackjackResolver.Resolve(player, dealer));

        var player2 = Hand(100, Rank.Ten, Rank.Six); // 16
        var dealer2 = Hand(0, Rank.Ten, Rank.Eight); // 18
        Assert.AreEqual(BlackjackOutcome.Lose, BlackjackResolver.Resolve(player2, dealer2));
    }

    [Test]
    public void EqualTotals_Push()
    {
        var player = Hand(100, Rank.Ten, Rank.Eight);
        var dealer = Hand(0, Rank.Nine, Rank.Nine);
        var outcome = BlackjackResolver.Resolve(player, dealer);
        Assert.AreEqual(BlackjackOutcome.Push, outcome);
        Assert.AreEqual(100, BlackjackResolver.Payout(player, outcome));
    }

    [Test]
    public void Surrender_ReturnsHalfBet()
    {
        var player = Hand(100, Rank.Ten, Rank.Six);
        player.Surrendered = true;
        var dealer = Hand(0, Rank.Ten, Rank.Seven);
        var outcome = BlackjackResolver.Resolve(player, dealer);
        Assert.AreEqual(BlackjackOutcome.Surrender, outcome);
        Assert.AreEqual(50, BlackjackResolver.Payout(player, outcome));
    }

    [Test]
    public void Insurance_PaysTwoToOne_WhenDealerHasBlackjack()
    {
        var dealer = Hand(0, Rank.Ace, Rank.King);
        long payout = BlackjackResolver.ResolveInsurance(taken: true, insuranceBet: 50, dealer);
        Assert.AreEqual(150, payout); // 50 stake + 100 winnings (2:1)
    }

    [Test]
    public void Insurance_PaysNothing_WhenDealerHasNoBlackjack()
    {
        var dealer = Hand(0, Rank.Ace, Rank.Nine);
        long payout = BlackjackResolver.ResolveInsurance(taken: true, insuranceBet: 50, dealer);
        Assert.AreEqual(0, payout);
    }

    [Test]
    public void Insurance_PaysNothing_WhenNotTaken()
    {
        var dealer = Hand(0, Rank.Ace, Rank.King);
        long payout = BlackjackResolver.ResolveInsurance(taken: false, insuranceBet: 0, dealer);
        Assert.AreEqual(0, payout);
    }
}
