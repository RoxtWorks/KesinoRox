using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEngine;
using UnityEngine.UI;

// Play-mode QA driver for Three Card Poker: rigs the deck and presses the real click handlers, e.g.
//   ThreeCardPokerQA.Step("25 ante; 25 pp", "9S 9H 2C | QD 4H 2S")   then   ThreeCardPokerQA.Play()  or  .Fold()
//   bets: "<amount> ante|pp", "down ante|pp", "undo", "clear", "repeat". Hands: player | dealer, cards like "9S", "TH", "AC".
public static class ThreeCardPokerQA
{
    const BindingFlags BF = BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Instance;

    static ThreeCardPokerBettingUIController Ctrl => UnityEngine.Object.FindObjectOfType<ThreeCardPokerBettingUIController>();
    static ThreeCardPokerGameManager Gm => UnityEngine.Object.FindObjectOfType<ThreeCardPokerGameManager>();
    static object F(object o, string name) => o.GetType().GetField(name, BF).GetValue(o);
    static ThreeCardPokerRound Round => (ThreeCardPokerRound)F(Ctrl, "currentRound");
    static void Call(string method, params object[] args) => typeof(ThreeCardPokerBettingUIController).GetMethod(method, BF).Invoke(Ctrl, args);
    static ThreeCardPokerBetType Spot(string s) => s == "pp" ? ThreeCardPokerBetType.PairPlus : ThreeCardPokerBetType.Ante;

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

    // hands: "player | dealer". Deal order is P D P D P D.
    public static string Deal(string hands)
    {
        var parts = hands.Split('|').Select(h => h.Trim().Split(' ').Select(ParseCard).ToArray()).ToArray();
        var order = new List<Card>();
        for (int c = 0; c < 3; c++) { order.Add(parts[0][c]); order.Add(parts[1][c]); }
        typeof(ThreeCardPokerRound).GetField("<Shoe>k__BackingField", BF).SetValue(Round, new Shoe(order));
        typeof(ThreeCardPokerRound).GetField("shuffleEachHand", BF).SetValue(Round, false);
        ((Button)F(Ctrl, "dealButton")).onClick.Invoke();
        return "phase=" + F(Ctrl, "phase");
    }

    public static string Step(string bets, string hands) => "BEFORE " + Bets(bets) + " | " + Deal(hands);
    public static string Play() { ((Button)F(Ctrl, "playButton")).onClick.Invoke(); return State(); }
    public static string Fold() { ((Button)F(Ctrl, "foldButton")).onClick.Invoke(); return State(); }

    public static string State()
    {
        var bankroll = (Bankroll)F(Gm, "bankroll");
        var status = (Text)F(Ctrl, "statusText");
        return $"bal={bankroll.Balance} ante={Round.GetBet(ThreeCardPokerBetType.Ante)} pp={Round.GetBet(ThreeCardPokerBetType.PairPlus)} " +
               $"phase={F(Ctrl, "phase")} streak={F(Ctrl, "winStreak")} status='{status.text}'";
    }

    public static string History()
    {
        var recs = (List<ThreeCardPokerRoundRecord>)F(Gm, "sessionRecords");
        return string.Join(" | ", recs.Skip(Math.Max(0, recs.Count - 4)).Select(r =>
            $"#{r.RoundIndex + 1} {r.PlayerRank} vs {r.DealerRank} {r.Outcome}{(r.Folded ? " FOLD" : "")} staked={r.TotalStaked} ret={r.TotalReturned} net={r.NetChange} bal={r.BalanceAfter}"));
    }
}
