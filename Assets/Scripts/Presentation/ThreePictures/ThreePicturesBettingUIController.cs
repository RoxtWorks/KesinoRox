using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Febucci.UI;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

// Three Pictures (Royal Three Pictures / 3 Kings) betting controller.
// Structure mirrors BaccaratBettingUIController closely — two bets (Main + Royal Bonus),
// one DEAL button, zero mid-hand decisions. The key difference: two card hands of 3,
// and "Royal" (3 picture cards) is the top hand rank.
public class ThreePicturesBettingUIController : MonoBehaviour
{
    Bankroll bankroll;
    ChipSelectorUI chipSelector;
    Shoe shoe;
    SoundManager soundManager;
    JuiceManager juiceManager;
    FloatingTextUI floatingText;
    FloatingTextUI milestoneToast;
    Action<ThreePicturesRoundRecord> onRoundResolved;

    ThreePicturesRound currentRound;
    bool roundActive;
    int roundIndex;
    int winStreak;
    long bestRoundNet;
    bool doubledMilestoneFired;
    bool dealWasEnabled;
    readonly HashSet<int> roundMilestonesFired = new HashSet<int>();

    TextMeshProUGUI streakText;
    TextAnimator_TMP streakAnimator;
    GameObject streakBadgeGO;

    readonly Dictionary<ThreePicturesBetType, long> pendingBets = new Dictionary<ThreePicturesBetType, long>();
    readonly Dictionary<ThreePicturesBetType, long> lastBets    = new Dictionary<ThreePicturesBetType, long>();
    readonly List<Dictionary<ThreePicturesBetType, long>> undoStack = new List<Dictionary<ThreePicturesBetType, long>>();
    const int MaxUndoDepth = 30;

    Transform tableRoot;
    Text statusText;
    Button dealButton, clearBetButton, repeatButton, undoButton;
    Color dealBaseColor, clearBaseColor, repeatBaseColor;

    // Card display — 3 per side
    readonly List<GameObject> playerCardGOs = new List<GameObject>();
    readonly List<GameObject> dealerCardGOs = new List<GameObject>();
    Text playerPointText, dealerPointText;

    class BetSpot
    {
        public ThreePicturesBetType Type;
        public GameObject Root;
        public Image FillImg;
        public Text AmountText;
        public Text PayoutText;
        public readonly List<GameObject> ChipVisuals = new List<GameObject>();
    }

    readonly Dictionary<ThreePicturesBetType, BetSpot> spots = new Dictionary<ThreePicturesBetType, BetSpot>();
    static readonly Color[] ChipStackColors =
    {
        new Color(0.65f, 0.12f, 0.12f),
        new Color(0.1f, 0.35f, 0.6f),
        UIFactory.Chip500White,
    };

    const float PanelCenterX = 0f;
    static readonly string[] WinFlavors  = { "Nice bet!", "There it is!", "Keep it going!", "Well played" };
    static readonly string[] LoseFlavors = { "Press DEAL again", "Try again", "Onward", "Next hand's yours" };

    public void Build(Transform canvas, Bankroll bankroll, ChipSelectorUI chipSelector, Shoe shoe,
        SoundManager soundManager, JuiceManager juiceManager, FloatingTextUI floatingText,
        FloatingTextUI milestoneToast, Action<ThreePicturesRoundRecord> onRoundResolved)
    {
        this.bankroll = bankroll;
        this.chipSelector = chipSelector;
        this.shoe = shoe;
        this.soundManager = soundManager;
        this.juiceManager = juiceManager;
        this.floatingText = floatingText;
        this.milestoneToast = milestoneToast;
        this.onRoundResolved = onRoundResolved;

        foreach (ThreePicturesBetType t in Enum.GetValues(typeof(ThreePicturesBetType))) pendingBets[t] = 0;

        var tableRootGO = new GameObject("ThreePicturesUIRoot");
        tableRootGO.transform.SetParent(canvas, false);
        var rt = tableRootGO.AddComponent<RectTransform>();
        rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 0.5f);
        rt.anchoredPosition = Vector2.zero;
        tableRoot = tableRootGO.transform;

