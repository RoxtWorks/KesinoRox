using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEngine;
using UnityEngine.UI;

// Play-mode QA driver for Three Pictures: rigs the deck and presses the real click handlers, e.g.
//   ThreePicturesQA.Step("100 0; 25 2; down 2; repeat; undo; clear", "9S TS TH | QS QH 9D | 6C TC KC")
//   bets: "<amount> <box 0-4>". Hands: one per bet box left to right, then the dealer LAST, cards like "9S", "TH", "KD", "AC".
public static class ThreePicturesQA
{
    const BindingFlags BF = BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Instance;

    static ThreePicturesBettingUIController Ctrl => UnityEngine.Object.FindObjectOfType<ThreePicturesBettingUIController>();
    static ThreePicturesGameManager Gm => UnityEngine.Object.FindObjectOfType<ThreePicturesGameManager>();
    static object F(object o, string name) => o.GetType().GetField(name, BF).GetValue(o);
    static ThreePicturesRound Round => (ThreePicturesRound)F(Ctrl, "currentRound");
    static void Call(string method, params object[] args) => typeof(ThreePicturesBettingUIController).GetMethod(method, BF).Invoke(Ctrl, args);

    public static string Bets(string bets)
    {
        var chipSel = (ChipSelectorUI)F(Gm, "chipSelector");
        foreach (var raw in bets.Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries))
        {
            var p = raw.Trim().Split(' ');
            if (p[0] == "clear") { Call("OnClearBetClicked"); continue; }
            if (p[0] == "repeat") { Call("OnRepeatBetClicked"); continue; }
            if (p[0] == "undo") { Call("UndoLastBetAction"); continue; }
            if (p[0] == "down") { Call("TakeDown", int.Parse(p[1])); continue; }
            typeof(ChipSelectorUI).GetField("<SelectedChip>k__BackingField", BF).SetValue(chipSel, long.Parse(p[0]));
            Call("OnSeatClicked", int.Parse(p[1]));
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

    // hands: "|"-separated, one per bet box in box order, dealer last. Builds the deal order card by card.
    public static string Deal(string hands)
    {
        var parts = hands.Split('|').Select(h => h.Trim().Split(' ').Select(ParseCard).ToArray()).ToArray();
        var order = new List<Card>();
        for (int card = 0; card < 3; card++)
            foreach (var h in parts) order.Add(h[card]);
        typeof(ThreePicturesRound).GetField("<Shoe>k__BackingField", BF).SetValue(Round, new Shoe(order));
        typeof(ThreePicturesRound).GetField("shuffleEachRound", BF).SetValue(Round, false);
        ((Button)F(Ctrl, "dealButton")).onClick.Invoke();
        return "dealing=" + F(Ctrl, "dealing");
    }

    public static string Step(string bets, string hands) => "BEFORE " + Bets(bets) + " | " + Deal(hands);

    public static string State()
    {
        var bankroll = (Bankroll)F(Gm, "bankroll");
        var bets = string.Join(" ", Enumerable.Range(0, ThreePicturesRound.MaxHands).Where(i => Round.GetBet(i) > 0).Select(i => $"H{i}={Round.GetBet(i)}"));
        var status = (Text)F(Ctrl, "statusText");
        return $"bal={bankroll.Balance} table={Round.TotalOnTable()} dealing={F(Ctrl, "dealing")} streak={F(Ctrl, "winStreak")} bets[{bets}] status='{status.text}'";
    }

    public static string History()
    {
        var recs = (List<ThreePicturesRoundRecord>)F(Gm, "sessionRecords");
        return string.Join(" | ", recs.Skip(Math.Max(0, recs.Count - 4)).Select(r =>
            $"#{r.RoundIndex + 1} dlr={(r.DealerRoyal ? "3P" : r.DealerPoint.ToString())} " +
            string.Join(",", r.Boxes.Select(b => b.Stake <= 0 ? "-" : $"{b.Stake}:{b.Outcome}{(b.HalfPay ? "½" : "")}")) +
            $" staked={r.TotalStaked} ret={r.TotalReturned} net={r.NetChange} bal={r.BalanceAfter}"));
    }
}
