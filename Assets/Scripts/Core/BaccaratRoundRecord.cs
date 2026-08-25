// One resolved round for history/save purposes — same shape as BlackjackRoundRecord.
public class BaccaratRoundRecord
{
    public int RoundIndex { get; }
    public int PlayerPoint { get; }
    public int BankerPoint { get; }
    public BaccaratOutcome Outcome { get; }
    public long TotalStaked { get; }
    public long TotalReturned { get; }
    public long BalanceAfter { get; }

    public BaccaratRoundRecord(int roundIndex, int playerPoint, int bankerPoint, BaccaratOutcome outcome,
        long totalStaked, long totalReturned, long balanceAfter)
    {
        RoundIndex = roundIndex;
        PlayerPoint = playerPoint;
        BankerPoint = bankerPoint;
        Outcome = outcome;
        TotalStaked = totalStaked;
        TotalReturned = totalReturned;
        BalanceAfter = balanceAfter;
    }

    public long NetChange => TotalReturned - TotalStaked;
}
