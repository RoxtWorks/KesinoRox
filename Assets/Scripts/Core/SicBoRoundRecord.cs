// Per-roll summary for history and save file.
public class SicBoRoundRecord
{
    public int  RoundIndex   { get; }
    public int  Die1         { get; }
    public int  Die2         { get; }
    public int  Die3         { get; }
    public int  Total        => Die1 + Die2 + Die3;
    public long TotalStaked  { get; }
    public long TotalReturned{ get; }
    public long BalanceAfter { get; }
    public long NetChange    => TotalReturned - TotalStaked;

    public SicBoRoundRecord(int roundIndex, int die1, int die2, int die3,
        long totalStaked, long totalReturned, long balanceAfter)
    {
        RoundIndex    = roundIndex;
        Die1          = die1;
        Die2          = die2;
        Die3          = die3;
        TotalStaked   = totalStaked;
        TotalReturned = totalReturned;
        BalanceAfter  = balanceAfter;
    }
}
