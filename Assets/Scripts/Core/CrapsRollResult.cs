using System.Collections.Generic;
using System.Linq;

// Everything that happened on one physical dice roll — Presentation reads this to
// drive status text, juice, and bankroll (CrapsRound never touches Bankroll itself,
// same separation BaccaratRound/BlackjackRound use).
public class CrapsRollResult
{
    public int Die1;
    public int Die2;
    public int Total => Die1 + Die2;
    public bool IsHard => Die1 == Die2;

    public long FieldReturn;
    public long AnyCrapsReturn;
    public long AnySevenReturn;
    public long AnyElevenReturn;
    public long HornReturn;

    // number -> total returned, only entries that actually hit this roll.
    public readonly Dictionary<int, long> PlaceHits = new Dictionary<int, long>();
    public readonly Dictionary<int, long> HardwayHits = new Dictionary<int, long>();

    // Come wagers that resolved (won) this roll, and what they paid.
    public readonly Dictionary<ComeWager, long> ComeReturns = new Dictionary<ComeWager, long>();
    // Wagers that were traveling and just parked at a new point this roll (no payout).
    public readonly List<ComeWager> ComeParked = new List<ComeWager>();

    public bool PassResolved;
    public long PassReturn;

    public bool PointEstablishedThisRoll;
    public int? NewPoint;

    // True only on an actual seven-out (a 7 during the point phase) — that's the
    // one event that ends the whole shooter's turn. A come-out 7 pays Pass Line but
    // does NOT end the round; the shooter keeps rolling a fresh come-out.
    public bool RoundOver;

    public long TotalReturned => FieldReturn + AnyCrapsReturn + AnySevenReturn + AnyElevenReturn + HornReturn
        + PlaceHits.Values.Sum() + HardwayHits.Values.Sum() + ComeReturns.Values.Sum() + PassReturn;
}
