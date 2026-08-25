// A single Come or Don't Come bet. Unlike every other craps bet this can't be a flat
// dictionary slot — a player can have several Come bets parked at different points
// simultaneously, so CrapsRound keeps a List<ComeWager> instead. Point == null means
// "traveling": placed, but hasn't had its own point-establishing roll yet.
public class ComeWager
{
    public bool IsDontCome { get; }
    public long Amount { get; }
    public long OddsAmount { get; private set; }
    public int? Point { get; private set; }

    public ComeWager(bool isDontCome, long amount)
    {
        IsDontCome = isDontCome;
        Amount = amount;
    }

    public void SetPoint(int point) => Point = point;
    public void AddOdds(long amount) => OddsAmount += amount;
}
