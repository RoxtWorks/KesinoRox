// Per-hand record for the History column and the save file.
public class ThreeCardPokerRoundRecord
{
    public int    RoundIndex       { get; }
    public ThreeCardPokerRank PlayerRank    { get; }
    public int    PlayerHigh       { get; }   // top card value (14 = Ace) for "K high"-style labels
    public ThreeCardPokerRank DealerRank    { get; }
    public int    DealerHigh       { get; }
    public ThreeCardPokerOutcome Outcome    { get; }
    public bool   DealerQualified  { get; }
    public bool   Folded           { get; }
    public long   TotalStaked      { get; }
    public long   TotalReturned    { get; }
    public long   BalanceAfter     { get; }
    public long   NetChange        => TotalReturned - TotalStaked;

    public ThreeCardPokerRoundRecord(int roundIndex,
        ThreeCardPokerRank playerRank, int playerHigh, ThreeCardPokerRank dealerRank, int dealerHigh,
        ThreeCardPokerOutcome outcome, bool dealerQualified, bool folded,
        long totalStaked, long totalReturned, long balanceAfter)
    {
        RoundIndex      = roundIndex;
        PlayerRank      = playerRank;
        PlayerHigh      = playerHigh;
        DealerRank      = dealerRank;
        DealerHigh      = dealerHigh;
        Outcome         = outcome;
        DealerQualified = dealerQualified;
        Folded          = folded;
        TotalStaked     = totalStaked;
        TotalReturned   = totalReturned;
        BalanceAfter    = balanceAfter;
    }
}
