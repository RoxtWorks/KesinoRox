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

    // Highest card: Ace = 14, King = 13, ... Two = 2 (used for the dealer's Queen-high qualifier).
    public int HighCard => HighestRankValue();

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

    // Rank of the pair (or trips) in the hand, 0 if none.
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

    // Card values used to break ties between hands of the same rank, most significant first.
    // Straights (and straight flushes) compare by top card, with A-2-3 the lowest (top card 3).
    // Pairs compare pair rank then kicker; everything else compares all three cards high to low.
    public int[] TiebreakValues()
    {
        int[] v = SortedValues();
        switch (Rank)
        {
            case ThreeCardPokerRank.StraightFlush:
            case ThreeCardPokerRank.Straight:
                return new[] { IsWheel(v) ? 3 : v[2] };
            case ThreeCardPokerRank.ThreeOfAKind:
                return new[] { v[0] };
            case ThreeCardPokerRank.Pair:
                return new[] { PairRank(), KickerRank() };
            default:
                return new[] { v[2], v[1], v[0] };
        }
    }

    static bool IsWheel(int[] sorted) => sorted[0] == 2 && sorted[1] == 3 && sorted[2] == 14;

    // Plain-language hand name for the table, e.g. "Pair of 9s", "Jack high", "Straight, Queen high".
    public string Describe()
    {
        int[] v = SortedValues();
        switch (Rank)
        {
            case ThreeCardPokerRank.StraightFlush: return $"Straight flush, {RankName(IsWheel(v) ? 3 : v[2])} high";
            case ThreeCardPokerRank.ThreeOfAKind:  return $"Three {RankPlural(v[0])}";
            case ThreeCardPokerRank.Straight:      return $"Straight, {RankName(IsWheel(v) ? 3 : v[2])} high";
            case ThreeCardPokerRank.Flush:         return $"Flush, {RankName(v[2])} high";
            case ThreeCardPokerRank.Pair:          return $"Pair of {RankPlural(PairRank())}";
            default:                               return $"{RankName(v[2])} high";
        }
    }

    static string RankName(int value) => value switch
    {
        14 => "Ace", 13 => "King", 12 => "Queen", 11 => "Jack", _ => value.ToString()
    };

    static string RankPlural(int value) => value switch
    {
        14 => "Aces", 13 => "Kings", 12 => "Queens", 11 => "Jacks", _ => $"{value}s"
    };

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
