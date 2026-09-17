// Per-round summary for history panel and save file.
public class ThreePicturesRoundRecord
{
    public int  RoundIndex   { get; }
    public int  PlayerPoint  { get; }
    public int  DealerPoint  { get; }
    public bool PlayerRoyal  { get; }
    public bool DealerRoyal  { get; }
    public ThreePicturesOutcome Outcome { get; }
    public long TotalStaked  { get; }
    public long TotalReturned{ get; }
    public long BalanceAfter { get; }

    public long NetChange => TotalReturned - TotalStaked;

    public ThreePicturesRoundRecord(int roundIndex, int playerPoint, int dealerPoint,
        bool playerRoyal, bool dealerRoyal, ThreePicturesOutcome outcome,
        long totalStaked, long totalReturned, long balanceAfter)
    {
        RoundIndex    = roundIndex;
        PlayerPoint   = playerPoint;
        DealerPoint   = dealerPoint;
        PlayerRoyal   = playerRoyal;
        DealerRoyal   = dealerRoyal;
        Outcome       = outcome;
        TotalStaked   = totalStaked;
        TotalReturned = totalReturned;
        BalanceAfter  = balanceAfter;
    }
}
