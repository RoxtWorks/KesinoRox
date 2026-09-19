using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Febucci.UI;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

// Blackjack's equivalent of BettingUIController — owns bet building, the DEAL/HIT/
// STAND/DOUBLE/SPLIT/SURRENDER flow, and every juice/sound reaction to a resolved
// round. Talks only to Core types (Bankroll, Shoe, BlackjackRound, BlackjackResolver)
// for anything that affects money or odds; this class just captures clicks and
// displays state — same split as the roulette controller.
public class BlackjackBettingUIController : MonoBehaviour
{
    Bankroll bankroll;
    ChipSelectorUI chipSelector;
    Shoe shoe;
    SoundManager soundManager;
    JuiceManager juiceManager;
    FloatingTextUI floatingText;
    FloatingTextUI milestoneToast;
    Action<BlackjackRoundRecord> onRoundResolved;

    BlackjackRound currentRound;
    bool roundActive;
    long pendingBet;
    readonly List<long> undoStack = new List<long>();
    const int MaxUndoDepth = 30;
    long lastBetAmount;
    int roundIndex;
    int winStreak;
    bool doubledMilestoneFired;
    long bestRoundNet;
    readonly HashSet<int> roundMilestonesFired = new HashSet<int>();
    int lastKnownCardCount;
    bool insurancePromptWasVisible;

    Transform tableRoot;
    HandUI dealerHandUI;
    readonly List<HandUI> playerHandUIs = new List<HandUI>();
    Transform handUIParent;

    Text statusText;
    Text betText;
    Button dealButton, hitButton, standButton, doubleButton, splitButton, surrenderButton, clearBetButton, repeatButton, undoButton;
    // Tracks each action button's visibility from the previous refresh, so newly-
    // appearing ones (e.g. SPLIT becoming legal) get a pop-in instead of just
    // snapping into existence — and DEAL specifically pulses the moment it becomes
    // affordable, since that's the one button that stays on screen the whole time
    // rather than appearing/disappearing.
    readonly Dictionary<Button, bool> buttonWasVisible = new Dictionary<Button, bool>();
    bool dealWasEnabled;

    GameObject betSpotGO;
    Image betSpotFillImg;
    Text betSpotText;
    readonly List<GameObject> betChipVisuals = new List<GameObject>();
    // Same red/blue/black-by-denomination mapping ChipSelectorUI uses for its own
    // chip buttons, so a bet built from a given chip visually matches the chip that
    // placed it.
    static readonly Color[] ChipStackColors =
    {
        new Color(0.65f, 0.12f, 0.12f),
        new Color(0.1f, 0.35f, 0.6f),
        UIFactory.Chip500White,
    };

    GameObject insurancePromptGO;

    TextMeshProUGUI streakText;
    TextAnimator_TMP streakAnimator;
    GameObject streakBadgeGO;
    TextMeshProUGUI achievementText;
    TextAnimator_TMP achievementAnimator;
    GameObject achievementBadgeGO;
    Coroutine achievementHideRoutine;

    static readonly string[] WinFlavors = { "Nice hand!", "There it is!", "Press DEAL again", "Keep it going!", "Well played" };
    static readonly string[] LoseFlavors = { "Press DEAL again", "Try again", "Onward", "Next hand's yours", "Deal again" };

    Action onBankrollChanged;

    // Layout (canvas coordinates) — same felt footprint as the other card tables:
    // felt fills the space left of the History column and below the top-band buttons.
    static readonly Vector2 FeltCenter = new Vector2(-140f, -175f);
    static readonly Vector2 FeltSize = new Vector2(1580f, 700f);
    static readonly Vector2 DealerCardSize = new Vector2(92f, 128f);
    static readonly Vector2 PlayerCardSize = new Vector2(88f, 124f);
    const float DealerCardSpacing = 102f, PlayerCardSpacing = 96f; // wider than the cards so they never overlap
    const float PlayerHandY = -225f, BetSpotY = -385f;
    static readonly Color RailColor = new Color(0.30f, 0.22f, 0.10f);
    static readonly Color FeltText = new Color(0.82f, 0.86f, 0.80f);
    static readonly Color TitleGold = new Color(1f, 0.85f, 0.1f);

    public void Build(Transform canvas, Bankroll bankroll, ChipSelectorUI chipSelector, Shoe shoe,
        SoundManager soundManager, JuiceManager juiceManager, FloatingTextUI floatingText, FloatingTextUI milestoneToast,
        Action<BlackjackRoundRecord> onRoundResolved, Action onBankrollChanged)
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

        var tableRootGO = new GameObject("BlackjackUIRoot");
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
        statusText.text = "Place your bet, then DEAL";

        BuildRulesCard();

