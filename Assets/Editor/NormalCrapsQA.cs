using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEngine;
using UnityEngine.UI;

// Play-mode QA driver for Normal Craps: rigs dice and presses the real click handlers,
// so MCP execute_code can run a scenario in one line, e.g.
//   NormalCrapsQA.Step("100 PassLine; 25 Place6", 4, 4)   then later   NormalCrapsQA.State()
public static class NormalCrapsQA
{
    const BindingFlags BF = BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Instance;

    static NormalCrapsBettingUIController Ctrl => UnityEngine.Object.FindObjectOfType<NormalCrapsBettingUIController>();
    static NormalCrapsGameManager Gm => UnityEngine.Object.FindObjectOfType<NormalCrapsGameManager>();
    static object F(object o, string name) => o.GetType().GetField(name, BF).GetValue(o);
    static NormalCrapsRound Round => (NormalCrapsRound)F(Ctrl, "currentRound");
    static void Call(string method, params object[] args) => typeof(NormalCrapsBettingUIController).GetMethod(method, BF).Invoke(Ctrl, args);

    // bets: "100 PassLine; 25 Place6; 25 Lay10; 5 Hard8; 10 CAndE; 100 Come; 50 AtsLows; down Place6; clear; repeat; undo"
    public static string Bets(string bets)
    {
        var chipSel = (ChipSelectorUI)F(Gm, "chipSelector");
        foreach (var raw in bets.Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries))
        {
            var p = raw.Trim().Split(' ');
            if (p[0] == "clear") { Call("OnClearBetClicked"); continue; }
            if (p[0] == "repeat") { Call("OnRepeatBetClicked"); continue; }
            if (p[0] == "undo") { Call("OnUndoClicked"); continue; }
            if (p[0] == "toggle") { Call("OnBetsToggleClicked"); continue; }
            if (p[0] == "down")
            {
                if (p[1] == "Come") Call("TakeDownUnparkedCome");
                else if (p[1] == "DontCome") Call("TakeDownUnparkedDontCome");
                else Call("TakeDown", Enum.Parse(typeof(NormalCrapsBetType), p[1]), p[1]);
                continue;
            }
            long amt = long.Parse(p[0]);
            string k = p[1];
            typeof(ChipSelectorUI).GetField("<SelectedChip>k__BackingField", BF).SetValue(chipSel, amt);
            if (k.StartsWith("Place")) Call("OnPlaceClicked", int.Parse(k.Substring(5)));
            else if (k.StartsWith("Lay")) Call("OnLayClicked", int.Parse(k.Substring(3)));
            else if (k.StartsWith("Hard")) Call("OnHardwayClicked", int.Parse(k.Substring(4)));
            else if (k is "AnyCraps" or "AnySeven" or "AnyEleven" or "Horn" or "CAndE")
                Call("OnPropClicked", Enum.Parse(typeof(NormalCrapsBetType), k));
            else Call("On" + k + "Clicked");
        }
        return State();
    }

    public static string Roll(int d1, int d2)
    {
        typeof(NormalCrapsRound).GetField("rng", BF).SetValue(Round, new FixedDiceSource(d1, d2));
        ((Button)F(Ctrl, "rollButton")).onClick.Invoke();
        return "rolling=" + F(Ctrl, "rolling");
    }

    public static string Step(string bets, int d1, int d2)
    {
        string s = Bets(bets);
        return "BEFORE " + s + " | " + Roll(d1, d2);
    }

    public static string Prompt(bool keepOn)
    {
        var go = GameObject.Find(keepOn ? "NCShooterKeepOn" : "NCShooterTurnOff");
        if (go == null) return "no prompt";
        go.GetComponent<Button>().onClick.Invoke();
        return State();
    }

    public static string Odds(bool take)
    {
        var skip = GameObject.Find("NCOddsSkip");
        if (skip == null) return "no odds modal";
        if (take)
        {
            Call("SetOddsModalAmount", (long)F(Ctrl, "oddsModalBaseAmount"));
            GameObject.Find("NCOddsConfirm").GetComponent<Button>().onClick.Invoke();
        }
        else skip.GetComponent<Button>().onClick.Invoke();
        return State();
    }

    public static string State()
    {
        var c = Ctrl; var r = Round; var gm = Gm;
        var br = (Bankroll)F(gm, "bankroll");
        var strip = F(gm, "resultsStrip");
        var entries = (IList)F(strip, "entries");
        string badge = "none";
        if (entries.Count > 0)
        {
            var e0 = entries[0];
            badge = e0.GetType().GetField("Label").GetValue(e0) + "(" + ColorName((Color)e0.GetType().GetField("Color").GetValue(e0)) + ")";
        }
        var hp = F(gm, "historyPanel");
        var recs = (List<NormalCrapsRoundRecord>)F(hp, "records");
        string hist = recs.Count == 0 ? "none" : Row(recs[recs.Count - 1]);
        var status = (Text)F(c, "statusText");
        var prompt = (GameObject)F(c, "shooterPromptRoot");
        string promptTxt = prompt.activeSelf
            ? ((Text)F(c, "shooterPromptTitleText")).text + " / " + ((Text)F(c, "shooterPromptBetsText")).text.Replace("\n", " ")
            : "-";
        var bets = string.Join(",", Enum.GetValues(typeof(NormalCrapsBetType)).Cast<NormalCrapsBetType>()
            .Where(t => r.GetBet(t) != 0).Select(t => t + "=" + r.GetBet(t)));
        var come = string.Join(",", r.ComeWagers.Select(w => $"C{w.Amount}@{w.Point}+o{w.OddsAmount}"))
                 + string.Join(",", r.DontComeWagers.Select(w => $"DC{w.Amount}@{w.Point}+lo{w.LayOddsAmount}"));
        return $"rolling={F(c, "rolling")} phase={r.Phase} pt={r.Point} work={r.PlaceBetsWorking} atsOpen={r.CanPlaceAts} " +
               $"bal={br.Balance} onTable={c.OnTableTotal()} streak={F(c, "winStreak")} " +
               $"status='{status.text}'({ColorName(status.color)}) badge={badge} hist=[{hist}] " +
               $"bets={bets} {come} prompt={promptTxt} oddsModal={(GameObject.Find("NCOddsSkip") != null ? ((Text)F(c, "oddsModalTitleText")).text : "-")}";
    }

    public static string Spot(string spotField, int key = 0)
    {
        object spot = key == 0 ? F(Ctrl, spotField) : ((IDictionary)F(Ctrl, spotField))[key];
        var amt = (Text)spot.GetType().GetField("AmountText").GetValue(spot);
        var chips = (IList)spot.GetType().GetField("ChipVisuals").GetValue(spot);
        return $"{spotField}[{key}] label='{amt.text}' chips={chips.Count}";
    }

    public static string History(int last = 6)
    {
        var recs = (List<NormalCrapsRoundRecord>)F(F(Gm, "historyPanel"), "records");
        return string.Join(" | ", recs.Skip(Math.Max(0, recs.Count - last)).Select(Row));
    }

    static string Row(NormalCrapsRoundRecord h) =>
        $"#{h.RoundIndex + 1} roll{h.RollTotal} pt{h.FinalPoint} table{h.TotalStaked} net{h.NetChange} bal{h.BalanceAfter}";

    static string ColorName(Color col)
    {
        if (col == UIFactory.Positive) return "GREEN";
        if (col == UIFactory.Negative) return "RED";
        if (col == UIFactory.Accent) return "NEUTRAL";
        return ColorUtility.ToHtmlStringRGB(col);
    }
}
