using System.Collections.Generic;

// One hand of cards (Player or Banker) in a baccarat round. Point totals wrap mod
// 10 — that's baccarat's whole arithmetic, no soft/hard ace handling or bust concept
// like blackjack.
public class BaccaratHand
{
    public List<Card> Cards { get; } = new List<Card>();

    public void AddCard(Card card) => Cards.Add(card);

    public int Point
    {
        get
        {
            int total = 0;
            foreach (var card in Cards) total += BaccaratResolver.CardValue(card);
            return total % 10;
        }
    }

    // An 8 or 9 from the first two cards — once either hand has one, no third card
    // is drawn by either side.
    public bool IsNatural => Cards.Count == 2 && Point >= 8;
}
