using System.Linq;
using NUnit.Framework;

public class BlackjackRoundTests
{
    // Deals from a fixed, caller-specified card order (Shoe's test-only constructor)
    // so split/double/dealer-play behavior can be exercised deterministically instead
    // of depending on a real shuffle.
    static Shoe FixedShoe(params Rank[] ranks) => new Shoe(ranks.Select(r => new Card(r, Suit.Spades)));

    [Test]
    public void Deal_CreatesOneHandWithTwoCardsEach()
    {
        // player1, dealer1(up), player2, dealer2(hole) — dealer up-card 7, no peek.
        var round = new BlackjackRound(FixedShoe(Rank.Ten, Rank.Seven, Rank.Six, Rank.Two));
        round.Deal(100);

        Assert.AreEqual(1, round.PlayerHands.Count);
        Assert.AreEqual(2, round.PlayerHands[0].Cards.Count);
        Assert.AreEqual(2, round.Dealer.Cards.Count);
        Assert.IsFalse(round.RoundOver);
        Assert.AreSame(round.PlayerHands[0], round.CurrentHand);
    }

    [Test]
    public void Split_CreatesTwoHandsWithSameBetAndClearsPairFlags()
    {
        var round = new BlackjackRound(FixedShoe(Rank.Eight, Rank.Seven, Rank.Eight, Rank.Two, Rank.Three, Rank.Four));
        round.Deal(100);
        Assert.IsTrue(round.CurrentHand.CanSplit);

        round.Split();

        Assert.AreEqual(2, round.PlayerHands.Count);
        Assert.AreEqual(100, round.PlayerHands[0].Bet);
        Assert.AreEqual(100, round.PlayerHands[1].Bet);
        Assert.IsTrue(round.PlayerHands[0].FromSplit);
        Assert.IsTrue(round.PlayerHands[1].FromSplit);
        Assert.AreEqual(2, round.PlayerHands[0].Cards.Count);
        Assert.AreEqual(2, round.PlayerHands[1].Cards.Count);
    }

    [Test]
    public void SplitAces_EachHandGetsExactlyOneCardAndCannotActFurther()
    {
        // Dealer's own hand is [Seven,Two,Five,Five,...] filler so it plays out
        // normally afterward without needing an exact predicted total.
        var round = new BlackjackRound(FixedShoe(
            Rank.Ace, Rank.Seven, Rank.Ace, Rank.Two,
            Rank.King, Rank.Nine,
            Rank.Five, Rank.Five, Rank.Five, Rank.Five, Rank.Five));
        round.Deal(100);
        round.Split();

        Assert.AreEqual(2, round.PlayerHands.Count);
        Assert.IsTrue(round.PlayerHands[0].IsSplitAce);
        Assert.IsTrue(round.PlayerHands[1].IsSplitAce);
        Assert.AreEqual(2, round.PlayerHands[0].Cards.Count);
        Assert.AreEqual(2, round.PlayerHands[1].Cards.Count);
        Assert.IsFalse(round.PlayerHands[0].CanHit);
        Assert.IsFalse(round.PlayerHands[1].CanHit);
        Assert.IsFalse(round.PlayerHands[0].IsNaturalBlackjack, "21 via split ace is not a natural");

        // Both split-ace hands resolve immediately on their forced card, so play
        // should already be past the player's turn and into the dealer's.
        Assert.IsTrue(round.RoundOver);
    }

    [Test]
    public void ReSplit_AllowedUpToFourHandsThenBlocked()
    {
        var round = new BlackjackRound(FixedShoe(
            Rank.Two, Rank.Seven, Rank.Two, Rank.Two, // initial deal, dealer up=7 (no peek)
            Rank.Two, Rank.Two,                       // split #1 forced cards
            Rank.Two, Rank.Two,                       // split #2 forced cards
            Rank.Two, Rank.Two));                     // split #3 forced cards
        round.Deal(100);

        round.Split(); // 1 -> 2 hands
        Assert.AreEqual(2, round.PlayerHands.Count);
        round.Split(); // 2 -> 3 hands (still splitting the current hand, still a pair of Twos)
        Assert.AreEqual(3, round.PlayerHands.Count);
        round.Split(); // 3 -> 4 hands, at the cap
        Assert.AreEqual(4, round.PlayerHands.Count);

        // Current hand is still a literal pair of Twos, but the 4-hand cap must
        // block a further split rather than mutating anything.
        Assert.IsTrue(round.CurrentHand.CanSplit);
        int cardsBefore = round.CurrentHand.Cards.Count;
        round.Split();
        Assert.AreEqual(4, round.PlayerHands.Count, "split beyond the 4-hand cap must be a no-op");
        Assert.AreEqual(cardsBefore, round.CurrentHand.Cards.Count);
    }

    [Test]
    public void DoubleAfterSplit_DoublesBetAndForcesExactlyOneMoreCard()
    {
        var round = new BlackjackRound(FixedShoe(
            Rank.Six, Rank.Seven, Rank.Six, Rank.Two,
            Rank.Five, Rank.Five,
            Rank.Five));
        round.Deal(100);
        round.Split();
        Assert.IsTrue(round.CurrentHand.CanDouble, "double-after-split is allowed in this ruleset");

        round.DoubleDown();

        Assert.IsTrue(round.PlayerHands[0].DoubledDown);
        Assert.AreEqual(200, round.PlayerHands[0].Bet);
        Assert.AreEqual(3, round.PlayerHands[0].Cards.Count);
        Assert.IsTrue(round.PlayerHands[0].IsResolved);
        // Turn should have advanced to the other split hand.
        Assert.AreSame(round.PlayerHands[1], round.CurrentHand);
    }

