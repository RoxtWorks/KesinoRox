// Per-round record for history and save file.
public class ThreeCardPokerRoundRecord
{
    public int    RoundIndex       { get; }
    public ThreeCardPokerRank PlayerRank    { get; }
    public ThreeCardPokerRank DealerRank    { get; }
    public ThreeCardPokerOutcome Outcome    { get; }
    public bool   DealerQualified  { get; }
    public long   TotalStaked      { get; }
    public long   TotalReturned    { get; }
    public long   BalanceAfter     { get; }
    public long   NetChange        => TotalReturned - TotalStaked;

    public ThreeCardPokerRoundRecord(int roundIndex,
        ThreeCardPokerRank playerRank, ThreeCardPokerRank dealerRank,
        ThreeCardPokerOutcome outcome, bool dealerQualified,
        long totalStaked, long totalReturned, long balanceAfter)
    {
        RoundIndex      = roundIndex;
        PlayerRank      = playerRank;
        DealerRank      = dealerRank;
        Outcome         = outcome;
        DealerQualified = dealerQualified;
        TotalStaked     = totalStaked;
        TotalReturned   = totalReturned;
        BalanceAfter    = balanceAfter;
    }
}
