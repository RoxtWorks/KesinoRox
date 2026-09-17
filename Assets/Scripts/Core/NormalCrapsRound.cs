using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;

public enum NormalCrapsPhase { ComeOut, Point }

// One shooter's turn in STANDARD craps (not crapless). Differences from CrapsRound:
// - Come-out: 7/11 = Pass natural win, 2/3/12 = Pass craps loss, 4/5/6/8/9/10 = point
// - Don't Pass: 2/3 win on come-out, 12 pushes, 7/11 lose; point phase is mirror of Pass
// - Come: unparked 7/11=win, 2/3/12=craps-out, else establishes own point
// - Don't Come: unparked 2/3=win, 12=push, 7/11=lose, else parks at point
// - Place bets: 4/5/6/8/9/10 only (no 2/3/11/12)
// Never touches Bankroll — Roll() returns NormalCrapsRollResult; controller applies it.
public class NormalCrapsRound
{
    static readonly NormalCrapsBetType[] PlaceTypes =
    {
        NormalCrapsBetType.Place4, NormalCrapsBetType.Place5, NormalCrapsBetType.Place6,
        NormalCrapsBetType.Place8, NormalCrapsBetType.Place9, NormalCrapsBetType.Place10
    };
    static readonly NormalCrapsBetType[] LayTypes =
    {
        NormalCrapsBetType.Lay4, NormalCrapsBetType.Lay5, NormalCrapsBetType.Lay6,
        NormalCrapsBetType.Lay8, NormalCrapsBetType.Lay9, NormalCrapsBetType.Lay10
    };
    static readonly NormalCrapsBetType[] HardTypes =
    {
        NormalCrapsBetType.Hard4, NormalCrapsBetType.Hard6,
        NormalCrapsBetType.Hard8, NormalCrapsBetType.Hard10
    };
    static readonly int[] AtsLowNums  = { 2, 3, 4, 5, 6 };
    static readonly int[] AtsHighNums = { 8, 9, 10, 11, 12 };

    readonly IRandomSource rng;
    readonly Dictionary<NormalCrapsBetType, long> bets = new Dictionary<NormalCrapsBetType, long>();
    readonly List<NormalComeWager> comeWagers = new List<NormalComeWager>();
    readonly List<NormalDontComeWager> dontComeWagers = new List<NormalDontComeWager>();
    readonly HashSet<int> atsLowsCollected  = new HashSet<int>();
    readonly HashSet<int> atsHighsCollected = new HashSet<int>();

    public NormalCrapsPhase Phase { get; private set; } = NormalCrapsPhase.ComeOut;
    public int? Point { get; private set; }
    public bool RoundOver { get; private set; }
    public IReadOnlyList<NormalComeWager> ComeWagers => comeWagers;
    public IReadOnlyList<NormalDontComeWager> DontComeWagers => dontComeWagers;
    public IReadOnlyCollection<int> AtsLowsCollected  => atsLowsCollected;
    public IReadOnlyCollection<int> AtsHighsCollected => atsHighsCollected;

    // Player-controlled BETS ON/OFF toggle (same as crapless version).
    public bool PlaceBetsWorking { get; set; }

    public NormalCrapsRound(IRandomSource rng) { this.rng = rng; }

    public long GetBet(NormalCrapsBetType type) => bets.TryGetValue(type, out var v) ? v : 0;
    public void PlaceBet(NormalCrapsBetType type, long amount) => bets[type] = GetBet(type) + amount;
    public void ClearBet(NormalCrapsBetType type) => bets[type] = 0;

    public NormalComeWager PlaceComeBet(long amount)
    {
        var w = new NormalComeWager(amount);
        comeWagers.Add(w);
        return w;
    }
    public bool RemoveComeWager(NormalComeWager w) => comeWagers.Remove(w);
    public void AddComeOdds(NormalComeWager w, long amount) => w.AddOdds(amount);

    public NormalDontComeWager PlaceDontComeBet(long amount)
    {
        var w = new NormalDontComeWager(amount);
        dontComeWagers.Add(w);
        return w;
    }
    public bool RemoveDontComeWager(NormalDontComeWager w) => dontComeWagers.Remove(w);
    public void AddDontComeLayOdds(NormalDontComeWager w, long amount) => w.AddLayOdds(amount);

    static int PlaceNumber(NormalCrapsBetType t) => t switch
    {
        NormalCrapsBetType.Place4  => 4,
        NormalCrapsBetType.Place5  => 5,
        NormalCrapsBetType.Place6  => 6,
        NormalCrapsBetType.Place8  => 8,
        NormalCrapsBetType.Place9  => 9,
        NormalCrapsBetType.Place10 => 10,
        _ => 0
    };

