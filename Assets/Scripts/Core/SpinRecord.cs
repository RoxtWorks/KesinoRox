using System.Collections.Generic;

public class SpinRecord
{
    public int SpinIndex { get; }
    public int WinningNumber { get; }
    public long TotalStaked { get; }
    public long TotalReturned { get; }
    public long NetChange => TotalReturned - TotalStaked;
    public long BalanceAfter { get; }

    public SpinRecord(int spinIndex, int winningNumber, long totalStaked, long totalReturned, long balanceAfter)
    {
        SpinIndex = spinIndex;
        WinningNumber = winningNumber;
        TotalStaked = totalStaked;
        TotalReturned = totalReturned;
        BalanceAfter = balanceAfter;
    }
}
