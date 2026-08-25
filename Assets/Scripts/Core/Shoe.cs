using System;
using System.Collections.Generic;

// Multi-deck shoe with a fixed reshuffle threshold ("penetration") — real casino
// shoes get reshuffled once enough cards have been dealt, never mid-hand, so the
// remaining composition doesn't get thin enough to meaningfully skew card-counting
// strategy testing. Fisher-Yates via the same injected IRandomSource pattern
// SpinResultGenerator uses, so shuffles are deterministic/testable with a seed.
public class Shoe
{
    readonly IRandomSource rng;
    readonly int deckCount;
    readonly int totalCards;
    // Reshuffle once at or below this many cards remain — 25% remaining is
    // equivalent to ~75% penetration.
    readonly int reshuffleThreshold;

    List<Card> cards = new List<Card>();
    int nextIndex;

    public Shoe(int deckCount, IRandomSource rng)
    {
        this.deckCount = deckCount;
        this.rng = rng;
        totalCards = deckCount * 52;
        reshuffleThreshold = totalCards / 4;
        Shuffle();
    }

    // Test-only seam: deals from a fixed, caller-specified order instead of a real
    // shuffle, so round/split/dealer-play tests can force exact hands. Reshuffling
    // is never triggered in this mode (reshuffleThreshold is 0) — tests using this
    // constructor are expected to supply enough cards for the scenario they cover.
    public Shoe(IEnumerable<Card> orderedCards)
    {
        cards = new List<Card>(orderedCards);
        totalCards = cards.Count;
        reshuffleThreshold = 0;
        nextIndex = 0;
    }

    public int RemainingCards => cards.Count - nextIndex;
    public bool NeedsReshuffle => RemainingCards <= reshuffleThreshold;

    public void Shuffle()
    {
        cards = new List<Card>(totalCards);
        for (int d = 0; d < deckCount; d++)
        {
            foreach (Suit suit in Enum.GetValues(typeof(Suit)))
            {
                foreach (Rank rank in Enum.GetValues(typeof(Rank)))
                {
                    cards.Add(new Card(rank, suit));
                }
            }
        }

        // Fisher-Yates.
        for (int i = cards.Count - 1; i > 0; i--)
        {
            int j = rng.Next(0, i + 1);
            (cards[i], cards[j]) = (cards[j], cards[i]);
        }

        nextIndex = 0;
    }

    public Card Draw()
    {
        if (nextIndex >= cards.Count) Shuffle();
        return cards[nextIndex++];
    }
}
