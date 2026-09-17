using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Febucci.UI;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

// Three Pictures table (Malaysian no-commission rules): a blackjack-style felt with the dealer on top and
// up to five player boxes on an arc. Chips leave the wallet when placed (same as craps / Sic Bo); right-click
// a box to take its bet down. DEAL deals every box with a bet card by card, then settles the boxes left to right.
// Leaving the table mid-deal pays out every hand already decided and returns chips on the felt.
public class ThreePicturesBettingUIController : MonoBehaviour
{
    Bankroll bankroll;
    ChipSelectorUI chipSelector;
    SoundManager soundManager;
    JuiceManager juiceManager;
    FloatingTextUI floatingText;
    FloatingTextUI milestoneToast;
    Action<ThreePicturesRoundRecord> onRoundResolved;
    Action onBankrollChanged;

    ThreePicturesRound currentRound;
    ThreePicturesRoundResult dealtResult;
    readonly bool[] settled = new bool[ThreePicturesRound.MaxHands];
    bool dealing;
    int roundIndex;
    int winStreak;
    long bestRoundNet;
    bool doubledMilestoneFired;
    bool dealWasEnabled;
    readonly HashSet<int> roundMilestonesFired = new HashSet<int>();
    readonly long[] lastBets = new long[ThreePicturesRound.MaxHands];
    readonly List<long[]> undoStack = new List<long[]>();
    const int MaxUndoDepth = 10;

    Transform tableRoot;
    Text statusText;
    Button dealButton, clearBetButton, repeatButton, undoButton;
    Color dealBaseColor, clearBaseColor, repeatBaseColor;

    TextMeshProUGUI streakText;
    TextAnimator_TMP streakAnimator;
    GameObject streakBadgeGO;

    class Seat
    {
        public Vector2 Center;
        public GameObject BetCircle;
        public Image BetFrame;
        public Text EmptyLabel, AmountText, InfoText, ResultText;
        public readonly List<GameObject> ChipVisuals = new List<GameObject>();
        public readonly List<GameObject> Cards = new List<GameObject>();
    }

    readonly Seat[] seats = new Seat[ThreePicturesRound.MaxHands];
    readonly List<GameObject> dealerCards = new List<GameObject>();
    Text dealerInfoText;

    static readonly Color RailColor     = new Color(0.30f, 0.22f, 0.10f);
    static readonly Color FeltLine      = new Color(0.32f, 0.58f, 0.40f);
    static readonly Color TitleGold     = new Color(1f, 0.85f, 0.1f);
    static readonly Color FeltText      = new Color(0.82f, 0.86f, 0.80f);
    static readonly Color[] ChipColors  = { new Color(0.65f, 0.12f, 0.12f), new Color(0.1f, 0.35f, 0.6f), UIFactory.Chip500White };

    static readonly string[] WinFlavors  = { "Nice hands!", "There it is!", "Keep it going!", "Well played" };
    static readonly string[] LoseFlavors = { "Deal again", "Try again", "Onward", "Next hand's yours" };

    // Layout (canvas coordinates): felt fills the space left of the History column and below the action buttons
    static readonly Vector2 FeltCenter = new Vector2(-140f, -175f);
    static readonly Vector2 FeltSize = new Vector2(1580f, 700f);
    static readonly Vector2 CardSize = new Vector2(84f, 118f);
    const float CardGap = 8f;
    const float SeatSpacing = 300f;
    const float BetCircleSize = 120f;
    const float ChipSize = 46f;
    const float CardStepDelay = 0.18f, SettleStepDelay = 0.45f;

    public void Build(Transform canvas, Bankroll bankroll, ChipSelectorUI chipSelector, Shoe shoe,
        SoundManager soundManager, JuiceManager juiceManager, FloatingTextUI floatingText,
        FloatingTextUI milestoneToast, Action<ThreePicturesRoundRecord> onRoundResolved, Action onBankrollChanged)
    {
        this.bankroll = bankroll;
        this.chipSelector = chipSelector;
        this.soundManager = soundManager;
        this.juiceManager = juiceManager;
        this.floatingText = floatingText;
        this.milestoneToast = milestoneToast;
        this.onRoundResolved = onRoundResolved;
        this.onBankrollChanged = onBankrollChanged;

        currentRound = new ThreePicturesRound(shoe);

        var rootGO = new GameObject("ThreePicturesUIRoot");
        rootGO.transform.SetParent(canvas, false);
        var rt = rootGO.AddComponent<RectTransform>();
        rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 0.5f);
        rt.anchoredPosition = Vector2.zero;
        tableRoot = rootGO.transform;

