using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEngine;
using UnityEngine.UI;

// Play-mode QA driver for Blackjack: rigs the shoe and presses the real click handlers, e.g.
//   BlackjackQA.Step("100", "8S 8H | 6D TC | 3S 2H TD")   then   BlackjackQA.Press("split") ... State()
//   bets: "<amount>" (click the circle with that chip), "down", "undo", "clear", "repeat".
//   cards: "player1 player2 | dealerUp dealerHole | cards drawn after that, in order". Cards like "9S", "TH", "AC".
//   actions: hit, stand, double, split, surrender, insyes, insno.
public static class BlackjackQA
{
    const BindingFlags BF = BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Instance;

    static BlackjackBettingUIController Ctrl => UnityEngine.Object.FindObjectOfType<BlackjackBettingUIController>();
    static BlackjackGameManager Gm => UnityEngine.Object.FindObjectOfType<BlackjackGameManager>();
    static object F(object o, string name) => o.GetType().GetField(name, BF).GetValue(o);
    static BlackjackRound Round => (BlackjackRound)F(Ctrl, "currentRound");
    static void Call(string method) => typeof(BlackjackBettingUIController).GetMethod(method, BF).Invoke(Ctrl, null);
    static void Click(string button) => ((Button)F(Ctrl, button)).onClick.Invoke();

    public static string Bets(string bets)
    {
        var chipSel = (ChipSelectorUI)F(Gm, "chipSelector");
        foreach (var raw in bets.Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries))
        {
            var p = raw.Trim();
            if (p == "clear") { Call("OnClearBetClicked"); continue; }
            if (p == "repeat") { Call("OnRepeatBetClicked"); continue; }
            if (p == "undo") { Call("UndoLastBetAction"); continue; }
            if (p == "down") { Call("TakeDownBet"); continue; }
            typeof(ChipSelectorUI).GetField("<SelectedChip>k__BackingField", BF).SetValue(chipSel, long.Parse(p));
            Call("OnBetSpotClicked");
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

    // Deal order is P D P D, then the rest in the order given; padded with small cards so the shoe never runs dry.
    public static string Deal(string cards)
    {
        var parts = cards.Split('|').Select(h => h.Trim().Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries).Select(ParseCard).ToList()).ToList();
        var order = new List<Card> { parts[0][0], parts[1][0], parts[0][1], parts[1][1] };
        if (parts.Count > 2) order.AddRange(parts[2]);
        for (int i = 0; i < 20; i++) order.Add(new Card(Rank.Two, Suit.Clubs));
        typeof(BlackjackBettingUIController).GetField("shoe", BF).SetValue(Ctrl, new Shoe(order));
        Click("dealButton");
        return "dealt";
    }

    public static string Step(string bets, string cards) => "BEFORE " + Bets(bets) + " | " + Deal(cards);

    public static string Press(string action)
    {
        switch (action)
        {
            case "hit": Click("hitButton"); break;
            case "stand": Click("standButton"); break;
            case "double": Click("doubleButton"); break;
            case "split": Click("splitButton"); break;
            case "surrender": Click("surrenderButton"); break;
            case "insyes": Call("OnInsuranceYes"); break;
            case "insno": Call("OnInsuranceNo"); break;
        }
        return State();
    }

    // Puts a real six-deck shoe back once QA is done.
    public static void RestoreShoe() =>
        typeof(BlackjackBettingUIController).GetField("shoe", BF).SetValue(Ctrl, F(Gm, "shoe"));

    public static string State()
    {
        var bankroll = (Bankroll)F(Gm, "bankroll");
        var status = (Text)F(Ctrl, "statusText");
        var spot = (Text)F(Ctrl, "betSpotText");
        string hands = Round == null ? "-" : string.Join(",", Round.PlayerHands.Select(h => $"{h.BestTotal}/{h.Bet}"));
        return $"bal={bankroll.Balance} pending={F(Ctrl, "pendingBet")} active={F(Ctrl, "roundActive")} hands={hands} " +
               $"dealer={(Round == null ? 0 : Round.Dealer.BestTotal)} spot='{spot.text.Replace("\n", " ")}' status='{status.text}'";
    }

    public static string History()
    {
        var recs = (List<BlackjackRoundRecord>)F(Gm, "sessionRecords");
        return string.Join(" | ", recs.Skip(Math.Max(0, recs.Count - 4)).Select(r =>
            $"#{r.RoundIndex + 1} staked={r.TotalStaked} ret={r.TotalReturned} net={r.NetChange} bal={r.BalanceAfter}"));
    }
}
