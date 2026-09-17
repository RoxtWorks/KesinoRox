using System.Collections.Generic;
using System.Linq;

public enum CrapsPhase { ComeOut, Point }

// One shooter's whole turn, from the first come-out roll until they seven-out. A
// point being made does NOT end the turn — the shooter keeps the dice and a fresh
// come-out begins immediately, so a single CrapsRound can contain several Pass Line
// cycles internally. RoundOver only flips true on an actual seven-out (a 7 rolled
// during the point phase). Never touches Bankroll — Roll() returns a CrapsRollResult
// describing every payout; the betting controller applies it to the bankroll itself,
// same split BaccaratRound/BlackjackRound already use.
// Crapless rules: every total except 7 is a point, the come-out never craps out,
// Place bets cover 2-12, and there are no Don't bets.
public class CrapsRound
{
    static readonly CrapsBetType[] PlaceTypes =
    {
        CrapsBetType.Place2, CrapsBetType.Place3, CrapsBetType.Place4, CrapsBetType.Place5, CrapsBetType.Place6,
        CrapsBetType.Place8, CrapsBetType.Place9, CrapsBetType.Place10, CrapsBetType.Place11, CrapsBetType.Place12
    };
    static readonly CrapsBetType[] HardTypes =
    {
        CrapsBetType.Hard4, CrapsBetType.Hard6, CrapsBetType.Hard8, CrapsBetType.Hard10
    };
    static readonly int[] AtsLowNums  = { 2, 3, 4, 5, 6 };
    static readonly int[] AtsHighNums = { 8, 9, 10, 11, 12 };

    readonly IRandomSource rng;
    readonly Dictionary<CrapsBetType, long> bets = new Dictionary<CrapsBetType, long>();
    readonly List<ComeWager> comeWagers = new List<ComeWager>();
    readonly HashSet<int> atsLowsCollected  = new HashSet<int>();
    readonly HashSet<int> atsHighsCollected = new HashSet<int>();

    public CrapsPhase Phase { get; private set; } = CrapsPhase.ComeOut;
    public int? Point { get; private set; }
    public bool RoundOver { get; private set; }
    public IReadOnlyList<ComeWager> ComeWagers => comeWagers;
    public IReadOnlyCollection<int> AtsLowsCollected  => atsLowsCollected;
    public IReadOnlyCollection<int> AtsHighsCollected => atsHighsCollected;

    // Player-controlled BETS ON/OFF toggle. Covers Place bets and Hardways; the game never flips it.
    public bool PlaceBetsWorking { get; set; }

    // Vegas Bonus Craps: Lucky Roller bets only go down before a run starts (no numbers
    // collected since the last 7) and only on a come-out roll.
    public bool CanPlaceAts => Phase == CrapsPhase.ComeOut
        && atsLowsCollected.Count == 0 && atsHighsCollected.Count == 0;

    public CrapsRound(IRandomSource rng)
    {
        this.rng = rng;
    }

    public long GetBet(CrapsBetType type) => bets.TryGetValue(type, out var v) ? v : 0;

    public void PlaceBet(CrapsBetType type, long amount) => bets[type] = GetBet(type) + amount;

    public void ClearBet(CrapsBetType type) => bets[type] = 0;

    public ComeWager PlaceComeBet(long amount)
    {
        var wager = new ComeWager(amount);
        comeWagers.Add(wager);
        return wager;
    }

    public void AddComeOdds(ComeWager wager, long amount) => wager.AddOdds(amount);

    // Lets the UI take down a Come bet that hasn't traveled yet (no point assigned) —
    // once it's parked it's a contract bet and can't be pulled back.
    public bool RemoveComeWager(ComeWager wager) => comeWagers.Remove(wager);

    static int PlaceNumber(CrapsBetType t) => t switch
    {
        CrapsBetType.Place2 => 2,
        CrapsBetType.Place3 => 3,
        CrapsBetType.Place4 => 4,
        CrapsBetType.Place5 => 5,
        CrapsBetType.Place6 => 6,
        CrapsBetType.Place8 => 8,
        CrapsBetType.Place9 => 9,
        CrapsBetType.Place10 => 10,
        CrapsBetType.Place11 => 11,
        CrapsBetType.Place12 => 12,
        _ => 0
    };

