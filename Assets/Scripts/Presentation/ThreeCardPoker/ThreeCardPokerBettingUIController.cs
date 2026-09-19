using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Febucci.UI;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

// Three Card Poker table (Las Vegas rules), one hand against the dealer.
// Bet ANTE (and optional PAIR PLUS) → DEAL → the dealer's cards stay face down → PLAY (match the Ante) or FOLD.
// Chips leave the wallet when placed (same as craps / Sic Bo / Three Pictures); right-click a spot to take its bet down.
// Leaving mid-hand counts as a fold: the Ante is lost and Pair Plus is settled on your cards.
public class ThreeCardPokerBettingUIController : MonoBehaviour
{
    Bankroll bankroll;
    ChipSelectorUI chipSelector;
    SoundManager soundManager;
    JuiceManager juiceManager;
    FloatingTextUI floatingText;
    FloatingTextUI milestoneToast;
    Action<ThreeCardPokerRoundRecord> onRoundResolved;
    Action onBankrollChanged;

    enum Phase { Betting, Dealing, Deciding, Revealing }
    Phase phase = Phase.Betting;

    ThreeCardPokerRound currentRound;
    ThreeCardPokerRoundResult pendingResult;   // decided but not yet paid (dealer reveal still animating)
    long settledPlay;                            // Play stake shown on the felt after calling
    bool handOnFelt;                             // last hand's cards/results still showing
    int roundIndex;
    int winStreak;
    long bestRoundNet;
    bool doubledMilestoneFired;
    bool dealWasEnabled;
    readonly HashSet<int> roundMilestonesFired = new HashSet<int>();
    long lastAnte, lastPairPlus;
    readonly List<(long ante, long pp)> undoStack = new List<(long ante, long pp)>();
    const int MaxUndoDepth = 10;

    Transform tableRoot;
    Text statusText;
    Button dealButton, clearBetButton, repeatButton, undoButton, playButton, foldButton;
    Color dealBaseColor, clearBaseColor, repeatBaseColor;

    TextMeshProUGUI streakText;
    TextAnimator_TMP streakAnimator;
    GameObject streakBadgeGO;

    class Spot
    {
        public ThreeCardPokerBetType Type;
        public GameObject Circle;
        public Image Frame;
        public Text EmptyLabel, AmountText, ResultText;
        public readonly List<GameObject> ChipVisuals = new List<GameObject>();
    }

    readonly Dictionary<ThreeCardPokerBetType, Spot> spots = new Dictionary<ThreeCardPokerBetType, Spot>();
    readonly List<GameObject> playerCards = new List<GameObject>();
    readonly List<CardUI> dealerCards = new List<CardUI>();
    Text playerInfoText, dealerInfoText, dealerQualifyText;

    static readonly Color RailColor   = new Color(0.30f, 0.22f, 0.10f);
    static readonly Color FeltLine    = new Color(0.32f, 0.58f, 0.40f);
    static readonly Color TitleGold   = new Color(1f, 0.85f, 0.1f);
    static readonly Color FeltText    = new Color(0.82f, 0.86f, 0.80f);
    static readonly Color[] ChipColors = { new Color(0.65f, 0.12f, 0.12f), new Color(0.1f, 0.35f, 0.6f), UIFactory.Chip500White };

    static readonly string[] WinFlavors  = { "Nice hand!", "There it is!", "Keep it going!", "Well played" };
    static readonly string[] LoseFlavors = { "Deal again", "Try again", "Onward", "Next hand's yours" };

    // Layout (canvas coordinates): felt fills the space left of the History column and below the action buttons
    static readonly Vector2 FeltCenter = new Vector2(-140f, -175f);
    static readonly Vector2 FeltSize = new Vector2(1580f, 700f);
    static readonly Vector2 DealerCardSize = new Vector2(88f, 124f);
    static readonly Vector2 PlayerCardSize = new Vector2(104f, 146f);
    const float BetCircleSize = 130f;
    const float SpotSpacing = 250f;
    const float ChipSize = 48f;
    const float CardStepDelay = 0.22f;

