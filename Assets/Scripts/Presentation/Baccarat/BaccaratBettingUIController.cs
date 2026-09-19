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
    Action onBankrollChanged;

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

    // Chips on the felt for the next hand (already out of the wallet), and the ones riding on the hand being dealt
    readonly Dictionary<BaccaratBetType, long> pendingBets = new Dictionary<BaccaratBetType, long>();
    readonly Dictionary<BaccaratBetType, long> roundBets = new Dictionary<BaccaratBetType, long>();
    readonly Dictionary<BaccaratBetType, long> lastBets = new Dictionary<BaccaratBetType, long>();
    readonly List<Dictionary<BaccaratBetType, long>> undoStack = new List<Dictionary<BaccaratBetType, long>>();
    const int MaxUndoDepth = 30;

    Transform tableRoot;
    BaccaratHandUI playerHandUI;
    BaccaratHandUI bankerHandUI;

    Text statusText;
    Text hintText;
    Button dealButton, clearBetButton, repeatButton, undoButton;
    Color dealBaseColor, clearBaseColor, repeatBaseColor;
    Coroutine clearResultRoutine;

    class BetSpot
    {
        public BaccaratBetType Type;
        public GameObject Root;
        public Image FillImg;
        public Text AmountText;
        public Text ResultText;
        public readonly List<GameObject> ChipVisuals = new List<GameObject>();
    }

    readonly Dictionary<BaccaratBetType, BetSpot> spots = new Dictionary<BaccaratBetType, BetSpot>();
    // Same red/blue/white-by-denomination mapping ChipSelectorUI uses for its own chip buttons
    static readonly Color[] ChipStackColors =
    {
        new Color(0.65f, 0.12f, 0.12f),
        new Color(0.1f, 0.35f, 0.6f),
        UIFactory.Chip500White,
    };

    // Layout (canvas coordinates) — same felt footprint as the other card tables
    static readonly Vector2 FeltCenter = new Vector2(-140f, -175f);
    static readonly Vector2 FeltSize = new Vector2(1580f, 700f);
    static readonly Vector2 CardSize = new Vector2(88f, 124f);
    const float CardSpacing = 98f; // wider than the cards so they never overlap
    const float HandOffsetX = 330f, HandY = 60f, SpotY = -300f;
    static readonly Color RailColor = new Color(0.30f, 0.22f, 0.10f);
    static readonly Color FeltText = new Color(0.82f, 0.86f, 0.80f);
    static readonly Color TitleGold = new Color(1f, 0.85f, 0.1f);
    // Player blue, Banker red — the colors every baccarat layout uses
    static readonly Color PlayerBlue = new Color(0.35f, 0.60f, 1f);
    static readonly Color BankerRed = new Color(1f, 0.38f, 0.35f);
    static readonly Color TieGreen = new Color(0.45f, 0.85f, 0.45f);

    static readonly string[] WinFlavors = { "Nice bet!", "There it is!", "Press DEAL again", "Keep it going!", "Well played" };
    static readonly string[] LoseFlavors = { "Press DEAL again", "Try again", "Onward", "Next hand's yours", "Deal again" };

    public void Build(Transform canvas, Bankroll bankroll, ChipSelectorUI chipSelector, Shoe shoe,
        SoundManager soundManager, JuiceManager juiceManager, FloatingTextUI floatingText, FloatingTextUI milestoneToast,
        Action<BaccaratRoundRecord> onRoundResolved, Action onBankrollChanged)
    {
        this.bankroll = bankroll;
        this.chipSelector = chipSelector;
        this.shoe = shoe;
        this.soundManager = soundManager;
        this.juiceManager = juiceManager;
        this.floatingText = floatingText;
        this.milestoneToast = milestoneToast;
        this.onRoundResolved = onRoundResolved;
        this.onBankrollChanged = onBankrollChanged;

        foreach (BaccaratBetType t in Enum.GetValues(typeof(BaccaratBetType))) { pendingBets[t] = 0; roundBets[t] = 0; lastBets[t] = 0; }

        var tableRootGO = new GameObject("BaccaratUIRoot");
        tableRootGO.transform.SetParent(canvas, false);
        var tableRootRT = tableRootGO.AddComponent<RectTransform>();
        tableRootRT.anchorMin = new Vector2(0.5f, 0.5f);
        tableRootRT.anchorMax = new Vector2(0.5f, 0.5f);
        tableRootRT.pivot = new Vector2(0.5f, 0.5f);
        tableRootRT.anchoredPosition = Vector2.zero;
        tableRoot = tableRootGO.transform;

        // Top band under the HUD: status bar, betting buttons, right-click tip (same spots as the other games)
        var statusPanelBg = UIFactory.MakePanel(tableRoot, "StatusPanelBg", new Vector2(0, 350), new Vector2(600, 40), UIFactory.PanelDark, shadow: false);
        UIFactory.AddSharpFrame(statusPanelBg, UIFactory.AccentDim, square: true);
        statusText = UIFactory.MakeText(tableRoot, "StatusText", new Vector2(0, 350), 19,
            sizeDelta: new Vector2(590, 36), color: UIFactory.Accent, style: FontStyle.Bold);
        statusText.text = "Place bets, then DEAL";

        BuildRulesCard();

        // Felt with a wooden rail: Player and Banker hands on top, the three betting boxes below
        var rail = UIFactory.MakePanel(tableRoot, "FeltRail", FeltCenter, FeltSize + new Vector2(24f, 24f), RailColor);
        UIFactory.AddSharpFrame(rail, new Color(0.62f, 0.52f, 0.25f), square: true);
        UIFactory.MakePanel(tableRoot, "Felt", FeltCenter, FeltSize, UIFactory.FeltGreen, shadow: false);
        float top = FeltCenter.y + FeltSize.y / 2f;
        UIFactory.MakeText(tableRoot, "PlayerLabel", new Vector2(FeltCenter.x - HandOffsetX, top - 24f), 20,
            TextAnchor.MiddleCenter, new Vector2(300, 28), PlayerBlue, FontStyle.Bold).text = "PLAYER";
        UIFactory.MakeText(tableRoot, "BankerLabel", new Vector2(FeltCenter.x + HandOffsetX, top - 24f), 20,
            TextAnchor.MiddleCenter, new Vector2(300, 28), BankerRed, FontStyle.Bold).text = "BANKER";
        UIFactory.MakeText(tableRoot, "FeltTitle", new Vector2(FeltCenter.x, HandY + 10f), 30,
            TextAnchor.MiddleCenter, new Vector2(300, 40), TitleGold, FontStyle.Bold).text = "BACCARAT";
        UIFactory.MakeText(tableRoot, "FeltSub", new Vector2(FeltCenter.x, HandY - 24f), 15,
            TextAnchor.MiddleCenter, new Vector2(300, 24), FeltText).text = "8-DECK SHOE";
        UIFactory.MakeText(tableRoot, "FeltRules", new Vector2(FeltCenter.x, -120f), 16,
            TextAnchor.MiddleCenter, new Vector2(1000, 24), FeltText).text =
            "BANKER WINS PAY 1 TO 1 LESS 5% COMMISSION   ·   TIE PAYS 8 TO 1";

        playerHandUI = new BaccaratHandUI();
        playerHandUI.Build(tableRoot, new Vector2(FeltCenter.x - HandOffsetX, HandY), CardSize, CardSpacing);
        bankerHandUI = new BaccaratHandUI();
        bankerHandUI.Build(tableRoot, new Vector2(FeltCenter.x + HandOffsetX, HandY), CardSize, CardSpacing);

        BuildBetSpot(BaccaratBetType.Player, new Vector2(FeltCenter.x - 430f, SpotY), new Vector2(400, 170), PlayerBlue, "PAYS 1 TO 1");
        BuildBetSpot(BaccaratBetType.Tie,    new Vector2(FeltCenter.x,        SpotY), new Vector2(300, 170), TieGreen, "PAYS 8 TO 1");
        BuildBetSpot(BaccaratBetType.Banker, new Vector2(FeltCenter.x + 430f, SpotY), new Vector2(400, 170), BankerRed, "PAYS 1 TO 1 LESS 5%");

        hintText = UIFactory.MakeText(tableRoot, "HintText", new Vector2(FeltCenter.x, SpotY - 150f), 15,
            sizeDelta: new Vector2(600, 24), color: FeltText);

        BuildActionButtons();
        BuildTakeDownTip();
        BuildStreakBadge();

        RefreshActionButtons();
        RefreshBetDisplay();
    }

    // Rules at a glance, in the free space between the chips and the HUD (same card as the other tables)
    void BuildRulesCard()
    {
        var center = new Vector2(-495f, 340f);
        var bg = UIFactory.MakePanel(tableRoot, "RulesCardBg", center, new Vector2(330f, 290f), UIFactory.PanelDark, shadow: false);
        UIFactory.AddSharpFrame(bg, UIFactory.AccentDim, square: true);
        (string text, bool header)[] lines =
        {
            ("PAYOUTS", true),
            ("Player pays 1 to 1", false),
            ("Banker pays 1 to 1 less 5% commission", false),
            ("Tie pays 8 to 1  ·  Player/Banker push", false),
            ("THIRD CARD", true),
            ("Natural 8 or 9: nobody draws", false),
            ("Player draws on 0-5, stands on 6-7", false),
            ("Banker draws by the house table", false),
            ("No decisions after DEAL", false),
        };
        for (int i = 0; i < lines.Length; i++)
        {
            var (text, header) = lines[i];
            UIFactory.MakeText(tableRoot, $"RuleLine{i}", center + new Vector2(0f, 120f - i * 30f), header ? 16 : 15,
                TextAnchor.MiddleCenter, new Vector2(310f, 28f),
                header ? TitleGold : FeltText, header ? FontStyle.Bold : FontStyle.Normal).text = text;
        }
    }

    // One framed line under the betting buttons
    void BuildTakeDownTip()
    {
        var tip = UIFactory.MakePanel(tableRoot, "TakeDownTipBg", new Vector2(0f, 238f), new Vector2(360f, 26f), UIFactory.PanelDarker, shadow: false);
        UIFactory.AddSharpFrame(tip, UIFactory.AccentDim, square: true);
        UIFactory.MakeText(tableRoot, "TakeDownTip", new Vector2(0f, 238f), 14, TextAnchor.MiddleCenter,
            new Vector2(350f, 24f), UIFactory.TextLight).text = "RIGHT-CLICK a bet to take it down";
    }

    // Same construction pattern as blackjack's streak badge: framed black panel +
    // TMP + Text Animator, built while the GameObject is still ACTIVE (TMP's
    // outlineWidth/outlineColor throw if set on an already-inactive object).
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
        var textRt = textGO.AddComponent<RectTransform>();
        textRt.sizeDelta = new Vector2(250, 32);
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

    void BuildBetSpot(BaccaratBetType type, Vector2 pos, Vector2 size, Color accent, string payout)
    {
        var go = new GameObject($"BetSpot_{type}");
        go.transform.SetParent(tableRoot, false);
        var rt = go.AddComponent<RectTransform>();
        rt.sizeDelta = size;
        rt.anchoredPosition = pos;
        var fill = go.AddComponent<Image>();
        fill.sprite = UIFactory.RoundedRect();
        fill.type = Image.Type.Sliced;
        fill.color = new Color(0f, 0f, 0f, 0.22f);
        UIFactory.AddSharpFrame(go, accent, square: true);
        var btn = go.AddComponent<Button>();
        btn.targetGraphic = fill;
        btn.onClick.AddListener(() => OnSpotClicked(type));
        RightClickRelay.Attach(go, () => TakeDown(type));

        UIFactory.MakeText(go.transform, "NameText", new Vector2(0, 58), 24,
            TextAnchor.MiddleCenter, new Vector2(size.x - 20, 32), accent, FontStyle.Bold).text = type.ToString().ToUpperInvariant();
        UIFactory.MakeText(go.transform, "PayoutText", new Vector2(0, 32), 14,
            TextAnchor.MiddleCenter, new Vector2(size.x - 20, 22), FeltText).text = payout;

        var amountText = UIFactory.MakeText(go.transform, "AmountText", new Vector2(0, -66), 18,
            TextAnchor.MiddleCenter, new Vector2(size.x - 20, 26), UIFactory.TextLight, FontStyle.Bold);
        var amountShadow = amountText.gameObject.AddComponent<Shadow>();
        amountShadow.effectColor = new Color(0, 0, 0, 0.85f);
        amountShadow.effectDistance = new Vector2(1, -1);

        var resultText = UIFactory.MakeText(tableRoot, $"Result_{type}", pos + new Vector2(0, -size.y / 2f - 20f), 18,
            TextAnchor.MiddleCenter, new Vector2(size.x, 26), UIFactory.TextLight, FontStyle.Bold);
        resultText.supportRichText = true;
        resultText.text = "";

        spots[type] = new BetSpot { Type = type, Root = go, FillImg = fill, AmountText = amountText, ResultText = resultText };
    }

    // Betting row sits in the top band, same spots as every other table
    const float BettingButtonY = 295f;

    void BuildActionButtons()
    {
        clearBaseColor = UIFactory.RedBet;
        dealBaseColor = UIFactory.Positive;
        repeatBaseColor = UIFactory.AccentDim;

        undoButton = UIFactory.MakeButton(tableRoot, "UndoBtn", new Vector2(-230f, BettingButtonY), new Vector2(110, 46),
            "UNDO", UIFactory.AccentDim, UndoLastBetAction, 13, pixelFont: true);
        clearBetButton = UIFactory.MakeButton(tableRoot, "ClearBetBtn", new Vector2(-105f, BettingButtonY), new Vector2(130, 46),
            "CLEAR BET", clearBaseColor, OnClearBetClicked, 13, pixelFont: true);
        dealButton = UIFactory.MakeButton(tableRoot, "DealBtn", new Vector2(45f, BettingButtonY), new Vector2(150, 50),
            "DEAL", dealBaseColor, OnDealClicked, 20, pixelFont: true);
        repeatButton = UIFactory.MakeButton(tableRoot, "RepeatBetBtn", new Vector2(195f, BettingButtonY), new Vector2(130, 46),
            "REPEAT BET", repeatBaseColor, OnRepeatBetClicked, 12, pixelFont: true);
    }

    // ---- Betting (pre-round) ----
    // Chips leave the wallet the moment they're placed (same as every other table), so the
    // balance always shows what's still in your pocket; undo / clear / take-down put it back.

    // A blocked action used to only change the status text — easy to miss mid-
    // click. A quick shake makes it felt, not just read.
    void FlashBlocked() => juiceManager?.MicroShake(1.2f);

    void OnSpotClicked(BaccaratBetType type)
    {
        if (roundActive) return;
        long chip = chipSelector.SelectedChip;
        if (!bankroll.CanAfford(chip))
        {
            statusText.text = bankroll.Balance < ChipDenominations.Values[0]
                ? "Out of chips — use ADD FUNDS above to keep playing"
                : "Not enough balance for that bet";
            FlashBlocked();
            return;
        }
        ClearLastResult();
        PushUndoSnapshot();
        bankroll.TryWithdraw(chip);
        pendingBets[type] += chip;
        soundManager?.PlayChip();
        JuiceTweens.Pulse(this, (RectTransform)spots[type].Root.transform, peakScale: 1.06f, duration: 0.18f);
        onBankrollChanged?.Invoke();
        RefreshBetDisplay();
        RefreshActionButtons();
    }

    // Moves the chips on the felt to exactly these amounts; the difference goes back to (or comes out of) the wallet.
    bool SetPendingBets(Dictionary<BaccaratBetType, long> target)
    {
        long current = pendingBets.Values.Sum();
        long wanted = target.Values.Sum();
        if (wanted - current > bankroll.Balance) return false;
        bankroll.Deposit(current);
        foreach (var t in spots.Keys.ToList())
        {
            pendingBets[t] = target.TryGetValue(t, out long amount) ? amount : 0;
        }
        bankroll.TryWithdraw(wanted);
        onBankrollChanged?.Invoke();
        RefreshBetDisplay();
        RefreshActionButtons();
        return true;
    }

    void OnClearBetClicked()
    {
        if (roundActive) return;
        if (pendingBets.Values.Sum() <= 0) { statusText.text = "Nothing to clear"; FlashBlocked(); return; }
        PushUndoSnapshot();
        SetPendingBets(new Dictionary<BaccaratBetType, long>());
        soundManager?.PlayClick();
    }

    void TakeDown(BaccaratBetType type)
    {
        if (roundActive || pendingBets[type] <= 0) return;
        long amt = pendingBets[type];
        PushUndoSnapshot();
        var target = new Dictionary<BaccaratBetType, long>(pendingBets) { [type] = 0 };
        SetPendingBets(target);
        soundManager?.PlayClick();
        statusText.color = UIFactory.Accent;
        statusText.text = $"{type} bet down — {UIFactory.FormatMoney(amt)} back to wallet";
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
        SetPendingBets(snapshot);
        soundManager?.PlayClick();
    }

    void OnRepeatBetClicked()
    {
        if (roundActive) return;
        if (lastBets.Values.Sum() <= 0)
        {
            statusText.text = "No previous bet to repeat";
            FlashBlocked();
            return;
        }
        if (lastBets.Values.Sum() - pendingBets.Values.Sum() > bankroll.Balance)
        {
            statusText.text = "Not enough balance to repeat that bet";
            FlashBlocked();
            return;
        }
        ClearLastResult();
        PushUndoSnapshot();
        SetPendingBets(lastBets);
        soundManager?.PlayChip();
        JuiceTweens.Pulse(this, repeatButton.GetComponent<RectTransform>(), peakScale: 1.15f, duration: 0.2f);
    }

    // Chip pile in a betting box, largest denominations on top, colored like the chip selector.
    void RebuildChips(BetSpot spot, long amount)
    {
        foreach (var go in spot.ChipVisuals) Destroy(go);
        spot.ChipVisuals.Clear();
        var colors = new List<Color>();
        long remaining = amount;
        var denoms = ChipDenominations.Values;
        for (int d = denoms.Length - 1; d >= 0 && colors.Count < 5; d--)
            while (remaining >= denoms[d] && colors.Count < 5)
            {
                remaining -= denoms[d];
                colors.Add(ChipStackColors[d]);
            }
        colors.Reverse();
        for (int i = 0; i < colors.Count; i++)
        {
            var go = new GameObject($"BetChip_{i}");
            go.transform.SetParent(spot.Root.transform, false);
            var img = go.AddComponent<Image>();
            img.sprite = UIFactory.Circle();
            img.color = colors[i];
            img.raycastTarget = false;
            var edge = go.AddComponent<Outline>();
            edge.effectColor = new Color(0f, 0f, 0f, 0.8f);
            edge.effectDistance = new Vector2(1.5f, -1.5f);
            var rt = go.GetComponent<RectTransform>();
            rt.sizeDelta = new Vector2(48, 48);
            rt.anchoredPosition = new Vector2(0f, -22f + i * 4f);
            spot.ChipVisuals.Add(go);
        }
        spot.AmountText.transform.SetAsLastSibling();
    }

    // ---- Round flow ----

    void OnDealClicked()
    {
        long total = pendingBets.Values.Sum();
        if (roundActive || total <= 0) return;

        if (shoe.NeedsReshuffle)
            milestoneToast?.Show("New shoe — reshuffling", UIFactory.Accent, fontSize: 24);

        ClearLastResult();
        roundActive = true;
        undoStack.Clear();
        foreach (var t in spots.Keys.ToList())
        {
            lastBets[t] = pendingBets[t];
            roundBets[t] = pendingBets[t];
            pendingBets[t] = 0;
        }
        currentRound = new BaccaratRound(shoe);
        currentRound.Deal();

        statusText.color = UIFactory.Accent;
        statusText.text = "Dealing...";

        // Last round's hands stay on the table until now — this is the moment a new
        // one actually starts, so sweep them out right as the new deal begins.
        playerHandUI.Clear();
        bankerHandUI.Clear();

        // The chips stay in their boxes for the whole deal
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

        if (roundActive) ResolveRound();
    }

    // ---- Resolution + juice ----

    long RoundReturn() => roundBets.Sum(kv => kv.Value > 0 ? BaccaratResolver.Payout(kv.Key, kv.Value, currentRound.Outcome) : 0);

    void ResolveRound()
    {
        long totalStaked = roundBets.Values.Sum();
        long totalReturned = RoundReturn();
        bankroll.Deposit(totalReturned);
        onBankrollChanged?.Invoke();

        long net = totalReturned - totalStaked;
        bool tieBetWon = roundBets[BaccaratBetType.Tie] > 0 && currentRound.Outcome == BaccaratOutcome.Tie;

        // Per-box result under each betting box
        foreach (var kv in spots)
        {
            long stake = roundBets[kv.Key];
            if (stake <= 0) continue;
            long ret = BaccaratResolver.Payout(kv.Key, stake, currentRound.Outcome);
            kv.Value.ResultText.text = ret > stake ? $"<color=#6CE26C>WIN +{UIFactory.FormatMoney(ret - stake)}</color>"
                : ret == stake ? "PUSH" : $"<color=#FF6B6B>LOSE -{UIFactory.FormatMoney(stake)}</color>";
        }
        // The winning side's box lights up
        var winningType = currentRound.Outcome == BaccaratOutcome.PlayerWin ? BaccaratBetType.Player
            : currentRound.Outcome == BaccaratOutcome.BankerWin ? BaccaratBetType.Banker : BaccaratBetType.Tie;
        spots[winningType].FillImg.color = new Color(1f, 0.85f, 0.1f, 0.22f);
        JuiceTweens.Pulse(this, (RectTransform)spots[winningType].Root.transform, peakScale: 1.06f, duration: 0.3f);

        statusText.color = net > 0 ? UIFactory.Positive : net < 0 ? UIFactory.Negative : UIFactory.Accent;
        string outcomeLabel = DescribeOutcome(currentRound);
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
                floatingText?.Show($"TIE PAYS 8 TO 1! +{UIFactory.FormatMoney(net)}", UIFactory.Positive, fontSize: 42);
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

        roundActive = false;
        var record = new BaccaratRoundRecord(roundIndex, currentRound.Player.Point, currentRound.Banker.Point,
            currentRound.Outcome, totalStaked, totalReturned, bankroll.Balance);
        onRoundResolved?.Invoke(record);
        roundIndex++;
        int[] handTargets = { 50, 100, 250, 500, 1000 };
        foreach (var t in handTargets)
            if (roundIndex == t && roundMilestonesFired.Add(t))
                milestoneToast?.Show($"{t} Hands This Session", UIFactory.Accent, fontSize: 26);

        RefreshActionButtons();
        // The paid hand's chips and results stay a moment to read, then the boxes clear for the next bet;
        // the cards stay up until the next deal.
        clearResultRoutine = StartCoroutine(ClearResultAfterDelay());
    }

    IEnumerator ClearResultAfterDelay()
    {
        yield return new WaitForSeconds(2.2f);
        clearResultRoutine = null;
        ClearLastResult();
    }

    // Takes the finished hand's chips and result labels off the boxes (safe to call any time)
    void ClearLastResult()
    {
        if (clearResultRoutine != null) { StopCoroutine(clearResultRoutine); clearResultRoutine = null; }
        if (roundActive) return;
        foreach (var t in spots.Keys.ToList()) roundBets[t] = 0;
        foreach (var spot in spots.Values) spot.ResultText.text = "";
        RefreshBetDisplay();
    }

    static string DescribeOutcome(BaccaratRound round)
    {
        string how = round.Player.IsNatural || round.Banker.IsNatural ? " (natural)" : "";
        return round.Outcome switch
        {
            BaccaratOutcome.PlayerWin => $"Player wins {round.Player.Point} to {round.Banker.Point}{how}",
            BaccaratOutcome.BankerWin => $"Banker wins {round.Banker.Point} to {round.Player.Point}{how}",
            _ => $"Tie at {round.Player.Point}{how}",
        };
    }

    // ---- Leaving the table ----

    public long OnTableTotal() => roundActive ? 0 : pendingBets.Values.Sum();

    // Leaving the table: chips not yet dealt go back to the wallet; a hand mid-reveal is already decided
    // (Deal() resolves instantly), so it's paid out. Pure bankroll math — safe from OnDestroy / OnApplicationQuit
    // and safe to call twice.
    public void RefundTableBets()
    {
        if (roundActive && currentRound != null)
        {
            bankroll.Deposit(RoundReturn());
            foreach (var t in spots.Keys.ToList()) roundBets[t] = 0;
            roundActive = false;
        }
        long pending = pendingBets.Values.Sum();
        if (pending > 0) bankroll.Deposit(pending);
        foreach (var t in spots.Keys.ToList()) pendingBets[t] = 0;
    }

    // ---- Display refresh ----

    void RefreshBetDisplay()
    {
        foreach (var kv in spots)
        {
            // While a hand is out (and briefly after it pays), the boxes show what rode on it
            long shown = roundBets.Values.Sum() > 0 && pendingBets.Values.Sum() == 0 ? roundBets[kv.Key] : pendingBets[kv.Key];
            kv.Value.AmountText.text = shown > 0 ? UIFactory.FormatMoney(shown) : "";
            RebuildChips(kv.Value, shown);
            if (!roundActive && kv.Value.ResultText.text == "") kv.Value.FillImg.color = new Color(0f, 0f, 0f, shown > 0 ? 0.32f : 0.22f);
        }
        long pending = pendingBets.Values.Sum();
        hintText.text = roundActive ? "" : pending > 0 ? "Click DEAL when you're ready" : "Pick a chip, then click PLAYER, BANKER or TIE";
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

    // Used by the HUD's RESET button — the bankroll is reset right before this, so chips on the felt just vanish
    public void ResetRound()
    {
        StopAllCoroutines();
        clearResultRoutine = null;
        currentRound = null;
        roundActive = false;
        winStreak = 0;
        bestRoundNet = 0;
        doubledMilestoneFired = false;
        roundMilestonesFired.Clear();
        streakBadgeGO.SetActive(false);
        undoStack.Clear();
        foreach (var t in spots.Keys.ToList()) { pendingBets[t] = 0; lastBets[t] = 0; roundBets[t] = 0; }
        foreach (var spot in spots.Values) spot.ResultText.text = "";
        playerHandUI.Clear();
        bankerHandUI.Clear();
        statusText.color = UIFactory.Accent;
        statusText.text = "Place bets, then DEAL";
        RefreshBetDisplay();
        RefreshActionButtons();
    }
}