    [Test]
    public void Dealer_HitsOnSoftSeventeen()
    {
        var round = new BlackjackRound(FixedShoe(
            Rank.Ten, Rank.Ace, Rank.Nine, Rank.Six, // player 19, dealer up=Ace (soft 17 hole)
            Rank.Two));                              // dealer's hit on soft 17
        round.Deal(100);
        round.TakeInsurance(false); // required before play continues when an Ace is showing
        round.Stand();

        Assert.IsTrue(round.RoundOver);
        Assert.AreEqual(3, round.Dealer.Cards.Count, "soft 17 must take a hit, not stand");
        Assert.AreEqual(19, round.Dealer.BestTotal);
    }

    [Test]
    public void Dealer_StandsOnHardSeventeen()
    {
        var round = new BlackjackRound(FixedShoe(Rank.Ten, Rank.Ten, Rank.Nine, Rank.Seven));
        round.Deal(100);
        round.Stand();

        Assert.IsTrue(round.RoundOver);
        Assert.AreEqual(2, round.Dealer.Cards.Count, "hard 17 must stand, not hit");
        Assert.AreEqual(17, round.Dealer.BestTotal);
    }

    [Test]
    public void ResolveAll_ReturnsIndependentOutcomesForEachSplitHand()
    {
        var round = new BlackjackRound(FixedShoe(
            Rank.Eight, Rank.Seven, Rank.Eight, Rank.Nine, // deal, dealer up=7 (no peek)
            Rank.Three, Rank.King,                          // split forced cards -> 11 and 18
            Rank.King,                                      // hand0's hit -> 21
            Rank.Three));                                   // dealer's one hit, 16 -> 19
        round.Deal(100);
        round.Split();
        round.Hit();   // hand0: 8+3+K = 21
        round.Stand(); // moves to hand1
        round.Stand(); // hand1 stands at 18, dealer plays out (16 -> 19)

        Assert.IsTrue(round.RoundOver);
        Assert.AreEqual(19, round.Dealer.BestTotal);

        var results = round.ResolveAll();
        Assert.AreEqual(2, results.Count);
        Assert.AreEqual(BlackjackOutcome.Win, results[0].outcome, "21 beats dealer's 19");
        Assert.AreEqual(200, results[0].payout);
        Assert.AreEqual(BlackjackOutcome.Lose, results[1].outcome, "18 loses to dealer's 19");
        Assert.AreEqual(0, results[1].payout);
    }

    [Test]
    public void DealerBlackjackPeek_EndsRoundImmediatelyWithNoAceUpCard()
    {
        // Dealer shows a Ten (not an Ace) with an Ace in the hole — no insurance
        // offer, but the peek still fires and should end the round before the
        // player ever gets to act.
        var round = new BlackjackRound(FixedShoe(Rank.Nine, Rank.Ten, Rank.Eight, Rank.Ace));
        round.Deal(100);

        Assert.IsFalse(round.InsuranceOffered);
        Assert.IsTrue(round.RoundOver);
        Assert.IsTrue(round.Dealer.IsNaturalBlackjack);
    }

    [Test]
    public void PlayerNaturalAgainstAceUpCard_DoesNotRevealOrPlayDealerBeforeInsuranceDecided()
    {
        // Player has a natural blackjack immediately, but the dealer shows an Ace —
        // the insurance decision must still happen, and nothing about the dealer's
        // hand (hole card reveal, dealer draws) may happen before it's answered.
        var round = new BlackjackRound(FixedShoe(Rank.Ace, Rank.Ace, Rank.King, Rank.Nine));
        round.Deal(100);

        Assert.IsTrue(round.PlayerHands[0].IsNaturalBlackjack);
        Assert.IsTrue(round.InsuranceOffered);
        Assert.IsFalse(round.RoundOver, "round must not resolve before insurance is decided");
        Assert.AreEqual(2, round.Dealer.Cards.Count, "dealer must not draw further before insurance is decided");

        round.TakeInsurance(false);
        Assert.IsTrue(round.RoundOver, "with the insurance decision made, the already-resolved natural can now settle");
    }

    [Test]
    public void Insurance_OfferedOnlyWithAceUpCard_AndBlocksPlayUntilAnswered()
    {
        var round = new BlackjackRound(FixedShoe(Rank.Ten, Rank.Ace, Rank.Nine, Rank.King));
        round.Deal(100);

        Assert.IsTrue(round.InsuranceOffered);
        round.Hit(); // must be a no-op while the insurance decision is pending
        Assert.AreEqual(2, round.CurrentHand.Cards.Count, "hit should have been blocked");

        round.TakeInsurance(true);
        Assert.AreEqual(50, round.InsuranceBet);
        // Dealer's hole card is a King -> dealer has blackjack -> peek ends the round.
        Assert.IsTrue(round.RoundOver);
        Assert.AreEqual(150, round.InsurancePayout); // 50 stake + 100 winnings (2:1)
    }
}
