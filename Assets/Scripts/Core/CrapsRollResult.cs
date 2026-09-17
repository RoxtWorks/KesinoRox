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
    public long CAndEReturn;

    // number -> amount paid. Place = winnings only (stake stays); Hardway = stake + winnings.
    public readonly Dictionary<int, long> PlaceHits = new Dictionary<int, long>();
    public readonly Dictionary<int, long> PlaceLosses = new Dictionary<int, long>();
    public readonly Dictionary<int, long> HardwayHits = new Dictionary<int, long>();

    // Come wagers that paid this roll (base win, or odds returned when a 7 kills a parked
    // bet on the come-out), and wagers that were traveling and just parked at a new point.
    public readonly Dictionary<ComeWager, long> ComeReturns = new Dictionary<ComeWager, long>();
    public readonly List<ComeWager> ComeParked = new List<ComeWager>();

    public bool PassResolved;
    public long PassReturn;

    public bool PointEstablishedThisRoll;
    public int? NewPoint;

    // True only on an actual seven-out (a 7 during the point phase) — that's the
    // one event that ends the whole shooter's turn. A come-out 7 pays Pass Line but
    // does NOT end the round; the shooter keeps rolling a fresh come-out.
    public bool RoundOver;
    public bool PlaceBetsCarriedOver;  // seven-out with BETS OFF — Place/Hardway stakes stay for next shooter

    // Lucky Roller (ATS)
    public long AtsLowsReturn;   // 0 or stake*31
    public long AtsHighsReturn;  // 0 or stake*31
    public long AtsAllReturn;    // 0 or stake*156
    public bool AtsSevenOut;     // any 7 → ATS run over, bets lost

    // All bet stakes consumed/resolved this roll (wins + losses).
    // net = TotalReturned - TotalStaked = true profit/loss per roll.
    public long TotalStaked;

    public long TotalReturned => FieldReturn + AnyCrapsReturn + AnySevenReturn + AnyElevenReturn + HornReturn + CAndEReturn
        + PlaceHits.Values.Sum() + HardwayHits.Values.Sum() + ComeReturns.Values.Sum() + PassReturn
        + AtsLowsReturn + AtsHighsReturn + AtsAllReturn;
}
