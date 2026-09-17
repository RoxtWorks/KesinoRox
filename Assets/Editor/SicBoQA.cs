using System;
using System.Linq;
using System.Reflection;
using UnityEngine;
using UnityEngine.UI;

// Play-mode QA driver for Sic Bo: rigs dice and presses the real click handlers, e.g.
//   SicBoQA.Step("100 Big; 25 Single4; down Big; repeat; undo; clear", 3, 4, 4)   then later   SicBoQA.State()
public static class SicBoQA
{
    const BindingFlags BF = BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Instance;

    static SicBoBettingUIController Ctrl => UnityEngine.Object.FindObjectOfType<SicBoBettingUIController>();
    static SicBoGameManager Gm => UnityEngine.Object.FindObjectOfType<SicBoGameManager>();
    static object F(object o, string name) => o.GetType().GetField(name, BF).GetValue(o);
    static SicBoRound Round => (SicBoRound)F(Ctrl, "currentRound");
    static void Call(string method, params object[] args) => typeof(SicBoBettingUIController).GetMethod(method, BF).Invoke(Ctrl, args);

    public static string Bets(string bets)
    {
        var chipSel = (ChipSelectorUI)F(Gm, "chipSelector");
        foreach (var raw in bets.Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries))
        {
            var p = raw.Trim().Split(' ');
            if (p[0] == "clear") { Call("OnClearBetClicked"); continue; }
            if (p[0] == "repeat") { Call("OnRepeatBetClicked"); continue; }
            if (p[0] == "undo") { Call("UndoLastBetAction"); continue; }
            if (p[0] == "down") { Call("TakeDown", Enum.Parse(typeof(SicBoBetType), p[1])); continue; }
            typeof(ChipSelectorUI).GetField("<SelectedChip>k__BackingField", BF).SetValue(chipSel, long.Parse(p[0]));
            Call("OnTileClicked", Enum.Parse(typeof(SicBoBetType), p[1]));
        }
        return State();
    }

    public static string Roll(int d1, int d2, int d3)
    {
        typeof(SicBoRound).GetField("rng", BF).SetValue(Round, new FixedDiceSource(d1, d2, d3));
        ((Button)F(Ctrl, "rollButton")).onClick.Invoke();
        return "rolling=" + F(Ctrl, "rolling");
    }

    public static string Step(string bets, int d1, int d2, int d3) => "BEFORE " + Bets(bets) + " | " + Roll(d1, d2, d3);

    public static string State()
    {
        var bankroll = (Bankroll)F(Gm, "bankroll");
        var bets = string.Join(" ", Enum.GetValues(typeof(SicBoBetType)).Cast<SicBoBetType>()
            .Where(t => Round.GetBet(t) > 0).Select(t => $"{t}={Round.GetBet(t)}"));
        var status = (Text)F(Ctrl, "statusText");
        return $"bal={bankroll.Balance} table={Round.TotalOnTable()} rolling={F(Ctrl, "rolling")} streak={F(Ctrl, "winStreak")} " +
               $"bets[{bets}] status='{status.text}'";
    }

    public static string History()
    {
        var recs = (System.Collections.Generic.List<SicBoRoundRecord>)F(Gm, "sessionRecords");
        return string.Join(" | ", recs.Skip(Math.Max(0, recs.Count - 5))
            .Select(r => $"#{r.RoundIndex + 1} {r.Die1}{r.Die2}{r.Die3} staked={r.TotalStaked} ret={r.TotalReturned} net={r.NetChange} bal={r.BalanceAfter}"));
    }
}
