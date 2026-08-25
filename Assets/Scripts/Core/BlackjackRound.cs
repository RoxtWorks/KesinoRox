using System.Collections.Generic;
using System.Linq;

// Stateful orchestrator for one round: initial deal, the dealer's real-table "peek"
// for blackjack when showing an Ace/ten (which can end the round before the player
// gets to act at all), the player's hit/stand/double/split/surrender turn across
// however many hands a split produces, then the dealer playing out and every hand
// resolving. Shoe is shared/injected so penetration carries across rounds.
public class BlackjackRound
{
    const int MaxHands = 4;

    public Shoe Shoe { get; }
    public BlackjackHand Dealer { get; private set; }
    public List<BlackjackHand> PlayerHands { get; private set; } = new List<BlackjackHand>();
    public int CurrentHandIndex { get; private set; }
    public bool InsuranceOffered { get; private set; }
    public bool InsuranceTaken { get; private set; }
    public long InsuranceBet { get; private set; }
    public bool RoundOver { get; private set; }

    public BlackjackRound(Shoe shoe)
    {
        Shoe = shoe;
    }

    public BlackjackHand CurrentHand => CurrentHandIndex < PlayerHands.Count ? PlayerHands[CurrentHandIndex] : null;
    public bool CanTakeInsurance => InsuranceOffered;

    public void Deal(long bet)
    {
        // Reshuffle happens here, between rounds, never mid-hand.
        if (Shoe.NeedsReshuffle) Shoe.Shuffle();

        Dealer = new BlackjackHand();
        PlayerHands = new List<BlackjackHand> { new BlackjackHand { Bet = bet } };
        CurrentHandIndex = 0;
        InsuranceOffered = false;
        InsuranceTaken = false;
        InsuranceBet = 0;
        RoundOver = false;

        PlayerHands[0].AddCard(Shoe.Draw());
        Dealer.AddCard(Shoe.Draw());
        PlayerHands[0].AddCard(Shoe.Draw());
        Dealer.AddCard(Shoe.Draw());

        InsuranceOffered = Dealer.Cards[0].IsAce;

        // No Ace up: no insurance offer, so the real-table peek (and the round-ending
        // consequence of it) happens immediately, and play can proceed right away.
        // With an Ace up, BOTH the peek and any turn advancement must wait until the
        // player has answered the insurance offer (see TakeInsurance) — even if the
        // player's own hand is already resolved (e.g. a natural blackjack), nothing
        // about the dealer's hand may be revealed or played out before that.
        if (!InsuranceOffered)
        {
            PeekForDealerBlackjack();
            if (!RoundOver) AdvanceIfHandResolved();
        }
    }

    public void TakeInsurance(bool take)
    {
        if (!CanTakeInsurance) return;
        if (take)
        {
            InsuranceTaken = true;
            InsuranceBet = PlayerHands[0].Bet / 2;
        }
        InsuranceOffered = false;
        PeekForDealerBlackjack();
        if (!RoundOver) AdvanceIfHandResolved();
    }

    // Only ten-value or Ace up-cards can possibly be hiding a blackjack.
    void PeekForDealerBlackjack()
    {
        bool possible = Dealer.Cards[0].IsAce || Dealer.Cards[0].HardValue == 10;
        if (possible && Dealer.IsNaturalBlackjack)
        {
            Dealer.Stood = true;
            RoundOver = true;
        }
    }

    public void Hit()
    {
        if (InsuranceOffered || RoundOver) return;
        var hand = CurrentHand;
        if (hand == null || !hand.CanHit) return;
        hand.AddCard(Shoe.Draw());
        AdvanceIfHandResolved();
    }

    public void Stand()
    {
        if (InsuranceOffered || RoundOver) return;
        var hand = CurrentHand;
        if (hand == null || hand.IsResolved) return;
        hand.Stood = true;
        AdvanceIfHandResolved();
    }

    public void DoubleDown()
    {
        if (InsuranceOffered || RoundOver) return;
        var hand = CurrentHand;
        if (hand == null || !hand.CanDouble) return;
        hand.Bet *= 2;
        hand.DoubledDown = true;
        hand.AddCard(Shoe.Draw());
        AdvanceIfHandResolved();
    }

    public void Surrender()
    {
        if (InsuranceOffered || RoundOver) return;
        var hand = CurrentHand;
        if (hand == null || !hand.CanSurrender) return;
        hand.Surrendered = true;
        AdvanceIfHandResolved();
    }

    public void Split()
    {
        if (InsuranceOffered || RoundOver) return;
        var hand = CurrentHand;
        if (hand == null || !hand.CanSplit || PlayerHands.Count >= MaxHands) return;

        bool splittingAces = hand.Cards[0].IsAce;

        var second = new BlackjackHand { Bet = hand.Bet, FromSplit = true, IsSplitAce = splittingAces };
        var movedCard = hand.Cards[1];
        hand.Cards.RemoveAt(1);
        hand.FromSplit = true;
        hand.IsSplitAce = splittingAces;
        second.AddCard(movedCard);

        PlayerHands.Insert(CurrentHandIndex + 1, second);

        // Each half immediately gets its second card, same as a real table.
        hand.AddCard(Shoe.Draw());
        second.AddCard(Shoe.Draw());

        AdvanceIfHandResolved();
    }

    // Moves to the next unresolved hand once the current one is done — loops in
    // case the newly current hand is itself already resolved (lands on 21 right
    // after a split, another split Ace, etc). Once every hand is past, plays the
    // dealer's hand out.
    void AdvanceIfHandResolved()
    {
        while (CurrentHandIndex < PlayerHands.Count && PlayerHands[CurrentHandIndex].IsResolved)
            CurrentHandIndex++;

        if (CurrentHandIndex >= PlayerHands.Count)
            PlayDealerHandOut();
    }

    void PlayDealerHandOut()
    {
        // No point drawing the dealer's hand out further if every player hand
        // already busted or surrendered — the table's already settled either way.
        bool anyStillLive = PlayerHands.Any(h => !h.IsBust && !h.Surrendered);
        if (anyStillLive)
        {
            // Hits on soft 17 and everything below, stands on hard 17+.
            while (Dealer.BestTotal < 17 || (Dealer.BestTotal == 17 && Dealer.IsSoft))
                Dealer.AddCard(Shoe.Draw());
        }
        Dealer.Stood = true;
        RoundOver = true;
    }

    // Per-hand outcome + total amount returned (stake+winnings, 0 on a loss/bust) —
    // caller sums these plus InsurancePayout for the round's grand total.
    public List<(BlackjackHand hand, BlackjackOutcome outcome, long payout)> ResolveAll()
    {
        var results = new List<(BlackjackHand, BlackjackOutcome, long)>();
        foreach (var hand in PlayerHands)
        {
            var outcome = BlackjackResolver.Resolve(hand, Dealer);
            results.Add((hand, outcome, BlackjackResolver.Payout(hand, outcome)));
        }
        return results;
    }

    public long InsurancePayout => BlackjackResolver.ResolveInsurance(InsuranceTaken, InsuranceBet, Dealer);
}