    public void Build(Transform canvas, Bankroll bankroll, ChipSelectorUI chipSelector, Shoe shoe,
        SoundManager soundManager, JuiceManager juiceManager, FloatingTextUI floatingText,
        FloatingTextUI milestoneToast, Action<ThreeCardPokerRoundRecord> onRoundResolved, Action onBankrollChanged)
    {
        this.bankroll = bankroll;
        this.chipSelector = chipSelector;
        this.soundManager = soundManager;
        this.juiceManager = juiceManager;
        this.floatingText = floatingText;
        this.milestoneToast = milestoneToast;
        this.onRoundResolved = onRoundResolved;
        this.onBankrollChanged = onBankrollChanged;

        currentRound = new ThreeCardPokerRound(shoe);

        var rootGO = new GameObject("ThreeCardPokerUIRoot");
        rootGO.transform.SetParent(canvas, false);
        var rt = rootGO.AddComponent<RectTransform>();
        rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 0.5f);
        rt.anchoredPosition = Vector2.zero;
        tableRoot = rootGO.transform;

        // Top band under the HUD: status bar, action buttons, right-click tip (same spots as the other games)
        var statusPanelBg = UIFactory.MakePanel(tableRoot, "StatusPanelBg", new Vector2(0, 350), new Vector2(600, 40), UIFactory.PanelDark, shadow: false);
        UIFactory.AddSharpFrame(statusPanelBg, UIFactory.AccentDim, square: true);
        statusText = UIFactory.MakeText(tableRoot, "StatusText", new Vector2(0, 350), 19,
            sizeDelta: new Vector2(590, 36), color: UIFactory.Accent, style: FontStyle.Bold);
        statusText.text = "Bet the ANTE (and PAIR PLUS if you like), then DEAL";

