// Two uses: per-shooter-turn summary saved to disk, and per-roll History row
// (TotalStaked = chips on the felt when the dice were thrown, NetChange = that roll's profit/loss).
public class NormalCrapsRoundRecord
{
    public int RoundIndex { get; }
    public int FinalPoint { get; }
    public int RollCount { get; }
    public long TotalStaked { get; }
    public long TotalReturned { get; }
    public long BalanceAfter { get; }
    public int RollTotal { get; }

    public long NetChange => TotalReturned - TotalStaked;

    public NormalCrapsRoundRecord(int roundIndex, int finalPoint, int rollCount,
        long totalStaked, long totalReturned, long balanceAfter, int rollTotal)
    {
        RoundIndex = roundIndex;
        FinalPoint = finalPoint;
        RollCount = rollCount;
        TotalStaked = totalStaked;
        TotalReturned = totalReturned;
        BalanceAfter = balanceAfter;
        RollTotal = rollTotal;
    }
}