    static int HardNumber(CrapsBetType t) => t switch
    {
        CrapsBetType.Hard4 => 4,
        CrapsBetType.Hard6 => 6,
        CrapsBetType.Hard8 => 8,
        CrapsBetType.Hard10 => 10,
        _ => 0
    };

    public CrapsRollResult Roll()
    {
        var result = new CrapsRollResult
        {
            Die1 = rng.Next(1, 7),
            Die2 = rng.Next(1, 7)
        };
        int total = result.Total;

        // 1. Field + one-roll props — resolve every roll, stake always consumed.
        long fieldBet = GetBet(CrapsBetType.Field);
        if (fieldBet > 0) { result.FieldReturn = CrapsResolver.FieldPayout(fieldBet, total); bets[CrapsBetType.Field] = 0; result.TotalStaked += fieldBet; }
        long anyCrapsBet = GetBet(CrapsBetType.AnyCraps);
        if (anyCrapsBet > 0) { result.AnyCrapsReturn = CrapsResolver.AnyCrapsPayout(anyCrapsBet, total); bets[CrapsBetType.AnyCraps] = 0; result.TotalStaked += anyCrapsBet; }
        long anySevenBet = GetBet(CrapsBetType.AnySeven);
        if (anySevenBet > 0) { result.AnySevenReturn = CrapsResolver.AnySevenPayout(anySevenBet, total); bets[CrapsBetType.AnySeven] = 0; result.TotalStaked += anySevenBet; }
        long anyElevenBet = GetBet(CrapsBetType.AnyEleven);
        if (anyElevenBet > 0) { result.AnyElevenReturn = CrapsResolver.AnyElevenPayout(anyElevenBet, total); bets[CrapsBetType.AnyEleven] = 0; result.TotalStaked += anyElevenBet; }
        long hornBet = GetBet(CrapsBetType.Horn);
        if (hornBet > 0) { result.HornReturn = CrapsResolver.HornPayout(hornBet, total); bets[CrapsBetType.Horn] = 0; result.TotalStaked += hornBet; }
        long ceBet = GetBet(CrapsBetType.CAndE);
        if (ceBet > 0) { result.CAndEReturn = CrapsResolver.CAndEPayout(ceBet, total); bets[CrapsBetType.CAndE] = 0; result.TotalStaked += ceBet; }

        // 2. Hardways — lose on any 7 or the easy version, win on the hard version.
        // Like Place bets they only act while bets are working (Vegas: off on come-out by default).
        foreach (var t in HardTypes)
        {
            long stake = GetBet(t);
            if (stake <= 0 || !PlaceBetsWorking) continue;
            int num = HardNumber(t);
            if (total == 7) { result.TotalStaked += stake; bets[t] = 0; }
            else if (total == num)
            {
                if (result.IsHard) result.HardwayHits[num] = CrapsResolver.HardwayPayout(stake, num);
                result.TotalStaked += stake; // consumed whether won or lost the easy way
                bets[t] = 0;
            }
        }

        // 3. Lucky Roller (ATS) — Las Vegas Bonus Craps rules. Every roll counts (come-out
        // included). ANY 7 ends the run and loses all ATS bets. Numbers stay collected
        // until a 7 so a Small/Tall win doesn't wipe progress toward All.
        long atsLowBet  = GetBet(CrapsBetType.AtsLows);
        long atsHighBet = GetBet(CrapsBetType.AtsHighs);
        long atsAllBet  = GetBet(CrapsBetType.AtsAll);
        if (total == 7)
        {
            if (atsLowBet  > 0) { bets[CrapsBetType.AtsLows]  = 0; result.AtsSevenOut = true; result.TotalStaked += atsLowBet; }
            if (atsHighBet > 0) { bets[CrapsBetType.AtsHighs] = 0; result.AtsSevenOut = true; result.TotalStaked += atsHighBet; }
            if (atsAllBet  > 0) { bets[CrapsBetType.AtsAll]   = 0; result.AtsSevenOut = true; result.TotalStaked += atsAllBet; }
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
                bets[CrapsBetType.AtsAll] = 0;
            }
            if (atsLowBet > 0 && lowsComplete)
            {
                result.AtsLowsReturn = atsLowBet * 31; result.TotalStaked += atsLowBet;
                bets[CrapsBetType.AtsLows] = 0;
            }
            if (atsHighBet > 0 && highsComplete)
            {
                result.AtsHighsReturn = atsHighBet * 31; result.TotalStaked += atsHighBet;
                bets[CrapsBetType.AtsHighs] = 0;
            }
        }

