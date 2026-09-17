// Outcome determination and payout math for Three Pictures.
// Ranking: Royal (3 pictures) > any point total. Points break ties.
// If both Royal: push. If equal points and equal picture count: push.
// Singapore rules: points decide first; picture count breaks point ties.
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

        // Both non-royal: compare points first, then picture count as tiebreaker.
        int pPt = player.Point;
        int dPt = dealer.Point;
        if (pPt != dPt) return pPt > dPt ? ThreePicturesOutcome.PlayerWins : ThreePicturesOutcome.DealerWins;

        // Points equal: more pictures wins.
        int pPic = player.PictureCount;
        int dPic = dealer.PictureCount;
        if (pPic != dPic) return pPic > dPic ? ThreePicturesOutcome.PlayerWins : ThreePicturesOutcome.DealerWins;

        return ThreePicturesOutcome.Tie;
    }

    // Main bet: 1:1 on win, push on tie, 0 on loss. Returns total amount back.
    public static long MainPayout(long stake, ThreePicturesOutcome outcome) => outcome switch
    {
        ThreePicturesOutcome.PlayerWins => stake * 2,
        ThreePicturesOutcome.Tie        => stake,   // push — stake returned
        _                               => 0
    };

    // Royal Bonus (side bet on player's hand only):
    // 3 picture cards (Royal) pays 5:1; 2 picture cards pays 1:1; else 0.
    public static long RoyalBonusPayout(long stake, ThreePicturesHand player) =>
        player.PictureCount switch
        {
            3 => stake * 6,  // 5:1 → stake + 5×stake
            2 => stake * 2,  // 1:1
            _ => 0
        };
}