    static int LayNumber(NormalCrapsBetType t) => t switch
    {
        NormalCrapsBetType.Lay4  => 4,
        NormalCrapsBetType.Lay5  => 5,
        NormalCrapsBetType.Lay6  => 6,
        NormalCrapsBetType.Lay8  => 8,
        NormalCrapsBetType.Lay9  => 9,
        NormalCrapsBetType.Lay10 => 10,
        _ => 0
    };

    static int HardNumber(NormalCrapsBetType t) => t switch
    {
        NormalCrapsBetType.Hard4  => 4,
        NormalCrapsBetType.Hard6  => 6,
        NormalCrapsBetType.Hard8  => 8,
        NormalCrapsBetType.Hard10 => 10,
        _ => 0
    };

    public NormalCrapsRollResult Roll()
    {
        var result = new NormalCrapsRollResult
        {
            Die1 = rng.Next(1, 7),
            Die2 = rng.Next(1, 7)
        };
        int total = result.Total;

        // 1. Field (one-roll — stake always consumed)
        long fieldBet = GetBet(NormalCrapsBetType.Field);
        if (fieldBet > 0) { result.FieldReturn = NormalCrapsResolver.FieldPayout(fieldBet, total); bets[NormalCrapsBetType.Field] = 0; result.TotalStaked += fieldBet; }

        // 2. One-roll props (stake always consumed)
        long anyCrapsBet = GetBet(NormalCrapsBetType.AnyCraps);
        if (anyCrapsBet > 0) { result.AnyCrapsReturn = NormalCrapsResolver.AnyCrapsPayout(anyCrapsBet, total); bets[NormalCrapsBetType.AnyCraps] = 0; result.TotalStaked += anyCrapsBet; }
        long anySevenBet = GetBet(NormalCrapsBetType.AnySeven);
        if (anySevenBet > 0) { result.AnySevenReturn = NormalCrapsResolver.AnySevenPayout(anySevenBet, total); bets[NormalCrapsBetType.AnySeven] = 0; result.TotalStaked += anySevenBet; }
        long anyElevenBet = GetBet(NormalCrapsBetType.AnyEleven);
        if (anyElevenBet > 0) { result.AnyElevenReturn = NormalCrapsResolver.AnyElevenPayout(anyElevenBet, total); bets[NormalCrapsBetType.AnyEleven] = 0; result.TotalStaked += anyElevenBet; }
        long hornBet = GetBet(NormalCrapsBetType.Horn);
        if (hornBet > 0) { result.HornReturn = NormalCrapsResolver.HornPayout(hornBet, total); bets[NormalCrapsBetType.Horn] = 0; result.TotalStaked += hornBet; }

        // 3. Hardways — lose on any 7 or easy version; win on hard version
        foreach (var t in HardTypes)
        {
            long stake = GetBet(t);
            if (stake <= 0) continue;
            int num = HardNumber(t);
            if (total == 7) { result.TotalStaked += stake; bets[t] = 0; }
            else if (total == num)
            {
                if (result.IsHard) result.HardwayHits[num] = NormalCrapsResolver.HardwayPayout(stake, num);
                result.TotalStaked += stake; // consumed whether win or easy-loss
                bets[t] = 0;
            }
        }

        // 3b. ATS (All-Tall-Small / Lucky Roller)
        long atsLowBet  = GetBet(NormalCrapsBetType.AtsLows);
        long atsHighBet = GetBet(NormalCrapsBetType.AtsHighs);
        long atsAllBet  = GetBet(NormalCrapsBetType.AtsAll);
        if (atsLowBet > 0 || atsHighBet > 0 || atsAllBet > 0)
        {
            if (total == 7)
            {
                if (Phase == NormalCrapsPhase.Point)
                {
                    // Seven-out: stakes consumed/lost
                    if (atsLowBet  > 0) { bets[NormalCrapsBetType.AtsLows]  = 0; result.AtsSevenOut = true; result.TotalStaked += atsLowBet; }
                    if (atsHighBet > 0) { bets[NormalCrapsBetType.AtsHighs] = 0; result.AtsSevenOut = true; result.TotalStaked += atsHighBet; }
                    if (atsAllBet  > 0) { bets[NormalCrapsBetType.AtsAll]   = 0; result.AtsSevenOut = true; result.TotalStaked += atsAllBet; }
                }
                atsLowsCollected.Clear();
                atsHighsCollected.Clear();
            }
            else
            {
                if (AtsLowNums.Contains(total))  atsLowsCollected.Add(total);
                if (AtsHighNums.Contains(total)) atsHighsCollected.Add(total);

                bool lowsComplete  = AtsLowNums.All(n => atsLowsCollected.Contains(n));
                bool highsComplete = AtsHighNums.All(n => atsHighsCollected.Contains(n));

                if (atsAllBet > 0 && lowsComplete && highsComplete)
                {
                    result.AtsAllReturn = atsAllBet * 156; result.TotalStaked += atsAllBet;
                    bets[NormalCrapsBetType.AtsAll] = 0;
                }
                if (atsLowBet > 0 && lowsComplete)
                {
                    result.AtsLowsReturn = atsLowBet * 31; result.TotalStaked += atsLowBet;
                    bets[NormalCrapsBetType.AtsLows] = 0;
                }
                if (atsHighBet > 0 && highsComplete)
                {
                    result.AtsHighsReturn = atsHighBet * 31; result.TotalStaked += atsHighBet;
                    bets[NormalCrapsBetType.AtsHighs] = 0;
                }
                if (lowsComplete)  atsLowsCollected.Clear();
                if (highsComplete) atsHighsCollected.Clear();
            }
        }

        // 4. Come wagers
        // Parked: 7 kills (even come-out), own point wins.
        // Unparked: 7/11 natural win, 2/3/12 craps loss, else park.
        foreach (var w in comeWagers.ToList())
        {
            if (w.Point != null)
            {
                bool oddsWorking = Phase == NormalCrapsPhase.Point;
                if (total == 7)
                {
                    // Base always lost; odds returned when off, consumed when on.
                    // Odds count as staked either way so a returned odds stake nets to zero.
                    if (!oddsWorking && w.OddsAmount > 0) result.ComeReturns[w] = w.OddsAmount;
                    result.TotalStaked += w.Amount + w.OddsAmount;
                    comeWagers.Remove(w);
                }
                else if (total == w.Point)
                {
                    long oddsReturn = oddsWorking && w.OddsAmount > 0
                        ? NormalCrapsResolver.OddsPayout(w.OddsAmount, w.Point.Value)
                        : w.OddsAmount;
                    result.ComeReturns[w] = w.Amount * 2 + oddsReturn;
                    result.TotalStaked += w.Amount + w.OddsAmount;
                    comeWagers.Remove(w);
                }
            }
            else
            {
                if (total == 7 || total == 11)
                {
                    result.ComeReturns[w] = w.Amount * 2;
                    result.TotalStaked += w.Amount;
                    comeWagers.Remove(w);
                }
                else if (total is 2 or 3 or 12)
                {
                    result.ComeCrapsOut.Add(w);
                    result.TotalStaked += w.Amount;
                    comeWagers.Remove(w);
                }
                else
                {
                    w.SetPoint(total);
                    result.ComeParked.Add(w);
                }
            }
        }

        // 5. Don't Come wagers
        // Unparked: 2/3 win, 12 push, 7/11 lose, else park.
        // Parked: 7 wins, own point loses.
        foreach (var w in dontComeWagers.ToList())
        {
            if (w.Point != null)
            {
                if (total == 7)
                {
                    long ret = w.Amount * 2 + (w.LayOddsAmount > 0 ? NormalCrapsResolver.LayOddsPayout(w.LayOddsAmount, w.Point.Value) : 0);
                    result.DontComeReturns[w] = ret;
                    result.TotalStaked += w.Amount + w.LayOddsAmount;
                    dontComeWagers.Remove(w);
                }
                else if (total == w.Point)
                {
                    result.TotalStaked += w.Amount + w.LayOddsAmount;
                    dontComeWagers.Remove(w); // DC point hit — DC loses
                }
            }
            else
            {
                if (total is 2 or 3)
                {
                    result.DontComeReturns[w] = w.Amount * 2;
                    result.TotalStaked += w.Amount;
                    dontComeWagers.Remove(w);
                }
                else if (total == 12)
                {
                    result.DontComePushed.Add(w);
                    result.TotalStaked += w.Amount; // push: stake returned, net = 0
                    dontComeWagers.Remove(w);
                }
                else if (total == 7 || total == 11)
                {
                    result.TotalStaked += w.Amount;
                    dontComeWagers.Remove(w); // DC loses on 7/11
                }
                else
                {
                    w.SetPoint(total);
                    result.DontComeParked.Add(w);
                }
            }
        }

        // 6. Place bets (only when PlaceBetsWorking)
        // Stake stays on table on win (winnings-only returned); consumed on 7.
        if (PlaceBetsWorking)
        {
            foreach (var t in PlaceTypes)
            {
                long stake = GetBet(t);
                if (stake <= 0) continue;
                int num = PlaceNumber(t);
                if (total == 7) { result.PlaceLosses[num] = stake; result.TotalStaked += stake; bets[t] = 0; }
                else if (total == num) result.PlaceHits[num] = NormalCrapsResolver.PlacePayout(stake, num);
            }

            // 6b. Lay bets — win on 7 (stake stays), lose stake on number hit
            foreach (var t in LayTypes)
            {
                long stake = GetBet(t);
                if (stake <= 0) continue;
                int num = LayNumber(t);
                if (total == 7) result.LayHits[num] = NormalCrapsResolver.LayBetPayout(stake, num);
                else if (total == num) { result.LayLosses[num] = stake; result.TotalStaked += stake; bets[t] = 0; }
            }
        }

        // 7. Main line: Pass / Don't Pass
        if (Phase == NormalCrapsPhase.ComeOut)
        {
            long passBet = GetBet(NormalCrapsBetType.PassLine);
            long dontBet = GetBet(NormalCrapsBetType.DontPass);

            if (total == 7 || total == 11)
            {
                // Natural: Pass wins, Don't Pass loses
                if (passBet > 0) { result.PassReturn = passBet * 2; result.PassResolved = true; result.TotalStaked += passBet; bets[NormalCrapsBetType.PassLine] = 0; }
                if (dontBet > 0) { result.TotalStaked += dontBet; result.DontPassReturn = 0; result.DontPassResolved = true; bets[NormalCrapsBetType.DontPass] = 0; bets[NormalCrapsBetType.DontPassOdds] = 0; }
            }
            else if (total is 2 or 3 or 12)
            {
                // Craps: Pass loses; Don't Pass wins on 2/3, pushes on 12
                if (passBet > 0) { result.TotalStaked += passBet; result.PassReturn = 0; result.PassResolved = true; bets[NormalCrapsBetType.PassLine] = 0; }
                if (dontBet > 0)
                {
                    if (total == 12) { result.DontPassReturn = dontBet; result.DontPassPushed = true; } // push: stake returned
                    else             { result.DontPassReturn = dontBet * 2; }
                    result.TotalStaked += dontBet;
                    result.DontPassResolved = true;
                    bets[NormalCrapsBetType.DontPass] = 0;
                    bets[NormalCrapsBetType.DontPassOdds] = 0;
                }
            }
            else
            {
                Point = total;
                Phase = NormalCrapsPhase.Point;
                result.PointEstablishedThisRoll = true;
                result.NewPoint = total;
            }
        }
        else // Point phase
        {
            long passBet      = GetBet(NormalCrapsBetType.PassLine);
            long passOdds     = GetBet(NormalCrapsBetType.PassOdds);
            long dontBet      = GetBet(NormalCrapsBetType.DontPass);
            long dontPassOdds = GetBet(NormalCrapsBetType.DontPassOdds);

            if (total == Point)
            {
                // Point repeats: Pass wins, Don't Pass loses
                long passRet = 0;
                if (passBet  > 0) passRet += passBet * 2;
                if (passOdds > 0) passRet += NormalCrapsResolver.OddsPayout(passOdds, Point.Value);
                if (passRet > 0 || passBet > 0) { result.PassReturn = passRet; result.PassResolved = true; }
                result.TotalStaked += passBet + passOdds;
                bets[NormalCrapsBetType.PassLine] = 0;
                bets[NormalCrapsBetType.PassOdds] = 0;

                if (dontBet > 0 || dontPassOdds > 0) { result.DontPassResolved = true; result.TotalStaked += dontBet + dontPassOdds; }
                bets[NormalCrapsBetType.DontPass]     = 0;
                bets[NormalCrapsBetType.DontPassOdds] = 0;

                Phase = NormalCrapsPhase.ComeOut;
                Point = null;
            }
            else if (total == 7)
            {
                // Seven-out: Pass loses, Don't Pass wins
                if (passBet > 0 || passOdds > 0) { result.PassResolved = true; result.TotalStaked += passBet + passOdds; }
                bets[NormalCrapsBetType.PassLine] = 0;
                bets[NormalCrapsBetType.PassOdds] = 0;

                long dontRet = 0;
                if (dontBet      > 0) dontRet += dontBet * 2;
                if (dontPassOdds > 0) dontRet += NormalCrapsResolver.LayOddsPayout(dontPassOdds, Point.Value);
                if (dontRet > 0 || dontBet > 0) { result.DontPassReturn = dontRet; result.DontPassResolved = true; result.TotalStaked += dontBet + dontPassOdds; }
                bets[NormalCrapsBetType.DontPass]     = 0;
                bets[NormalCrapsBetType.DontPassOdds] = 0;

                if (PlaceBetsWorking)
                    foreach (var t in PlaceTypes) bets[t] = 0;
                else
                    result.PlaceBetsCarriedOver = PlaceTypes.Any(t => GetBet(t) > 0)
                        || LayTypes.Any(t => GetBet(t) > 0);

                Phase = NormalCrapsPhase.ComeOut;
                Point = null;
                result.RoundOver = true;
                RoundOver = true;
            }
        }

        return result;
    }
}