        // 4. Come bets. Traveling: a 7 wins, any other total parks the bet at that point
        // (crapless — nothing craps out). Parked: its own number wins, any 7 loses.
        // Odds behind a parked Come bet are off on the come-out: returned, not lost or paid.
        foreach (var w in comeWagers.ToList())
        {
            if (w.Point == null)
            {
                if (total == 7)
                {
                    result.ComeReturns[w] = w.Amount * 2;
                    result.TotalStaked += w.Amount;
                    comeWagers.Remove(w);
                }
                else
                {
                    w.SetPoint(total);
                    result.ComeParked.Add(w);
                }
                continue;
            }

            bool oddsWorking = Phase == CrapsPhase.Point;
            if (total == 7)
            {
                if (!oddsWorking && w.OddsAmount > 0) result.ComeReturns[w] = w.OddsAmount;
                result.TotalStaked += w.Amount + w.OddsAmount;
                comeWagers.Remove(w);
            }
            else if (total == w.Point)
            {
                long oddsReturn = oddsWorking && w.OddsAmount > 0
                    ? CrapsResolver.OddsPayout(w.OddsAmount, w.Point.Value)
                    : w.OddsAmount;
                result.ComeReturns[w] = w.Amount * 2 + oddsReturn;
                result.TotalStaked += w.Amount + w.OddsAmount;
                comeWagers.Remove(w);
            }
        }

        // 5. Place bets — only act while bets are working. Stake stays on a win
        // (winnings only returned); consumed on a 7.
        if (PlaceBetsWorking)
        {
            foreach (var t in PlaceTypes)
            {
                long stake = GetBet(t);
                if (stake <= 0) continue;
                int num = PlaceNumber(t);
                if (total == 7) { result.PlaceLosses[num] = stake; result.TotalStaked += stake; bets[t] = 0; }
                else if (total == num) result.PlaceHits[num] = CrapsResolver.PlacePayout(stake, num);
            }
        }

        // 6. The main line. Crapless craps has no Don't Pass.
        long passBet = GetBet(CrapsBetType.PassLine);
        long passOdds = GetBet(CrapsBetType.PassOdds);
        if (Phase == CrapsPhase.ComeOut)
        {
            if (total == 7)
            {
                if (passBet > 0) { result.PassReturn = passBet * 2; result.PassResolved = true; result.TotalStaked += passBet; }
                bets[CrapsBetType.PassLine] = 0;
            }
            else
            {
                Point = total;
                Phase = CrapsPhase.Point;
                result.PointEstablishedThisRoll = true;
                result.NewPoint = total;
            }
        }
        else if (total == Point)
        {
            long passRet = 0;
            if (passBet > 0) passRet += passBet * 2;
            if (passOdds > 0) passRet += CrapsResolver.OddsPayout(passOdds, Point.Value);
            result.PassReturn = passRet;
            result.PassResolved = passBet > 0 || passOdds > 0;
            result.TotalStaked += passBet + passOdds;
            bets[CrapsBetType.PassLine] = 0;
            bets[CrapsBetType.PassOdds] = 0;
            Phase = CrapsPhase.ComeOut;
            Point = null;
        }
        else if (total == 7)
        {
            result.PassResolved = passBet > 0 || passOdds > 0;
            result.TotalStaked += passBet + passOdds;
            bets[CrapsBetType.PassLine] = 0;
            bets[CrapsBetType.PassOdds] = 0;

            // Working Place bets were already taken in step 5; off ones stay for the next shooter.
            if (!PlaceBetsWorking)
                result.PlaceBetsCarriedOver = PlaceTypes.Any(t => GetBet(t) > 0) || HardTypes.Any(t => GetBet(t) > 0);

            Phase = CrapsPhase.ComeOut;
            Point = null;
            result.RoundOver = true;
            RoundOver = true;
        }

        return result;
    }
}
