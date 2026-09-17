// Outcome and payout math for Three Pictures — Malaysian / RWS non-commission rules.
// Ranking: three pictures (J/Q/K) beats everything; otherwise the higher point wins;
// equal points → the hand with more picture cards wins (Q-Q-9 beats J-10-9); otherwise a tie.
// Two three-picture hands tie.
public enum ThreePicturesOutcome { PlayerWins, DealerWins, Tie }

public static class ThreePicturesResolver
{
    public static ThreePicturesOutcome Resolve(ThreePicturesHand player, ThreePicturesHand dealer)
    {
        bool pRoyal = player.IsRoyal;
        bool dRoyal = dealer.IsRoyal;

        if (pRoyal && dRoyal) return ThreePicturesOutcome.Tie;
        if (pRoyal)           return ThreePicturesOutcome.PlayerWins;
        if (dRoyal)           return ThreePicturesOutcome.DealerWins;

        int pPt = player.Point;
        int dPt = dealer.Point;
        if (pPt != dPt) return pPt > dPt ? ThreePicturesOutcome.PlayerWins : ThreePicturesOutcome.DealerWins;

        int pPic = player.PictureCount;
        int dPic = dealer.PictureCount;
        if (pPic != dPic) return pPic > dPic ? ThreePicturesOutcome.PlayerWins : ThreePicturesOutcome.DealerWins;

        return ThreePicturesOutcome.Tie;
    }

    // True when a winning hand is paid half (no-commission rule: winning with a point of 6 pays 1:2).
    public static bool IsHalfPayWin(ThreePicturesHand player, ThreePicturesOutcome outcome) =>
        outcome == ThreePicturesOutcome.PlayerWins && !player.IsRoyal && player.Point == 6;

    // Total amount back for one hand's bet (stake + winnings), 0 on loss.
    // Win 1:1; win with 6 pays 1:2 (half-chip remainders round down, as the table pays in chips); tie = push.
    public static long Payout(long stake, ThreePicturesHand player, ThreePicturesOutcome outcome) => outcome switch
    {
        ThreePicturesOutcome.PlayerWins => IsHalfPayWin(player, outcome) ? stake + stake / 2 : stake * 2,
        ThreePicturesOutcome.Tie        => stake,
        _                               => 0
    };
}
