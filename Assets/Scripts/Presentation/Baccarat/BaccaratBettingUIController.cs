using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Febucci.UI;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

// Baccarat's equivalent of BlackjackBettingUIController — much simpler since baccarat
// has zero player decisions mid-hand: place chips on Player/Banker/Tie (any or all of
// them at once), hit DEAL, and BaccaratRound resolves the whole thing synchronously.
// This class only captures clicks, stages the reveal, and displays/animates the
// result — same Core-only-owns-the-money-and-odds split as every other controller.
public class BaccaratBettingUIController : MonoBehaviour
{
    Bankroll bankroll;
    ChipSelectorUI chipSelector;
    Shoe shoe;
    SoundManager soundManager;
    JuiceManager juiceManager;
    FloatingTextUI floatingText;
    FloatingTextUI milestoneToast;
    Action<BaccaratRoundRecord> onRoundResolved;

    BaccaratRound currentRound;
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

    readonly Dictionary<BaccaratBetType, long> pendingBets = new Dictionary<BaccaratBetType, long>();
    readonly Dictionary<BaccaratBetType, long> lastBets = new Dictionary<BaccaratBetType, long>();
    readonly List<Dictionary<BaccaratBetType, long>> undoStack = new List<Dictionary<BaccaratBetType, long>>();
    const int MaxUndoDepth = 30;

    Transform tableRoot;
    BaccaratHandUI playerHandUI;
    BaccaratHandUI bankerHandUI;

    Text statusText;
    Button dealButton, clearBetButton, repeatButton, undoButton;
    Color dealBaseColor, clearBaseColor, repeatBaseColor;

    class BetSpot
    {
        public BaccaratBetType Type;
        public GameObject Root;
        public Image FillImg;
        public Text AmountText;
        public Text PayoutText;
        public readonly List<GameObject> ChipVisuals = new List<GameObject>();
    }

    readonly Dictionary<BaccaratBetType, BetSpot> spots = new Dictionary<BaccaratBetType, BetSpot>();
    static readonly Color[] ChipStackColors =
    {
        new Color(0.65f, 0.12f, 0.12f),
        new Color(0.1f, 0.35f, 0.6f),
        new Color(0.1f, 0.1f, 0.1f),
    };

    const float PanelCenterX = 0f;
    static readonly string[] WinFlavors = { "Nice bet!", "There it is!", "Press DEAL again", "Keep it going!", "Well played" };
    static readonly string[] LoseFlavors = { "Press DEAL again", "Try again", "Onward", "Next hand's yours", "Deal again" };

    public void Build(Transform canvas, Bankroll bankroll, ChipSelectorUI chipSelector, Shoe shoe,
        SoundManager soundManager, JuiceManager juiceManager, FloatingTextUI floatingText, FloatingTextUI milestoneToast,
        Action<BaccaratRoundRecord> onRoundResolved)
    {
        this.bankroll = bankroll;
        this.chipSelector = chipSelector;
        this.shoe = shoe;
        this.soundManager = soundManager;
        this.juiceManager = juiceManager;
        this.floatingText = floatingText;
        this.milestoneToast = milestoneToast;
        this.onRoundResolved = onRoundResolved;

        foreach (BaccaratBetType t in Enum.GetValues(typeof(BaccaratBetType))) pendingBets[t] = 0;

        var tableRootGO = new GameObject("BaccaratUIRoot");
        tableRootGO.transform.SetParent(canvas, false);
        var tableRootRT = tableRootGO.AddComponent<RectTransform>();
        tableRootRT.anchorMin = new Vector2(0.5f, 0.5f);
        tableRootRT.anchorMax = new Vector2(0.5f, 0.5f);
        tableRootRT.pivot = new Vector2(0.5f, 0.5f);
        tableRootRT.anchoredPosition = Vector2.zero;
        tableRoot = tableRootGO.transform;

        UIFactory.MakePanel(tableRoot, "BaccaratPanelBg", new Vector2(PanelCenterX, -100), new Vector2(1000, 660), UIFactory.PanelDark);
        UIFactory.MakeHeroTitle(tableRoot, "Header_Baccarat", new Vector2(PanelCenterX, 195), "BACCARAT TABLE", 26);

        UIFactory.MakeText(tableRoot, "PlayerLabel", new Vector2(-170, 150), 13,
            TextAnchor.MiddleCenter, new Vector2(200, 20), UIFactory.TextDim, FontStyle.Bold).text = "PLAYER";
        UIFactory.MakeText(tableRoot, "BankerLabel", new Vector2(170, 150), 13,
            TextAnchor.MiddleCenter, new Vector2(200, 20), UIFactory.TextDim, FontStyle.Bold).text = "BANKER";

        playerHandUI = new BaccaratHandUI();
        playerHandUI.Build(tableRoot, new Vector2(-170, 90));
        bankerHandUI = new BaccaratHandUI();
        bankerHandUI.Build(tableRoot, new Vector2(170, 90));

        var statusPanelBg = UIFactory.MakePanel(tableRoot, "StatusPanelBg", new Vector2(PanelCenterX, -20), new Vector2(720, 40), UIFactory.PanelDark, shadow: false);
        UIFactory.AddSharpFrame(statusPanelBg, UIFactory.AccentDim, square: true);
        statusText = UIFactory.MakeText(tableRoot, "StatusText", new Vector2(PanelCenterX, -20), 19,
            sizeDelta: new Vector2(700, 34), color: UIFactory.Accent, style: FontStyle.Bold);
        statusText.text = "Place bets, then DEAL";

        BuildBetSpot(BaccaratBetType.Player, new Vector2(-260, -140), UIFactory.Positive,    new Vector2(280, 110));
        BuildBetSpot(BaccaratBetType.Tie,    new Vector2(   0, -140), new Color(0.55f, 0.45f, 0.15f), new Vector2(200, 110));
        BuildBetSpot(BaccaratBetType.Banker, new Vector2( 260, -140), UIFactory.Negative,   new Vector2(280, 110));

        BuildActionButtons();
        BuildStreakBadge();

        RefreshActionButtons();
        RefreshBetDisplay();
    }

