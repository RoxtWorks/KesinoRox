using NUnit.Framework;

[TestFixture]
public class ThreeCardPokerResolverTests
{
    static Card C(Rank r, Suit s = Suit.Clubs) => new Card(r, s);

    static ThreeCardPokerHand Hand(Card c1, Card c2, Card c3)
    {
        var h = new ThreeCardPokerHand();
        h.AddCard(c1); h.AddCard(c2); h.AddCard(c3);
        return h;
    }

    // ── Hand ranks ──────────────────────────────────────────────────────────
    [Test]
    public void StraightFlush_Detected()
    {
        var h = Hand(C(Rank.Ace, Suit.Hearts), C(Rank.Two, Suit.Hearts),
                     C(Rank.Three, Suit.Hearts));
        Assert.AreEqual(ThreeCardPokerRank.StraightFlush, h.Rank);
    }

    [Test]
    public void ThreeOfAKind_Detected()
    {
        var h = Hand(C(Rank.Seven), C(Rank.Seven, Suit.Hearts), C(Rank.Seven, Suit.Spades));
        Assert.AreEqual(ThreeCardPokerRank.ThreeOfAKind, h.Rank);
    }

    [Test]
    public void Straight_Detected()
    {
        var h = Hand(C(Rank.Four), C(Rank.Five, Suit.Hearts), C(Rank.Six, Suit.Spades));
        Assert.AreEqual(ThreeCardPokerRank.Straight, h.Rank);
    }

    [Test]
    public void Flush_Detected()
    {
        var h = Hand(C(Rank.Two, Suit.Spades), C(Rank.Five, Suit.Spades),
                     C(Rank.Nine, Suit.Spades));
        Assert.AreEqual(ThreeCardPokerRank.Flush, h.Rank);
    }

    [Test]
    public void Pair_Detected()
    {
        var h = Hand(C(Rank.King), C(Rank.King, Suit.Hearts), C(Rank.Three));
        Assert.AreEqual(ThreeCardPokerRank.Pair, h.Rank);
    }

    [Test]
    public void HighCard_When_Nothing()
    {
        var h = Hand(C(Rank.Two), C(Rank.Five, Suit.Hearts), C(Rank.Nine, Suit.Spades));
        Assert.AreEqual(ThreeCardPokerRank.HighCard, h.Rank);
    }

    // ── Dealer qualifying ──────────────────────────────────────────────────
    [Test]
    public void Dealer_Qualifies_With_Queen_High()
    {
        var h = Hand(C(Rank.Queen), C(Rank.Two, Suit.Hearts), C(Rank.Five, Suit.Spades));
        Assert.IsTrue(h.DealerQualifies);
    }

    [Test]
    public void Dealer_Does_Not_Qualify_With_Jack_High()
    {
        var h = Hand(C(Rank.Jack), C(Rank.Two, Suit.Hearts), C(Rank.Five, Suit.Spades));
        Assert.IsFalse(h.DealerQualifies);
    }

    [Test]
    public void Dealer_Qualifies_With_Pair()
    {
        var h = Hand(C(Rank.Two), C(Rank.Two, Suit.Hearts), C(Rank.Three));
        Assert.IsTrue(h.DealerQualifies);
    }

    // ── Outcomes ───────────────────────────────────────────────────────────
    [Test]
    public void Player_Wins_With_Better_Hand()
    {
        var player = Hand(C(Rank.Ace), C(Rank.King, Suit.Hearts), C(Rank.Queen, Suit.Spades));
        var dealer = Hand(C(Rank.Two), C(Rank.Three, Suit.Hearts), C(Rank.Four, Suit.Spades));
        Assert.AreEqual(ThreeCardPokerOutcome.PlayerWins,
            ThreeCardPokerResolver.Resolve(player, dealer));
    }

    [Test]
    public void DealerNoQualify_When_Below_QueenHigh()
    {
        var player = Hand(C(Rank.Two), C(Rank.Three, Suit.Hearts), C(Rank.Five));
        var dealer = Hand(C(Rank.Jack), C(Rank.Four, Suit.Hearts), C(Rank.Six));
        Assert.AreEqual(ThreeCardPokerOutcome.DealerNoQualify,
            ThreeCardPokerResolver.Resolve(player, dealer));
    }

    // ── Payouts ────────────────────────────────────────────────────────────
    [Test]
    public void Ante_Returns_Stake_Plus_Winnings_On_Win()
    {
        Assert.AreEqual(200, ThreeCardPokerResolver.AntePayout(100, ThreeCardPokerOutcome.PlayerWins));
    }

    [Test]
    public void Ante_Pushed_On_DealerNoQualify()
    {
        Assert.AreEqual(200, ThreeCardPokerResolver.AntePayout(100, ThreeCardPokerOutcome.DealerNoQualify));
    }

    [Test]
    public void Play_Pushed_On_DealerNoQualify()
    {
        Assert.AreEqual(100, ThreeCardPokerResolver.PlayPayout(100, ThreeCardPokerOutcome.DealerNoQualify));
    }

    [Test]
    public void AnteBonus_StraightFlush_Pays_5to1()
    {
        var h = Hand(C(Rank.Ace, Suit.Hearts), C(Rank.Two, Suit.Hearts),
                     C(Rank.Three, Suit.Hearts));
        Assert.AreEqual(600, ThreeCardPokerResolver.AnteBonusPayout(100, h));
    }

    [Test]
    public void AnteBonus_ThreeOfAKind_Pays_4to1()
    {
        var h = Hand(C(Rank.King), C(Rank.King, Suit.Hearts), C(Rank.King, Suit.Spades));
        Assert.AreEqual(500, ThreeCardPokerResolver.AnteBonusPayout(100, h));
    }

    [Test]
    public void AnteBonus_Straight_Pays_1to1()
    {
        var h = Hand(C(Rank.Four), C(Rank.Five, Suit.Hearts), C(Rank.Six));
        Assert.AreEqual(200, ThreeCardPokerResolver.AnteBonusPayout(100, h));
    }

    [Test]
    public void AnteBonus_Zero_On_Pair()
    {
        var h = Hand(C(Rank.King), C(Rank.King, Suit.Hearts), C(Rank.Three));
        Assert.AreEqual(0, ThreeCardPokerResolver.AnteBonusPayout(100, h));
    }

    [Test]
    public void PairPlus_StraightFlush_Pays_40to1()
    {
        var h = Hand(C(Rank.Ten, Suit.Diamonds), C(Rank.Jack, Suit.Diamonds),
                     C(Rank.Queen, Suit.Diamonds));
        Assert.AreEqual(4100, ThreeCardPokerResolver.PairPlusPayout(100, h));
    }

    [Test]
    public void PairPlus_Pair_Pays_1to1()
    {
        var h = Hand(C(Rank.Five), C(Rank.Five, Suit.Hearts), C(Rank.Nine));
        Assert.AreEqual(200, ThreeCardPokerResolver.PairPlusPayout(100, h));
    }

    [Test]
    public void PairPlus_HighCard_Returns_Zero()
    {
        var h = Hand(C(Rank.Two), C(Rank.Seven, Suit.Hearts), C(Rank.Nine, Suit.Spades));
        Assert.AreEqual(0, ThreeCardPokerResolver.PairPlusPayout(100, h));
    }
}