        // Top band under the HUD: status bar, action buttons, right-click tip (same spots as Sic Bo)
        var statusPanelBg = UIFactory.MakePanel(tableRoot, "StatusPanelBg", new Vector2(0, 350), new Vector2(600, 40), UIFactory.PanelDark, shadow: false);
        UIFactory.AddSharpFrame(statusPanelBg, UIFactory.AccentDim, square: true);
        statusText = UIFactory.MakeText(tableRoot, "StatusText", new Vector2(0, 350), 19,
            sizeDelta: new Vector2(590, 36), color: UIFactory.Accent, style: FontStyle.Bold);
        statusText.text = "Place bets on 1 to 5 hands, then DEAL";

        BuildRulesCard();
        BuildFelt();
        BuildActionButtons();
        BuildTakeDownTip();
        BuildStreakBadge();
        RefreshAllSeats();
        RefreshActionButtons();
    }

    // --- Table ---

    // Hand ranking + payouts, readable at a glance, in the free space between the chips and the HUD
    void BuildRulesCard()
    {
        var center = new Vector2(-495f, 340f);
        var bg = UIFactory.MakePanel(tableRoot, "RulesCardBg", center, new Vector2(330f, 290f), UIFactory.PanelDark, shadow: false);
        UIFactory.AddSharpFrame(bg, UIFactory.AccentDim, square: true);
        (string text, bool header)[] lines =
        {
            ("HAND RANKING", true),
            ("1   Three pictures (J Q K)", false),
            ("2   Higher point  (9 is best)", false),
            ("3   Same point: more pictures", false),
            ("4   Otherwise: push", false),
            ("PAYOUTS", true),
            ("Win pays 1 to 1", false),
            ("Win with 6 pays 1 to 2", false),
            ("Tie with dealer: push", false),
        };
        for (int i = 0; i < lines.Length; i++)
        {
            var (text, header) = lines[i];
            UIFactory.MakeText(tableRoot, $"RuleLine{i}", center + new Vector2(0f, 120f - i * 30f), header ? 17 : 16,
                header ? TextAnchor.MiddleCenter : TextAnchor.MiddleLeft, new Vector2(290f, 28f),
                header ? TitleGold : FeltText, header ? FontStyle.Bold : FontStyle.Normal).text = text;
        }
    }

    void BuildFelt()
    {
        var rail = UIFactory.MakePanel(tableRoot, "FeltRail", FeltCenter, FeltSize + new Vector2(24f, 24f), RailColor);
        UIFactory.AddSharpFrame(rail, new Color(0.62f, 0.52f, 0.25f), square: true);
        UIFactory.MakePanel(tableRoot, "Felt", FeltCenter, FeltSize, UIFactory.FeltGreen, shadow: false);

        float top = FeltCenter.y + FeltSize.y / 2f;
        UIFactory.MakeText(tableRoot, "DealerLabel", new Vector2(FeltCenter.x, top - 26f), 18, TextAnchor.MiddleCenter,
            new Vector2(300f, 26f), TitleGold, FontStyle.Bold).text = "DEALER";
        dealerInfoText = UIFactory.MakeText(tableRoot, "DealerInfo", new Vector2(FeltCenter.x, top - 190f), 20, TextAnchor.MiddleCenter,
            new Vector2(420f, 28f), FeltText, FontStyle.Bold);
        dealerInfoText.text = "";

        UIFactory.MakeText(tableRoot, "FeltTitle", new Vector2(FeltCenter.x, top - 235f), 30, TextAnchor.MiddleCenter,
            new Vector2(700f, 38f), TitleGold, FontStyle.Bold).text = "THREE PICTURES";
        UIFactory.MakeText(tableRoot, "FeltRules", new Vector2(FeltCenter.x, top - 268f), 16, TextAnchor.MiddleCenter,
            new Vector2(900f, 24f), FeltText).text = "WIN PAYS 1 TO 1   ·   WIN WITH 6 PAYS 1 TO 2   ·   TIE IS A PUSH";

        for (int i = 0; i < ThreePicturesRound.MaxHands; i++)
        {
            int edge = Mathf.Abs(i - 2);
            var center = new Vector2(FeltCenter.x + (i - 2) * SeatSpacing, -335f + edge * edge * 28f);
            seats[i] = BuildSeat(i, center);
        }

        // Faint printed card outlines where every hand's cards land, so the empty felt still reads as a table
        for (int card = 0; card < 3; card++)
        {
            MakeCardOutline(DealerCardPos(card));
            for (int i = 0; i < ThreePicturesRound.MaxHands; i++) MakeCardOutline(SeatCardPos(i, card));
        }
    }

    void MakeCardOutline(Vector2 pos)
    {
        var go = new GameObject("CardOutline");
        go.transform.SetParent(tableRoot, false);
        var rt = go.AddComponent<RectTransform>();
        rt.sizeDelta = CardSize;
        rt.anchoredPosition = pos;
        var img = go.AddComponent<Image>();
        img.sprite = UIFactory.RoundedRect();
        img.type = Image.Type.Sliced;
        img.color = new Color(0f, 0f, 0f, 0.16f);
        img.raycastTarget = false;
        UIFactory.AddSharpFrame(go, new Color(FeltLine.r, FeltLine.g, FeltLine.b, 0.45f), square: true);
    }

    Seat BuildSeat(int box, Vector2 center)
    {
        var seat = new Seat { Center = center };

        seat.InfoText = UIFactory.MakeText(tableRoot, $"Seat{box}Info", center + new Vector2(0f, 8f), 17, TextAnchor.MiddleCenter,
            new Vector2(280f, 24f), FeltText, FontStyle.Bold);
        seat.ResultText = UIFactory.MakeText(tableRoot, $"Seat{box}Result", center + new Vector2(0f, -18f), 18, TextAnchor.MiddleCenter,
            new Vector2(280f, 26f), FeltText, FontStyle.Bold);

        var go = new GameObject($"Seat{box}Bet");
        go.transform.SetParent(tableRoot, false);
        var rt = go.AddComponent<RectTransform>();
        rt.sizeDelta = Vector2.one * BetCircleSize;
        rt.anchoredPosition = center + new Vector2(0f, -100f);
        var fill = go.AddComponent<Image>();
        fill.sprite = UIFactory.Circle();
        fill.color = new Color(0f, 0f, 0f, 0.22f);
        seat.BetFrame = UIFactory.AddSharpFrame(go, FeltLine, square: false);
        var btn = go.AddComponent<Button>();
        btn.targetGraphic = fill;
        btn.onClick.AddListener(() => OnSeatClicked(box));
        RightClickRelay.Attach(go, () => TakeDown(box));
        seat.BetCircle = go;

        seat.EmptyLabel = UIFactory.MakeText(go.transform, "Empty", Vector2.zero, 16, TextAnchor.MiddleCenter,
            new Vector2(100f, 50f), FeltText, FontStyle.Bold);
        seat.EmptyLabel.text = $"HAND\n{box + 1}";
        seat.AmountText = UIFactory.MakeText(go.transform, "Amount", new Vector2(0f, -40f), 17, TextAnchor.MiddleCenter,
            new Vector2(110f, 24f), UIFactory.TextLight, FontStyle.Bold);
        seat.AmountText.text = "";
        return seat;
    }

    Vector2 SeatCardPos(int box, int card) =>
        seats[box].Center + new Vector2((card - 1) * (CardSize.x + CardGap), 90f);

    Vector2 DealerCardPos(int card) =>
        new Vector2(FeltCenter.x + (card - 1) * (CardSize.x + CardGap), FeltCenter.y + FeltSize.y / 2f - 105f);

    GameObject DealCard(Card card, Vector2 pos)
    {
        var ui = CardUI.Create(tableRoot, pos, CardSize);
        foreach (var t in ui.GetComponentsInChildren<Text>()) t.fontSize = 30;
        ui.SetCard(card);
        soundManager?.PlayChip();
        return ui.gameObject;
    }

    void ClearCards()
    {
        foreach (var s in seats)
        {
            foreach (var c in s.Cards) Destroy(c);
            s.Cards.Clear();
            s.InfoText.text = "";
            s.ResultText.text = "";
            s.BetFrame.color = FeltLine;
        }
        foreach (var c in dealerCards) Destroy(c);
        dealerCards.Clear();
        dealerInfoText.text = "";
    }

    static string HandInfo(ThreePicturesHand h) =>
        h.IsRoyal ? "THREE PICTURES" : $"{h.Point} POINTS  ·  {h.PictureCount} PIC{(h.PictureCount == 1 ? "" : "S")}";

    // --- Chips ---

    // Stake shown on a box: the live bet, or while a deal is resolving, the stake still riding on that hand
    long SeatStake(int box)
    {
        if (dealtResult != null && !settled[box] && dealtResult.Boxes[box] != null) return dealtResult.Boxes[box].Stake;
        return currentRound.GetBet(box);
    }

    void RefreshSeat(int box)
    {
        var seat = seats[box];
        foreach (var go in seat.ChipVisuals) Destroy(go);
        seat.ChipVisuals.Clear();
        long amt = SeatStake(box);
        seat.EmptyLabel.gameObject.SetActive(amt <= 0);
        seat.AmountText.text = amt > 0 ? UIFactory.FormatMoney(amt) : "";
        if (amt <= 0) return;

        var colors = new List<Color>();
        long remaining = amt;
        var denoms = ChipDenominations.Values;
        for (int d = denoms.Length - 1; d >= 0 && colors.Count < 4; d--)
            while (remaining >= denoms[d] && colors.Count < 4)
            {
                remaining -= denoms[d];
                colors.Add(ChipColors[d]);
            }
        if (colors.Count == 0) colors.Add(ChipColors[0]);
        colors.Reverse();
        for (int i = 0; i < colors.Count; i++)
        {
            var go = new GameObject("Chip");
            go.transform.SetParent(seat.BetCircle.transform, false);
            var img = go.AddComponent<Image>();
            img.sprite = UIFactory.Circle();
            img.color = colors[i];
            img.raycastTarget = false;
            var edge = go.AddComponent<Outline>();
            edge.effectColor = new Color(0f, 0f, 0f, 0.8f);
            edge.effectDistance = new Vector2(1.5f, -1.5f);
            var crt = go.GetComponent<RectTransform>();
            crt.sizeDelta = Vector2.one * ChipSize;
            crt.anchoredPosition = new Vector2(0f, 10f + i * 4f);
            seat.ChipVisuals.Add(go);
        }
        seat.AmountText.transform.SetAsLastSibling();
    }

    void RefreshAllSeats()
    {
        for (int i = 0; i < seats.Length; i++) RefreshSeat(i);
    }

    // --- Bet actions ---

    long[] Snapshot()
    {
        var s = new long[ThreePicturesRound.MaxHands];
        for (int i = 0; i < s.Length; i++) s[i] = currentRound.GetBet(i);
        return s;
    }

    void PushUndoSnapshot()
    {
        undoStack.Add(Snapshot());
        if (undoStack.Count > MaxUndoDepth) undoStack.RemoveAt(0);
    }

    // Puts the felt back to exactly these bets; money moves between felt and wallet to match.
    void ApplyBets(long[] bets)
    {
        bankroll.Deposit(currentRound.TotalOnTable());
        currentRound.ClearAllBets();
        long total = bets.Sum();
        if (total > 0 && bankroll.TryWithdraw(total))
            for (int i = 0; i < bets.Length; i++) if (bets[i] > 0) currentRound.PlaceBet(i, bets[i]);
        RefreshAllSeats();
        onBankrollChanged?.Invoke();
    }

    // A fresh bet after a finished round clears the previous hands off the felt
    void ClearFinishedRound()
    {
        if (dealtResult == null) return;
        dealtResult = null;
        ClearCards();
    }

    void OnSeatClicked(int box)
    {
        if (dealing) return;
        long chip = chipSelector.SelectedChip;
        if (!bankroll.CanAfford(chip))
        {
            statusText.color = UIFactory.Accent;
            statusText.text = bankroll.Balance < ChipDenominations.Values[0]
                ? "Out of chips — use ADD FUNDS above to keep playing"
                : "Not enough balance for that bet";
            FlashBlocked();
            return;
        }
        ClearFinishedRound();
        PushUndoSnapshot();
        bankroll.TryWithdraw(chip);
        currentRound.PlaceBet(box, chip);
        soundManager?.PlayChip();
        JuiceTweens.Pulse(this, (RectTransform)seats[box].BetCircle.transform, peakScale: 1.1f, duration: 0.18f);
        RefreshSeat(box);
        onBankrollChanged?.Invoke();
        RefreshActionButtons();
    }

    void TakeDown(int box)
    {
        if (dealing) return;
        long amt = currentRound.GetBet(box);
        if (amt <= 0) return;
        PushUndoSnapshot();
        currentRound.ClearBet(box);
        bankroll.Deposit(amt);
        soundManager?.PlayClick();
        statusText.color = UIFactory.Accent;
        statusText.text = $"Hand {box + 1} down — {UIFactory.FormatMoney(amt)} back to wallet";
        RefreshSeat(box);
        onBankrollChanged?.Invoke();
        RefreshActionButtons();
    }

    void FlashBlocked() => juiceManager?.MicroShake(1.2f);

    void OnClearBetClicked()
    {
        if (dealing) return;
        if (currentRound.TotalOnTable() <= 0) { statusText.text = "Nothing to clear"; FlashBlocked(); return; }
        PushUndoSnapshot();
        ApplyBets(new long[ThreePicturesRound.MaxHands]);
        soundManager?.PlayClick();
        RefreshActionButtons();
    }

    void UndoLastBetAction()
    {
        if (dealing) return;
        if (undoStack.Count == 0) { statusText.text = "Nothing to undo"; FlashBlocked(); return; }
        var snap = undoStack[undoStack.Count - 1];
        undoStack.RemoveAt(undoStack.Count - 1);
        ApplyBets(snap);
        soundManager?.PlayClick();
        RefreshActionButtons();
    }

    // Re-places every bet from the last round on boxes that are empty now.
    void OnRepeatBetClicked()
    {
        if (dealing) return;
        if (lastBets.Sum() <= 0) { statusText.text = "No previous bets to repeat"; FlashBlocked(); return; }
        long cost = 0;
        for (int i = 0; i < lastBets.Length; i++) if (currentRound.GetBet(i) == 0) cost += lastBets[i];
        if (cost == 0) { statusText.text = "Last round's bets are already down"; FlashBlocked(); return; }
        if (!bankroll.CanAfford(cost))
        {
            statusText.color = UIFactory.Accent;
            statusText.text = $"Not enough balance to repeat ({UIFactory.FormatMoney(cost)})";
            FlashBlocked();
            return;
        }
        ClearFinishedRound();
        PushUndoSnapshot();
        bankroll.TryWithdraw(cost);
        for (int i = 0; i < lastBets.Length; i++)
            if (currentRound.GetBet(i) == 0 && lastBets[i] > 0) currentRound.PlaceBet(i, lastBets[i]);
        RefreshAllSeats();
        soundManager?.PlayChip();
        JuiceTweens.Pulse(this, repeatButton.GetComponent<RectTransform>(), peakScale: 1.15f, duration: 0.2f);
        onBankrollChanged?.Invoke();
        RefreshActionButtons();
    }

    public long OnTableTotal() => currentRound.TotalOnTable();

    // Leaving the table: pay every dealt hand not yet settled on screen, then return chips still on the felt.
    // Pure bankroll/round math only — safe from OnDestroy / OnApplicationQuit, and safe to call twice.
    public void RefundTableBets()
    {
        if (dealtResult != null)
            for (int i = 0; i < settled.Length; i++)
                if (!settled[i] && dealtResult.Boxes[i] != null)
                {
                    bankroll.Deposit(dealtResult.Boxes[i].Return);
                    settled[i] = true;
                }
        long onTable = currentRound.TotalOnTable();
        if (onTable > 0) bankroll.Deposit(onTable);
        currentRound.ClearAllBets();
    }

    // --- Deal ---

    void OnDealClicked()
    {
        if (dealing) return;
        if (currentRound.TotalOnTable() <= 0) { statusText.text = "Place a bet on at least one hand"; FlashBlocked(); return; }

        for (int i = 0; i < lastBets.Length; i++) lastBets[i] = currentRound.GetBet(i);
        undoStack.Clear();
        ClearCards();

        dealing = true;
        dealtResult = currentRound.Deal();
        for (int i = 0; i < settled.Length; i++) settled[i] = dealtResult.Boxes[i] == null;
        RefreshActionButtons();
        statusText.color = UIFactory.Accent;
        statusText.text = "Dealing...";
        StartCoroutine(DealSequence(dealtResult));
    }

    IEnumerator DealSequence(ThreePicturesRoundResult result)
    {
        // Card by card around the table: every active box, then the dealer
        for (int card = 0; card < 3; card++)
        {
            for (int box = 0; box < ThreePicturesRound.MaxHands; box++)
            {
                if (result.Boxes[box] == null) continue;
                seats[box].Cards.Add(DealCard(result.Boxes[box].Hand.Cards[card], SeatCardPos(box, card)));
                yield return new WaitForSeconds(CardStepDelay);
            }
            dealerCards.Add(DealCard(result.Dealer.Cards[card], DealerCardPos(card)));
            yield return new WaitForSeconds(CardStepDelay);
        }

        dealerInfoText.text = HandInfo(result.Dealer);
        dealerInfoText.color = result.Dealer.IsRoyal ? TitleGold : FeltText;
        yield return new WaitForSeconds(0.3f);

        // Settle the boxes left to right
        for (int box = 0; box < ThreePicturesRound.MaxHands; box++)
        {
            var b = result.Boxes[box];
            if (b == null) continue;
            SettleSeat(b);
            yield return new WaitForSeconds(SettleStepDelay);
        }

        try { FinishRound(result); }
        finally
        {
            dealing = false;
            RefreshActionButtons();
        }
    }

    void SettleSeat(ThreePicturesBoxResult b)
    {
        var seat = seats[b.Box];
        // RefundTableBets may already have paid this hand (leaving mid-deal) — never pay it twice
        if (!settled[b.Box])
        {
            settled[b.Box] = true;
            bankroll.Deposit(b.Return);
            onBankrollChanged?.Invoke();
        }

        seat.InfoText.text = HandInfo(b.Hand);
        seat.InfoText.color = b.Hand.IsRoyal ? TitleGold : FeltText;
        string net = UIFactory.FormatMoney(b.Net);
        switch (b.Outcome)
        {
            case ThreePicturesOutcome.PlayerWins:
                seat.ResultText.text = b.HalfPay ? $"WIN ON 6 · PAYS 1:2  +{net}" : $"WIN  +{net}";
                seat.ResultText.color = UIFactory.Positive;
                seat.BetFrame.color = TitleGold;
                soundManager?.PlayChip();
                break;
            case ThreePicturesOutcome.Tie:
                seat.ResultText.text = "PUSH";
                seat.ResultText.color = UIFactory.Accent;
                break;
            default:
                seat.ResultText.text = $"LOSE  {net}";
                seat.ResultText.color = UIFactory.Negative;
                seat.BetFrame.color = UIFactory.Negative;
                break;
        }
        JuiceTweens.Pulse(this, (RectTransform)seat.ResultText.transform, peakScale: 1.2f, duration: 0.25f);
        RefreshSeat(b.Box);
    }

    void FinishRound(ThreePicturesRoundResult result)
    {
        long staked = result.TotalStaked;
        long returned = result.TotalReturned;
        long net = returned - staked;
        int wins = result.Boxes.Count(b => b != null && b.Outcome == ThreePicturesOutcome.PlayerWins);
        int hands = result.Boxes.Count(b => b != null);

        string flavor = net > 0 ? WinFlavors[UnityEngine.Random.Range(0, WinFlavors.Length)]
            : net < 0 ? LoseFlavors[UnityEngine.Random.Range(0, LoseFlavors.Length)]
            : "Even round";
        statusText.color = net > 0 ? UIFactory.Positive : net < 0 ? UIFactory.Negative : UIFactory.Accent;
        statusText.text = $"Won {wins} of {hands}  ({(net >= 0 ? "+" : "")}{UIFactory.FormatMoney(net)})  — {flavor}";

        bool royalWin = result.Boxes.Any(b => b != null && b.Outcome == ThreePicturesOutcome.PlayerWins && b.Hand.IsRoyal);
        if (net > 0)
        {
            soundManager?.PlayWin();
            if (royalWin)
            {
                juiceManager?.Shake(0.6f, 4.5f); juiceManager?.Flash(new Color(1f, 0.85f, 0.2f, 0.25f), 0.7f);
                juiceManager?.PlayConfetti(2.5f); juiceManager?.PulseLight(0.9f, 0.7f);
                juiceManager?.PlayMoneyFountain(Vector2.zero);
                floatingText?.Show($"THREE PICTURES! +{UIFactory.FormatMoney(net)}", TitleGold, fontSize: 42);
            }
            else if (net >= ChipDenominations.Values[2])
            {
                juiceManager?.Shake(0.5f, 4f); juiceManager?.Flash(new Color(0.3f, 1f, 0.4f, 0.28f), 0.7f);
                juiceManager?.PlayConfetti(2f); juiceManager?.PulseLight(0.9f, 0.7f);
                juiceManager?.PlayMoneyFountain(Vector2.zero);
                floatingText?.Show($"HUGE WIN! +{UIFactory.FormatMoney(net)}", UIFactory.Positive, fontSize: 42);
            }
            else if (net >= ChipDenominations.Values[0] * 4L)
            {
                juiceManager?.Shake(0.3f, 2f); juiceManager?.Flash(new Color(0.25f, 0.9f, 0.35f, 0.18f), 0.5f);
                juiceManager?.PlayConfetti();
                floatingText?.Show($"+{UIFactory.FormatMoney(net)}", UIFactory.Positive);
            }
            else
            {
                juiceManager?.MicroShake(1.3f);
                juiceManager?.Flash(new Color(0.25f, 0.9f, 0.35f, 0.1f), 0.3f);
                floatingText?.Show($"+{UIFactory.FormatMoney(net)}", UIFactory.Positive);
            }
            winStreak++;
            if (!doubledMilestoneFired && bankroll.TotalFunded > 0 && bankroll.Balance >= bankroll.TotalFunded * 2)
            { doubledMilestoneFired = true; milestoneToast?.Show("BANKROLL DOUBLED!", UIFactory.Accent, fontSize: 30); }
            if (winStreak == 5 || winStreak == 10 || winStreak == 15 || winStreak == 20)
                milestoneToast?.Show($"{winStreak} WIN STREAK!", TitleGold, fontSize: 30);
            if (net > bestRoundNet && roundIndex >= 2)
            {
                bestRoundNet = net;
                milestoneToast?.Show($"BEST WIN: +{UIFactory.FormatMoney(net)}!", UIFactory.Positive, fontSize: 26);
            }
            else if (net > bestRoundNet) { bestRoundNet = net; }
        }
        else if (net < 0)
        {
            soundManager?.PlayLose();
            juiceManager?.Shake(0.2f, 1f); juiceManager?.Flash(new Color(0.85f, 0.2f, 0.2f, 0.14f), 0.4f);
            floatingText?.Show($"{UIFactory.FormatMoney(net)}", UIFactory.Negative);
            winStreak = 0;
        }
        else { soundManager?.PlayClick(); floatingText?.Show("EVEN", UIFactory.Accent); }

        bool showStreak = winStreak >= 2;
        streakAnimator?.SetText(showStreak ? $"<wave><rainb>{winStreak} WIN STREAK</rainb></wave>" : "");
        streakBadgeGO?.SetActive(showStreak);
        if (showStreak) JuiceTweens.Pulse(this, (RectTransform)streakBadgeGO.transform, peakScale: 1.15f, duration: 0.3f);

        var entries = new ThreePicturesRoundRecord.BoxEntry[ThreePicturesRound.MaxHands];
        foreach (var b in result.Boxes)
            if (b != null) entries[b.Box] = new ThreePicturesRoundRecord.BoxEntry { Stake = b.Stake, Outcome = b.Outcome, HalfPay = b.HalfPay };
        var record = new ThreePicturesRoundRecord(roundIndex, result.Dealer.Point, result.Dealer.IsRoyal, entries,
            staked, returned, bankroll.Balance);
        onRoundResolved?.Invoke(record);
        roundIndex++;
        int[] handTargets = { 50, 100, 250, 500, 1000 };
        foreach (var t in handTargets)
            if (roundIndex == t && roundMilestonesFired.Add(t))
                milestoneToast?.Show($"{t} Rounds This Session", UIFactory.Accent, fontSize: 26);
    }

    // --- Buttons / badges ---

    void BuildActionButtons()
    {
        const float y = 295f;
        clearBaseColor = UIFactory.RedBet;
        dealBaseColor  = UIFactory.Positive;
        repeatBaseColor = UIFactory.AccentDim;

        undoButton     = UIFactory.MakeButton(tableRoot, "UndoBtn",      new Vector2(-230f, y), new Vector2(110, 46), "UNDO",       UIFactory.AccentDim, UndoLastBetAction, 13, pixelFont: true);
        clearBetButton = UIFactory.MakeButton(tableRoot, "ClearBetBtn",  new Vector2(-105f, y), new Vector2(130, 46), "CLEAR BET",  clearBaseColor,  OnClearBetClicked, 13, pixelFont: true);
        dealButton     = UIFactory.MakeButton(tableRoot, "DealBtn",      new Vector2(  45f, y), new Vector2(150, 50), "DEAL",       dealBaseColor,   OnDealClicked, 20, pixelFont: true);
        repeatButton   = UIFactory.MakeButton(tableRoot, "RepeatBetBtn", new Vector2( 195f, y), new Vector2(130, 46), "REPEAT BET", repeatBaseColor, OnRepeatBetClicked, 12, pixelFont: true);
    }

    // One framed line under the action buttons
    void BuildTakeDownTip()
    {
        var tip = UIFactory.MakePanel(tableRoot, "TakeDownTipBg", new Vector2(0f, 238f), new Vector2(360f, 26f), UIFactory.PanelDarker, shadow: false);
        UIFactory.AddSharpFrame(tip, UIFactory.AccentDim, square: true);
        UIFactory.MakeText(tableRoot, "TakeDownTip", new Vector2(0f, 238f), 14, TextAnchor.MiddleCenter,
            new Vector2(350f, 24f), UIFactory.TextLight).text = "RIGHT-CLICK a bet to take it down";
    }

    // Slim badge under the HUD, only shown on a 2+ win streak
    void BuildStreakBadge()
    {
        streakBadgeGO = new GameObject("StreakBadge");
        streakBadgeGO.transform.SetParent(tableRoot, false);
        var rt = streakBadgeGO.AddComponent<RectTransform>();
        rt.sizeDelta = new Vector2(260, 36);
        rt.anchoredPosition = new Vector2(0, 395);
        UIFactory.MakeFramedPanel(streakBadgeGO.transform, "StreakBadgeBg", Vector2.zero, new Vector2(260, 36), Color.black);
        var textGO = new GameObject("StreakText");
        textGO.transform.SetParent(streakBadgeGO.transform, false);
        textGO.AddComponent<RectTransform>().sizeDelta = new Vector2(250, 32);
        streakText = textGO.AddComponent<TextMeshProUGUI>();
        streakText.alignment = TextAlignmentOptions.Center;
        streakText.fontStyle = FontStyles.Bold;
        streakText.raycastTarget = false;
        streakText.enableWordWrapping = false;
        streakText.fontSize = 20;
        streakText.outlineWidth = 0.25f;
        streakText.outlineColor = new Color32(0, 0, 0, 230);
        streakAnimator = textGO.AddComponent<TextAnimator_TMP>();
        streakBadgeGO.SetActive(false);
    }

    void RefreshActionButtons()
    {
        long onTable = currentRound.TotalOnTable();
        bool canDeal = !dealing && onTable > 0;
        UIFactory.SetButtonState(dealButton,     dealBaseColor,   canDeal);
        UIFactory.SetButtonState(clearBetButton, clearBaseColor,  !dealing && onTable > 0);
        UIFactory.SetButtonState(repeatButton,   repeatBaseColor, !dealing && lastBets.Sum() > 0);
        UIFactory.SetButtonState(undoButton,     UIFactory.AccentDim, !dealing && undoStack.Count > 0);
        if (canDeal && !dealWasEnabled) JuiceTweens.Pulse(this, dealButton.GetComponent<RectTransform>(), peakScale: 1.15f, duration: 0.25f);
        dealWasEnabled = canDeal;
    }

    public void SetRoundIndex(int index) => roundIndex = index;

    // Bankroll reset wipes the wallet, so chips on the felt are dropped rather than refunded.
    public void ResetRound()
    {
        StopAllCoroutines();
        dealing = false;
        dealtResult = null;
        winStreak = 0;
        bestRoundNet = 0;
        doubledMilestoneFired = false;
        roundMilestonesFired.Clear();
        streakBadgeGO?.SetActive(false);
        undoStack.Clear();
        Array.Clear(lastBets, 0, lastBets.Length);
        currentRound.ClearAllBets();
        ClearCards();
        RefreshAllSeats();
        statusText.color = UIFactory.Accent;
        statusText.text = "Place bets on 1 to 5 hands, then DEAL";
        RefreshActionButtons();
    }
}
