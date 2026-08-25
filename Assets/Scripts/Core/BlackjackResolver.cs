// Pure payout resolution — mirrors BetResolver's shape (no RNG, no state, safe to
// call in a tight loop). Resolve() decides the outcome; Payout()/ResolveInsurance()
// convert that into the total amount returned to the player (stake + winnings, 0 on
// a loss — same "total returned" convention BetResolver.Resolve uses).
public static class BlackjackResolver
{
    public static BlackjackOutcome Resolve(BlackjackHand player, BlackjackHand dealer)
    {
        if (player.Surrendered) return BlackjackOutcome.Surrender;
        if (player.IsBust) return BlackjackOutcome.Bust;

        if (player.IsNaturalBlackjack)
            return dealer.IsNaturalBlackjack ? BlackjackOutcome.Push : BlackjackOutcome.PlayerBlackjack;

        // A non-natural 21 (e.g. from a split) still loses to a dealer natural —
        // only a player natural can push against one.
        if (dealer.IsNaturalBlackjack) return BlackjackOutcome.Lose;

        if (dealer.IsBust) return BlackjackOutcome.Win;
        if (player.BestTotal > dealer.BestTotal) return BlackjackOutcome.Win;
        if (player.BestTotal < dealer.BestTotal) return BlackjackOutcome.Lose;
        return BlackjackOutcome.Push;
    }

    public static long Payout(BlackjackHand hand, BlackjackOutcome outcome) => outcome switch
    {
        BlackjackOutcome.PlayerBlackjack => hand.Bet + hand.Bet * 3 / 2, // 3:2
        BlackjackOutcome.Win => hand.Bet * 2, // 1:1
        BlackjackOutcome.Push => hand.Bet,
        BlackjackOutcome.Surrender => hand.Bet / 2,
        BlackjackOutcome.Lose => 0,
        BlackjackOutcome.Bust => 0,
        _ => 0
    };

    // Resolved independently of the main hand — a side bet on the dealer's hole
    // card being a ten, only offered when the up-card is an Ace. Pays 2:1.
    public static long ResolveInsurance(bool taken, long insuranceBet, BlackjackHand dealer)
    {
        if (!taken || insuranceBet <= 0) return 0;
        return dealer.IsNaturalBlackjack ? insuranceBet * 3 : 0;
    }
}
