// One resolved round for history/save purposes — a round can cover several hands
// (after splitting) but this records the round as a whole, same shape as SpinRecord.
public class BlackjackRoundRecord
{
    public int RoundIndex { get; }
    public int PlayerFinalTotal { get; }
    public int DealerFinalTotal { get; }
    public long TotalStaked { get; }
    public long TotalReturned { get; }
    public long BalanceAfter { get; }

    public BlackjackRoundRecord(int roundIndex, int playerFinalTotal, int dealerFinalTotal,
        long totalStaked, long totalReturned, long balanceAfter)
    {
        RoundIndex = roundIndex;
        PlayerFinalTotal = playerFinalTotal;
        DealerFinalTotal = dealerFinalTotal;
        TotalStaked = totalStaked;
        TotalReturned = totalReturned;
        BalanceAfter = balanceAfter;
    }

    public long NetChange => TotalReturned - TotalStaked;
}
