public enum Suit
{
    Hearts,
    Diamonds,
    Clubs,
    Spades
}

// Explicit values so HardValue can read straight off the enum for number cards —
// Jack/Queen/King/Ace all still need their own case (face cards clamp to 10, Ace
// counts as 11 here with the soft/hard reduction handled in BlackjackHand).
public enum Rank
{
    Two = 2,
    Three = 3,
    Four = 4,
    Five = 5,
    Six = 6,
    Seven = 7,
    Eight = 8,
    Nine = 9,
    Ten = 10,
    Jack = 11,
    Queen = 12,
    King = 13,
    Ace = 14
}

public readonly struct Card
{
    public Rank Rank { get; }
    public Suit Suit { get; }

    public Card(Rank rank, Suit suit)
    {
        Rank = rank;
        Suit = suit;
    }

    public bool IsRed => Suit == Suit.Hearts || Suit == Suit.Diamonds;
    public bool IsAce => Rank == Rank.Ace;

    // Blackjack value with the Ace counted high (11) — BlackjackHand is responsible
    // for demoting Aces to 1 when the total would otherwise bust.
    public int HardValue => Rank switch
    {
        Rank.Jack or Rank.Queen or Rank.King => 10,
        Rank.Ace => 11,
        _ => (int)Rank
    };

    public override string ToString() => $"{Rank} of {Suit}";
}