    // Same construction pattern as blackjack's streak badge: framed black panel +
    // TMP + Text Animator, built while the GameObject is still ACTIVE (TMP's
    // outlineWidth/outlineColor throw if set on an already-inactive object).
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
        textRt.anchoredPosition = Vector2.zero;
        streakText = textGO.AddComponent<TextMeshProUGUI>();
        streakText.alignment = TextAlignmentOptions.Center;
        streakText.fontStyle = FontStyles.Bold;
        streakText.raycastTarget = false;
        // Fixed size, not autosize — TextAnimator_TMP's SetText() doesn't trigger
        // TMP's autosize recalculation, so it kept rendering at fontSizeMax
        // regardless of content length and spilling out of the badge anyway.
        streakText.enableWordWrapping = false;
        streakText.fontSize = 20;
        streakText.outlineWidth = 0.25f;
        streakText.outlineColor = new Color32(0, 0, 0, 230);
        streakAnimator = textGO.AddComponent<TextAnimator_TMP>();

        streakBadgeGO.SetActive(false);
    }

    void BuildBetSpot(BaccaratBetType type, Vector2 pos, Color accentColor, Vector2 size)
    {
        var go = new GameObject($"BetSpot_{type}");
        go.transform.SetParent(tableRoot, false);
        var rt = go.AddComponent<RectTransform>();
        rt.sizeDelta = size;
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
            sizeDelta: new Vector2(size.x - 20, 50), color: UIFactory.TextDim, style: FontStyle.Bold);
        amountText.text = type.ToString().ToUpperInvariant();
        amountText.alignment = TextAnchor.MiddleCenter;
        var amountShadow = amountText.gameObject.AddComponent<Shadow>();
        amountShadow.effectColor = new Color(0, 0, 0, 0.85f);
        amountShadow.effectDistance = new Vector2(1, -1);

        string payoutLabel = type == BaccaratBetType.Tie ? "PAYS 8:1"
            : type == BaccaratBetType.Banker ? "PAYS 0.95:1" : "PAYS 1:1";
        var payoutText = UIFactory.MakeText(go.transform, "PayoutText", new Vector2(0, -28), 14,
            sizeDelta: new Vector2(size.x - 20, 22), color: UIFactory.TextDim, style: FontStyle.Normal);
        payoutText.text = payoutLabel;
        payoutText.alignment = TextAnchor.MiddleCenter;
        var payoutShadow = payoutText.gameObject.AddComponent<Shadow>();
        payoutShadow.effectColor = new Color(0, 0, 0, 0.85f);
        payoutShadow.effectDistance = new Vector2(1, -1);

        spots[type] = new BetSpot { Type = type, Root = go, FillImg = fill, AmountText = amountText, PayoutText = payoutText };
    }

    void BuildActionButtons()
    {
        const float y = -390f;
        clearBaseColor = UIFactory.RedBet;
        dealBaseColor = UIFactory.Positive;
        repeatBaseColor = UIFactory.AccentDim;

        clearBetButton = UIFactory.MakeButton(tableRoot, "ClearBetBtn", new Vector2(-183f, y), new Vector2(161, 53),
            "CLEAR BET", clearBaseColor, OnClearBetClicked, 13, pixelFont: true);
        dealButton = UIFactory.MakeButton(tableRoot, "DealBtn", new Vector2(0f, y), new Vector2(184, 62),
            "DEAL", dealBaseColor, OnDealClicked, 20, pixelFont: true);
        repeatButton = UIFactory.MakeButton(tableRoot, "RepeatBetBtn", new Vector2(183f, y), new Vector2(161, 53),
            "REPEAT BET", repeatBaseColor, OnRepeatBetClicked, 12, pixelFont: true);
        undoButton = UIFactory.MakeButton(tableRoot, "UndoBtn", new Vector2(-363f, y), new Vector2(138, 53),
            "UNDO", UIFactory.AccentDim, UndoLastBetAction, 13, pixelFont: true);
    }

    // ---- Betting (pre-round) ----

    // A blocked action used to only change the status text — easy to miss mid-
    // click. A quick shake makes it felt, not just read.
    void FlashBlocked() => juiceManager?.MicroShake(1.2f);

    void OnSpotClicked(BaccaratBetType type)
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

    void PushUndoSnapshot()
    {
        undoStack.Add(new Dictionary<BaccaratBetType, long>(pendingBets));
        if (undoStack.Count > MaxUndoDepth) undoStack.RemoveAt(0);
    }

    void UndoLastBetAction()
    {
        if (roundActive) return;
        if (undoStack.Count == 0)
        {
            statusText.text = "Nothing to undo";
            FlashBlocked();
            return;
        }
        var snapshot = undoStack[undoStack.Count - 1];
        undoStack.RemoveAt(undoStack.Count - 1);
        foreach (var t in spots.Keys.ToList()) pendingBets[t] = snapshot.TryGetValue(t, out long amount) ? amount : 0;
        RebuildAllChipVisuals();
        RefreshBetDisplay();
        RefreshActionButtons();
        soundManager?.PlayClick();
    }

    void RebuildAllChipVisuals()
    {
        foreach (var t in spots.Keys.ToList())
        {
            ClearBetChipVisuals(spots[t]);
            if (pendingBets[t] <= 0) continue;
            int chipCount = Mathf.Clamp((int)(pendingBets[t] / ChipDenominations.Values[0]), 1, 5);
            for (int i = 0; i < chipCount; i++) AddBetChipVisual(spots[t], -1);
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
        int stackIndex = spot.ChipVisuals.Count;
        float fanX = (stackIndex % 2 == 0 ? -1f : 1f) * (8f + stackIndex * 2f) + UnityEngine.Random.Range(-3f, 3f);
        // Payout text is hidden the moment a chip lands (see RefreshBetDisplay), so
        // the pile only has to clear the amount text above it, not the payout label.
        float fanY = -50f + Mathf.Min(stackIndex, 4) * 8f;
        rt.anchoredPosition = new Vector2(fanX, fanY);

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
        long lastTotal = lastBets.Values.Sum();
        if (lastTotal <= 0)
        {
            statusText.text = "No previous bet to repeat";
            FlashBlocked();
            return;
        }
        if (!bankroll.CanAfford(lastTotal))
        {
            statusText.text = "Not enough balance to repeat that bet";
            FlashBlocked();
            return;
        }
        PushUndoSnapshot();
        soundManager?.PlayChip();
        JuiceTweens.Pulse(this, repeatButton.GetComponent<RectTransform>(), peakScale: 1.15f, duration: 0.2f);
        foreach (var t in spots.Keys.ToList()) pendingBets[t] = lastBets.TryGetValue(t, out long amount) ? amount : 0;
        RebuildAllChipVisuals();
        RefreshBetDisplay();
        RefreshActionButtons();
    }

    // ---- Round flow ----

    void OnDealClicked()
    {
        long total = pendingBets.Values.Sum();
        if (roundActive || total <= 0) return;
        if (!bankroll.TryWithdraw(total))
        {
            statusText.text = "Not enough balance to deal";
            FlashBlocked();
            return;
        }

        if (shoe.NeedsReshuffle)
            milestoneToast?.Show("New shoe — reshuffling", UIFactory.Accent, fontSize: 24);

        roundActive = true;
        undoStack.Clear();
        foreach (var t in spots.Keys.ToList()) lastBets[t] = pendingBets[t];
        currentRound = new BaccaratRound(shoe);
        currentRound.Deal();
        foreach (var t in spots.Keys.ToList()) pendingBets[t] = 0;

        statusText.color = UIFactory.Accent;
        statusText.text = "Dealing...";

        // Last round's hands stay on the table until now — this is the moment a new
        // one actually starts, so sweep them out right as the new deal begins rather
        // than on a timer, and let friends see the finished hand for as long as they
        // like in between.
        playerHandUI.Clear();
        bankerHandUI.Clear();

        foreach (var spot in spots.Values) ClearBetChipVisuals(spot);
        RefreshBetDisplay();
        RefreshActionButtons();
        StartCoroutine(DealRevealSequence());
    }

    // Reveals cards one at a time in real dealing order (player, banker, player,
    // banker, then any third cards) instead of snapping the whole result in at once
    // — same trick blackjack's DealRevealSequence uses, just simpler since the whole
    // round is already decided the instant Deal() returns. Slower than a typical
    // "step" pace, with an extra beat before the third card specifically — that one
    // reads as "wait, one more card" rather than blurring into the same rhythm.
    IEnumerator DealRevealSequence()
    {
        const float stepDelay = 0.4f;
        const float thirdCardPause = 0.35f;
        int maxCards = Mathf.Max(currentRound.Player.Cards.Count, currentRound.Banker.Cards.Count);

        for (int step = 1; step <= maxCards; step++)
        {
            if (step == 3) yield return new WaitForSeconds(thirdCardPause);

            if (step <= currentRound.Player.Cards.Count)
            {
                playerHandUI.Render(currentRound.Player, maxCards: step);
                soundManager?.PlayChip();
                yield return new WaitForSeconds(stepDelay);
            }
            if (step <= currentRound.Banker.Cards.Count)
            {
                bankerHandUI.Render(currentRound.Banker, maxCards: step);
                soundManager?.PlayChip();
                yield return new WaitForSeconds(stepDelay);
            }
        }

        ResolveRound();
    }

    // ---- Resolution + juice ----

    void ResolveRound()
    {
        long totalStaked = lastBets.Values.Sum();
        long totalReturned = lastBets.Sum(kv => BaccaratResolver.Payout(kv.Key, kv.Value, currentRound.Outcome));
        bankroll.Deposit(totalReturned);

        long net = totalReturned - totalStaked;
        bool tieBetWon = lastBets.TryGetValue(BaccaratBetType.Tie, out long tieBet) && tieBet > 0 && currentRound.Outcome == BaccaratOutcome.Tie;

        statusText.color = net > 0 ? UIFactory.Positive : net < 0 ? UIFactory.Negative : UIFactory.Accent;
        string outcomeLabel = DescribeOutcome(currentRound.Outcome);
        string flavor = net > 0 ? WinFlavors[UnityEngine.Random.Range(0, WinFlavors.Length)]
            : net < 0 ? LoseFlavors[UnityEngine.Random.Range(0, LoseFlavors.Length)]
            : "Press DEAL again";
        statusText.text = $"{outcomeLabel}  ({(net >= 0 ? "+" : "")}{UIFactory.FormatMoney(net)})  — {flavor}";

        if (net > 0)
        {
            soundManager?.PlayWin();
            if (tieBetWon)
            {
                juiceManager?.Shake(0.5f, 4f);
                juiceManager?.Flash(new Color(0.3f, 1f, 0.4f, 0.28f), 0.7f);
                juiceManager?.PlayConfetti(2f);
                juiceManager?.PulseLight(0.9f, 0.7f);
                juiceManager?.PlayMoneyFountain(Vector2.zero);
                floatingText?.Show($"TIE PAYS 8:1! +{UIFactory.FormatMoney(net)}", UIFactory.Positive, fontSize: 42);
            }
            else if (net >= ChipDenominations.Values[2]) // $500+
            {
                juiceManager?.Shake(0.5f, 4f);
                juiceManager?.Flash(new Color(0.3f, 1f, 0.4f, 0.28f), 0.7f);
                juiceManager?.PlayConfetti(2f); juiceManager?.PulseLight(0.9f, 0.7f);
                juiceManager?.PlayMoneyFountain(Vector2.zero);
                floatingText?.Show($"HUGE WIN! +{UIFactory.FormatMoney(net)}", UIFactory.Positive, fontSize: 42);
            }
            else if (net >= ChipDenominations.Values[0] * 4L) // $100+
            {
                juiceManager?.Shake(0.3f, 2f);
                juiceManager?.Flash(new Color(0.25f, 0.9f, 0.35f, 0.18f), 0.5f);
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
            juiceManager?.Shake(0.2f, 1f);
            juiceManager?.Flash(new Color(0.85f, 0.2f, 0.2f, 0.14f), 0.4f);
            floatingText?.Show($"{UIFactory.FormatMoney(net)}", UIFactory.Negative);
            winStreak = 0;
        }
        else
        {
            soundManager?.PlayClick();
            floatingText?.Show("PUSH", UIFactory.Accent);
            // Push is neither a win nor a loss — streak carries through unchanged.
        }
        bool showStreak = winStreak >= 2;
        streakAnimator.SetText(showStreak ? $"<wave><rainb>{winStreak} WIN STREAK</rainb></wave>" : "");
        streakBadgeGO.SetActive(showStreak);
        if (showStreak) JuiceTweens.Pulse(this, (RectTransform)streakBadgeGO.transform, peakScale: 1.15f, duration: 0.3f);

        var record = new BaccaratRoundRecord(roundIndex, currentRound.Player.Point, currentRound.Banker.Point,
            currentRound.Outcome, totalStaked, totalReturned, bankroll.Balance);
        onRoundResolved?.Invoke(record);
        roundIndex++;
        int[] handTargets = { 50, 100, 250, 500, 1000 };
        foreach (var t in handTargets)
            if (roundIndex == t && roundMilestonesFired.Add(t))
                milestoneToast?.Show($"{t} Hands This Session", UIFactory.Accent, fontSize: 26);

        roundActive = false;
        RefreshBetDisplay(); // bet spots reappear right away; the finished hand stays up until the next deal
        RefreshActionButtons();
    }

    static string DescribeOutcome(BaccaratOutcome outcome) => outcome switch
    {
        BaccaratOutcome.PlayerWin => "Player wins",
        BaccaratOutcome.BankerWin => "Banker wins",
        BaccaratOutcome.Tie => "Tie!",
        _ => ""
    };

    // ---- Display refresh ----

    void RefreshBetDisplay()
    {
        bool showSpots = !roundActive;
        foreach (var kv in spots)
        {
            kv.Value.Root.SetActive(showSpots);
            long amount = pendingBets[kv.Key];
            kv.Value.AmountText.text = amount > 0 ? UIFactory.FormatMoney(amount) : kv.Key.ToString().ToUpperInvariant();
            kv.Value.AmountText.color = amount > 0 ? UIFactory.TextLight : UIFactory.TextDim;
            kv.Value.PayoutText.gameObject.SetActive(amount <= 0);
        }
    }

    void RefreshActionButtons()
    {
        long total = pendingBets.Values.Sum();
        bool canDeal = !roundActive && total > 0;
        UIFactory.SetButtonState(dealButton, dealBaseColor, canDeal);
        UIFactory.SetButtonState(clearBetButton, clearBaseColor, !roundActive && total > 0);
        UIFactory.SetButtonState(repeatButton, repeatBaseColor, !roundActive && lastBets.Values.Sum() > 0);
        UIFactory.SetButtonState(undoButton, UIFactory.AccentDim, !roundActive && undoStack.Count > 0);
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
        streakBadgeGO.SetActive(false);
        undoStack.Clear();
        foreach (var t in spots.Keys.ToList()) { pendingBets[t] = 0; lastBets[t] = 0; }
        foreach (var spot in spots.Values) ClearBetChipVisuals(spot);
        playerHandUI.Clear();
        bankerHandUI.Clear();
        statusText.color = UIFactory.Accent;
        statusText.text = "Place bets, then DEAL";
        RefreshBetDisplay();
        RefreshActionButtons();
    }
}
