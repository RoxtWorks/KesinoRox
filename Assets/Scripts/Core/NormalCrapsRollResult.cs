using System.Collections.Generic;
using System.Linq;

// Everything that happened on one physical dice roll in standard craps.
// Presentation reads this to drive status text, bankroll updates, and juice.
public class NormalCrapsRollResult
{
    public int Die1;
    public int Die2;
    public int Total => Die1 + Die2;
    public bool IsHard => Die1 == Die2;

    // One-roll bets
    public long FieldReturn;
    public long AnyCrapsReturn;
    public long AnySevenReturn;
    public long AnyElevenReturn;
    public long HornReturn;

    // number -> payout (winnings only for place, stake+winnings for hardways)
    public readonly Dictionary<int, long> PlaceHits   = new Dictionary<int, long>();
    public readonly Dictionary<int, long> PlaceLosses = new Dictionary<int, long>(); // 7 killed a working place bet
    public readonly Dictionary<int, long> HardwayHits = new Dictionary<int, long>();
    // Lay bets: winnings only on 7-win (stake stays on table); stake lost on number-hit
    public readonly Dictionary<int, long> LayHits     = new Dictionary<int, long>();
    public readonly Dictionary<int, long> LayLosses   = new Dictionary<int, long>();

    // Pass Line
    public bool PassResolved;
    public long PassReturn;

    // Don't Pass
    public bool DontPassResolved;
    public long DontPassReturn;
    public bool DontPassPushed;  // 12 on come-out → push (stake returned, no win)

    // Point lifecycle
    public bool PointEstablishedThisRoll;
    public int? NewPoint;
    public bool RoundOver;             // seven-out in point phase only
    public bool PlaceBetsCarriedOver;  // seven-out with BETS OFF — place stakes stay for next shooter

    // Come bets that won (value = total returned)
    public readonly Dictionary<NormalComeWager, long> ComeReturns = new Dictionary<NormalComeWager, long>();
    // Come bets that just established their own point this roll
    public readonly List<NormalComeWager> ComeParked = new List<NormalComeWager>();
    // Come bets that crapped out (2/3/12 while unparked)
    public readonly List<NormalComeWager> ComeCrapsOut = new List<NormalComeWager>();

    // Don't Come wagers that resolved (7 before DC point = win, DC point before 7 = lose)
    public readonly Dictionary<NormalDontComeWager, long> DontComeReturns = new Dictionary<NormalDontComeWager, long>();
    // Don't Come wagers that just parked at a point
    public readonly List<NormalDontComeWager> DontComeParked = new List<NormalDontComeWager>();
    // Don't Come wagers that pushed on 12 while unparked
    public readonly List<NormalDontComeWager> DontComePushed = new List<NormalDontComeWager>();

    // ATS (All-Tall-Small / Lucky Roller)
    public long AtsLowsReturn;   // 0 or stake*31
    public long AtsHighsReturn;  // 0 or stake*31
    public long AtsAllReturn;    // 0 or stake*156
    public bool AtsSevenOut;     // 7 in point phase → ATS bets lost

    public long TotalReturned =>
        FieldReturn + AnyCrapsReturn + AnySevenReturn + AnyElevenReturn + HornReturn
        + PlaceHits.Values.Sum() + HardwayHits.Values.Sum()
        + LayHits.Values.Sum()
        + ComeReturns.Values.Sum() + PassReturn + DontPassReturn
        + DontComeReturns.Values.Sum()
        + AtsLowsReturn + AtsHighsReturn + AtsAllReturn;
}
