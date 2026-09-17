using System.Collections.Generic;

// One hand in Three Pictures (Royal Three Pictures / 3 Kings). Three cards are
// dealt; the hand's "point" is the sum of card values mod 10 (baccarat-style),
// where 10/J/Q/K = 0 and Ace = 1. A hand with THREE picture cards (J/Q/K) is
// the highest-ranking hand and beats any point total. Picture cards have no
// rank among themselves (KQJ == KKJ, suits don't matter).
public class ThreePicturesHand
{
    readonly List<Card> cards = new List<Card>(3);

    public IReadOnlyList<Card> Cards => cards;
    public int CardCount => cards.Count;

    public void AddCard(Card card) => cards.Add(card);

    // Baccarat-style point: only 2-9 count; 10/face = 0; drop tens digit.
    public int Point
    {
        get
        {
            int sum = 0;
            foreach (var c in cards) sum += CardValue(c);
            return sum % 10;
        }
    }

    // Number of picture cards (J/Q/K) in this hand.
    public int PictureCount
    {
        get
        {
            int count = 0;
            foreach (var c in cards) if (IsPicture(c)) count++;
            return count;
        }
    }

    // A "Royal Hand" = all three cards are pictures — beats any point total.
    public bool IsRoyal => CardCount == 3 && PictureCount == 3;

    public static int CardValue(Card card) => card.Rank switch
    {
        Rank.Ace   => 1,
        Rank.Two   => 2,
        Rank.Three => 3,
        Rank.Four  => 4,
        Rank.Five  => 5,
        Rank.Six   => 6,
        Rank.Seven => 7,
        Rank.Eight => 8,
        Rank.Nine  => 9,
        _          => 0 // Ten, J, Q, K
    };

    public static bool IsPicture(Card card) =>
        card.Rank is Rank.Jack or Rank.Queen or Rank.King;
}
