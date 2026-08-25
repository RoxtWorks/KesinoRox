// Pure rules: card values, who draws a third card, who wins, what a bet pays. No
// shoe/state — BaccaratRound owns the actual dealing sequence and calls into this.
// Mirrors BlackjackResolver's shape (Resolve() decides the outcome, Payout()
// converts it to the total amount returned — stake + winnings, 0 on a loss).
public static class BaccaratResolver
{
    // Ace counts as 1, tens/face cards count as 0, everything else at face value.
    public static int CardValue(Card card) => card.Rank switch
    {
        Rank.Ace => 1,
        Rank.Ten or Rank.Jack or Rank.Queen or Rank.King => 0,
        _ => (int)card.Rank
    };

    // Standard "player table": stands on 6 or 7, draws on 0-5. Never called if
    // either hand already has a natural.
    public static bool PlayerDraws(BaccaratHand player) => player.Point <= 5;

    // Banker's draw table depends on the Banker's own total and, only if the player
    // drew a third card, the value of that specific card — the classic 6-row table.
    // If the player stood, Banker just mirrors the player's own stand-on-6/7 rule.
    public static bool BankerDraws(BaccaratHand banker, bool playerDrew, int playerThirdCardValue)
    {
        int b = banker.Point;
        if (!playerDrew) return b <= 5;

        return b switch
        {
            <= 2 => true,
            3 => playerThirdCardValue != 8,
            4 => playerThirdCardValue >= 2 && playerThirdCardValue <= 7,
            5 => playerThirdCardValue >= 4 && playerThirdCardValue <= 7,
            6 => playerThirdCardValue == 6 || playerThirdCardValue == 7,
            _ => false // 7 always stands
        };
    }

    public static BaccaratOutcome Resolve(BaccaratHand player, BaccaratHand banker)
    {
        if (player.Point > banker.Point) return BaccaratOutcome.PlayerWin;
        if (banker.Point > player.Point) return BaccaratOutcome.BankerWin;
        return BaccaratOutcome.Tie;
    }

    // A tie pushes Player/Banker bets (stake back, no win/loss) and pays 8:1 on a
    // Tie bet itself. Banker wins carry the standard 5% commission; Player pays
    // flat 1:1. Integer math throughout — no floats on money.
    public static long Payout(BaccaratBetType bet, long stake, BaccaratOutcome outcome)
    {
        if (outcome == BaccaratOutcome.Tie)
            return bet == BaccaratBetType.Tie ? stake * 9 : stake;

        bool won = (bet == BaccaratBetType.Player && outcome == BaccaratOutcome.PlayerWin)
            || (bet == BaccaratBetType.Banker && outcome == BaccaratOutcome.BankerWin);
        if (!won) return 0;

        if (bet == BaccaratBetType.Banker)
            return stake + (stake * 19) / 20; // 1:1 minus 5% commission
        return stake * 2;
    }
}
