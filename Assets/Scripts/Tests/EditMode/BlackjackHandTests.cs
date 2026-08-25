using NUnit.Framework;

public class BlackjackHandTests
{
    static Card C(Rank rank, Suit suit = Suit.Spades) => new Card(rank, suit);

    [Test]
    public void BestTotal_SimpleHardHand()
    {
        var hand = new BlackjackHand();
        hand.AddCard(C(Rank.Ten));
        hand.AddCard(C(Rank.Seven));
        Assert.AreEqual(17, hand.BestTotal);
        Assert.IsFalse(hand.IsSoft);
    }

    [Test]
    public void BestTotal_AceCountsAsElevenWhenSafe()
    {
        var hand = new BlackjackHand();
        hand.AddCard(C(Rank.Ace));
        hand.AddCard(C(Rank.Six));
        Assert.AreEqual(17, hand.BestTotal);
        Assert.IsTrue(hand.IsSoft, "A+6 is a soft 17");
    }

    [Test]
    public void BestTotal_AceDemotesToOneToAvoidBust()
    {
        var hand = new BlackjackHand();
        hand.AddCard(C(Rank.Ace));
        hand.AddCard(C(Rank.Six));
        hand.AddCard(C(Rank.King));
        Assert.AreEqual(17, hand.BestTotal, "A+6+K must count the Ace as 1, not 11");
        Assert.IsFalse(hand.IsSoft);
    }

    [Test]
    public void BestTotal_TwoAcesOnlyOneCountsHigh()
    {
        var hand = new BlackjackHand();
        hand.AddCard(C(Rank.Ace));
        hand.AddCard(C(Rank.Ace));
        hand.AddCard(C(Rank.Nine));
        // 11 + 1 + 9 = 21 (one ace high, one low) — 11+11+9 would bust.
        Assert.AreEqual(21, hand.BestTotal);
        Assert.IsTrue(hand.IsSoft);
    }

    [Test]
    public void IsBust_TrueOverTwentyOne()
    {
        var hand = new BlackjackHand();
        hand.AddCard(C(Rank.King));
        hand.AddCard(C(Rank.Queen));
        hand.AddCard(C(Rank.Two));
        Assert.IsTrue(hand.IsBust);
    }

    [Test]
    public void IsNaturalBlackjack_TrueForTwoCardTwentyOne()
    {
        var hand = new BlackjackHand();
        hand.AddCard(C(Rank.Ace));
        hand.AddCard(C(Rank.King));
        Assert.IsTrue(hand.IsNaturalBlackjack);
    }

    [Test]
    public void IsNaturalBlackjack_FalseForTwentyOneFromSplit()
    {
        var hand = new BlackjackHand { FromSplit = true };
        hand.AddCard(C(Rank.Ace));
        hand.AddCard(C(Rank.King));
        Assert.IsFalse(hand.IsNaturalBlackjack, "a 21 reached via a split hand is not a natural");
    }

    [Test]
    public void IsNaturalBlackjack_FalseForThreeCardTwentyOne()
    {
        var hand = new BlackjackHand();
        hand.AddCard(C(Rank.Seven));
        hand.AddCard(C(Rank.Seven));
        hand.AddCard(C(Rank.Seven));
        Assert.IsFalse(hand.IsNaturalBlackjack);
    }

    [Test]
    public void CanSplit_TrueForEqualValuePair()
    {
        var hand = new BlackjackHand();
        hand.AddCard(C(Rank.King));
        hand.AddCard(C(Rank.Queen));
        Assert.IsTrue(hand.CanSplit, "two different ten-value cards are still splittable");
    }

    [Test]
    public void CanSplit_FalseForUnequalPair()
    {
        var hand = new BlackjackHand();
        hand.AddCard(C(Rank.King));
        hand.AddCard(C(Rank.Nine));
        Assert.IsFalse(hand.CanSplit);
    }

    [Test]
    public void SplitAceHand_CannotHitOrDoubleAfterItsForcedCard()
    {
        var hand = new BlackjackHand { IsSplitAce = true, FromSplit = true };
        hand.AddCard(C(Rank.Ace));
        hand.AddCard(C(Rank.Six));
        Assert.IsTrue(hand.IsResolved);
        Assert.IsFalse(hand.CanHit);
        Assert.IsFalse(hand.CanDouble);
    }

    [Test]
    public void DoubledDown_ResolvedAfterThirdCard()
    {
        var hand = new BlackjackHand();
        hand.AddCard(C(Rank.Five));
        hand.AddCard(C(Rank.Six));
        Assert.IsFalse(hand.IsResolved);
        hand.DoubledDown = true;
        hand.AddCard(C(Rank.Two));
        Assert.IsTrue(hand.IsResolved);
        Assert.IsFalse(hand.CanHit);
    }
}