        // Felt with a wooden rail, dealer on top, your hand and bet circle below
        var rail = UIFactory.MakePanel(tableRoot, "FeltRail", FeltCenter, FeltSize + new Vector2(24f, 24f), RailColor);
        UIFactory.AddSharpFrame(rail, new Color(0.62f, 0.52f, 0.25f), square: true);
        UIFactory.MakePanel(tableRoot, "Felt", FeltCenter, FeltSize, UIFactory.FeltGreen, shadow: false);
        float top = FeltCenter.y + FeltSize.y / 2f;
        UIFactory.MakeText(tableRoot, "DealerLabel", new Vector2(FeltCenter.x, top - 24f), 18,
            TextAnchor.MiddleCenter, new Vector2(300, 26), TitleGold, FontStyle.Bold).text = "DEALER";
        UIFactory.MakeText(tableRoot, "FeltTitle", new Vector2(FeltCenter.x, top - 262f), 28,
            TextAnchor.MiddleCenter, new Vector2(800, 36), TitleGold, FontStyle.Bold).text = "BLACKJACK PAYS 3 TO 2";
        UIFactory.MakeText(tableRoot, "FeltRules", new Vector2(FeltCenter.x, top - 292f), 16,
            TextAnchor.MiddleCenter, new Vector2(900, 24), FeltText).text = "DEALER HITS SOFT 17   ·   INSURANCE PAYS 2 TO 1";

        handUIParent = tableRoot;
        dealerHandUI = new HandUI();
        dealerHandUI.Build(tableRoot, new Vector2(FeltCenter.x, top - 115f), DealerCardSize, DealerCardSpacing);

        // First (only, until a split happens) player hand slot.
        var firstHand = new HandUI();
        firstHand.Build(tableRoot, new Vector2(FeltCenter.x, PlayerHandY), PlayerCardSize, PlayerCardSpacing);
        playerHandUIs.Add(firstHand);

        betText = UIFactory.MakeText(tableRoot, "BetText", new Vector2(FeltCenter.x, BetSpotY - 78f), 15,
            sizeDelta: new Vector2(420, 24), color: FeltText);
        betText.text = "Pick a chip, then click the circle";

        // The bet circle stays on the felt the whole hand, showing everything riding on it
        betSpotGO = new GameObject("BetSpot");
        betSpotGO.transform.SetParent(tableRoot, false);
        var betSpotRt = betSpotGO.AddComponent<RectTransform>();
        betSpotRt.sizeDelta = new Vector2(130, 130);
        betSpotRt.anchoredPosition = new Vector2(FeltCenter.x, BetSpotY);
        betSpotFillImg = betSpotGO.AddComponent<Image>();
        betSpotFillImg.sprite = UIFactory.Circle();
        betSpotFillImg.color = new Color(0f, 0f, 0f, 0.22f);
        UIFactory.AddSharpFrame(betSpotGO, new Color(0.32f, 0.58f, 0.40f), square: false);
        var betSpotBtnComponent = betSpotGO.AddComponent<Button>();
        betSpotBtnComponent.targetGraphic = betSpotFillImg;
        betSpotBtnComponent.onClick.AddListener(OnBetSpotClicked);
        RightClickRelay.Attach(betSpotGO, TakeDownBet);

        betSpotText = UIFactory.MakeText(betSpotGO.transform, "BetSpotText", new Vector2(0, 34), 17,
            sizeDelta: new Vector2(120, 50), color: FeltText, style: FontStyle.Bold);
        betSpotText.text = "PLACE\nBET";
        var betSpotTextShadow = betSpotText.gameObject.AddComponent<Shadow>();
        betSpotTextShadow.effectColor = new Color(0, 0, 0, 0.85f);
        betSpotTextShadow.effectDistance = new Vector2(1, -1);

        BuildActionButtons();
        BuildTakeDownTip();
        BuildInsurancePrompt();
        BuildStreakBadge();
        BuildAchievementBadge();

