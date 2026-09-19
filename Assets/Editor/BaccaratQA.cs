using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEngine;
using UnityEngine.UI;

// Play-mode QA driver for Baccarat: rigs the shoe and presses the real click handlers, e.g.
//   BaccaratQA.Step("100 player; 25 tie", "9S 2H 3C KD")
//   bets: "<amount> player|banker|tie", "down player|banker|tie", "undo", "clear", "repeat".
//   cards: in draw order (P B P B, then third cards). Cards like "9S", "TH", "AC".
public static class BaccaratQA
{
    const BindingFlags BF = BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Instance;

    static BaccaratBettingUIController Ctrl => UnityEngine.Object.FindObjectOfType<BaccaratBettingUIController>();
    static BaccaratGameManager Gm => UnityEngine.Object.FindObjectOfType<BaccaratGameManager>();
    static object F(object o, string name) => o.GetType().GetField(name, BF).GetValue(o);
    static void Call(string method, params object[] args) => typeof(BaccaratBettingUIController).GetMethod(method, BF).Invoke(Ctrl, args);
    static BaccaratBetType Spot(string s) => s == "banker" ? BaccaratBetType.Banker : s == "tie" ? BaccaratBetType.Tie : BaccaratBetType.Player;

    public static string Bets(string bets)
    {
        var chipSel = (ChipSelectorUI)F(Gm, "chipSelector");
        foreach (var raw in bets.Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries))
        {
            var p = raw.Trim().Split(' ');
            if (p[0] == "clear") { Call("OnClearBetClicked"); continue; }
            if (p[0] == "repeat") { Call("OnRepeatBetClicked"); continue; }
            if (p[0] == "undo") { Call("UndoLastBetAction"); continue; }
            if (p[0] == "down") { Call("TakeDown", Spot(p[1])); continue; }
            typeof(ChipSelectorUI).GetField("<SelectedChip>k__BackingField", BF).SetValue(chipSel, long.Parse(p[0]));
            Call("OnSpotClicked", Spot(p[1]));
        }
        return State();
    }

    static Card ParseCard(string s)
    {
        Rank r = s[0] switch
        {
            'A' => Rank.Ace, 'K' => Rank.King, 'Q' => Rank.Queen, 'J' => Rank.Jack, 'T' => Rank.Ten,
            _ => (Rank)(s[0] - '0')
        };
        Suit su = s[1] switch { 'H' => Suit.Hearts, 'D' => Suit.Diamonds, 'C' => Suit.Clubs, _ => Suit.Spades };
        return new Card(r, su);
    }

    // Cards in draw order, padded so the shoe never runs dry.
    public static string Deal(string cards)
    {
        var order = cards.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries).Select(ParseCard).ToList();
        for (int i = 0; i < 10; i++) order.Add(new Card(Rank.Ten, Suit.Clubs));
        typeof(BaccaratBettingUIController).GetField("shoe", BF).SetValue(Ctrl, new Shoe(order));
        ((Button)F(Ctrl, "dealButton")).onClick.Invoke();
        return "dealt";
    }

    public static string Step(string bets, string cards) => "BEFORE " + Bets(bets) + " | " + Deal(cards);

    // Puts the real eight-deck shoe back once QA is done.
    public static void RestoreShoe() =>
        typeof(BaccaratBettingUIController).GetField("shoe", BF).SetValue(Ctrl, F(Gm, "shoe"));

    public static string State()
    {
        var bankroll = (Bankroll)F(Gm, "bankroll");
        var status = (Text)F(Ctrl, "statusText");
        var pending = (Dictionary<BaccaratBetType, long>)F(Ctrl, "pendingBets");
        var round = (Dictionary<BaccaratBetType, long>)F(Ctrl, "roundBets");
        string Fmt(Dictionary<BaccaratBetType, long> d) => string.Join("/", d.Values);
        return $"bal={bankroll.Balance} pending(P/B/T)={Fmt(pending)} round={Fmt(round)} active={F(Ctrl, "roundActive")} status='{status.text}'";
    }

    public static string History()
    {
        var recs = (List<BaccaratRoundRecord>)F(Gm, "sessionRecords");
        return string.Join(" | ", recs.Skip(Math.Max(0, recs.Count - 4)).Select(r =>
            $"#{r.RoundIndex + 1} P{r.PlayerPoint} B{r.BankerPoint} {r.Outcome} staked={r.TotalStaked} ret={r.TotalReturned} net={r.NetChange} bal={r.BalanceAfter}"));
    }
}
