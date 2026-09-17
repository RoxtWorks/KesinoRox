// Three Card Poker bet types.
// Ante+Play are linked: player posts Ante, then decides to Play (match Ante)
// or Fold (forfeit Ante). PairPlus is independent — no decision needed.
public enum ThreeCardPokerBetType
{
    Ante,
    Play,
    PairPlus
}
