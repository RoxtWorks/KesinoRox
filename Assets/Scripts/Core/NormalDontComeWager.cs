// A single Don't Come bet in standard craps. Unparked = traveling: 2/3 wins,
// 12 pushes, 7/11 loses, 4/5/6/8/9/10 parks it at that number. Parked: wins
// when 7 rolls before its point, loses when its point rolls first.
// Lay odds behind a parked DC bet via LayOddsAmount.
public class NormalDontComeWager
{
    public long Amount { get; }
    public long LayOddsAmount { get; private set; }
    public int? Point { get; private set; }
    public bool Pushed { get; private set; }

    public NormalDontComeWager(long amount) { Amount = amount; }

    public void SetPoint(int point) => Point = point;
    public void SetPushed() => Pushed = true;
    public void AddLayOdds(long amount) => LayOddsAmount += amount;
}