        UIFactory.MakePanel(tableRoot, "TPPanelBg", new Vector2(PanelCenterX, -80), new Vector2(1000, 700), UIFactory.PanelDark);
        UIFactory.MakeHeroTitle(tableRoot, "Header", new Vector2(PanelCenterX, 220), "3 KINGS TABLE", 26);

        // Hand rankings — standalone floating panel, outside the main table dark background (panel left edge = -500)
        const float rankX = -590f;
        var rankBg = UIFactory.MakePanel(tableRoot, "RankingsBg", new Vector2(rankX, 55), new Vector2(185, 215), UIFactory.PanelDark, shadow: false);
        UIFactory.AddSharpFrame(rankBg, UIFactory.AccentDim, square: true);
        UIFactory.MakeText(tableRoot, "RankHdr", new Vector2(rankX, 152), 12,
            TextAnchor.MiddleCenter, new Vector2(175, 20), UIFactory.Accent, FontStyle.Bold).text = "HAND RANKINGS";
        string[] rankLines = { "3 Kings = ROYAL", "2 Kings + No. wins", "1 King + No. wins", "Higher No. wins", "(9 is highest)" };
        for (int i = 0; i < rankLines.Length; i++)
            UIFactory.MakeText(tableRoot, $"RankLine{i}", new Vector2(rankX, 127 - i * 22), 10,
                TextAnchor.MiddleCenter, new Vector2(175, 18), i == 0 ? new Color(1f, 0.85f, 0.2f) : UIFactory.TextDim).text = rankLines[i];

        // Dealer hand at top, player hand below — centered vertical stack
        UIFactory.MakeText(tableRoot, "DealerLabel", new Vector2(0, 175), 13,
            TextAnchor.MiddleCenter, new Vector2(220, 20), UIFactory.TextDim, FontStyle.Bold).text = "DEALER HAND";
        dealerPointText = UIFactory.MakeText(tableRoot, "DealerPt", new Vector2(0, 88), 22,
            TextAnchor.MiddleCenter, new Vector2(250, 30), UIFactory.TextDim, FontStyle.Bold);

        UIFactory.MakeText(tableRoot, "PlayerLabel", new Vector2(0, 50), 13,
            TextAnchor.MiddleCenter, new Vector2(220, 20), UIFactory.TextDim, FontStyle.Bold).text = "YOUR HAND";
        playerPointText = UIFactory.MakeText(tableRoot, "PlayerPt", new Vector2(0, -35), 22,
            TextAnchor.MiddleCenter, new Vector2(250, 30), UIFactory.TextDim, FontStyle.Bold);

        var statusPanelBg = UIFactory.MakePanel(tableRoot, "StatusPanelBg", new Vector2(PanelCenterX, -90), new Vector2(700, 40), UIFactory.PanelDark, shadow: false);
        UIFactory.AddSharpFrame(statusPanelBg, UIFactory.AccentDim, square: true);
        statusText = UIFactory.MakeText(tableRoot, "StatusText", new Vector2(PanelCenterX, -90), 19,
            sizeDelta: new Vector2(680, 34), color: UIFactory.Accent, style: FontStyle.Bold);
        statusText.text = "Place bets, then DEAL";

        BuildBetSpot(ThreePicturesBetType.Main,       new Vector2(-160, -210), UIFactory.Positive, "MAIN", "PAYS 1:1");
        BuildBetSpot(ThreePicturesBetType.RoyalBonus, new Vector2( 160, -210), new Color(0.8f, 0.6f, 0.1f), "ROYAL\nBONUS", "3 PICS 5:1 · 2 PICS 1:1");

