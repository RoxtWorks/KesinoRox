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
    Button dealButton, hitButton, standButton, doubleButton, splitButton, surrenderButton, clearBetButton, repeatButton;
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
    const int MaxVisibleBetChips = 10;
    // Same red/blue/black-by-denomination mapping ChipSelectorUI uses for its own
    // chip buttons, so a bet built from a given chip visually matches the chip that
    // placed it.
    static readonly Color[] ChipStackColors =
    {
        new Color(0.65f, 0.12f, 0.12f),
        new Color(0.1f, 0.35f, 0.6f),
        new Color(0.1f, 0.1f, 0.1f),
    };

    GameObject insurancePromptGO;

    TextMeshProUGUI streakText;
    TextAnimator_TMP streakAnimator;
    GameObject streakBadgeGO;
    TextMeshProUGUI achievementText;
    TextAnimator_TMP achievementAnimator;
    GameObject achievementBadgeGO;
    Coroutine achievementHideRoutine;

    const float PanelCenterX = 0f;
    static readonly string[] WinFlavors = { "Nice hand!", "There it is!", "Press DEAL again", "Keep it going!", "Well played" };
    static readonly string[] LoseFlavors = { "Press DEAL again", "Try again", "Onward", "Next hand's yours", "Deal again" };

    public void Build(Transform canvas, Bankroll bankroll, ChipSelectorUI chipSelector, Shoe shoe,
        SoundManager soundManager, JuiceManager juiceManager, FloatingTextUI floatingText, FloatingTextUI milestoneToast,
        Action<BlackjackRoundRecord> onRoundResolved)
    {
        this.bankroll = bankroll;
        this.chipSelector = chipSelector;
        this.shoe = shoe;
        this.soundManager = soundManager;
        this.juiceManager = juiceManager;
        this.floatingText = floatingText;
        this.milestoneToast = milestoneToast;
        this.onRoundResolved = onRoundResolved;

        var tableRootGO = new GameObject("BlackjackUIRoot");
        tableRootGO.transform.SetParent(canvas, false);
        var tableRootRT = tableRootGO.AddComponent<RectTransform>();
        tableRootRT.anchorMin = new Vector2(0.5f, 0.5f);
        tableRootRT.anchorMax = new Vector2(0.5f, 0.5f);
        tableRootRT.pivot = new Vector2(0.5f, 0.5f);
        tableRootRT.anchoredPosition = Vector2.zero;
        tableRoot = tableRootGO.transform;

        UIFactory.MakePanel(tableRoot, "BlackjackPanelBg", new Vector2(PanelCenterX, -100), new Vector2(1000, 660), UIFactory.PanelDark);
        UIFactory.MakeHeroTitle(tableRoot, "Header_Blackjack", new Vector2(PanelCenterX, 195), "BLACKJACK TABLE", 26);
        UIFactory.MakeText(tableRoot, "DealerLabel", new Vector2(PanelCenterX, 155), 13,
            TextAnchor.MiddleCenter, new Vector2(200, 20), UIFactory.TextDim, FontStyle.Bold).text = "DEALER";

        handUIParent = tableRoot;
        dealerHandUI = new HandUI();
        dealerHandUI.Build(tableRoot, new Vector2(PanelCenterX, 95));

        UIFactory.MakePanel(tableRoot, "StatusPanelBg", new Vector2(PanelCenterX, 5), new Vector2(520, 40), UIFactory.PanelDark, shadow: false);
        statusText = UIFactory.MakeText(tableRoot, "StatusText", new Vector2(PanelCenterX, 5), 20,
            sizeDelta: new Vector2(500, 34), color: UIFactory.Accent, style: FontStyle.Bold);
        statusText.text = "Place your bet, then DEAL";

        UIFactory.MakeText(tableRoot, "PlayerLabel", new Vector2(PanelCenterX, -55), 13,
            TextAnchor.MiddleCenter, new Vector2(200, 20), UIFactory.TextDim, FontStyle.Bold).text = "YOUR HAND";

        // First (only, until a split happens) player hand slot.
        var firstHand = new HandUI();
        firstHand.Build(tableRoot, new Vector2(PanelCenterX, -140));
        playerHandUIs.Add(firstHand);

        betText = UIFactory.MakeText(tableRoot, "BetText", new Vector2(PanelCenterX, -260), 15,
            sizeDelta: new Vector2(400, 24), color: UIFactory.TextDim);
        betText.text = "Pick a chip, then click the circle below";

        // A real visible betting spot — outlined circle, translucent fill, clear
        // placeholder text — instead of the old near-invisible click zone that gave
        // no indication bets even happened there. Hidden once a round starts (cards
        // take over this same screen area) and shown again between rounds.
        betSpotGO = new GameObject("BetSpot");
        betSpotGO.transform.SetParent(tableRoot, false);
        var betSpotRt = betSpotGO.AddComponent<RectTransform>();
        betSpotRt.sizeDelta = new Vector2(160, 160);
        betSpotRt.anchoredPosition = new Vector2(PanelCenterX, -150);
        betSpotFillImg = betSpotGO.AddComponent<Image>();
        betSpotFillImg.sprite = UIFactory.Circle();
        betSpotFillImg.color = new Color(1f, 1f, 1f, 0.06f);
        UIFactory.AddSharpFrame(betSpotGO, UIFactory.Accent, square: false);
        var betSpotBtnComponent = betSpotGO.AddComponent<Button>();
        betSpotBtnComponent.targetGraphic = betSpotFillImg;
        betSpotBtnComponent.onClick.AddListener(OnBetSpotClicked);

        betSpotText = UIFactory.MakeText(betSpotGO.transform, "BetSpotText", Vector2.zero, 18,
            sizeDelta: new Vector2(140, 140), color: UIFactory.TextDim, style: FontStyle.Bold);
        betSpotText.text = "PLACE\nBET";

        BuildActionButtons();
        BuildInsurancePrompt();
        BuildStreakBadge();
        BuildAchievementBadge();

        RefreshHandDisplays();
        RefreshActionButtons();
        RefreshBetDisplay();
    }

    const float ActionButtonY = -390f;
    static readonly Color DisabledButtonColor = new Color(0.22f, 0.22f, 0.24f, 0.7f);

    // Base (enabled) color per button, so toggling between enabled/disabled can swap
    // the Image color directly — Unity's built-in ColorBlock.disabledColor tint was
    // too subtle to read as "disabled" against this project's already-muted palette.
    Color dealBaseColor, clearBaseColor, repeatBaseColor;

    void BuildActionButtons()
    {
        // Betting-phase row: CLEAR BET / DEAL / REPEAT BET, centered as a group of 3.
        dealBaseColor = UIFactory.Positive;
        clearBaseColor = UIFactory.RedBet;
        repeatBaseColor = UIFactory.AccentDim;

        clearBetButton = UIFactory.MakeButton(tableRoot, "ClearBetBtn", new Vector2(-170f, ActionButtonY), new Vector2(140, 46),
            "CLEAR BET", clearBaseColor, OnClearBetClicked, 13, pixelFont: true);
        dealButton = UIFactory.MakeButton(tableRoot, "DealBtn", new Vector2(0f, ActionButtonY), new Vector2(160, 54),
            "DEAL", dealBaseColor, OnDealClicked, 18, pixelFont: true);
        repeatButton = UIFactory.MakeButton(tableRoot, "RepeatBetBtn", new Vector2(170f, ActionButtonY), new Vector2(140, 46),
            "REPEAT BET", repeatBaseColor, OnRepeatBetClicked, 12, pixelFont: true);

        // Action-phase row: HIT / STAND / DOUBLE / SPLIT / SURRENDER — built here but
        // repositioned and shown/hidden dynamically by LayoutActionButtons(), since
        // which ones apply changes hand to hand.
        hitButton = UIFactory.MakeButton(tableRoot, "HitBtn", new Vector2(0, ActionButtonY), new Vector2(130, 46),
            "HIT", UIFactory.AccentDim, OnHitClicked, 15, pixelFont: true);
        standButton = UIFactory.MakeButton(tableRoot, "StandBtn", new Vector2(0, ActionButtonY), new Vector2(130, 46),
            "STAND", UIFactory.AccentDim, OnStandClicked, 15, pixelFont: true);
        doubleButton = UIFactory.MakeButton(tableRoot, "DoubleBtn", new Vector2(0, ActionButtonY), new Vector2(130, 46),
            "DOUBLE", UIFactory.AccentDim, OnDoubleClicked, 14, pixelFont: true);
        splitButton = UIFactory.MakeButton(tableRoot, "SplitBtn", new Vector2(0, ActionButtonY), new Vector2(130, 46),
            "SPLIT", UIFactory.AccentDim, OnSplitClicked, 15, pixelFont: true);
        surrenderButton = UIFactory.MakeButton(tableRoot, "SurrenderBtn", new Vector2(0, ActionButtonY), new Vector2(140, 46),
            "SURRENDER", UIFactory.RedBet, OnSurrenderClicked, 12, pixelFont: true);

        foreach (var btn in new[] { hitButton, standButton, doubleButton, splitButton, surrenderButton })
            buttonWasVisible[btn] = false;
    }

    // Sets a betting-phase button's visible/enabled state, swapping its Image color
    // directly between its base color and a flat grey rather than relying on Unity's
    // (too-subtle-here) disabled color tint.
    static void SetBettingButtonState(Button btn, Color baseColor, bool visible, bool enabled)
    {
        btn.gameObject.SetActive(visible);
        btn.interactable = enabled;
        btn.GetComponent<Image>().color = enabled ? baseColor : DisabledButtonColor;
    }

    void BuildInsurancePrompt()
    {
        insurancePromptGO = UIFactory.MakeFramedPanel(tableRoot, "InsurancePromptBg", new Vector2(PanelCenterX, 260), new Vector2(420, 90), Color.black);
        UIFactory.MakeText(insurancePromptGO.transform, "InsuranceText", new Vector2(0, 20), 16,
            sizeDelta: new Vector2(380, 28), color: UIFactory.Accent, style: FontStyle.Bold).text = "Dealer shows an Ace — take insurance?";
        UIFactory.MakeButton(insurancePromptGO.transform, "InsuranceYes", new Vector2(-90, -20), new Vector2(140, 34),
            "YES (half bet)", UIFactory.Positive, OnInsuranceYes, 12, pixelFont: true);
        UIFactory.MakeButton(insurancePromptGO.transform, "InsuranceNo", new Vector2(90, -20), new Vector2(140, 34),
            "NO", UIFactory.RedBet, OnInsuranceNo, 13, pixelFont: true);
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

    void OnBetSpotClicked()
    {
        if (roundActive) return;
        long chip = chipSelector.SelectedChip;
        if (!bankroll.CanAfford(pendingBet + chip))
        {
            statusText.text = bankroll.Balance < ChipDenominations.Values[0]
                ? "Out of chips — use ADD FUNDS above to keep playing"
                : "Not enough balance for that bet";
            return;
        }
        pendingBet += chip;
        soundManager?.PlayChip();
        JuiceTweens.Pulse(this, (RectTransform)betSpotGO.transform, peakScale: 1.12f, duration: 0.18f);
        AddBetChipVisual(chip);
        RefreshBetDisplay();
        RefreshActionButtons();
    }

    void OnClearBetClicked()
    {
        if (roundActive) return;
        pendingBet = 0;
        soundManager?.PlayClick();
        ClearBetChipVisuals();
        RefreshBetDisplay();
        RefreshActionButtons();
    }

    // Drops one small chip icon, colored to match whichever denomination placed it,
    // stacked with a slight offset like a real chip pile — the bet spot previously
    // only showed a number, which read as "nothing actually happened" when clicked.
    // denomination < 0 (used by REPEAT BET, which doesn't know the original chip
    // breakdown) draws a generic gold chip instead of a denomination color.
    void AddBetChipVisual(long denomination)
    {
        if (betChipVisuals.Count >= MaxVisibleBetChips) return;
        int colorIndex = Array.IndexOf(ChipDenominations.Values, denomination);
        Color fill = colorIndex >= 0 ? ChipStackColors[colorIndex % ChipStackColors.Length] : UIFactory.Accent;

        var go = new GameObject($"BetChip_{betChipVisuals.Count}");
        go.transform.SetParent(betSpotGO.transform, false);
        var img = go.AddComponent<Image>();
        img.sprite = UIFactory.Circle();
        img.color = fill;
        img.raycastTarget = false;
        var rt = go.GetComponent<RectTransform>();
        rt.sizeDelta = new Vector2(34, 34);
        // Alternating left/right fan (not full-random jitter) plus a real vertical
        // climb per chip — random X alone tended to cluster chips almost directly on
        // top of each other instead of reading as a visible pile.
        int stackIndex = betChipVisuals.Count;
        float fanX = (stackIndex % 2 == 0 ? -1f : 1f) * (10f + stackIndex * 2f) + UnityEngine.Random.Range(-4f, 4f);
        rt.anchoredPosition = new Vector2(fanX, -55f + stackIndex * 10f);

        betChipVisuals.Add(go);
        betSpotText.transform.SetAsLastSibling(); // keep the amount readable over the pile
        JuiceTweens.PopIn(this, rt, overshoot: 1.3f, duration: 0.18f);
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
            return;
        }
        if (!bankroll.CanAfford(lastBetAmount))
        {
            statusText.text = "Not enough balance to repeat that bet";
            return;
        }
        pendingBet = lastBetAmount;
        soundManager?.PlayChip();
        JuiceTweens.Pulse(this, repeatButton.GetComponent<RectTransform>(), peakScale: 1.15f, duration: 0.2f);
        // Repeat doesn't know which individual chips made up the original bet, just
        // the total — a handful of generic gold chips still reads as "a bet is
        // there" rather than leaving the spot showing only text like before.
        ClearBetChipVisuals();
        int repeatChipCount = Mathf.Clamp((int)(lastBetAmount / ChipDenominations.Values[0]), 1, 5);
        for (int i = 0; i < repeatChipCount; i++) AddBetChipVisual(-1);
        RefreshBetDisplay();
        RefreshActionButtons();
    }

    // ---- Round flow ----

    void OnDealClicked()
    {
        if (roundActive || pendingBet <= 0) return;
        if (!bankroll.TryWithdraw(pendingBet))
        {
            statusText.text = "Not enough balance to deal";
            return;
        }

        // Deal() itself reshuffles silently once penetration is hit — check it here
        // first, before that happens, so the player gets told why a "new" shoe just
        // showed up instead of it being an invisible background event.
        if (shoe.NeedsReshuffle)
            milestoneToast?.Show("New shoe — reshuffling", UIFactory.Accent, fontSize: 24);

        roundActive = true;
        lastBetAmount = pendingBet;
        currentRound = new BlackjackRound(shoe);
        currentRound.Deal(pendingBet);
        pendingBet = 0;

        statusText.color = UIFactory.Accent;
        statusText.text = "Dealing...";

        // Bet's committed now — clear the pile immediately rather than leaving it to
        // sit under the incoming cards (RefreshBetDisplay also hides the bet spot
        // itself since roundActive is now true).
        ClearBetChipVisuals();
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
            return;
        }
        currentRound.DoubleDown();
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
            return;
        }
        currentRound.Split();
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
        }
        else
        {
            statusText.text = "Not enough balance for insurance";
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

        RefreshHandDisplays(); // dealer's hole card reveals now that RoundOver is true

        long net = totalReturned - totalStaked;
        bool anyBlackjack = results.Any(r => r.outcome == BlackjackOutcome.PlayerBlackjack);
        bool anyBust = results.Any(r => r.outcome == BlackjackOutcome.Bust);

        statusText.color = net >= 0 ? UIFactory.Positive : UIFactory.Negative;
        string outcomeLabel = results.Count == 1 ? DescribeOutcome(results[0].outcome) : "Round resolved";
        string flavor = net > 0 ? WinFlavors[UnityEngine.Random.Range(0, WinFlavors.Length)]
            : net < 0 ? LoseFlavors[UnityEngine.Random.Range(0, LoseFlavors.Length)]
            : "Press DEAL again";
        statusText.text = $"{outcomeLabel}  ({(net >= 0 ? "+" : "")}{net})  — {flavor}";

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
                floatingText?.Show($"BLACKJACK! +{net}", UIFactory.Positive, fontSize: 42);
            }
            else
            {
                juiceManager?.Shake(0.35f, 2.5f);
                juiceManager?.Flash(new Color(0.25f, 0.9f, 0.35f, 0.18f), 0.5f);
                juiceManager?.PlayConfetti();
                floatingText?.Show($"+{net}", UIFactory.Positive);
            }
            winStreak++;
        }
        else if (net < 0)
        {
            soundManager?.PlayLose();
            juiceManager?.Shake(anyBust ? 0.3f : 0.2f, anyBust ? 1.5f : 1f);
            juiceManager?.Flash(new Color(0.85f, 0.2f, 0.2f, 0.14f), 0.4f);
            floatingText?.Show($"{net}", UIFactory.Negative);
            winStreak = 0;
        }
        else
        {
            floatingText?.Show("PUSH", UIFactory.Accent);
            // Push is neither a win nor a loss — win streak carries through unchanged.
        }
        streakAnimator.SetText(winStreak >= 2 ? $"<wave><rainb>{winStreak} WIN STREAK</rainb></wave>" : "");
        streakBadgeGO.SetActive(winStreak >= 2);

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
            ShowAchievement($"BEST WIN: +{net}!");
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
            hu.Build(handUIParent, Vector2.zero);
            playerHandUIs.Add(hu);
        }
    }

    void RepositionPlayerHandUIs(int count)
    {
        const float spacing = 260f;
        float startX = -(count - 1) * spacing / 2f;
        for (int i = 0; i < count && i < playerHandUIs.Count; i++)
            playerHandUIs[i].Root.anchoredPosition = new Vector2(startX + i * spacing, -140f);
    }

    void RefreshBetDisplay()
    {
        betSpotGO.SetActive(!roundActive);
        betText.gameObject.SetActive(!roundActive);
        if (roundActive) return;

        bool hasBet = pendingBet > 0;
        betSpotText.text = hasBet ? $"BET\n{pendingBet}" : "PLACE\nBET";
        betSpotText.color = hasBet ? UIFactory.Accent : UIFactory.TextDim;
        betSpotFillImg.color = hasBet ? new Color(0.72f, 0.79f, 0.88f, 0.18f) : new Color(1f, 1f, 1f, 0.06f);
        betText.text = hasBet ? "Click DEAL when you're ready" : "Pick a chip, then click the circle below";
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
        float startX = -(visible.Count - 1) * spacing / 2f;

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
