using System.Collections.Generic;
using System.Linq;

public class SessionHistory
{
    public List<SpinRecord> Records { get; } = new List<SpinRecord>();

    public void Add(SpinRecord record) => Records.Add(record);

    public int SpinCount => Records.Count;
    public int WinCount => Records.Count(r => r.NetChange > 0);
    public double WinRate => SpinCount == 0 ? 0 : (double)WinCount / SpinCount;
    public long NetProfit => Records.Count == 0 ? 0 : Records[Records.Count - 1].BalanceAfter - (Records[0].BalanceAfter - Records[0].NetChange);

    // Largest peak-to-trough drop in balance over the session.
    public long MaxDrawdown()
    {
        long peak = long.MinValue;
        long maxDrawdown = 0;
        foreach (var r in Records)
        {
            if (r.BalanceAfter > peak) peak = r.BalanceAfter;
            long drawdown = peak - r.BalanceAfter;
            if (drawdown > maxDrawdown) maxDrawdown = drawdown;
        }
        return maxDrawdown;
    }

    public bool Busted => Records.Count > 0 && Records[Records.Count - 1].BalanceAfter <= 0;
}
