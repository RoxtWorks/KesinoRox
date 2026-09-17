// Pure payout math for Three Card Poker.
// All methods return total amount returned (stake + winnings), 0 on loss.
// Ante Bonus pays regardless of dealer qualifying; Play pays vs dealer hand.
public static class ThreeCardPokerResolver
{
    // Compare player vs dealer. Called after dealer hand is evaluated.
    // Returns win/loss/tie for the Ante+Play wagers.
    public static ThreeCardPokerOutcome Resolve(ThreeCardPokerHand player,
                                                ThreeCardPokerHand dealer)
    {
        if (!dealer.DealerQualifies) return ThreeCardPokerOutcome.DealerNoQualify;

        int cmp = CompareHands(player, dealer);
        if (cmp > 0) return ThreeCardPokerOutcome.PlayerWins;
        if (cmp < 0) return ThreeCardPokerOutcome.DealerWins;
        return ThreeCardPokerOutcome.Tie;
    }

    // Ante return: dealer no-qualify → Ante paid 1:1, Play returned (push).
    //              player wins → Ante+Play both paid 1:1.
    //              tie → both push.
    //              dealer wins → both forfeit (return 0).
    public static long AntePayout(long anteBet, ThreeCardPokerOutcome outcome) => outcome switch
    {
        ThreeCardPokerOutcome.DealerNoQualify => anteBet * 2, // 1:1 on Ante
        ThreeCardPokerOutcome.PlayerWins      => anteBet * 2, // 1:1
        ThreeCardPokerOutcome.Tie             => anteBet,     // push
        _                                     => 0
    };

    public static long PlayPayout(long playBet, ThreeCardPokerOutcome outcome) => outcome switch
    {
        ThreeCardPokerOutcome.DealerNoQualify => playBet,     // push (returned)
        ThreeCardPokerOutcome.PlayerWins      => playBet * 2, // 1:1
        ThreeCardPokerOutcome.Tie             => playBet,     // push
        _                                     => 0
    };

    // Ante Bonus: paid on player's hand regardless of dealer qualifying.
    // Standard pay table: Straight Flush 5:1, Three of a Kind 4:1, Straight 1:1.
    public static long AnteBonusPayout(long anteBet, ThreeCardPokerHand player) =>
        player.Rank switch
        {
            ThreeCardPokerRank.StraightFlush => anteBet * 6, // 5:1
            ThreeCardPokerRank.ThreeOfAKind  => anteBet * 5, // 4:1
            ThreeCardPokerRank.Straight      => anteBet * 2, // 1:1
            _                                => 0
        };

    // Pair Plus pays on the player's own hand, no comparison with dealer.
    // Standard pay table: Straight Flush 40:1, Three of Kind 30:1,
    // Straight 6:1, Flush 3:1, Pair 1:1.
    public static long PairPlusPayout(long ppBet, ThreeCardPokerHand player) =>
        player.Rank switch
        {
            ThreeCardPokerRank.StraightFlush => ppBet * 41, // 40:1
            ThreeCardPokerRank.ThreeOfAKind  => ppBet * 31, // 30:1
            ThreeCardPokerRank.Straight      => ppBet * 7,  // 6:1
            ThreeCardPokerRank.Flush         => ppBet * 4,  // 3:1
            ThreeCardPokerRank.Pair          => ppBet * 2,  // 1:1
            _                                => 0
        };

    // Returns >0 if a beats b, <0 if b beats a, 0 if equal.
    static int CompareHands(ThreeCardPokerHand a, ThreeCardPokerHand b)
    {
        int rankCmp = (int)a.Rank - (int)b.Rank;
        if (rankCmp != 0) return rankCmp;

        // Same rank — compare high card / pair rank / kicker
        return a.Rank switch
        {
            ThreeCardPokerRank.Pair =>
                a.PairRank() != b.PairRank()
                    ? a.PairRank() - b.PairRank()
                    : a.KickerRank() - b.KickerRank(),

            ThreeCardPokerRank.ThreeOfAKind =>
                a.HighCard - b.HighCard,

            _ =>
                a.HighCard != b.HighCard ? a.HighCard - b.HighCard
                : a.SecondCard != b.SecondCard ? a.SecondCard - b.SecondCard
                : 0
        };
    }
}

public enum ThreeCardPokerOutcome
{
    PlayerWins,
    DealerWins,
    Tie,
    DealerNoQualify
}
