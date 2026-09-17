// A single Come bet in standard craps. Unparked (Point==null) = just placed,
// will establish its own point on the next roll (7/11 win, 2/3/12 lose,
// 4/5/6/8/9/10 travel). Parked = wins when its point repeats, loses on 7.
public class NormalComeWager
{
    public long Amount { get; }
    public long OddsAmount { get; private set; }
    public int? Point { get; private set; }

    public NormalComeWager(long amount) { Amount = amount; }

    public void SetPoint(int point) => Point = point;
    public void AddOdds(long amount) => OddsAmount += amount;
}
