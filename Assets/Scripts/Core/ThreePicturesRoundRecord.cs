// Per-round summary for the History column and the save file.
public class ThreePicturesRoundRecord
{
    // Per box: stake (0 = box not played), outcome and whether a win paid half
    public struct BoxEntry
    {
        public long Stake;
        public ThreePicturesOutcome Outcome;
        public bool HalfPay;
    }

    public int  RoundIndex    { get; }
    public int  DealerPoint   { get; }
    public bool DealerRoyal   { get; }
    public BoxEntry[] Boxes   { get; }
    public long TotalStaked   { get; }
    public long TotalReturned { get; }
    public long BalanceAfter  { get; }

    public long NetChange => TotalReturned - TotalStaked;

    public ThreePicturesRoundRecord(int roundIndex, int dealerPoint, bool dealerRoyal, BoxEntry[] boxes,
        long totalStaked, long totalReturned, long balanceAfter)
    {
        RoundIndex    = roundIndex;
        DealerPoint   = dealerPoint;
        DealerRoyal   = dealerRoyal;
        Boxes         = boxes;
        TotalStaked   = totalStaked;
        TotalReturned = totalReturned;
        BalanceAfter  = balanceAfter;
    }
}