        RefreshHandDisplays();
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
            ("Blackjack pays 3 to 2", false),
            ("Win pays 1 to 1  ·  Push returns bet", false),
            ("Insurance pays 2 to 1", false),
            ("DEALER", true),
            ("Hits soft 17, stands hard 17+", false),
            ("YOU MAY", true),
            ("Double any 2 cards (also after split)", false),
            ("Split pairs to 4 hands  ·  Surrender", false),
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
            new Vector2(350f, 24f), UIFactory.TextLight).text = "RIGHT-CLICK your bet to take it down";
    }

    // Betting row sits in the top band; the HIT / STAND / ... row sits on the felt right under your hand
    const float BettingButtonY = 295f;
    const float ActionButtonY = -495f;

    // Base (enabled) color per button, so toggling between enabled/disabled can swap
    // the Image color directly — Unity's built-in ColorBlock.disabledColor tint was
    // too subtle to read as "disabled" against this project's already-muted palette.
    Color dealBaseColor, clearBaseColor, repeatBaseColor;

    void BuildActionButtons()
    {
        dealBaseColor = UIFactory.Positive;
        clearBaseColor = UIFactory.RedBet;
        repeatBaseColor = UIFactory.AccentDim;

        undoButton = UIFactory.MakeButton(tableRoot, "UndoBtn", new Vector2(-230f, BettingButtonY), new Vector2(110, 46),
            "UNDO", UIFactory.AccentDim, UndoLastBetAction, 13, pixelFont: true);
        clearBetButton = UIFactory.MakeButton(tableRoot, "ClearBetBtn", new Vector2(-105f, BettingButtonY), new Vector2(130, 46),
            "CLEAR BET", clearBaseColor, OnClearBetClicked, 13, pixelFont: true);
        dealButton = UIFactory.MakeButton(tableRoot, "DealBtn", new Vector2(45f, BettingButtonY), new Vector2(150, 50),
            "DEAL", dealBaseColor, OnDealClicked, 20, pixelFont: true);
        repeatButton = UIFactory.MakeButton(tableRoot, "RepeatBetBtn", new Vector2(195f, BettingButtonY), new Vector2(130, 46),
            "REPEAT BET", repeatBaseColor, OnRepeatBetClicked, 12, pixelFont: true);

        // Action-phase row: HIT / STAND / DOUBLE / SPLIT / SURRENDER — built here but
        // repositioned and shown/hidden dynamically by LayoutActionButtons(), since
        // which ones apply changes hand to hand.
        hitButton = UIFactory.MakeButton(tableRoot, "HitBtn", new Vector2(0, ActionButtonY), new Vector2(150, 50),
            "HIT", UIFactory.Positive, OnHitClicked, 18, pixelFont: true);
        standButton = UIFactory.MakeButton(tableRoot, "StandBtn", new Vector2(0, ActionButtonY), new Vector2(150, 50),
            "STAND", UIFactory.AccentDim, OnStandClicked, 18, pixelFont: true);
        doubleButton = UIFactory.MakeButton(tableRoot, "DoubleBtn", new Vector2(0, ActionButtonY), new Vector2(150, 50),
            "DOUBLE", UIFactory.AccentDim, OnDoubleClicked, 17, pixelFont: true);
        splitButton = UIFactory.MakeButton(tableRoot, "SplitBtn", new Vector2(0, ActionButtonY), new Vector2(150, 50),
            "SPLIT", UIFactory.AccentDim, OnSplitClicked, 18, pixelFont: true);
        surrenderButton = UIFactory.MakeButton(tableRoot, "SurrenderBtn", new Vector2(0, ActionButtonY), new Vector2(170, 50),
            "SURRENDER", UIFactory.RedBet, OnSurrenderClicked, 14, pixelFont: true);

        foreach (var btn in new[] { hitButton, standButton, doubleButton, splitButton, surrenderButton })
            buttonWasVisible[btn] = false;
    }

    // Sets a betting-phase button's visible/enabled state — visibility is specific
    // to this row (shown only during betting, hidden during the action phase), the
    // enabled/color half is the shared UIFactory helper every controller uses.
    static void SetBettingButtonState(Button btn, Color baseColor, bool visible, bool enabled)
    {
        btn.gameObject.SetActive(visible);
        UIFactory.SetButtonState(btn, baseColor, enabled);
    }

    // Sits in the middle of the felt, over the table inscription, while the dealer's Ace asks the question
    void BuildInsurancePrompt()
    {
        insurancePromptGO = UIFactory.MakeFramedPanel(tableRoot, "InsurancePromptBg", new Vector2(FeltCenter.x, -85f), new Vector2(520, 100), Color.black);
        UIFactory.MakeText(insurancePromptGO.transform, "InsuranceText", new Vector2(0, 24), 18,
            sizeDelta: new Vector2(480, 28), color: UIFactory.Accent, style: FontStyle.Bold).text = "Dealer shows an Ace — take insurance?";
        UIFactory.MakeButton(insurancePromptGO.transform, "InsuranceYes", new Vector2(-110, -20), new Vector2(190, 40),
            "YES (half your bet)", UIFactory.Positive, OnInsuranceYes, 13, pixelFont: true);
        UIFactory.MakeButton(insurancePromptGO.transform, "InsuranceNo", new Vector2(110, -20), new Vector2(190, 40),
            "NO", UIFactory.RedBet, OnInsuranceNo, 14, pixelFont: true);
        insurancePromptGO.SetActive(false);
    }

    // Same construction pattern as roulette's streak/achievement badges: framed
    // black panel + TMP + Text Animator, built while the GameObject is still ACTIVE
    // (TMP's outlineWidth/outlineColor throw ArgumentNullException if set on an
    // already-inactive object — its material isn't initialized until first
    // Awake/OnEnable) and deactivated only once everything is configured.
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
        // TMP's autosize recalculation, so it just kept rendering at fontSizeMax
        // regardless of content length and spilling out of the badge anyway. 20pt
        // in a widened 280px box comfortably fits "12 WIN STREAK" and beyond.
        streakText.enableWordWrapping = false;
        streakText.fontSize = 20;
        streakText.outlineWidth = 0.25f;
        streakText.outlineColor = new Color32(0, 0, 0, 230);
        streakAnimator = textGO.AddComponent<TextAnimator_TMP>();

        streakBadgeGO.SetActive(false);
    }

    void BuildAchievementBadge()
    {
        achievementBadgeGO = new GameObject("AchievementBadge");
        achievementBadgeGO.transform.SetParent(tableRoot, false);
        var rt = achievementBadgeGO.AddComponent<RectTransform>();
        rt.sizeDelta = new Vector2(280, 90);
        rt.anchoredPosition = new Vector2(480, 465);
        UIFactory.MakeFramedPanel(achievementBadgeGO.transform, "AchievementBadgeBg", Vector2.zero, new Vector2(280, 90), Color.black);

        var textGO = new GameObject("AchievementText");
        textGO.transform.SetParent(achievementBadgeGO.transform, false);
        var textRt = textGO.AddComponent<RectTransform>();
        textRt.sizeDelta = new Vector2(260, 70);
        textRt.anchoredPosition = Vector2.zero;
        achievementText = textGO.AddComponent<TextMeshProUGUI>();
        achievementText.alignment = TextAlignmentOptions.Center;
        achievementText.fontSize = 24;
        achievementText.fontStyle = FontStyles.Bold;
        achievementText.raycastTarget = false;
        achievementText.enableWordWrapping = true;
        achievementText.outlineWidth = 0.25f;
        achievementText.outlineColor = new Color32(0, 0, 0, 230);
        achievementAnimator = textGO.AddComponent<TextAnimator_TMP>();

        achievementBadgeGO.SetActive(false);
    }

    // ---- Betting (pre-round) ----
    // Chips leave the wallet the moment they're placed (same as every other table), so the
    // balance always shows what's still in your pocket; undo / clear / take-down put it back.

    // A blocked action used to only change the status text — easy to miss mid-
    // click. A quick shake makes it felt, not just read.
    void FlashBlocked() => juiceManager?.MicroShake(1.2f);

    void OnBetSpotClicked()
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
        PushUndoSnapshot();
        bankroll.TryWithdraw(chip);
        pendingBet += chip;
        soundManager?.PlayChip();
        JuiceTweens.Pulse(this, (RectTransform)betSpotGO.transform, peakScale: 1.12f, duration: 0.18f);
        RebuildBetChips(pendingBet);
        onBankrollChanged?.Invoke();
        RefreshBetDisplay();
        RefreshActionButtons();
    }

    // Moves the bet on the felt to exactly this amount; the difference goes back to (or comes out of) the wallet.
    void SetPendingBet(long amount)
    {
        bankroll.Deposit(pendingBet);
        pendingBet = 0;
        if (amount > 0 && bankroll.TryWithdraw(amount)) pendingBet = amount;
        RebuildBetChips(pendingBet);
        onBankrollChanged?.Invoke();
        RefreshBetDisplay();
        RefreshActionButtons();
    }

    void OnClearBetClicked()
    {
        if (roundActive) return;
        if (pendingBet <= 0) { statusText.text = "Nothing to clear"; FlashBlocked(); return; }
        PushUndoSnapshot();
        SetPendingBet(0);
        soundManager?.PlayClick();
    }

    void TakeDownBet()
    {
        if (roundActive || pendingBet <= 0) return;
        long amt = pendingBet;
        PushUndoSnapshot();
        SetPendingBet(0);
        soundManager?.PlayClick();
        statusText.color = UIFactory.Accent;
        statusText.text = $"Bet down — {UIFactory.FormatMoney(amt)} back to wallet";
    }

    void PushUndoSnapshot()
    {
        undoStack.Add(pendingBet);
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
        long previous = undoStack[undoStack.Count - 1];
        undoStack.RemoveAt(undoStack.Count - 1);
        SetPendingBet(previous);
        soundManager?.PlayClick();
    }

    // Chip pile on the bet circle, largest denominations on top, colored like the chip selector.
    void RebuildBetChips(long amount)
    {
        ClearBetChipVisuals();
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
            go.transform.SetParent(betSpotGO.transform, false);
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
            betChipVisuals.Add(go);
        }
        betSpotText.transform.SetAsLastSibling(); // keep the amount readable over the pile
    }

    void ClearBetChipVisuals()
    {
        foreach (var go in betChipVisuals) Destroy(go);
        betChipVisuals.Clear();
    }

    void OnRepeatBetClicked()
    {
        if (roundActive) return;
        if (lastBetAmount <= 0)
        {
            statusText.text = "No previous bet to repeat";
            FlashBlocked();
            return;
        }
        if (!bankroll.CanAfford(lastBetAmount - pendingBet))
        {
            statusText.text = "Not enough balance to repeat that bet";
            FlashBlocked();
            return;
        }
        PushUndoSnapshot();
        SetPendingBet(lastBetAmount);
        soundManager?.PlayChip();
        JuiceTweens.Pulse(this, repeatButton.GetComponent<RectTransform>(), peakScale: 1.15f, duration: 0.2f);
    }

    public long OnTableTotal() => roundActive ? 0 : pendingBet;

    // Leaving the table: a bet not yet dealt goes back to the wallet. A hand in progress declines insurance,
    // stands every remaining hand, lets the dealer play out, and pays the result — pure round/bankroll math,
    // safe from OnDestroy / OnApplicationQuit and safe to call twice.
    public void RefundTableBets()
    {
        if (!roundActive)
        {
            if (pendingBet > 0) bankroll.Deposit(pendingBet);
            pendingBet = 0;
            return;
        }
        if (currentRound == null) return;
        if (currentRound.InsuranceOffered) currentRound.TakeInsurance(false);
        while (!currentRound.RoundOver) currentRound.Stand();
        long returned = currentRound.ResolveAll().Sum(r => r.payout) + currentRound.InsurancePayout;
        bankroll.Deposit(returned);
        roundActive = false;
    }

    // ---- Round flow ----

    void OnDealClicked()
    {
        if (roundActive || pendingBet <= 0) return;

        // Deal() itself reshuffles silently once penetration is hit — check it here
        // first, before that happens, so the player gets told why a "new" shoe just
        // showed up instead of it being an invisible background event.
        if (shoe.NeedsReshuffle)
            milestoneToast?.Show("New shoe — reshuffling", UIFactory.Accent, fontSize: 24);

        roundActive = true;
        undoStack.Clear();
        lastBetAmount = pendingBet;
        currentRound = new BlackjackRound(shoe);
        currentRound.Deal(pendingBet);
        pendingBet = 0;

        statusText.color = UIFactory.Accent;
        statusText.text = "Dealing...";

        // The chips stay on the circle for the whole hand; RefreshBetDisplay shows everything riding on it
        RefreshBetDisplay();
        StartCoroutine(DealRevealSequence());
    }

    // Reveals the opening deal one card at a time in real dealing order (player,
    // dealer, player, dealer) instead of all four cards snapping in simultaneously —
    // Core already resolved the whole hand instantly, this only staggers how it's
    // shown. Everything that used to run right after Deal() (insurance prompt, the
    // immediate-blackjack check, button refresh) now waits until the reveal finishes.
    IEnumerator DealRevealSequence()
    {
        const float stepDelay = 0.18f;
        var playerHand = currentRound.PlayerHands[0];
        var dealer = currentRound.Dealer;
        RepositionPlayerHandUIs(1);

        for (int step = 1; step <= 2; step++)
        {
            playerHandUIs[0].Render(playerHand, hideHoleCard: false, highlighted: false, maxCards: step);
            soundManager?.PlayChip();
            yield return new WaitForSeconds(stepDelay);
            dealerHandUI.Render(dealer, hideHoleCard: true, maxCards: step);
            soundManager?.PlayChip();
            yield return new WaitForSeconds(stepDelay);
        }

        lastKnownCardCount = dealer.Cards.Count + currentRound.PlayerHands.Sum(h => h.Cards.Count);
        statusText.text = "Your move";

        RefreshInsurancePrompt();
        CheckRoundOver();
        RefreshActionButtons();
    }

    void OnHitClicked()
    {
        if (!roundActive || currentRound == null) return;
        currentRound.Hit();
        AfterAction(); // RefreshHandDisplays inside here fires the card-deal cue
    }

    void OnStandClicked()
    {
        if (!roundActive || currentRound == null) return;
        currentRound.Stand();
        soundManager?.PlayClick();
        AfterAction();
    }

    void OnDoubleClicked()
    {
        if (!roundActive || currentRound == null) return;
        var hand = currentRound.CurrentHand;
        if (hand == null || !hand.CanDouble) return;
        if (!bankroll.TryWithdraw(hand.Bet))
        {
            statusText.text = "Not enough balance to double";
            FlashBlocked();
            return;
        }
        currentRound.DoubleDown();
        onBankrollChanged?.Invoke();
        RefreshBetDisplay();
        AfterAction(); // card-deal cue fires from RefreshHandDisplays
    }

    void OnSplitClicked()
    {
        if (!roundActive || currentRound == null) return;
        var hand = currentRound.CurrentHand;
        if (hand == null || !hand.CanSplit || currentRound.PlayerHands.Count >= 4) return;
        if (!bankroll.TryWithdraw(hand.Bet))
        {
            statusText.text = "Not enough balance to split";
            FlashBlocked();
            return;
        }
        currentRound.Split();
        onBankrollChanged?.Invoke();
        RefreshBetDisplay();
        AfterAction(); // card-deal cue fires from RefreshHandDisplays
    }

    void OnSurrenderClicked()
    {
        if (!roundActive || currentRound == null) return;
        var hand = currentRound.CurrentHand;
        if (hand == null || !hand.CanSurrender) return;
        currentRound.Surrender();
        soundManager?.PlayClick();
        AfterAction();
    }

    void OnInsuranceYes()
    {
        if (currentRound == null || !currentRound.InsuranceOffered) return;
        // Insurance can only ever be decided before any split, so there's always
        // exactly one player hand at this point.
        long insuranceAmount = currentRound.PlayerHands[0].Bet / 2;
        if (insuranceAmount > 0 && bankroll.TryWithdraw(insuranceAmount))
        {
            currentRound.TakeInsurance(true);
            onBankrollChanged?.Invoke();
            RefreshBetDisplay();
        }
        else
        {
            statusText.text = "Not enough balance for insurance";
            FlashBlocked();
            currentRound.TakeInsurance(false);
        }
        FinishInsuranceDecision();
    }

    void OnInsuranceNo()
    {
        if (currentRound == null || !currentRound.InsuranceOffered) return;
        currentRound.TakeInsurance(false);
        FinishInsuranceDecision();
    }

    void FinishInsuranceDecision()
    {
        soundManager?.PlayClick();
        RefreshInsurancePrompt();
        AfterAction();
    }

    void AfterAction()
    {
        RefreshHandDisplays();
        CheckRoundOver();
        RefreshActionButtons();
    }

    void RefreshInsurancePrompt()
    {
        bool visible = currentRound != null && currentRound.InsuranceOffered;
        insurancePromptGO.SetActive(visible);
        if (visible && !insurancePromptWasVisible)
            JuiceTweens.PopIn(this, (RectTransform)insurancePromptGO.transform, overshoot: 1.2f, duration: 0.25f);
        insurancePromptWasVisible = visible;
    }

    void CheckRoundOver()
    {
        if (currentRound == null || !currentRound.RoundOver || !roundActive) return;
        ResolveRound();
    }

    // ---- Resolution + juice ----

    void ResolveRound()
    {
        var results = currentRound.ResolveAll();
        long totalStaked = currentRound.PlayerHands.Sum(h => h.Bet) + currentRound.InsuranceBet;
        long totalReturned = results.Sum(r => r.payout) + currentRound.InsurancePayout;
        bankroll.Deposit(totalReturned);
        onBankrollChanged?.Invoke();

        RefreshHandDisplays(); // dealer's hole card reveals now that RoundOver is true

        long net = totalReturned - totalStaked;
        bool anyBlackjack = results.Any(r => r.outcome == BlackjackOutcome.PlayerBlackjack);
        bool anyBust = results.Any(r => r.outcome == BlackjackOutcome.Bust);

        statusText.color = net > 0 ? UIFactory.Positive : net < 0 ? UIFactory.Negative : UIFactory.Accent;
        string outcomeLabel = currentRound.InsurancePayout > 0 ? "Dealer blackjack — insurance pays"
            : results.Count == 1 ? DescribeOutcome(results[0].outcome) : "Round resolved";
        string flavor = net > 0 ? WinFlavors[UnityEngine.Random.Range(0, WinFlavors.Length)]
            : net < 0 ? LoseFlavors[UnityEngine.Random.Range(0, LoseFlavors.Length)]
            : "Press DEAL again";
        statusText.text = $"{outcomeLabel}  ({(net >= 0 ? "+" : "")}{UIFactory.FormatMoney(net)})  — {flavor}";

        if (net > 0)
        {
            soundManager?.PlayWin();
            if (anyBlackjack)
            {
                juiceManager?.Shake(0.5f, 4f);
                juiceManager?.Flash(new Color(0.3f, 1f, 0.4f, 0.28f), 0.7f);
                juiceManager?.PlayConfetti(2f);
                juiceManager?.PulseLight(0.9f, 0.7f);
                juiceManager?.PlayMoneyFountain(Vector2.zero);
                floatingText?.Show($"BLACKJACK! +{UIFactory.FormatMoney(net)}", UIFactory.Positive, fontSize: 42);
            }
            else if (net >= ChipDenominations.Values[2]) // $500+ (e.g. large split)
            {
                juiceManager?.Shake(0.5f, 4f);
                juiceManager?.Flash(new Color(0.3f, 1f, 0.4f, 0.28f), 0.7f);
                juiceManager?.PlayConfetti(2f);
                juiceManager?.PulseLight(0.9f, 0.7f);
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
        }
        else if (net < 0)
        {
            soundManager?.PlayLose();
            juiceManager?.Shake(anyBust ? 0.3f : 0.2f, anyBust ? 1.5f : 1f);
            juiceManager?.Flash(new Color(0.85f, 0.2f, 0.2f, 0.14f), 0.4f);
            floatingText?.Show($"{UIFactory.FormatMoney(net)}", UIFactory.Negative);
            winStreak = 0;
        }
        else
        {
            floatingText?.Show("PUSH", UIFactory.Accent);
            // Push is neither a win nor a loss — win streak carries through unchanged.
        }
        bool showStreak = winStreak >= 2;
        streakAnimator.SetText(showStreak ? $"<wave><rainb>{winStreak} WIN STREAK</rainb></wave>" : "");
        streakBadgeGO.SetActive(showStreak);
        if (showStreak) JuiceTweens.Pulse(this, (RectTransform)streakBadgeGO.transform, peakScale: 1.15f, duration: 0.3f);

        int playerFinalTotal = currentRound.PlayerHands.Count > 0 ? currentRound.PlayerHands[0].BestTotal : 0;
        var record = new BlackjackRoundRecord(roundIndex, playerFinalTotal, currentRound.Dealer.BestTotal,
            totalStaked, totalReturned, bankroll.Balance);
        onRoundResolved?.Invoke(record);
        roundIndex++;

        roundActive = false;
        CheckMilestones(net);
        RefreshActionButtons();
        // Betting buttons (CLEAR/DEAL/REPEAT) can come back immediately — they don't
        // sit anywhere near the cards. The bet spot circle does, though: showing it
        // right away left it fighting for the same screen space as the just-resolved
        // hand, which is exactly what made it hard to find the next bet spot. Give
        // the result a moment to read, then clear the table and reveal the bet spot.
        StartCoroutine(ClearTableAfterDelay());
    }

    IEnumerator ClearTableAfterDelay()
    {
        yield return new WaitForSeconds(1.8f);
        if (roundActive) yield break; // a new round already started — don't clobber it
        dealerHandUI.Clear();
        foreach (var h in playerHandUIs) h.Clear();
        RebuildBetChips(pendingBet); // the finished hand's chips go; a bet already placed for the next hand stays
        RefreshBetDisplay();
    }

    static string DescribeOutcome(BlackjackOutcome outcome) => outcome switch
    {
        BlackjackOutcome.PlayerBlackjack => "Blackjack!",
        BlackjackOutcome.Win => "You win",
        BlackjackOutcome.Lose => "Dealer wins",
        BlackjackOutcome.Bust => "Bust",
        BlackjackOutcome.Push => "Push",
        BlackjackOutcome.Surrender => "Surrendered",
        _ => ""
    };

    void CheckMilestones(long net)
    {
        if (!doubledMilestoneFired && bankroll.TotalFunded > 0 && bankroll.Balance >= bankroll.TotalFunded * 2)
        {
            doubledMilestoneFired = true;
            ShowAchievement("BANKROLL DOUBLED!");
        }
        int[] roundTargets = { 50, 100, 250, 500, 1000 };
        foreach (var target in roundTargets)
        {
            if (roundIndex == target && roundMilestonesFired.Add(target))
                milestoneToast?.Show($"{target} Hands This Session", UIFactory.Accent, fontSize: 30);
        }
        if (winStreak == 5 || winStreak == 10 || winStreak == 15 || winStreak == 20)
            ShowAchievement($"{winStreak} WIN STREAK!");

        // Only worth calling out once there's a real bar to clear — every win on
        // round 1 would technically be a "new best", which is a hollow milestone.
        if (net > 0 && net > bestRoundNet && roundIndex >= 2)
        {
            bestRoundNet = net;
            ShowAchievement($"BEST WIN: +{UIFactory.FormatMoney(net)}!");
        }
        else if (net > bestRoundNet)
        {
            bestRoundNet = net;
        }
    }

    void ShowAchievement(string text)
    {
        if (achievementHideRoutine != null) StopCoroutine(achievementHideRoutine);
        achievementBadgeGO.SetActive(true);
        achievementAnimator.SetText($"<wave><rainb>{text}</rainb></wave>");
        JuiceTweens.Pulse(this, (RectTransform)achievementBadgeGO.transform, peakScale: 1.2f, duration: 0.35f);
        achievementHideRoutine = StartCoroutine(HideAchievementAfter(3f));
    }

    IEnumerator HideAchievementAfter(float delay)
    {
        yield return new WaitForSeconds(delay);
        achievementBadgeGO.SetActive(false);
    }

    // ---- Display refresh ----

    void RefreshHandDisplays()
    {
        if (currentRound == null)
        {
            dealerHandUI.Clear();
            foreach (var h in playerHandUIs) h.Clear();
            return;
        }

        bool hideHole = !currentRound.RoundOver;
        dealerHandUI.Render(currentRound.Dealer, hideHoleCard: hideHole);

        EnsurePlayerHandUICount(currentRound.PlayerHands.Count);
        RepositionPlayerHandUIs(currentRound.PlayerHands.Count);
        for (int i = 0; i < currentRound.PlayerHands.Count; i++)
        {
            bool isActive = !currentRound.RoundOver && i == currentRound.CurrentHandIndex;
            playerHandUIs[i].Render(currentRound.PlayerHands[i], hideHoleCard: false, highlighted: isActive);
        }
        for (int i = currentRound.PlayerHands.Count; i < playerHandUIs.Count; i++)
            playerHandUIs[i].Clear();

        // A dedicated deal/hit "card lands" cue, distinct from the button-press click
        // — fires whenever the total card count across every hand actually grows,
        // so it plays once per new card regardless of which action dealt it (initial
        // deal, a hit, a split's forced second card, or the dealer drawing itself).
        int totalCards = currentRound.Dealer.Cards.Count + currentRound.PlayerHands.Sum(h => h.Cards.Count);
        if (totalCards > lastKnownCardCount) soundManager?.PlayChip();
        lastKnownCardCount = totalCards;
    }

    void EnsurePlayerHandUICount(int count)
    {
        while (playerHandUIs.Count < count)
        {
            var hu = new HandUI();
            hu.Build(handUIParent, Vector2.zero, PlayerCardSize, PlayerCardSpacing);
            playerHandUIs.Add(hu);
        }
    }

    void RepositionPlayerHandUIs(int count)
    {
        // Split hands share the felt width: 2 hands sit well apart, 4 still fit a 4-card hand each
        float spacing = Mathf.Min(460f, (FeltSize.x - 80f) / Mathf.Max(1, count));
        float startX = -(count - 1) * spacing / 2f;
        for (int i = 0; i < count && i < playerHandUIs.Count; i++)
            playerHandUIs[i].Root.anchoredPosition = new Vector2(FeltCenter.x + startX + i * spacing, PlayerHandY);
    }

    void RefreshBetDisplay()
    {
        // The circle stays on the felt; during a hand it shows everything riding (doubles, splits, insurance)
        betText.gameObject.SetActive(!roundActive);
        if (roundActive)
        {
            long riding = currentRound == null ? 0 : currentRound.PlayerHands.Sum(h => h.Bet) + currentRound.InsuranceBet;
            betSpotText.text = $"IN PLAY\n{UIFactory.FormatMoney(riding)}";
            betSpotText.color = UIFactory.TextLight;
            RebuildBetChips(riding);
            return;
        }

        bool hasBet = pendingBet > 0;
        betSpotText.text = hasBet ? $"BET\n{UIFactory.FormatMoney(pendingBet)}" : "PLACE\nBET";
        betSpotText.color = hasBet ? UIFactory.Accent : FeltText;
        betSpotFillImg.color = hasBet ? new Color(0f, 0f, 0f, 0.32f) : new Color(0f, 0f, 0f, 0.22f);
        betText.text = hasBet ? "Click DEAL when you're ready" : "Pick a chip, then click the circle";
    }

    void RefreshActionButtons()
    {
        // Betting phase: only CLEAR BET / DEAL / REPEAT BET are on screen at all.
        // DEAL is the one button that's always visible but grey until a bet exists —
        // everything else in this phase is either usable or it isn't shown.
        bool betting = !roundActive;
        bool canDeal = betting && pendingBet > 0;
        SetBettingButtonState(dealButton, dealBaseColor, betting, canDeal);
        SetBettingButtonState(clearBetButton, clearBaseColor, betting, betting && pendingBet > 0);
        SetBettingButtonState(repeatButton, repeatBaseColor, betting, betting && lastBetAmount > 0);
        SetBettingButtonState(undoButton, UIFactory.AccentDim, betting, betting && undoStack.Count > 0);
        if (canDeal && !dealWasEnabled) JuiceTweens.Pulse(this, dealButton.GetComponent<RectTransform>(), peakScale: 1.15f, duration: 0.25f);
        dealWasEnabled = canDeal;

        // Action phase: HIT/STAND always paired, DOUBLE/SPLIT/SURRENDER only appear
        // when actually legal for the current hand — no point showing SPLIT on a
        // hand that can't split.
        bool inTurn = roundActive && currentRound != null && !currentRound.RoundOver && !currentRound.InsuranceOffered;
        var hand = inTurn ? currentRound.CurrentHand : null;

        var visible = new List<Button>();
        if (inTurn && hand != null && hand.CanHit) visible.Add(hitButton);
        if (inTurn && hand != null && !hand.IsResolved) visible.Add(standButton);
        if (inTurn && hand != null && hand.CanDouble) visible.Add(doubleButton);
        if (inTurn && hand != null && hand.CanSplit && currentRound.PlayerHands.Count < 4) visible.Add(splitButton);
        if (inTurn && hand != null && hand.CanSurrender) visible.Add(surrenderButton);

        LayoutActionButtons(visible);
    }

    // Centers whichever action buttons currently apply, hides the rest entirely, and
    // pops in any that just newly appeared (e.g. SPLIT showing up right after a pair
    // is dealt) instead of having them silently snap into existence.
    void LayoutActionButtons(List<Button> visible)
    {
        const float spacing = 160f;
        float startX = FeltCenter.x - (visible.Count - 1) * spacing / 2f;

        foreach (var kv in buttonWasVisible.Keys.ToList())
        {
            bool nowVisible = visible.Contains(kv);
            kv.gameObject.SetActive(nowVisible);
            kv.interactable = nowVisible;
        }

        for (int i = 0; i < visible.Count; i++)
        {
            var rt = visible[i].GetComponent<RectTransform>();
            rt.anchoredPosition = new Vector2(startX + i * spacing, ActionButtonY);
            if (!buttonWasVisible[visible[i]]) JuiceTweens.PopIn(this, rt, overshoot: 1.15f, duration: 0.2f);
            buttonWasVisible[visible[i]] = true;
        }
        foreach (var btn in buttonWasVisible.Keys.ToList())
            if (!visible.Contains(btn)) buttonWasVisible[btn] = false;
    }

    // Used by the HUD's RESET button, mirroring roulette's ResetBets — exposed
    // publicly since that button lives outside this controller.
    public void ResetRound()
    {
        currentRound = null;
        roundActive = false;
        pendingBet = 0;
        lastBetAmount = 0;
        undoStack.Clear();
        winStreak = 0;
        doubledMilestoneFired = false;
        bestRoundNet = 0;
        lastKnownCardCount = 0;
        roundMilestonesFired.Clear();
        streakAnimator?.SetText("");
        streakBadgeGO?.SetActive(false);
        if (achievementHideRoutine != null) { StopCoroutine(achievementHideRoutine); achievementHideRoutine = null; }
        achievementBadgeGO?.SetActive(false);
        insurancePromptGO?.SetActive(false);
        insurancePromptWasVisible = false;
        ClearBetChipVisuals();
        statusText.color = UIFactory.Accent;
        statusText.text = "Place your bet, then DEAL";
        RefreshHandDisplays();
        RefreshActionButtons();
        RefreshBetDisplay();
    }

    // Used when restoring a save — continues numbering rounds from where the saved
    // session left off instead of restarting at #1.
    public void SetRoundIndex(int index) => roundIndex = index;
}
