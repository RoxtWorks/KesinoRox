using System.Collections.Generic;

// One hand of cards with its own bet — a player can end a round holding several of
// these at once (one per split), the dealer always holds exactly one.
public class BlackjackHand
{
    public List<Card> Cards { get; } = new List<Card>();
    public long Bet { get; set; }

    // Set on both halves of a split, including re-splits — distinguishes "hit 21
    // with two cards after splitting" from a real natural blackjack, which by
    // standard rule only pays 3:2 on the original two-card deal, not after a split.
    public bool FromSplit { get; set; }
    // Aces split get exactly one further card each and can't hit/double/split again
    // — a distinct flag from FromSplit since it changes what actions are legal.
    public bool IsSplitAce { get; set; }
    public bool DoubledDown { get; set; }
    public bool Surrendered { get; set; }
    public bool Stood { get; set; }

    public void AddCard(Card card) => Cards.Add(card);

    // Best total ≤21 if one exists, using each Ace as 11 until that would bust,
    // then demoting Aces to 1 one at a time — standard soft/hard reduction.
    public int BestTotal
    {
        get
        {
            int total = 0;
            int acesAsEleven = 0;
            foreach (var card in Cards)
            {
                total += card.HardValue;
                if (card.IsAce) acesAsEleven++;
            }
            while (total > 21 && acesAsEleven > 0)
            {
                total -= 10;
                acesAsEleven--;
            }
            return total;
        }
    }

    // True if at least one Ace is still counted as 11 in BestTotal — e.g. A+6 is a
    // soft 17 (could still take a hit without busting), 10+6+A is a hard 17.
    public bool IsSoft
    {
        get
        {
            int total = 0;
            int acesAsEleven = 0;
            foreach (var card in Cards)
            {
                total += card.HardValue;
                if (card.IsAce) acesAsEleven++;
            }
            while (total > 21 && acesAsEleven > 0)
            {
                total -= 10;
                acesAsEleven--;
            }
            return acesAsEleven > 0;
        }
    }

    public bool IsBust => BestTotal > 21;

    // Only the original two-card deal, not a split hand landing on 21.
    public bool IsNaturalBlackjack => Cards.Count == 2 && BestTotal == 21 && !FromSplit;

    public bool CanHit => !IsResolved && !IsSplitAce && !DoubledDown;

    // Any two cards may double in this ruleset (including after a split), except
    // a split Ace hand, which only ever gets the one forced extra card.
    public bool CanDouble => Cards.Count == 2 && !DoubledDown && !Surrendered && !IsSplitAce;

    public bool CanSplit => Cards.Count == 2 && Cards[0].HardValue == Cards[1].HardValue && !IsSplitAce;

    // Only ever legal on the original two-card hand, before any other action.
    public bool CanSurrender => Cards.Count == 2 && !FromSplit && !DoubledDown && !Surrendered;

    public bool IsResolved =>
        Stood || IsBust || Surrendered || IsNaturalBlackjack ||
        (IsSplitAce && Cards.Count >= 2) ||
        (DoubledDown && Cards.Count >= 3);
}