        BuildRulesCard();
        BuildFelt();
        BuildActionButtons();
        BuildTakeDownTip();
        BuildStreakBadge();
        RefreshAllSpots();
        RefreshActionButtons();
    }

    // --- Table ---

    // Ranking + pay tables at a glance, in the free space between the chips and the HUD
    void BuildRulesCard()
    {
        var center = new Vector2(-495f, 340f);
        var bg = UIFactory.MakePanel(tableRoot, "RulesCardBg", center, new Vector2(330f, 290f), UIFactory.PanelDark, shadow: false);
        UIFactory.AddSharpFrame(bg, UIFactory.AccentDim, square: true);
        (string text, bool header)[] lines =
        {
            ("HAND RANKING", true),
            ("Straight flush > Three of a kind", false),
            ("> Straight > Flush > Pair > High", false),
            ("A-K-Q highest straight, A-2-3 lowest", false),
            ("ANTE BONUS  (when you PLAY)", true),
            ("Str flush 5  ·  Trips 4  ·  Straight 1", false),
            ("PAIR PLUS  (even if you fold)", true),
            ("SF 40 · Trips 25 · Str 5 · Flush 4 · Pair 1", false),
            ("Dealer needs Queen high to play", false),
        };
        for (int i = 0; i < lines.Length; i++)
        {
            var (text, header) = lines[i];
            UIFactory.MakeText(tableRoot, $"RuleLine{i}", center + new Vector2(0f, 120f - i * 30f), header ? 16 : 15,
                TextAnchor.MiddleCenter, new Vector2(310f, 28f),
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
        dealerInfoText = UIFactory.MakeText(tableRoot, "DealerInfo", new Vector2(FeltCenter.x, top - 185f), 20, TextAnchor.MiddleCenter,
            new Vector2(500f, 28f), FeltText, FontStyle.Bold);
        dealerQualifyText = UIFactory.MakeText(tableRoot, "DealerQualify", new Vector2(FeltCenter.x, top - 212f), 16, TextAnchor.MiddleCenter,
            new Vector2(500f, 24f), FeltText, FontStyle.Bold);

        UIFactory.MakeText(tableRoot, "FeltTitle", new Vector2(FeltCenter.x, top - 255f), 28, TextAnchor.MiddleCenter,
            new Vector2(800f, 36f), TitleGold, FontStyle.Bold).text = "THREE CARD POKER";
        UIFactory.MakeText(tableRoot, "FeltRule", new Vector2(FeltCenter.x, top - 285f), 16, TextAnchor.MiddleCenter,
            new Vector2(900f, 24f), FeltText).text = "DEALER PLAYS WITH QUEEN HIGH OR BETTER";

        for (int c = 0; c < 3; c++)
        {
            MakeCardOutline(DealerCardPos(c), DealerCardSize);
            MakeCardOutline(PlayerCardPos(c), PlayerCardSize);
        }
        // Your hand name sits left of your cards, mirroring PLAY / FOLD on the right
        playerInfoText = UIFactory.MakeText(tableRoot, "PlayerInfo", new Vector2(FeltCenter.x - 330f, -225f), 24, TextAnchor.MiddleCenter,
            new Vector2(260f, 60f), FeltText, FontStyle.Bold);

        BuildSpot(ThreeCardPokerBetType.PairPlus, new Vector2(FeltCenter.x - SpotSpacing, -425f), "PAIR PLUS");
        BuildSpot(ThreeCardPokerBetType.Ante,     new Vector2(FeltCenter.x,               -425f), "ANTE");
        BuildSpot(ThreeCardPokerBetType.Play,     new Vector2(FeltCenter.x + SpotSpacing, -425f), "PLAY");

        // PLAY / FOLD sit right next to your cards while you decide
        playButton = UIFactory.MakeButton(tableRoot, "PlayBtn", new Vector2(FeltCenter.x + 330f, -195f), new Vector2(190, 58), "PLAY",
            UIFactory.Positive, OnPlayClicked, 20, pixelFont: true);
        foldButton = UIFactory.MakeButton(tableRoot, "FoldBtn", new Vector2(FeltCenter.x + 330f, -265f), new Vector2(190, 50), "FOLD",
            UIFactory.RedBet, OnFoldClicked, 16, pixelFont: true);
    }

    Vector2 DealerCardPos(int card) =>
        new Vector2(FeltCenter.x + (card - 1) * (DealerCardSize.x + 8f), FeltCenter.y + FeltSize.y / 2f - 105f);

    Vector2 PlayerCardPos(int card) => new Vector2(FeltCenter.x + (card - 1) * (PlayerCardSize.x + 10f), -225f);

    void MakeCardOutline(Vector2 pos, Vector2 size)
    {
        var go = new GameObject("CardOutline");
        go.transform.SetParent(tableRoot, false);
        var rt = go.AddComponent<RectTransform>();
        rt.sizeDelta = size;
        rt.anchoredPosition = pos;
        var img = go.AddComponent<Image>();
        img.sprite = UIFactory.RoundedRect();
        img.type = Image.Type.Sliced;
        img.color = new Color(0f, 0f, 0f, 0.16f);
        img.raycastTarget = false;
        UIFactory.AddSharpFrame(go, new Color(FeltLine.r, FeltLine.g, FeltLine.b, 0.45f), square: true);
    }

    void BuildSpot(ThreeCardPokerBetType type, Vector2 center, string label)
    {
        var spot = new Spot { Type = type };
        var go = new GameObject($"Spot_{type}");
        go.transform.SetParent(tableRoot, false);
        var rt = go.AddComponent<RectTransform>();
        rt.sizeDelta = Vector2.one * BetCircleSize;
        rt.anchoredPosition = center;
        var fill = go.AddComponent<Image>();
        fill.sprite = UIFactory.Circle();
        fill.color = new Color(0f, 0f, 0f, 0.22f);
        spot.Frame = UIFactory.AddSharpFrame(go, FeltLine, square: false);
        var btn = go.AddComponent<Button>();
        btn.targetGraphic = fill;
        btn.onClick.AddListener(() => OnSpotClicked(type));
        RightClickRelay.Attach(go, () => TakeDown(type));
        spot.Circle = go;

        spot.EmptyLabel = UIFactory.MakeText(go.transform, "Label", Vector2.zero, 17, TextAnchor.MiddleCenter,
            new Vector2(120f, 50f), FeltText, FontStyle.Bold);
        spot.EmptyLabel.text = label.Replace(" ", "\n");
        UIFactory.MakeText(tableRoot, $"{type}Title", center + new Vector2(0f, 80f), 15, TextAnchor.MiddleCenter,
            new Vector2(200f, 22f), TitleGold, FontStyle.Bold).text = label;
        spot.AmountText = UIFactory.MakeText(go.transform, "Amount", new Vector2(0f, -44f), 17, TextAnchor.MiddleCenter,
            new Vector2(120f, 24f), UIFactory.TextLight, FontStyle.Bold);
        spot.ResultText = UIFactory.MakeText(tableRoot, $"{type}Result", center + new Vector2(0f, -82f), 16, TextAnchor.MiddleCenter,
            new Vector2(240f, 24f), FeltText, FontStyle.Bold);
        spots[type] = spot;
    }

    GameObject DealCard(Card card, Vector2 pos, Vector2 size, int fontSize)
    {
        var ui = CardUI.Create(tableRoot, pos, size);
        foreach (var t in ui.GetComponentsInChildren<Text>()) t.fontSize = fontSize;
        ui.SetCard(card);
        soundManager?.PlayChip();
        return ui.gameObject;
    }

    void ClearHand()
    {
        foreach (var c in playerCards) Destroy(c);
        playerCards.Clear();
        foreach (var c in dealerCards) Destroy(c.gameObject);
        dealerCards.Clear();
        playerInfoText.text = "";
        dealerInfoText.text = "";
        dealerQualifyText.text = "";
        foreach (var s in spots.Values) { s.ResultText.text = ""; s.Frame.color = FeltLine; }
        settledPlay = 0;
        handOnFelt = false;
    }

    // --- Chips ---

    long SpotStake(ThreeCardPokerBetType type) =>
        type == ThreeCardPokerBetType.Play && settledPlay > 0 ? settledPlay : currentRound.GetBet(type);

    void RefreshSpot(Spot spot)
    {
        foreach (var go in spot.ChipVisuals) Destroy(go);
        spot.ChipVisuals.Clear();
        long amt = SpotStake(spot.Type);
        spot.EmptyLabel.gameObject.SetActive(amt <= 0);
        spot.AmountText.text = amt > 0 ? UIFactory.FormatMoney(amt) : "";
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
            go.transform.SetParent(spot.Circle.transform, false);
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
            spot.ChipVisuals.Add(go);
        }
        spot.AmountText.transform.SetAsLastSibling();
    }

    void RefreshAllSpots()
    {
        foreach (var s in spots.Values) RefreshSpot(s);
    }

    // --- Bet actions (betting phase only) ---

    void PushUndoSnapshot()
    {
        undoStack.Add((currentRound.GetBet(ThreeCardPokerBetType.Ante), currentRound.GetBet(ThreeCardPokerBetType.PairPlus)));
        if (undoStack.Count > MaxUndoDepth) undoStack.RemoveAt(0);
    }

    // Puts the felt back to exactly these bets; money moves between felt and wallet to match.
    void ApplyBets(long ante, long pp)
    {
        bankroll.Deposit(currentRound.TotalOnTable());
        currentRound.ClearAllBets();
        if (ante + pp > 0 && bankroll.TryWithdraw(ante + pp))
        {
            if (ante > 0) currentRound.PlaceBet(ThreeCardPokerBetType.Ante, ante);
            if (pp > 0) currentRound.PlaceBet(ThreeCardPokerBetType.PairPlus, pp);
        }
        RefreshAllSpots();
        onBankrollChanged?.Invoke();
    }

    void OnSpotClicked(ThreeCardPokerBetType type)
    {
        if (phase == Phase.Deciding && type == ThreeCardPokerBetType.Play) { OnPlayClicked(); return; }
        if (phase != Phase.Betting) return;
        if (type == ThreeCardPokerBetType.Play) { statusText.text = "PLAY is placed after you see your cards"; FlashBlocked(); return; }
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
        if (handOnFelt) ClearHand();
        PushUndoSnapshot();
        bankroll.TryWithdraw(chip);
        currentRound.PlaceBet(type, chip);
        soundManager?.PlayChip();
        JuiceTweens.Pulse(this, (RectTransform)spots[type].Circle.transform, peakScale: 1.1f, duration: 0.18f);
        RefreshSpot(spots[type]);
        onBankrollChanged?.Invoke();
        RefreshActionButtons();
    }

    void TakeDown(ThreeCardPokerBetType type)
    {
        if (phase != Phase.Betting || type == ThreeCardPokerBetType.Play) return;
        long amt = currentRound.GetBet(type);
        if (amt <= 0) return;
        PushUndoSnapshot();
        currentRound.ClearBet(type);
        bankroll.Deposit(amt);
        soundManager?.PlayClick();
        statusText.color = UIFactory.Accent;
        statusText.text = $"{(type == ThreeCardPokerBetType.Ante ? "Ante" : "Pair Plus")} down — {UIFactory.FormatMoney(amt)} back to wallet";
        RefreshSpot(spots[type]);
        onBankrollChanged?.Invoke();
        RefreshActionButtons();
    }

    void FlashBlocked() => juiceManager?.MicroShake(1.2f);

    void OnClearBetClicked()
    {
        if (phase != Phase.Betting) return;
        if (currentRound.TotalOnTable() <= 0) { statusText.text = "Nothing to clear"; FlashBlocked(); return; }
        PushUndoSnapshot();
        ApplyBets(0, 0);
        soundManager?.PlayClick();
        RefreshActionButtons();
    }

    void UndoLastBetAction()
    {
        if (phase != Phase.Betting) return;
        if (undoStack.Count == 0) { statusText.text = "Nothing to undo"; FlashBlocked(); return; }
        var (ante, pp) = undoStack[undoStack.Count - 1];
        undoStack.RemoveAt(undoStack.Count - 1);
        ApplyBets(ante, pp);
        soundManager?.PlayClick();
        RefreshActionButtons();
    }

    // Re-places last hand's Ante / Pair Plus on spots that are empty now.
    void OnRepeatBetClicked()
    {
        if (phase != Phase.Betting) return;
        if (lastAnte + lastPairPlus <= 0) { statusText.text = "No previous bets to repeat"; FlashBlocked(); return; }
        long ante = currentRound.GetBet(ThreeCardPokerBetType.Ante) == 0 ? lastAnte : 0;
        long pp = currentRound.GetBet(ThreeCardPokerBetType.PairPlus) == 0 ? lastPairPlus : 0;
        if (ante + pp == 0) { statusText.text = "Last hand's bets are already down"; FlashBlocked(); return; }
        if (!bankroll.CanAfford(ante + pp))
        {
            statusText.color = UIFactory.Accent;
            statusText.text = $"Not enough balance to repeat ({UIFactory.FormatMoney(ante + pp)})";
            FlashBlocked();
            return;
        }
        if (handOnFelt) ClearHand();
        PushUndoSnapshot();
        bankroll.TryWithdraw(ante + pp);
        if (ante > 0) currentRound.PlaceBet(ThreeCardPokerBetType.Ante, ante);
        if (pp > 0) currentRound.PlaceBet(ThreeCardPokerBetType.PairPlus, pp);
        RefreshAllSpots();
        soundManager?.PlayChip();
        JuiceTweens.Pulse(this, repeatButton.GetComponent<RectTransform>(), peakScale: 1.15f, duration: 0.2f);
        onBankrollChanged?.Invoke();
        RefreshActionButtons();
    }

    public long OnTableTotal() => phase == Phase.Betting ? currentRound.TotalOnTable() : 0;

    // Leaving the table: before the deal, chips go back to the wallet; once cards are out it counts as a fold
    // (Ante lost, Pair Plus settled on your cards); a hand already decided is paid.
    // Pure bankroll/round math only — safe from OnDestroy / OnApplicationQuit, and safe to call twice.
    public void RefundTableBets()
    {
        if (phase == Phase.Dealing || phase == Phase.Deciding)
        {
            pendingResult = currentRound.Fold();
            phase = Phase.Revealing;
        }
        if (pendingResult != null)
        {
            bankroll.Deposit(pendingResult.TotalReturned);
            pendingResult = null;
            return;
        }
        if (phase == Phase.Betting)
        {
            long onTable = currentRound.TotalOnTable();
            if (onTable > 0) bankroll.Deposit(onTable);
            currentRound.ClearAllBets();
        }
    }

    // --- Deal / decide ---

    void OnDealClicked()
    {
        if (phase != Phase.Betting) return;
        if (currentRound.GetBet(ThreeCardPokerBetType.Ante) <= 0) { statusText.text = "Bet the ANTE to play a hand"; FlashBlocked(); return; }

        lastAnte = currentRound.GetBet(ThreeCardPokerBetType.Ante);
        lastPairPlus = currentRound.GetBet(ThreeCardPokerBetType.PairPlus);
        undoStack.Clear();
        ClearHand();

        currentRound.Deal();
        phase = Phase.Dealing;
        RefreshActionButtons();
        statusText.color = UIFactory.Accent;
        statusText.text = "Dealing...";
        StartCoroutine(DealSequence());
    }

    IEnumerator DealSequence()
    {
        for (int c = 0; c < 3; c++)
        {
            playerCards.Add(DealCard(currentRound.PlayerHand.Cards[c], PlayerCardPos(c), PlayerCardSize, 34));
            yield return new WaitForSeconds(CardStepDelay);
            var back = CardUI.Create(tableRoot, DealerCardPos(c), DealerCardSize);
            back.SetFaceDown();
            dealerCards.Add(back);
            soundManager?.PlayChip();
            yield return new WaitForSeconds(CardStepDelay);
        }
        if (phase != Phase.Dealing) yield break; // left the table mid-deal

        playerInfoText.text = currentRound.PlayerHand.Describe();
        playerInfoText.color = currentRound.PlayerHand.Rank >= ThreeCardPokerRank.Straight ? TitleGold : FeltText;
        phase = Phase.Deciding;
        statusText.color = UIFactory.Accent;
        statusText.text = $"You have {currentRound.PlayerHand.Describe()} — PLAY ({UIFactory.FormatMoney(lastAnte)}) or FOLD?";
        RefreshActionButtons();
    }

    void OnPlayClicked()
    {
        if (phase != Phase.Deciding) return;
        long call = currentRound.GetBet(ThreeCardPokerBetType.Ante);
        if (!bankroll.TryWithdraw(call))
        {
            statusText.color = UIFactory.Accent;
            statusText.text = $"Need {UIFactory.FormatMoney(call)} to PLAY — ADD FUNDS or FOLD";
            FlashBlocked();
            return;
        }
        settledPlay = call;
        onBankrollChanged?.Invoke();
        soundManager?.PlayChip();
        pendingResult = currentRound.Play();
        RefreshSpot(spots[ThreeCardPokerBetType.Play]);
        StartCoroutine(RevealSequence(pendingResult));
    }

    void OnFoldClicked()
    {
        if (phase != Phase.Deciding) return;
        soundManager?.PlayClick();
        pendingResult = currentRound.Fold();
        StartCoroutine(RevealSequence(pendingResult));
    }

    IEnumerator RevealSequence(ThreeCardPokerRoundResult result)
    {
        phase = Phase.Revealing;
        RefreshActionButtons();
        statusText.color = UIFactory.Accent;
        statusText.text = result.PlayerFolded ? "Folded — dealer shows..." : "Dealer shows...";

        for (int c = 0; c < 3; c++)
        {
            dealerCards[c].SetCard(result.DealerHand.Cards[c]);
            foreach (var t in dealerCards[c].GetComponentsInChildren<Text>()) t.fontSize = 30;
            soundManager?.PlayChip();
            yield return new WaitForSeconds(CardStepDelay);
        }
        dealerInfoText.text = result.DealerHand.Describe();
        dealerQualifyText.text = result.DealerQualified ? "DEALER QUALIFIES" : "DEALER DOESN'T QUALIFY (NEEDS QUEEN HIGH)";
        dealerQualifyText.color = result.DealerQualified ? FeltText : TitleGold;
        yield return new WaitForSeconds(0.35f);

        try { Settle(result); }
        finally
        {
            phase = Phase.Betting;
            handOnFelt = true;
            RefreshActionButtons();
        }
    }

    void Settle(ThreeCardPokerRoundResult result)
    {
        if (pendingResult != null)
        {
            bankroll.Deposit(result.TotalReturned);
            pendingResult = null;
        }
        onBankrollChanged?.Invoke();

        long ante = lastAnte, pp = lastPairPlus;
        var anteSpot = spots[ThreeCardPokerBetType.Ante];
        var playSpot = spots[ThreeCardPokerBetType.Play];
        var ppSpot = spots[ThreeCardPokerBetType.PairPlus];

        if (result.PlayerFolded)
        {
            SetResult(anteSpot, $"FOLDED  -{UIFactory.FormatMoney(ante)}", UIFactory.Negative);
        }
        else
        {
            long anteNet = result.AnteReturn - ante;
            long playNet = result.PlayReturn - settledPlay;
            switch (result.Outcome)
            {
                case ThreeCardPokerOutcome.PlayerWins:
                    SetResult(anteSpot, $"WIN  +{UIFactory.FormatMoney(anteNet)}", UIFactory.Positive);
                    SetResult(playSpot, $"WIN  +{UIFactory.FormatMoney(playNet)}", UIFactory.Positive);
                    break;
                case ThreeCardPokerOutcome.DealerNoQualify:
                    SetResult(anteSpot, $"WIN  +{UIFactory.FormatMoney(anteNet)}", UIFactory.Positive);
                    SetResult(playSpot, "PUSH", UIFactory.Accent);
                    break;
                case ThreeCardPokerOutcome.Tie:
                    SetResult(anteSpot, "PUSH", UIFactory.Accent);
                    SetResult(playSpot, "PUSH", UIFactory.Accent);
                    break;
                default:
                    SetResult(anteSpot, $"LOSE  -{UIFactory.FormatMoney(ante)}", UIFactory.Negative);
                    SetResult(playSpot, $"LOSE  -{UIFactory.FormatMoney(settledPlay)}", UIFactory.Negative);
                    break;
            }
            if (result.AnteBonusReturn > 0)
                anteSpot.ResultText.text += $"   <color=#{ColorUtility.ToHtmlStringRGB(UIFactory.Positive)}>BONUS +{UIFactory.FormatMoney(result.AnteBonusReturn)}</color>";
        }
        if (pp > 0)
        {
            if (result.PairPlusReturn > 0) SetResult(ppSpot, $"{PairPlusLabel(result.PlayerHand.Rank)}  +{UIFactory.FormatMoney(result.PairPlusReturn - pp)}", UIFactory.Positive);
            else SetResult(ppSpot, $"LOSE  -{UIFactory.FormatMoney(pp)}", UIFactory.Negative);
        }

        // Chips come off the felt now that every bet is settled
        currentRound.ClearAllBets();
        settledPlay = 0;
        RefreshAllSpots();

        FinishRound(result);
    }

    static string PairPlusLabel(ThreeCardPokerRank rank) => rank switch
    {
        ThreeCardPokerRank.StraightFlush => "STRAIGHT FLUSH 40:1",
        ThreeCardPokerRank.ThreeOfAKind  => "TRIPS 25:1",
        ThreeCardPokerRank.Straight      => "STRAIGHT 5:1",
        ThreeCardPokerRank.Flush         => "FLUSH 4:1",
        _                                => "PAIR 1:1"
    };

    void SetResult(Spot spot, string text, Color color)
    {
        spot.ResultText.text = text;
        spot.ResultText.color = color;
        spot.Frame.color = color == UIFactory.Positive ? TitleGold : color == UIFactory.Negative ? UIFactory.Negative : FeltLine;
        JuiceTweens.Pulse(this, (RectTransform)spot.ResultText.transform, peakScale: 1.2f, duration: 0.25f);
    }

    void FinishRound(ThreeCardPokerRoundResult result)
    {
        long net = result.TotalReturned - result.TotalStaked;
        string headline = result.PlayerFolded ? "Folded"
            : result.Outcome switch
            {
                ThreeCardPokerOutcome.PlayerWins      => "You win!",
                ThreeCardPokerOutcome.DealerNoQualify => "Dealer doesn't qualify",
                ThreeCardPokerOutcome.Tie             => "Tie — push",
                _                                     => "Dealer wins"
            };
        string flavor = net > 0 ? WinFlavors[UnityEngine.Random.Range(0, WinFlavors.Length)]
            : net < 0 ? LoseFlavors[UnityEngine.Random.Range(0, LoseFlavors.Length)]
            : "Even hand";
        statusText.color = net > 0 ? UIFactory.Positive : net < 0 ? UIFactory.Negative : UIFactory.Accent;
        statusText.text = $"{headline}  ({(net >= 0 ? "+" : "")}{UIFactory.FormatMoney(net)})  — {flavor}";

        bool bigHand = result.PlayerHand.Rank >= ThreeCardPokerRank.ThreeOfAKind;
        if (net > 0)
        {
            soundManager?.PlayWin();
            if (bigHand)
            {
                juiceManager?.Shake(0.6f, 4.5f); juiceManager?.Flash(new Color(1f, 0.85f, 0.2f, 0.25f), 0.7f);
                juiceManager?.PlayConfetti(2.5f); juiceManager?.PulseLight(0.9f, 0.7f);
                juiceManager?.PlayMoneyFountain(Vector2.zero);
                floatingText?.Show($"{result.PlayerHand.Describe().ToUpper()}! +{UIFactory.FormatMoney(net)}", TitleGold, fontSize: 40);
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
        else { soundManager?.PlayClick(); floatingText?.Show("PUSH", UIFactory.Accent); }

        bool showStreak = winStreak >= 2;
        streakAnimator?.SetText(showStreak ? $"<wave><rainb>{winStreak} WIN STREAK</rainb></wave>" : "");
        streakBadgeGO?.SetActive(showStreak);
        if (showStreak) JuiceTweens.Pulse(this, (RectTransform)streakBadgeGO.transform, peakScale: 1.15f, duration: 0.3f);

        var record = new ThreeCardPokerRoundRecord(roundIndex,
            result.PlayerHand.Rank, result.PlayerHand.HighCard, result.DealerHand.Rank, result.DealerHand.HighCard,
            result.Outcome, result.DealerQualified, result.PlayerFolded,
            result.TotalStaked, result.TotalReturned, bankroll.Balance);
        onRoundResolved?.Invoke(record);
        roundIndex++;
        int[] handTargets = { 50, 100, 250, 500, 1000 };
        foreach (var t in handTargets)
            if (roundIndex == t && roundMilestonesFired.Add(t))
                milestoneToast?.Show($"{t} Hands This Session", UIFactory.Accent, fontSize: 26);
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
        bool betting = phase == Phase.Betting;
        bool canDeal = betting && currentRound.GetBet(ThreeCardPokerBetType.Ante) > 0;
        UIFactory.SetButtonState(dealButton,     dealBaseColor,   canDeal);
        UIFactory.SetButtonState(clearBetButton, clearBaseColor,  betting && currentRound.TotalOnTable() > 0);
        UIFactory.SetButtonState(repeatButton,   repeatBaseColor, betting && lastAnte + lastPairPlus > 0);
        UIFactory.SetButtonState(undoButton,     UIFactory.AccentDim, betting && undoStack.Count > 0);
        playButton.gameObject.SetActive(phase == Phase.Deciding);
        foldButton.gameObject.SetActive(phase == Phase.Deciding);
        if (phase == Phase.Deciding) JuiceTweens.Pulse(this, playButton.GetComponent<RectTransform>(), peakScale: 1.12f, duration: 0.25f);
        if (canDeal && !dealWasEnabled) JuiceTweens.Pulse(this, dealButton.GetComponent<RectTransform>(), peakScale: 1.15f, duration: 0.25f);
        dealWasEnabled = canDeal;
    }

    public void SetRoundIndex(int index) => roundIndex = index;

    // Bankroll reset wipes the wallet, so chips on the felt are dropped rather than refunded.
    public void ResetRound()
    {
        StopAllCoroutines();
        phase = Phase.Betting;
        pendingResult = null;
        winStreak = 0;
        bestRoundNet = 0;
        doubledMilestoneFired = false;
        roundMilestonesFired.Clear();
        streakBadgeGO?.SetActive(false);
        undoStack.Clear();
        lastAnte = lastPairPlus = 0;
        currentRound.ClearAllBets();
        ClearHand();
        RefreshAllSpots();
        statusText.color = UIFactory.Accent;
        statusText.text = "Bet the ANTE (and PAIR PLUS if you like), then DEAL";
        RefreshActionButtons();
    }
}
