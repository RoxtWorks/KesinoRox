// One shooter's whole turn (first come-out through the eventual seven-out) — same
// shape as BaccaratRoundRecord/BlackjackRoundRecord for history/save purposes.
public class CrapsRoundRecord
{
    public int RoundIndex { get; }
    public int FinalPoint { get; }
    public int RollCount { get; }
    public long TotalStaked { get; }
    public long TotalReturned { get; }
    public long BalanceAfter { get; }
    // The actual dice total on this roll (0 for a per-shooter-turn summary record,
    // which isn't tied to one single roll).
    public int RollTotal { get; }

    public CrapsRoundRecord(int roundIndex, int finalPoint, int rollCount,
        long totalStaked, long totalReturned, long balanceAfter, int rollTotal = 0)
    {
        RoundIndex = roundIndex;
        FinalPoint = finalPoint;
        RollCount = rollCount;
        TotalStaked = totalStaked;
        TotalReturned = totalReturned;
        BalanceAfter = balanceAfter;
        RollTotal = rollTotal;
    }

    public long NetChange => TotalReturned - TotalStaked;
}
