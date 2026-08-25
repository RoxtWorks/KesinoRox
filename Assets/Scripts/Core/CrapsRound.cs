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

    readonly IRandomSource rng;
    readonly Dictionary<CrapsBetType, long> bets = new Dictionary<CrapsBetType, long>();
    readonly List<ComeWager> comeWagers = new List<ComeWager>();

    public CrapsPhase Phase { get; private set; } = CrapsPhase.ComeOut;
    public int? Point { get; private set; }
    public bool RoundOver { get; private set; }
    public IReadOnlyList<ComeWager> ComeWagers => comeWagers;

    // Real bubble-craps machines let the player force Place bets "on" during the
    // come-out roll instead of the standard off-by-default house rule — a "BETS
    // ON/OFF" toggle. Defaults off (matching the original behavior); the UI flips it.
    public bool PlaceBetsWorking { get; set; }

    public CrapsRound(IRandomSource rng)
    {
        this.rng = rng;
    }

    public long GetBet(CrapsBetType type) => bets.TryGetValue(type, out var v) ? v : 0;

    public void PlaceBet(CrapsBetType type, long amount) => bets[type] = GetBet(type) + amount;

    public void ClearBet(CrapsBetType type) => bets[type] = 0;

    public ComeWager PlaceComeBet(bool isDontCome, long amount)
    {
        var wager = new ComeWager(isDontCome, amount);
        comeWagers.Add(wager);
        return wager;
    }

    public void AddComeOdds(ComeWager wager, long amount) => wager.AddOdds(amount);

    // Lets the UI undo a just-placed Come/Don't Come bet that hasn't traveled yet
    // (no point assigned) — once it's parked or resolved it's a contract bet like
    // everything else and can't be pulled back.
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

        // 1. Field — one-roll bet, resolves every roll regardless of phase.
        long fieldBet = GetBet(CrapsBetType.Field);
        if (fieldBet > 0)
            result.FieldReturn = CrapsResolver.FieldPayout(fieldBet, total);

        // 1b. One-roll proposition bets — Any Craps/Any Seven/Eleven/Horn, resolve
        // every roll regardless of phase, same as Field.
        long anyCrapsBet = GetBet(CrapsBetType.AnyCraps);
        if (anyCrapsBet > 0) result.AnyCrapsReturn = CrapsResolver.AnyCrapsPayout(anyCrapsBet, total);
        long anySevenBet = GetBet(CrapsBetType.AnySeven);
        if (anySevenBet > 0) result.AnySevenReturn = CrapsResolver.AnySevenPayout(anySevenBet, total);
        long anyElevenBet = GetBet(CrapsBetType.AnyEleven);
        if (anyElevenBet > 0) result.AnyElevenReturn = CrapsResolver.AnyElevenPayout(anyElevenBet, total);
        long hornBet = GetBet(CrapsBetType.Horn);
        if (hornBet > 0) result.HornReturn = CrapsResolver.HornPayout(hornBet, total);

        // 2. Hardways — always working (both phases), lose on ANY 7.
        foreach (var t in HardTypes)
        {
            long stake = GetBet(t);
            if (stake <= 0) continue;
            int num = HardNumber(t);
            if (total == 7)
            {
                bets[t] = 0;
            }
            else if (total == num)
            {
                if (result.IsHard)
                    result.HardwayHits[num] = CrapsResolver.HardwayPayout(stake, num);
                bets[t] = 0; // resolves either way (hit or easy-way loss) — re-bet fresh next time
            }
        }

        // 3 & 4. Come / Don't Come — traveling wagers resolve/park on their
        // establishing roll; already-parked wagers resolve on a repeat or a 7.
        foreach (var w in comeWagers.ToList())
        {
            if (w.Point == null)
            {
                if (total == 7)
                {
                    long ret = w.IsDontCome ? 0 : w.Amount * 2;
                    if (ret > 0) result.ComeReturns[w] = ret;
                    comeWagers.Remove(w);
                }
                else
                {
                    w.SetPoint(total);
                    result.ComeParked.Add(w);
                }
            }
            else if (total == w.Point)
            {
                long ret = w.IsDontCome ? 0
                    : w.Amount * 2 + (w.OddsAmount > 0 ? CrapsResolver.OddsPayout(w.OddsAmount, w.Point.Value) : 0);
                if (ret > 0) result.ComeReturns[w] = ret;
                comeWagers.Remove(w);
            }
            else if (total == 7)
            {
                long ret = w.IsDontCome
                    ? w.Amount * 2 + (w.OddsAmount > 0 ? CrapsResolver.LayOddsPayout(w.OddsAmount, w.Point.Value) : 0)
                    : 0;
                if (ret > 0) result.ComeReturns[w] = ret;
                comeWagers.Remove(w);
            }
        }

        // 5. Place bets — "working" during the point phase, or anytime the player
        // has forced bets on via PlaceBetsWorking (real bubble-craps machines offer
        // this "BETS ON/OFF" toggle instead of the standard off-on-come-out rule).
        // Repeat hits keep paying without clearing the bet. Only cleared by a real
        // seven-out (Phase == Point when the 7 lands) — a come-out 7 with bets
        // forced on must NOT clear them, it's not a seven-out, the shooter's turn
        // isn't over.
        if (Phase == CrapsPhase.Point || PlaceBetsWorking)
        {
            foreach (var t in PlaceTypes)
            {
                long stake = GetBet(t);
                if (stake <= 0) continue;
                int num = PlaceNumber(t);
                if (total == 7)
                {
                    if (Phase == CrapsPhase.Point) bets[t] = 0;
                }
                else if (total == num)
                {
                    result.PlaceHits[num] = CrapsResolver.PlacePayout(stake, num);
                }
            }
        }

        // 6. The main line.
        if (Phase == CrapsPhase.ComeOut)
        {
            if (total == 7)
            {
                long passBet = GetBet(CrapsBetType.PassLine);
                long dontBet = GetBet(CrapsBetType.DontPass);
                result.PassReturn = passBet > 0 ? passBet * 2 : 0;
                result.PassResolved = passBet > 0;
                result.DontPassResolved = dontBet > 0;
                bets[CrapsBetType.PassLine] = 0;
                bets[CrapsBetType.DontPass] = 0;
                // Phase stays ComeOut, Point stays null — shooter keeps rolling.
            }
            else
            {
                Point = total;
                Phase = CrapsPhase.Point;
                result.PointEstablishedThisRoll = true;
                result.NewPoint = total;
            }
        }
        else // Point phase
        {
            if (total == Point)
            {
                long passBet = GetBet(CrapsBetType.PassLine);
                long passOdds = GetBet(CrapsBetType.PassOdds);
                long dontBet = GetBet(CrapsBetType.DontPass);
                long dontOdds = GetBet(CrapsBetType.DontPassOdds);

                long passRet = 0;
                if (passBet > 0) passRet += passBet * 2;
                if (passOdds > 0) passRet += CrapsResolver.OddsPayout(passOdds, Point.Value);
                result.PassReturn = passRet;
                result.PassResolved = passBet > 0 || passOdds > 0;
                result.DontPassResolved = dontBet > 0 || dontOdds > 0;

                bets[CrapsBetType.PassLine] = 0;
                bets[CrapsBetType.PassOdds] = 0;
                bets[CrapsBetType.DontPass] = 0;
                bets[CrapsBetType.DontPassOdds] = 0;
                Phase = CrapsPhase.ComeOut;
                Point = null;
            }
            else if (total == 7)
            {
                long passBet = GetBet(CrapsBetType.PassLine);
                long passOdds = GetBet(CrapsBetType.PassOdds);
                long dontBet = GetBet(CrapsBetType.DontPass);
                long dontOdds = GetBet(CrapsBetType.DontPassOdds);

                result.PassResolved = passBet > 0 || passOdds > 0;

                long dontRet = 0;
                if (dontBet > 0) dontRet += dontBet * 2;
                if (dontOdds > 0) dontRet += CrapsResolver.LayOddsPayout(dontOdds, Point.Value);
                result.DontPassReturn = dontRet;
                result.DontPassResolved = dontBet > 0 || dontOdds > 0;

                bets[CrapsBetType.PassLine] = 0;
                bets[CrapsBetType.PassOdds] = 0;
                bets[CrapsBetType.DontPass] = 0;
                bets[CrapsBetType.DontPassOdds] = 0;
                Phase = CrapsPhase.ComeOut;
                Point = null;
                result.RoundOver = true;
                RoundOver = true;
            }
        }

        return result;
    }
}
