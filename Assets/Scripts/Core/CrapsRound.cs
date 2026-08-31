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

    public ComeWager PlaceComeBet(long amount)
    {
        var wager = new ComeWager(amount);
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

        // 1. Field — one-roll bet, resolves every roll regardless of phase, then
        // clears either way (win or lose) — it was never being cleared at all, so
        // the same stake kept re-resolving roll after roll instead of being a
        // single-roll bet, silently paying out (or losing) indefinitely until a
        // seven-out happened to wipe it via an unrelated code path.
        long fieldBet = GetBet(CrapsBetType.Field);
        if (fieldBet > 0)
        {
            result.FieldReturn = CrapsResolver.FieldPayout(fieldBet, total);
            bets[CrapsBetType.Field] = 0;
        }

        // 1b. One-roll proposition bets — Any Craps/Any Seven/Eleven/Horn, resolve
        // every roll regardless of phase, same as Field, and clear the same way.
        long anyCrapsBet = GetBet(CrapsBetType.AnyCraps);
        if (anyCrapsBet > 0) { result.AnyCrapsReturn = CrapsResolver.AnyCrapsPayout(anyCrapsBet, total); bets[CrapsBetType.AnyCraps] = 0; }
        long anySevenBet = GetBet(CrapsBetType.AnySeven);
        if (anySevenBet > 0) { result.AnySevenReturn = CrapsResolver.AnySevenPayout(anySevenBet, total); bets[CrapsBetType.AnySeven] = 0; }
        long anyElevenBet = GetBet(CrapsBetType.AnyEleven);
        if (anyElevenBet > 0) { result.AnyElevenReturn = CrapsResolver.AnyElevenPayout(anyElevenBet, total); bets[CrapsBetType.AnyEleven] = 0; }
        long hornBet = GetBet(CrapsBetType.Horn);
        if (hornBet > 0) { result.HornReturn = CrapsResolver.HornPayout(hornBet, total); bets[CrapsBetType.Horn] = 0; }

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

        // 3. Come bets — traveling wagers establish their own point on the next roll;
        // already-parked wagers win on a repeat or lose on a 7.
        foreach (var w in comeWagers.ToList())
        {
            if (w.Point == null)
            {
                if (total == 7)
                {
                    result.ComeReturns[w] = w.Amount * 2; // Come wins on come-out 7
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
                long ret = w.Amount * 2 + (w.OddsAmount > 0 ? CrapsResolver.OddsPayout(w.OddsAmount, w.Point.Value) : 0);
                result.ComeReturns[w] = ret;
                comeWagers.Remove(w);
            }
            else if (total == 7)
            {
                // Come bet parked at a point loses on seven-out — no return.
                comeWagers.Remove(w);
            }
        }

        // 5. Place bets — controlled entirely by PlaceBetsWorking (the BETS ON/OFF
        // toggle). When OFF, bets are dormant: a 7 does NOT clear them and a matching
        // number does NOT pay — the dealer physically marks them "off" and they sit
        // untouched until the player calls them back on. When ON, the dice result
        // applies normally (7 clears them, matching number pays). Auto-transitions
        // keep the default behavior correct: OFF on come-out, auto-ON when a point
        // is established, auto-OFF when a point is made (returning to come-out).
        if (PlaceBetsWorking)
        {
            foreach (var t in PlaceTypes)
            {
                long stake = GetBet(t);
                if (stake <= 0) continue;
                int num = PlaceNumber(t);
                if (total == 7)
                    bets[t] = 0;
                else if (total == num)
                    result.PlaceHits[num] = CrapsResolver.PlacePayout(stake, num);
            }
        }

        // 6. The main line. Crapless craps has no Don't Pass.
        if (Phase == CrapsPhase.ComeOut)
        {
            if (total == 7)
            {
                long passBet = GetBet(CrapsBetType.PassLine);
                result.PassReturn = passBet > 0 ? passBet * 2 : 0;
                result.PassResolved = passBet > 0;
                bets[CrapsBetType.PassLine] = 0;
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

                long passRet = 0;
                if (passBet > 0) passRet += passBet * 2;
                if (passOdds > 0) passRet += CrapsResolver.OddsPayout(passOdds, Point.Value);
                result.PassReturn = passRet;
                result.PassResolved = passBet > 0 || passOdds > 0;

                bets[CrapsBetType.PassLine] = 0;
                bets[CrapsBetType.PassOdds] = 0;
                Phase = CrapsPhase.ComeOut;
                Point = null;
            }
            else if (total == 7)
            {
                long passBet = GetBet(CrapsBetType.PassLine);
                long passOdds = GetBet(CrapsBetType.PassOdds);

                result.PassResolved = passBet > 0 || passOdds > 0;

                bets[CrapsBetType.PassLine] = 0;
                bets[CrapsBetType.PassOdds] = 0;
                Phase = CrapsPhase.ComeOut;
                Point = null;
                result.RoundOver = true;
                RoundOver = true;
            }
        }

        return result;
    }
}
