using System.Collections.Generic;

// Three Card Poker hand evaluation.
// Hand rankings (high→low): Straight Flush, Three of a Kind, Straight,
// Flush, Pair, High Card.
public class ThreeCardPokerHand
{
    public List<Card> Cards { get; } = new List<Card>(3);

    public void AddCard(Card c) { Cards.Add(c); }

    // Lazy-evaluated hand rank (higher int = better hand).
    public ThreeCardPokerRank Rank => Evaluate();

    // High card for tiebreaks: Ace = 14, King = 13, ... Two = 2.
    public int HighCard => HighestRankValue();
    public int SecondCard => SecondHighestRankValue();

    ThreeCardPokerRank Evaluate()
    {
        if (Cards.Count < 3) return ThreeCardPokerRank.HighCard;

        bool isFlush    = IsFlush();
        bool isStraight = IsStraight();
        bool isTrips    = IsThreeOfAKind();
        bool isPair     = HasPair();

        if (isFlush && isStraight) return ThreeCardPokerRank.StraightFlush;
        if (isTrips)               return ThreeCardPokerRank.ThreeOfAKind;
        if (isStraight)            return ThreeCardPokerRank.Straight;
        if (isFlush)               return ThreeCardPokerRank.Flush;
        if (isPair)                return ThreeCardPokerRank.Pair;
        return ThreeCardPokerRank.HighCard;
    }

    bool IsFlush()
    {
        return Cards[0].Suit == Cards[1].Suit && Cards[1].Suit == Cards[2].Suit;
    }

    bool IsStraight()
    {
        int[] vals = SortedValues();
        // Ace-2-3 wrap: A=14, 2=2, 3=3 → treat A as 1 for this check
        if (vals[0] == 2 && vals[1] == 3 && vals[2] == 14) return true;
        return vals[2] - vals[0] == 2 && vals[1] - vals[0] == 1;
    }

    bool IsThreeOfAKind()
    {
        return (int)Cards[0].Rank == (int)Cards[1].Rank &&
               (int)Cards[1].Rank == (int)Cards[2].Rank;
    }

    bool HasPair()
    {
        return (int)Cards[0].Rank == (int)Cards[1].Rank ||
               (int)Cards[1].Rank == (int)Cards[2].Rank ||
               (int)Cards[0].Rank == (int)Cards[2].Rank;
    }

    int[] SortedValues()
    {
        int a = (int)Cards[0].Rank;
        int b = (int)Cards[1].Rank;
        int c = (int)Cards[2].Rank;
        if (a > b) { int t = a; a = b; b = t; }
        if (b > c) { int t = b; b = c; c = t; }
        if (a > b) { int t = a; a = b; b = t; }
        return new[] { a, b, c };
    }

    int HighestRankValue()
    {
        int max = (int)Cards[0].Rank;
        for (int i = 1; i < Cards.Count; i++)
            if ((int)Cards[i].Rank > max) max = (int)Cards[i].Rank;
        return max;
    }

    int SecondHighestRankValue()
    {
        var vals = SortedValues();
        return vals[1]; // middle value in sorted 3-card hand
    }

    // For pair/trips tiebreak: return the rank of the pair or trips.
    public int PairRank()
    {
        if ((int)Cards[0].Rank == (int)Cards[1].Rank) return (int)Cards[0].Rank;
        if ((int)Cards[1].Rank == (int)Cards[2].Rank) return (int)Cards[1].Rank;
        if ((int)Cards[0].Rank == (int)Cards[2].Rank) return (int)Cards[0].Rank;
        return 0;
    }

    // "Kicker" — the non-pair card rank for pair hands.
    public int KickerRank()
    {
        if ((int)Cards[0].Rank != (int)Cards[1].Rank &&
            (int)Cards[0].Rank != (int)Cards[2].Rank) return (int)Cards[0].Rank;
        if ((int)Cards[1].Rank != (int)Cards[0].Rank &&
            (int)Cards[1].Rank != (int)Cards[2].Rank) return (int)Cards[1].Rank;
        return (int)Cards[2].Rank;
    }

    // Dealer qualifies at Queen-high or better.
    public bool DealerQualifies => Rank > ThreeCardPokerRank.HighCard ||
        (Rank == ThreeCardPokerRank.HighCard && HighCard >= (int)global::Rank.Queen);
}

public enum ThreeCardPokerRank
{
    HighCard       = 0,
    Pair           = 1,
    Flush          = 2,
    Straight       = 3,
    ThreeOfAKind   = 4,
    StraightFlush  = 5
}
