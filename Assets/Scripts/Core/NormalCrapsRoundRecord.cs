// Per-shooter-turn summary saved to disk and shown in the History panel.
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