        BuildActionButtons();
        BuildStreakBadge();
        RefreshActionButtons();
        RefreshBetDisplay();
    }

    void BuildStreakBadge()
    {
        streakBadgeGO = new GameObject("StreakBadge");
        streakBadgeGO.transform.SetParent(tableRoot, false);
        var rt = streakBadgeGO.AddComponent<RectTransform>();
        rt.sizeDelta = new Vector2(300, 90);
        rt.anchoredPosition = new Vector2(-480, 465);
        UIFactory.MakeFramedPanel(streakBadgeGO.transform, "StreakBadgeBg", Vector2.zero, new Vector2(300, 90), Color.black);

        var textGO = new GameObject("StreakText");
        textGO.transform.SetParent(streakBadgeGO.transform, false);
        var textRt = textGO.AddComponent<RectTransform>();
        textRt.sizeDelta = new Vector2(280, 70);
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

    void BuildBetSpot(ThreePicturesBetType type, Vector2 pos, Color accentColor, string label, string payoutLabel)
    {
        var go = new GameObject($"BetSpot_{type}");
        go.transform.SetParent(tableRoot, false);
        var rt = go.AddComponent<RectTransform>();
        rt.sizeDelta = new Vector2(160, 140);
        rt.anchoredPosition = pos;
        var fill = go.AddComponent<Image>();
        fill.sprite = UIFactory.RoundedRect();
        fill.type = Image.Type.Sliced;
        fill.color = new Color(1f, 1f, 1f, 0.06f);
        UIFactory.AddSharpFrame(go, accentColor, square: true);
        var btn = go.AddComponent<Button>();
        btn.targetGraphic = fill;
        btn.onClick.AddListener(() => OnSpotClicked(type));

        var amountText = UIFactory.MakeText(go.transform, "AmountText", new Vector2(0, 22), 16,
            sizeDelta: new Vector2(140, 50), color: UIFactory.TextDim, style: FontStyle.Bold);
        amountText.text = label;
        amountText.alignment = TextAnchor.MiddleCenter;

        var payoutText = UIFactory.MakeText(go.transform, "PayoutText", new Vector2(0, -40), 11,
            sizeDelta: new Vector2(150, 30), color: UIFactory.TextDim);
        payoutText.text = payoutLabel;
        payoutText.alignment = TextAnchor.MiddleCenter;

        spots[type] = new BetSpot { Type = type, Root = go, FillImg = fill, AmountText = amountText, PayoutText = payoutText };
    }

    void BuildActionButtons()
    {
        const float y = -345f;
        clearBaseColor  = UIFactory.RedBet;
        dealBaseColor   = UIFactory.Positive;
        repeatBaseColor = UIFactory.AccentDim;

        clearBetButton  = UIFactory.MakeButton(tableRoot, "ClearBetBtn",  new Vector2(-183f, y), new Vector2(161, 53), "CLEAR BET",  clearBaseColor,  OnClearBetClicked, 13, pixelFont: true);
        dealButton      = UIFactory.MakeButton(tableRoot, "DealBtn",      new Vector2(   0f, y), new Vector2(184, 62), "DEAL",        dealBaseColor,   OnDealClicked, 20, pixelFont: true);
        repeatButton    = UIFactory.MakeButton(tableRoot, "RepeatBetBtn", new Vector2( 183f, y), new Vector2(161, 53), "REPEAT BET", repeatBaseColor, OnRepeatBetClicked, 12, pixelFont: true);
        undoButton      = UIFactory.MakeButton(tableRoot, "UndoBtn",      new Vector2(-357f, y), new Vector2(138, 53), "UNDO",        UIFactory.AccentDim, UndoLastBetAction, 13, pixelFont: true);
    }

    void FlashBlocked() => juiceManager?.MicroShake(1.2f);

    void OnSpotClicked(ThreePicturesBetType type)
    {
        if (roundActive) return;
        long chip = chipSelector.SelectedChip;
        long totalPending = pendingBets.Values.Sum();
        if (!bankroll.CanAfford(totalPending + chip))
        {
            statusText.text = bankroll.Balance < ChipDenominations.Values[0]
                ? "Out of chips — use ADD FUNDS above to keep playing"
                : "Not enough balance for that bet";
            FlashBlocked();
            return;
        }
        PushUndoSnapshot();
        pendingBets[type] += chip;
        soundManager?.PlayChip();
        JuiceTweens.Pulse(this, (RectTransform)spots[type].Root.transform, peakScale: 1.12f, duration: 0.18f);
        AddBetChipVisual(spots[type], chip);
        RefreshBetDisplay();
        RefreshActionButtons();
    }

    void OnClearBetClicked()
    {
        if (roundActive) return;
        if (pendingBets.Values.Sum() <= 0) { statusText.text = "Nothing to clear"; FlashBlocked(); return; }
        PushUndoSnapshot();
        foreach (var t in spots.Keys.ToList()) pendingBets[t] = 0;
        soundManager?.PlayClick();
        foreach (var spot in spots.Values) ClearBetChipVisuals(spot);
        RefreshBetDisplay();
        RefreshActionButtons();
    }

    void PushUndoSnapshot() { undoStack.Add(new Dictionary<ThreePicturesBetType, long>(pendingBets)); if (undoStack.Count > MaxUndoDepth) undoStack.RemoveAt(0); }

    void UndoLastBetAction()
    {
        if (roundActive) return;
        if (undoStack.Count == 0) { statusText.text = "Nothing to undo"; FlashBlocked(); return; }
        var snapshot = undoStack[undoStack.Count - 1];
        undoStack.RemoveAt(undoStack.Count - 1);
        foreach (var t in spots.Keys.ToList()) pendingBets[t] = snapshot.TryGetValue(t, out long a) ? a : 0;
        RebuildAllChipVisuals();
        RefreshBetDisplay(); RefreshActionButtons();
        soundManager?.PlayClick();
    }

    void RebuildAllChipVisuals()
    {
        foreach (var t in spots.Keys.ToList())
        {
            ClearBetChipVisuals(spots[t]);
            if (pendingBets[t] <= 0) continue;
            int count = Mathf.Clamp((int)(pendingBets[t] / ChipDenominations.Values[0]), 1, 5);
            for (int i = 0; i < count; i++) AddBetChipVisual(spots[t], -1);
        }
    }

    void AddBetChipVisual(BetSpot spot, long denomination)
    {
        const int maxVisibleChips = 8;
        if (spot.ChipVisuals.Count >= maxVisibleChips) return;
        int colorIndex = Array.IndexOf(ChipDenominations.Values, denomination);
        Color fill = colorIndex >= 0 ? ChipStackColors[colorIndex % ChipStackColors.Length] : UIFactory.Accent;

        var go = new GameObject($"BetChip_{spot.ChipVisuals.Count}");
        go.transform.SetParent(spot.Root.transform, false);
        var img = go.AddComponent<Image>();
        img.sprite = UIFactory.Circle();
        img.color = fill;
        img.raycastTarget = false;
        var rt = go.GetComponent<RectTransform>();
        rt.sizeDelta = new Vector2(28, 28);
        int si = spot.ChipVisuals.Count;
        float fx = (si % 2 == 0 ? -1f : 1f) * (8f + si * 2f) + UnityEngine.Random.Range(-3f, 3f);
        float fy = -50f + Mathf.Min(si, 4) * 8f;
        rt.anchoredPosition = new Vector2(fx, fy);
        spot.ChipVisuals.Add(go);
        spot.AmountText.transform.SetAsLastSibling();
        JuiceTweens.PopIn(this, rt, overshoot: 1.3f, duration: 0.18f);
    }

    void ClearBetChipVisuals(BetSpot spot)
    {
        foreach (var go in spot.ChipVisuals) Destroy(go);
        spot.ChipVisuals.Clear();
    }

    void OnRepeatBetClicked()
    {
        if (roundActive) return;
        long last = lastBets.Values.Sum();
        if (last <= 0) { statusText.text = "No previous bet to repeat"; FlashBlocked(); return; }
        if (!bankroll.CanAfford(last)) { statusText.text = "Not enough balance to repeat that bet"; FlashBlocked(); return; }
        PushUndoSnapshot();
        soundManager?.PlayChip();
        JuiceTweens.Pulse(this, repeatButton.GetComponent<RectTransform>(), peakScale: 1.15f, duration: 0.2f);
        foreach (var t in spots.Keys.ToList()) pendingBets[t] = lastBets.TryGetValue(t, out long a) ? a : 0;
        RebuildAllChipVisuals();
        RefreshBetDisplay(); RefreshActionButtons();
    }

    void OnDealClicked()
    {
        long total = pendingBets.Values.Sum();
        if (roundActive || total <= 0) return;
        if (!bankroll.TryWithdraw(total)) { statusText.text = "Not enough balance to deal"; FlashBlocked(); return; }

        if (shoe.NeedsReshuffle) milestoneToast?.Show("New shoe — reshuffling", UIFactory.Accent, fontSize: 24);

        roundActive = true;
        undoStack.Clear();
        foreach (var t in spots.Keys.ToList()) lastBets[t] = pendingBets[t];
        currentRound = new ThreePicturesRound(shoe);
        foreach (var t in Enum.GetValues(typeof(ThreePicturesBetType)))
            currentRound.PlaceBet((ThreePicturesBetType)t, lastBets[(ThreePicturesBetType)t]);

        ClearCardDisplays();
        foreach (var t in spots.Keys.ToList()) pendingBets[t] = 0;
        foreach (var spot in spots.Values) ClearBetChipVisuals(spot);
        RefreshBetDisplay(); RefreshActionButtons();
        statusText.color = UIFactory.Accent;
        statusText.text = "Dealing...";

        StartCoroutine(DealRevealSequence());
    }

    void ClearCardDisplays()
    {
        foreach (var go in playerCardGOs) Destroy(go);
        foreach (var go in dealerCardGOs) Destroy(go);
        playerCardGOs.Clear(); dealerCardGOs.Clear();
        playerPointText.text = "";
        dealerPointText.text = "";
    }

    IEnumerator DealRevealSequence()
    {
        var result = currentRound.Deal();
        const float stepDelay = 0.35f;

        // Reveal cards one at a time: P-D-P-D-P-D (vertical stack: player centre-bottom, dealer centre-top)
        for (int i = 0; i < 3; i++)
        {
            RevealCard(result.PlayerHand.Cards[i], playerCardGOs, new Vector2(-54 + i * 54, 10));
            soundManager?.PlayChip();
            yield return new WaitForSeconds(stepDelay);

            RevealCard(result.DealerHand.Cards[i], dealerCardGOs, new Vector2(-54 + i * 54, 133));
            soundManager?.PlayChip();
            yield return new WaitForSeconds(stepDelay);
        }

        ShowHandPoints(result);
        yield return new WaitForSeconds(0.2f);
        ResolveRound(result);
    }

    void RevealCard(Card card, List<GameObject> list, Vector2 pos)
    {
        var go = new GameObject($"Card_{list.Count}");
        go.transform.SetParent(tableRoot, false);
        var rt = go.AddComponent<RectTransform>();
        rt.sizeDelta = new Vector2(44, 64);
        rt.anchoredPosition = pos;
        var bg = go.AddComponent<Image>();
        bg.color = Color.white;
        UIFactory.AddSharpFrame(go, new Color(0.3f, 0.3f, 0.35f), square: true);
        bool isPicture = card.Rank >= Rank.Jack;
        Color rankColor = isPicture ? UIFactory.Accent : new Color(0.1f, 0.1f, 0.1f);
        Color suitColor = card.IsRed ? new Color(0.75f, 0.15f, 0.15f) : new Color(0.12f, 0.12f, 0.12f);

        var rankText = UIFactory.MakeText(go.transform, "Rank", new Vector2(0, 16), isPicture ? 16 : 18,
            TextAnchor.MiddleCenter, new Vector2(40, 28), rankColor, FontStyle.Bold);
        rankText.text = RankLabel(card.Rank);

        var suitText = UIFactory.MakeText(go.transform, "Suit", new Vector2(0, -14), 14,
            TextAnchor.MiddleCenter, new Vector2(40, 20), suitColor);
        suitText.text = SuitSymbol(card.Suit);

        list.Add(go);
        JuiceTweens.PopIn(this, rt, overshoot: 1.25f, duration: 0.18f);
    }

    static string RankLabel(Rank r) => r switch
    {
        Rank.Ace   => "A",
        Rank.King  => "K",
        Rank.Queen => "Q",
        Rank.Jack  => "J",
        Rank.Ten   => "10",
        _          => ((int)r).ToString()
    };

    static string SuitSymbol(Suit s) => s switch
    {
        Suit.Hearts   => "♥",
        Suit.Diamonds => "♦",
        Suit.Clubs    => "♣",
        Suit.Spades   => "♠",
        _             => ""
    };

    void ShowHandPoints(ThreePicturesRoundResult result)
    {
        string FormatHand(ThreePicturesHand h) =>
            h.IsRoyal ? "ROYAL" : $"Point {h.Point}";
        playerPointText.text = FormatHand(result.PlayerHand);
        dealerPointText.text = FormatHand(result.DealerHand);

        Color gold = new Color(1f, 0.85f, 0.2f);
        playerPointText.color = result.PlayerHand.IsRoyal ? gold
            : result.Outcome == ThreePicturesOutcome.PlayerWins ? UIFactory.Positive
            : result.Outcome == ThreePicturesOutcome.DealerWins ? UIFactory.Negative
            : UIFactory.Accent;
        dealerPointText.color = result.DealerHand.IsRoyal ? gold
            : result.Outcome == ThreePicturesOutcome.DealerWins ? UIFactory.Positive
            : result.Outcome == ThreePicturesOutcome.PlayerWins ? UIFactory.Negative
            : UIFactory.Accent;
    }

    void ResolveRound(ThreePicturesRoundResult result)
    {
        bankroll.Deposit(result.TotalReturned);
        long staked = lastBets.Values.Sum();
        long net = result.TotalReturned - staked;

        string outcomeLabel = result.Outcome switch
        {
            ThreePicturesOutcome.PlayerWins => "You win!",
            ThreePicturesOutcome.DealerWins => "Dealer wins",
            ThreePicturesOutcome.Tie        => "Tie — push",
            _                               => ""
        };
        string flavor = net > 0 ? WinFlavors[UnityEngine.Random.Range(0, WinFlavors.Length)]
            : net < 0 ? LoseFlavors[UnityEngine.Random.Range(0, LoseFlavors.Length)]
            : "Press DEAL again";

        statusText.color = net > 0 ? UIFactory.Positive : net < 0 ? UIFactory.Negative : UIFactory.Accent;
        statusText.text  = $"{outcomeLabel}  ({(net >= 0 ? "+" : "")}{UIFactory.FormatMoney(net)})  — {flavor}";

        if (net > 0)
        {
            soundManager?.PlayWin();
            bool royal = result.PlayerHand.IsRoyal;
            if (royal)
            {
                juiceManager?.Shake(0.6f, 4.5f); juiceManager?.Flash(new Color(1f, 0.85f, 0.2f, 0.25f), 0.7f);
                juiceManager?.PlayConfetti(2.5f); juiceManager?.PulseLight(0.9f, 0.7f);
                juiceManager?.PlayMoneyFountain(Vector2.zero);
                floatingText?.Show($"ROYAL! +{UIFactory.FormatMoney(net)}", new Color(1f, 0.85f, 0.2f), fontSize: 42);
            }
            else if (net >= ChipDenominations.Values[2]) // $500+
            {
                juiceManager?.Shake(0.5f, 4f); juiceManager?.Flash(new Color(0.3f, 1f, 0.4f, 0.28f), 0.7f);
                juiceManager?.PlayConfetti(2f); juiceManager?.PulseLight(0.9f, 0.7f);
                juiceManager?.PlayMoneyFountain(Vector2.zero);
                floatingText?.Show($"HUGE WIN! +{UIFactory.FormatMoney(net)}", UIFactory.Positive, fontSize: 42);
            }
            else if (net >= ChipDenominations.Values[0] * 4L) // $100+
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
                milestoneToast?.Show($"{winStreak} WIN STREAK!", new Color(1f, 0.85f, 0.2f), fontSize: 30);
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

        var record = new ThreePicturesRoundRecord(roundIndex,
            result.PlayerHand.Point, result.DealerHand.Point,
            result.PlayerHand.IsRoyal, result.DealerHand.IsRoyal,
            result.Outcome, staked, result.TotalReturned, bankroll.Balance);
        onRoundResolved?.Invoke(record);
        roundIndex++;
        int[] handTargets = { 50, 100, 250, 500, 1000 };
        foreach (var t in handTargets)
            if (roundIndex == t && roundMilestonesFired.Add(t))
                milestoneToast?.Show($"{t} Hands This Session", UIFactory.Accent, fontSize: 26);

        roundActive = false;
        RefreshBetDisplay();
        RefreshActionButtons();
    }

    void RefreshBetDisplay()
    {
        bool showSpots = !roundActive;
        foreach (var kv in spots)
        {
            kv.Value.Root.SetActive(showSpots);
            long amount = pendingBets[kv.Key];
            kv.Value.AmountText.text = amount > 0 ? UIFactory.FormatMoney(amount)
                : kv.Key == ThreePicturesBetType.Main ? "MAIN" : "ROYAL\nBONUS";
            kv.Value.AmountText.color = amount > 0 ? UIFactory.TextLight : UIFactory.TextDim;
            kv.Value.PayoutText.gameObject.SetActive(amount <= 0);
        }
    }

    void RefreshActionButtons()
    {
        long total = pendingBets.Values.Sum();
        bool canDeal = !roundActive && total > 0;
        UIFactory.SetButtonState(dealButton,     dealBaseColor,   canDeal);
        UIFactory.SetButtonState(clearBetButton, clearBaseColor,  !roundActive && total > 0);
        UIFactory.SetButtonState(repeatButton,   repeatBaseColor, !roundActive && lastBets.Values.Sum() > 0);
        UIFactory.SetButtonState(undoButton,     UIFactory.AccentDim, !roundActive && undoStack.Count > 0);
        if (canDeal && !dealWasEnabled) JuiceTweens.Pulse(this, dealButton.GetComponent<RectTransform>(), peakScale: 1.15f, duration: 0.25f);
        dealWasEnabled = canDeal;
    }

    public void SetRoundIndex(int index) => roundIndex = index;

    public void ResetRound()
    {
        currentRound = null;
        roundActive = false;
        winStreak = 0;
        bestRoundNet = 0;
        doubledMilestoneFired = false;
        roundMilestonesFired.Clear();
        streakBadgeGO?.SetActive(false);
        undoStack.Clear();
        ClearCardDisplays();
        foreach (var t in spots.Keys.ToList()) { pendingBets[t] = 0; lastBets[t] = 0; }
        foreach (var spot in spots.Values) ClearBetChipVisuals(spot);
        statusText.color = UIFactory.Accent;
        statusText.text  = "Place bets, then DEAL";
        RefreshBetDisplay(); RefreshActionButtons();
    }
}
