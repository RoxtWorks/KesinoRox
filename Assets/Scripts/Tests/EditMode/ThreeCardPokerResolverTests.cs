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
        Assert.AreEqual(500, ThreeCardPokerResolver.AnteBonusPayout(100, h));
    }

    [Test]
    public void AnteBonus_ThreeOfAKind_Pays_4to1()
    {
        var h = Hand(C(Rank.King), C(Rank.King, Suit.Hearts), C(Rank.King, Suit.Spades));
        Assert.AreEqual(400, ThreeCardPokerResolver.AnteBonusPayout(100, h));
    }

    [Test]
    public void AnteBonus_Straight_Pays_1to1()
    {
        var h = Hand(C(Rank.Four), C(Rank.Five, Suit.Hearts), C(Rank.Six));
        Assert.AreEqual(100, ThreeCardPokerResolver.AnteBonusPayout(100, h));
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

    // ── Vegas Pair Plus table 40/25/5/4/1 ─────────────────────────────────
    [Test]
    public void PairPlus_ThreeOfAKind_Pays_25to1()
    {
        var h = Hand(C(Rank.Seven), C(Rank.Seven, Suit.Hearts), C(Rank.Seven, Suit.Spades));
        Assert.AreEqual(2600, ThreeCardPokerResolver.PairPlusPayout(100, h));
    }

    [Test]
    public void PairPlus_Straight_Pays_5to1()
    {
        var h = Hand(C(Rank.Eight), C(Rank.Nine, Suit.Hearts), C(Rank.Ten));
        Assert.AreEqual(600, ThreeCardPokerResolver.PairPlusPayout(100, h));
    }

    [Test]
    public void PairPlus_Flush_Pays_4to1()
    {
        var h = Hand(C(Rank.Two, Suit.Hearts), C(Rank.Nine, Suit.Hearts), C(Rank.King, Suit.Hearts));
        Assert.AreEqual(500, ThreeCardPokerResolver.PairPlusPayout(100, h));
    }

    // ── Tie-breaks (qualifying dealer) ─────────────────────────────────────
    static ThreeCardPokerOutcome Vs(ThreeCardPokerHand p, ThreeCardPokerHand d) => ThreeCardPokerResolver.Resolve(p, d);

    [Test]
    public void HighCard_Compares_Third_Card()
    {
        var p = Hand(C(Rank.King), C(Rank.Nine, Suit.Hearts), C(Rank.Five, Suit.Spades));
        var d = Hand(C(Rank.King, Suit.Diamonds), C(Rank.Nine, Suit.Spades), C(Rank.Four, Suit.Hearts));
        Assert.AreEqual(ThreeCardPokerOutcome.PlayerWins, Vs(p, d));
    }

    [Test]
    public void Identical_Ranks_Tie()
    {
        var p = Hand(C(Rank.King), C(Rank.Nine, Suit.Hearts), C(Rank.Five, Suit.Spades));
        var d = Hand(C(Rank.King, Suit.Diamonds), C(Rank.Nine, Suit.Spades), C(Rank.Five, Suit.Hearts));
        Assert.AreEqual(ThreeCardPokerOutcome.Tie, Vs(p, d));
    }

    [Test]
    public void Ace_Two_Three_Is_The_Lowest_Straight()
    {
        var p = Hand(C(Rank.Ace), C(Rank.Two, Suit.Hearts), C(Rank.Three, Suit.Spades));
        var d = Hand(C(Rank.Two, Suit.Diamonds), C(Rank.Three, Suit.Clubs), C(Rank.Four, Suit.Hearts));
        Assert.AreEqual(ThreeCardPokerOutcome.DealerWins, Vs(p, d));
    }

    [Test]
    public void Ace_King_Queen_Is_The_Highest_Straight()
    {
        var p = Hand(C(Rank.Ace), C(Rank.King, Suit.Hearts), C(Rank.Queen, Suit.Spades));
        var d = Hand(C(Rank.King, Suit.Diamonds), C(Rank.Queen, Suit.Clubs), C(Rank.Jack, Suit.Hearts));
        Assert.AreEqual(ThreeCardPokerOutcome.PlayerWins, Vs(p, d));
    }

    [Test]
    public void Ace_Two_Three_Straight_Flush_Loses_To_Two_Three_Four_Straight_Flush()
    {
        var p = Hand(C(Rank.Ace, Suit.Hearts), C(Rank.Two, Suit.Hearts), C(Rank.Three, Suit.Hearts));
        var d = Hand(C(Rank.Two, Suit.Spades), C(Rank.Three, Suit.Spades), C(Rank.Four, Suit.Spades));
        Assert.AreEqual(ThreeCardPokerOutcome.DealerWins, Vs(p, d));
    }

    [Test]
    public void Same_Pair_Kicker_Decides()
    {
        var p = Hand(C(Rank.Nine), C(Rank.Nine, Suit.Hearts), C(Rank.Ace, Suit.Spades));
        var d = Hand(C(Rank.Nine, Suit.Diamonds), C(Rank.Nine, Suit.Spades), C(Rank.King, Suit.Hearts));
        Assert.AreEqual(ThreeCardPokerOutcome.PlayerWins, Vs(p, d));
    }

    [Test]
    public void Flush_Compares_All_Three_Cards()
    {
        var p = Hand(C(Rank.King, Suit.Hearts), C(Rank.Eight, Suit.Hearts), C(Rank.Three, Suit.Hearts));
        var d = Hand(C(Rank.King, Suit.Spades), C(Rank.Eight, Suit.Spades), C(Rank.Four, Suit.Spades));
        Assert.AreEqual(ThreeCardPokerOutcome.DealerWins, Vs(p, d));
    }

    [Test]
    public void Dealer_Exactly_Queen_High_Qualifies()
    {
        var d = Hand(C(Rank.Queen), C(Rank.Four, Suit.Hearts), C(Rank.Two, Suit.Spades));
        Assert.IsTrue(d.DealerQualifies);
    }

    // ── Hand names ─────────────────────────────────────────────────────────
    [Test]
    public void Describe_Names_Hands_Plainly()
    {
        Assert.AreEqual("Pair of 9s", Hand(C(Rank.Nine), C(Rank.Nine, Suit.Hearts), C(Rank.Two)).Describe());
        Assert.AreEqual("Jack high", Hand(C(Rank.Jack), C(Rank.Eight, Suit.Hearts), C(Rank.Four)).Describe());
        Assert.AreEqual("Straight, 3 high", Hand(C(Rank.Ace), C(Rank.Two, Suit.Hearts), C(Rank.Three)).Describe());
        Assert.AreEqual("Three Kings", Hand(C(Rank.King), C(Rank.King, Suit.Hearts), C(Rank.King, Suit.Spades)).Describe());
    }
}
