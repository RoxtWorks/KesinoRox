// Bets available in Three Pictures (Royal Three Pictures / 3 Kings).
// Main bet: player's hand vs dealer's hand. Royal side bet: pays on player
// having 2+ picture cards, regardless of who wins the main hand.
public enum ThreePicturesBetType
{
    Main,       // Player beats Dealer → 1:1; Tie → push; Dealer wins → lose
    RoyalBonus  // Side bet: 3 pictures (Player) = 5:1, 2 pictures = 1:1
}
