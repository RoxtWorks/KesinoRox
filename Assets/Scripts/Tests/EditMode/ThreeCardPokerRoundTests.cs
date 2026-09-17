using NUnit.Framework;

[TestFixture]
public class ThreeCardPokerRoundTests
{
    // Fixed shoe: cards dealt in order P D P D P D
    static Shoe ShoeOf(params Card[] cards) => new Shoe(cards);

    static Card C(Rank r, Suit s = Suit.Clubs) => new Card(r, s);

    [Test]
    public void Deal_Distributes_Cards_Alternately()
    {
        // P gets cards [0],[2],[4]; D gets [1],[3],[5]
        var shoe = ShoeOf(
            C(Rank.Ace), C(Rank.Two), C(Rank.King), C(Rank.Three),
            C(Rank.Queen), C(Rank.Four));
        var round = new ThreeCardPokerRound(shoe);
        round.PlaceBet(ThreeCardPokerBetType.Ante, 100);
        round.Deal();

        Assert.AreEqual(3, round.PlayerHand.Cards.Count);
        Assert.AreEqual(3, round.DealerHand.Cards.Count);
        Assert.AreEqual(Rank.Ace,   round.PlayerHand.Cards[0].Rank);
        Assert.AreEqual(Rank.Two,   round.DealerHand.Cards[0].Rank);
        Assert.AreEqual(Rank.King,  round.PlayerHand.Cards[1].Rank);
        Assert.AreEqual(Rank.Three, round.DealerHand.Cards[1].Rank);
    }

    [Test]
    public void Fold_Forfeits_Ante_Returns_Zero()
    {
        var shoe = ShoeOf(
            C(Rank.Two), C(Rank.Queen), C(Rank.Three), C(Rank.Jack),
            C(Rank.Four), C(Rank.Ten));
        var round = new ThreeCardPokerRound(shoe);
        round.PlaceBet(ThreeCardPokerBetType.Ante, 100);
        round.Deal();
        var result = round.Fold();

        Assert.AreEqual(0, result.AnteReturn);
        Assert.AreEqual(0, result.PlayReturn);
        Assert.IsTrue(result.PlayerFolded);
    }

    [Test]
    public void PairPlus_Pays_Even_On_Fold()
    {
        // Player: Ace, Ace, King (pair) — PairPlus should pay 1:1
        // Dealer doesn't matter — PairPlus independent
        var shoe = ShoeOf(
            C(Rank.Ace), C(Rank.Two), C(Rank.Ace, Suit.Hearts), C(Rank.Three),
            C(Rank.King), C(Rank.Four));
        var round = new ThreeCardPokerRound(shoe);
        round.PlaceBet(ThreeCardPokerBetType.Ante, 100);
        round.PlaceBet(ThreeCardPokerBetType.PairPlus, 50);
        round.Deal();
        var result = round.Fold();

        Assert.AreEqual(100, result.PairPlusReturn); // 1:1 on 50
    }

    [Test]
    public void DealerNoQualify_Ante_Wins_Play_Pushed()
    {
        // Player: 7,8,9 (straight, clubs — qualifies), Dealer: J(C),2(H),5(D) — mixed suits, Jack high, no qualify
        var shoe = ShoeOf(
            C(Rank.Seven), C(Rank.Jack), C(Rank.Eight), C(Rank.Two, Suit.Hearts),
            C(Rank.Nine), C(Rank.Five, Suit.Diamonds));
        var round = new ThreeCardPokerRound(shoe);
        round.PlaceBet(ThreeCardPokerBetType.Ante, 100);
        round.Deal();
        var result = round.Play();

        Assert.IsFalse(result.DealerQualified);
        Assert.AreEqual(ThreeCardPokerOutcome.DealerNoQualify, result.Outcome);
        Assert.AreEqual(200, result.AnteReturn);  // 1:1
        Assert.AreEqual(100, result.PlayReturn);  // push
    }

    [Test]
    public void Player_Wins_Against_Qualifying_Dealer()
    {
        // Player: A,K,Q (straight flush of same suit wins)
        // Dealer: Q,2,3 (qualifies with queen, but high card loses to player straight)
        var shoe = ShoeOf(
            C(Rank.Ace,  Suit.Spades), C(Rank.Queen), C(Rank.King, Suit.Spades), C(Rank.Two),
            C(Rank.Queen, Suit.Spades), C(Rank.Three));
        var round = new ThreeCardPokerRound(shoe);
        round.PlaceBet(ThreeCardPokerBetType.Ante, 100);
        round.Deal();
        var result = round.Play();

        Assert.AreEqual(ThreeCardPokerOutcome.PlayerWins, result.Outcome);
        Assert.AreEqual(200, result.AnteReturn);
        Assert.AreEqual(200, result.PlayReturn);
    }

    [Test]
    public void AnteBonus_Straight_Pays_On_Top_Of_Regular_Win()
    {
        // Player 4♠5♠6♠ (straight flush), Dealer Q,2,3 (qualifies, player wins)
        var shoe = ShoeOf(
            C(Rank.Four, Suit.Spades), C(Rank.Queen), C(Rank.Five, Suit.Spades), C(Rank.Two),
            C(Rank.Six, Suit.Spades), C(Rank.Three));
        var round = new ThreeCardPokerRound(shoe);
        round.PlaceBet(ThreeCardPokerBetType.Ante, 100);
        round.Deal();
        var result = round.Play();

        // Straight Flush → AnteBonus 5:1 = 600
        Assert.AreEqual(600, result.AnteBonusReturn);
        Assert.AreEqual(200, result.AnteReturn);
        Assert.AreEqual(200, result.PlayReturn);
    }

    [Test]
    public void Dealer_Wins_Returns_Zero_For_Ante_And_Play()
    {
        // Player: 2,3,5 (high card), Dealer: A,K,Q (high card A high, beats player)
        var shoe = ShoeOf(
            C(Rank.Two), C(Rank.Ace), C(Rank.Three), C(Rank.King),
            C(Rank.Five), C(Rank.Queen));
        var round = new ThreeCardPokerRound(shoe);
        round.PlaceBet(ThreeCardPokerBetType.Ante, 100);
        round.Deal();
        var result = round.Play();

        Assert.AreEqual(ThreeCardPokerOutcome.DealerWins, result.Outcome);
        Assert.AreEqual(0, result.AnteReturn);
        Assert.AreEqual(0, result.PlayReturn);
    }
}
