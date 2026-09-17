using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Febucci.UI;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

// Three Card Poker betting controller.
// Flow: place Ante (required) + PairPlus (optional) → DEAL → see hand →
// PLAY (match Ante) or FOLD → hand compared vs dealer → resolve.
// PairPlus always resolves on the player's own hand regardless of fold.
public class ThreeCardPokerBettingUIController : MonoBehaviour
{
    Bankroll bankroll;
    ChipSelectorUI chipSelector;
    Shoe shoe;
    SoundManager soundManager;
    JuiceManager juiceManager;
    FloatingTextUI floatingText;
    FloatingTextUI milestoneToast;
    Action<ThreeCardPokerRoundRecord> onRoundResolved;

    ThreeCardPokerRound currentRound;
    bool roundActive;
    bool waitingForDecision; // after deal, before play/fold
    int roundIndex;
    int winStreak;
    long bestRoundNet;
    bool doubledMilestoneFired;
    bool dealWasEnabled;
    readonly HashSet<int> roundMilestonesFired = new HashSet<int>();

    TextMeshProUGUI streakText;
    TextAnimator_TMP streakAnimator;
    GameObject streakBadgeGO;

    // Ante and PairPlus are staged before deal; Play amount = Ante (auto)
    long antePending, ppPending;
    long anteLastBet, ppLastBet;
    readonly List<Dictionary<string, long>> undoStack = new List<Dictionary<string, long>>();
    const int MaxUndoDepth = 30;

    Transform tableRoot;
    Text statusText;
    Button dealButton, clearBetButton, repeatButton, undoButton, playButton, foldButton;
    Color dealBaseColor, clearBaseColor, repeatBaseColor;

    // Card display
    readonly List<GameObject> playerCardGOs = new List<GameObject>();
    readonly List<GameObject> dealerCardGOs = new List<GameObject>();
    Text playerHandLabel, dealerHandLabel;

    class BetArea
    {
        public GameObject Root;
        public Image FillImg;
        public Text AmountText;
        public Text PayoutText;
        public readonly List<GameObject> ChipVisuals = new List<GameObject>();
    }

    BetArea anteArea, ppArea, playArea;
    static readonly Color[] ChipStackColors =
    {
        new Color(0.65f, 0.12f, 0.12f),
        new Color(0.1f, 0.35f, 0.6f),
        UIFactory.Chip500White,
    };

    static readonly string[] WinFlavors  = { "Nice hand!", "There it is!", "Well played", "Keep it going!" };
    static readonly string[] LoseFlavors = { "Press DEAL again", "Try again", "Onward", "Next hand's yours" };

    public void Build(Transform canvas, Bankroll bankroll, ChipSelectorUI chipSelector, Shoe shoe,
        SoundManager soundManager, JuiceManager juiceManager, FloatingTextUI floatingText,
        FloatingTextUI milestoneToast, Action<ThreeCardPokerRoundRecord> onRoundResolved)
    {
        this.bankroll = bankroll;
        this.chipSelector = chipSelector;
        this.shoe = shoe;
        this.soundManager = soundManager;
        this.juiceManager = juiceManager;
        this.floatingText = floatingText;
        this.milestoneToast = milestoneToast;
        this.onRoundResolved = onRoundResolved;

        var tableRootGO = new GameObject("TCPUIRoot");
        tableRootGO.transform.SetParent(canvas, false);
        var rt = tableRootGO.AddComponent<RectTransform>();
        rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 0.5f);
        rt.anchoredPosition = Vector2.zero;
        tableRoot = tableRootGO.transform;

        UIFactory.MakePanel(tableRoot, "TCPPanelBg", new Vector2(0, -50), new Vector2(1050, 770), UIFactory.PanelDark);
        UIFactory.MakeHeroTitle(tableRoot, "Header", new Vector2(0, 280), "3 CARD POKER", 26);

        // Vertical layout: dealer on top, player below — expanded for non-overlapping spacing
        UIFactory.MakeText(tableRoot, "DealerLabel", new Vector2(0, 218), 13,
            TextAnchor.MiddleCenter, new Vector2(220, 20), UIFactory.TextDim, FontStyle.Bold).text = "DEALER HAND";
        dealerHandLabel = UIFactory.MakeText(tableRoot, "DealerHandLbl", new Vector2(0, 103), 20,
            TextAnchor.MiddleCenter, new Vector2(280, 30), UIFactory.TextDim, FontStyle.Bold);

        // Permanent qualifier reminder — between dealer hand rank and player label
        UIFactory.MakeText(tableRoot, "QualifierNote", new Vector2(0, 64), 13,
            TextAnchor.MiddleCenter, new Vector2(360, 22), new Color(1f, 0.85f, 0.2f), FontStyle.Bold)
            .text = "DEALER NEEDS QUEEN HIGH OR BETTER";

        UIFactory.MakeText(tableRoot, "PlayerLabel", new Vector2(0, 32), 13,
            TextAnchor.MiddleCenter, new Vector2(220, 20), UIFactory.TextDim, FontStyle.Bold).text = "YOUR HAND";
        playerHandLabel = UIFactory.MakeText(tableRoot, "PlayerHandLbl", new Vector2(0, -85), 20,
            TextAnchor.MiddleCenter, new Vector2(280, 30), UIFactory.TextDim, FontStyle.Bold);

        var statusPanelBg = UIFactory.MakePanel(tableRoot, "StatusPanelBg", new Vector2(0, -130), new Vector2(760, 40), UIFactory.PanelDark, shadow: false);
        UIFactory.AddSharpFrame(statusPanelBg, UIFactory.AccentDim, square: true);
        statusText = UIFactory.MakeText(tableRoot, "StatusText", new Vector2(0, -130), 18,
            sizeDelta: new Vector2(740, 34), color: UIFactory.Accent, style: FontStyle.Bold);
        statusText.text = "Place ANTE to start";

        // Three bet slots: ANTE (pre-deal), PLAY (shows during decision = auto-matched to Ante), PAIR+ (optional side)
        anteArea = BuildBetArea("ANTE",  new Vector2(-200, -250), UIFactory.Positive,  "PAYS 1:1");
        ppArea   = BuildBetArea("PAIR+", new Vector2( 200, -250), new Color(0.55f, 0.45f, 0.15f),
            "PAIR 1:1 · FLUSH 3:1\nSTR 6:1 · TRIPS 30:1\nSF 40:1");
        playArea = BuildBetArea("PLAY",  new Vector2(   0, -250), new Color(0.15f, 0.5f, 0.85f), "= ANTE BET");
        var playBtn = playArea.Root.GetComponent<Button>();
        playBtn.onClick.RemoveAllListeners();
        playBtn.interactable = false;
        playArea.Root.SetActive(false);

        // Ante Bonus note
        UIFactory.MakeText(tableRoot, "AnteBonusNote", new Vector2(0, -345), 12,
            TextAnchor.MiddleCenter, new Vector2(440, 18), UIFactory.TextDim)
            .text = "ANTE BONUS: Straight 1:1 · Three of a Kind 4:1 · Straight Flush 5:1";

        BuildActionButtons();
        BuildDecisionButtons();
        BuildStreakBadge();
        RefreshActionButtons();
        RefreshBetDisplay();
    }

    BetArea BuildBetArea(string label, Vector2 pos, Color accentColor, string payoutLabel)
    {
        var go = new GameObject($"BetArea_{label}");
        go.transform.SetParent(tableRoot, false);
        var rt = go.AddComponent<RectTransform>();
        rt.sizeDelta = new Vector2(170, 155);
        rt.anchoredPosition = pos;
        var fill = go.AddComponent<Image>();
        fill.sprite = UIFactory.RoundedRect();
        fill.type = Image.Type.Sliced;
        fill.color = new Color(1f, 1f, 1f, 0.06f);
        UIFactory.AddSharpFrame(go, accentColor, square: true);
        var btn = go.AddComponent<Button>();
        btn.targetGraphic = fill;
        btn.onClick.AddListener(() => OnBetAreaClicked(label));

        var amt = UIFactory.MakeText(go.transform, "AmountText", new Vector2(0, 28), 16,
            sizeDelta: new Vector2(150, 50), color: UIFactory.TextDim, style: FontStyle.Bold);
        amt.text = label;
        amt.alignment = TextAnchor.MiddleCenter;
        var pay = UIFactory.MakeText(go.transform, "PayoutText", new Vector2(0, -38), 10,
            sizeDelta: new Vector2(160, 50), color: UIFactory.TextDim);
        pay.text = payoutLabel;
        pay.alignment = TextAnchor.MiddleCenter;

        return new BetArea { Root = go, FillImg = fill, AmountText = amt, PayoutText = pay };
    }

    void OnBetAreaClicked(string label)
    {
        if (roundActive) return;
        long chip = chipSelector.SelectedChip;
        bool isAnte = label == "ANTE";
        long totalPending = antePending + ppPending;
        if (!bankroll.CanAfford(totalPending + chip))
        {
            statusText.text = bankroll.Balance < ChipDenominations.Values[0]
                ? "Out of chips — use ADD FUNDS above to keep playing"
                : "Not enough balance for that bet";
            juiceManager?.MicroShake(1.2f);
            return;
        }
        PushUndoSnapshot();
        if (isAnte) antePending += chip;
        else ppPending += chip;
        soundManager?.PlayChip();
        JuiceTweens.Pulse(this, (RectTransform)(isAnte ? anteArea.Root : ppArea.Root).transform, peakScale: 1.12f, duration: 0.18f);
        AddChipVisual(isAnte ? anteArea : ppArea, chip);
        RefreshBetDisplay(); RefreshActionButtons();
    }

    void AddChipVisual(BetArea area, long denomination)
    {
        const int max = 8;
        if (area.ChipVisuals.Count >= max) return;
        int ci = Array.IndexOf(ChipDenominations.Values, denomination);
        Color fill = ci >= 0 ? ChipStackColors[ci % ChipStackColors.Length] : UIFactory.Accent;
        var go = new GameObject($"Chip_{area.ChipVisuals.Count}");
        go.transform.SetParent(area.Root.transform, false);
        var img = go.AddComponent<Image>();
        img.sprite = UIFactory.Circle();
        img.color = fill;
        img.raycastTarget = false;
        var rt = go.GetComponent<RectTransform>();
        rt.sizeDelta = new Vector2(28, 28);
        int si = area.ChipVisuals.Count;
        rt.anchoredPosition = new Vector2((si % 2 == 0 ? -1f : 1f) * (8f + si * 2f) + UnityEngine.Random.Range(-3f, 3f), -50f + Mathf.Min(si, 4) * 8f);
        area.ChipVisuals.Add(go);
        area.AmountText.transform.SetAsLastSibling();
        JuiceTweens.PopIn(this, rt, overshoot: 1.3f, duration: 0.18f);
    }

    void ClearChipVisuals(BetArea area) { foreach (var go in area.ChipVisuals) Destroy(go); area.ChipVisuals.Clear(); }

    void FlashBlocked() => juiceManager?.MicroShake(1.2f);

    void PushUndoSnapshot() { undoStack.Add(new Dictionary<string, long> { { "ante", antePending }, { "pp", ppPending } }); if (undoStack.Count > MaxUndoDepth) undoStack.RemoveAt(0); }

    void OnClearBetClicked()
    {
        if (roundActive) return;
        if (antePending <= 0 && ppPending <= 0) { statusText.text = "Nothing to clear"; FlashBlocked(); return; }
        PushUndoSnapshot();
        antePending = ppPending = 0;
        ClearChipVisuals(anteArea); ClearChipVisuals(ppArea);
        soundManager?.PlayClick();
        RefreshBetDisplay(); RefreshActionButtons();
    }

    void UndoLastBetAction()
    {
        if (roundActive) return;
        if (undoStack.Count == 0) { statusText.text = "Nothing to undo"; juiceManager?.MicroShake(1.2f); return; }
        var snap = undoStack[undoStack.Count - 1];
        undoStack.RemoveAt(undoStack.Count - 1);
        antePending = snap["ante"]; ppPending = snap["pp"];
        ClearChipVisuals(anteArea); ClearChipVisuals(ppArea);
        int ac = Mathf.Clamp((int)(antePending / ChipDenominations.Values[0]), 1, 5);
        for (int i = 0; i < ac && antePending > 0; i++) AddChipVisual(anteArea, -1);
        int pc = Mathf.Clamp((int)(ppPending / ChipDenominations.Values[0]), 1, 5);
        for (int i = 0; i < pc && ppPending > 0; i++) AddChipVisual(ppArea, -1);
        soundManager?.PlayClick();
        RefreshBetDisplay(); RefreshActionButtons();
    }

    void OnRepeatBetClicked()
    {
        if (roundActive) return;
        if (anteLastBet <= 0) { statusText.text = "No previous bet to repeat"; juiceManager?.MicroShake(1.2f); return; }
        long total = anteLastBet + ppLastBet;
        if (!bankroll.CanAfford(total)) { statusText.text = "Not enough balance to repeat"; juiceManager?.MicroShake(1.2f); return; }
        PushUndoSnapshot();
        antePending = anteLastBet; ppPending = ppLastBet;
        ClearChipVisuals(anteArea); ClearChipVisuals(ppArea);
        int ac = Mathf.Clamp((int)(antePending / ChipDenominations.Values[0]), 1, 5);
        for (int i = 0; i < ac; i++) AddChipVisual(anteArea, -1);
        int pc = Mathf.Clamp((int)(ppPending / ChipDenominations.Values[0]), 1, 5);
        for (int i = 0; i < pc && ppPending > 0; i++) AddChipVisual(ppArea, -1);
        soundManager?.PlayChip();
        JuiceTweens.Pulse(this, repeatButton.GetComponent<RectTransform>(), peakScale: 1.15f, duration: 0.2f);
        RefreshBetDisplay(); RefreshActionButtons();
    }

    void OnDealClicked()
    {
        if (roundActive || antePending <= 0) return;
        long totalWithdraw = antePending + ppPending;
        if (!bankroll.TryWithdraw(totalWithdraw)) { statusText.text = "Not enough balance"; juiceManager?.MicroShake(1.2f); return; }

        if (shoe.NeedsReshuffle) milestoneToast?.Show("New shoe — reshuffling", UIFactory.Accent, fontSize: 24);

        anteLastBet = antePending; ppLastBet = ppPending;
        roundActive = true;
        waitingForDecision = false;
        undoStack.Clear();

        currentRound = new ThreeCardPokerRound(shoe);
        currentRound.PlaceBet(ThreeCardPokerBetType.Ante, antePending);
        if (ppPending > 0) currentRound.PlaceBet(ThreeCardPokerBetType.PairPlus, ppPending);
        antePending = ppPending = 0;
        ClearChipVisuals(anteArea); ClearChipVisuals(ppArea);

        ClearCardDisplays();
        RefreshBetDisplay(); RefreshActionButtons();
        statusText.color = UIFactory.Accent;
        statusText.text = "Dealing...";
        StartCoroutine(DealSequence());
    }

    void ClearCardDisplays()
    {
        foreach (var go in playerCardGOs) Destroy(go);
        foreach (var go in dealerCardGOs) Destroy(go);
        playerCardGOs.Clear(); dealerCardGOs.Clear();
        playerHandLabel.text = ""; dealerHandLabel.text = "";
    }

    IEnumerator DealSequence()
    {
        currentRound.Deal();
        const float step = 0.35f;

        // Reveal player cards first (bottom row), then dealer face-down (top row)
        for (int i = 0; i < 3; i++)
        {
            RevealCard(currentRound.PlayerHand.Cards[i], playerCardGOs, new Vector2(-54 + i * 54, -20));
            soundManager?.PlayChip();
            yield return new WaitForSeconds(step);
        }
        // Show player hand rank
        playerHandLabel.text = RankName(currentRound.PlayerHand.Rank);
        playerHandLabel.color = PlayerRankColor(currentRound.PlayerHand.Rank);

        // Dealer cards face down (top row)
        for (int i = 0; i < 3; i++)
        {
            RevealCardFaceDown(new Vector2(-54 + i * 54, 170));
            yield return new WaitForSeconds(step * 0.6f);
        }

        EnterDecisionPhase();
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
        Color suitColor = card.IsRed ? new Color(0.75f, 0.1f, 0.1f) : new Color(0.1f, 0.1f, 0.1f);
        var rankT = UIFactory.MakeText(go.transform, "Rank", new Vector2(0, 16), isPicture ? 16 : 18,
            TextAnchor.MiddleCenter, new Vector2(40, 28), suitColor, FontStyle.Bold);
        rankT.text = CardLabel(card);
        var suitT = UIFactory.MakeText(go.transform, "Suit", new Vector2(0, -14), 14,
            TextAnchor.MiddleCenter, new Vector2(40, 20), suitColor);
        suitT.text = SuitSymbol(card.Suit);
        list.Add(go);
        JuiceTweens.PopIn(this, rt, overshoot: 1.25f, duration: 0.18f);
    }

    void RevealCardFaceDown(Vector2 pos)
    {
        var go = new GameObject("DealerCardBack");
        go.transform.SetParent(tableRoot, false);
        var rt = go.AddComponent<RectTransform>();
        rt.sizeDelta = new Vector2(44, 64);
        rt.anchoredPosition = pos;
        var bg = go.AddComponent<Image>();
        bg.color = new Color(0.15f, 0.28f, 0.50f);
        UIFactory.AddSharpFrame(go, new Color(0.3f, 0.3f, 0.35f), square: true);
        dealerCardGOs.Add(go);
        JuiceTweens.PopIn(this, rt, overshoot: 1.1f, duration: 0.18f);
    }

    static string CardLabel(Card c) => c.Rank switch
    {
        Rank.Ace   => "A",  Rank.King  => "K",  Rank.Queen => "Q",
        Rank.Jack  => "J",  Rank.Ten   => "10",
        _          => ((int)c.Rank).ToString()
    };

    static string SuitSymbol(Suit s) => s switch
    {
        Suit.Hearts   => "♥",
        Suit.Diamonds => "♦",
        Suit.Clubs    => "♣",
        Suit.Spades   => "♠",
        _             => ""
    };

    static string RankName(ThreeCardPokerRank r) => r switch
    {
        ThreeCardPokerRank.StraightFlush => "Straight Flush",
        ThreeCardPokerRank.ThreeOfAKind  => "Three of a Kind",
        ThreeCardPokerRank.Straight      => "Straight",
        ThreeCardPokerRank.Flush         => "Flush",
        ThreeCardPokerRank.Pair          => "Pair",
        _                                => "High Card"
    };

    static Color PlayerRankColor(ThreeCardPokerRank r) => r switch
    {
        ThreeCardPokerRank.StraightFlush => new Color(1f, 0.85f, 0.2f),
        ThreeCardPokerRank.ThreeOfAKind  => new Color(0.8f, 0.5f, 1f),
        ThreeCardPokerRank.Straight      => UIFactory.Positive,
        ThreeCardPokerRank.Flush         => UIFactory.Positive,
        ThreeCardPokerRank.Pair          => UIFactory.TextLight,
        _                                => UIFactory.TextDim
    };

    void EnterDecisionPhase()
    {
        waitingForDecision = true;
        // Show all 3 bet slots with chips: locked ANTE, locked PAIR+, pending PLAY
        RebuildAreaChips(anteArea, anteLastBet);
        RebuildAreaChips(ppArea, ppLastBet);
        RebuildAreaChips(playArea, anteLastBet);
        anteArea.Root.SetActive(true);
        ppArea.Root.SetActive(true);
        playArea.Root.SetActive(true);
        anteArea.AmountText.text = UIFactory.FormatMoney(anteLastBet);
        anteArea.AmountText.color = UIFactory.TextLight;
        anteArea.PayoutText.gameObject.SetActive(false);
        ppArea.AmountText.text = ppLastBet > 0 ? UIFactory.FormatMoney(ppLastBet) : "PAIR+";
        ppArea.AmountText.color = ppLastBet > 0 ? UIFactory.TextLight : UIFactory.TextDim;
        ppArea.PayoutText.gameObject.SetActive(ppLastBet <= 0);
        playArea.AmountText.text = UIFactory.FormatMoney(anteLastBet);
        playArea.AmountText.color = UIFactory.TextLight;
        playArea.PayoutText.gameObject.SetActive(false);
        statusText.color = UIFactory.Accent;
        statusText.text = $"Your hand: {RankName(currentRound.PlayerHand.Rank)} — PLAY or FOLD?";
        RefreshActionButtons();
    }

    void RebuildAreaChips(BetArea area, long amount)
    {
        ClearChipVisuals(area);
        if (amount <= 0) return;
        int count = Mathf.Clamp((int)(amount / ChipDenominations.Values[0]), 1, 5);
        for (int i = 0; i < count; i++) AddChipVisual(area, -1);
    }

    void OnPlayClicked()
    {
        if (!waitingForDecision) return;
        if (!bankroll.TryWithdraw(anteLastBet)) { statusText.text = "Not enough balance to call"; juiceManager?.MicroShake(1.2f); return; }
        soundManager?.PlayChip();
        juiceManager?.Flash(new Color(0.2f, 0.8f, 0.3f, 0.1f), 0.25f);
        waitingForDecision = false;
        StartCoroutine(RevealAndResolve(folded: false));
    }

    void OnFoldClicked()
    {
        if (!waitingForDecision) return;
        soundManager?.PlayClick();
        juiceManager?.Flash(new Color(0.8f, 0.2f, 0.2f, 0.08f), 0.2f);
        waitingForDecision = false;
        StartCoroutine(RevealAndResolve(folded: true));
    }

    IEnumerator RevealAndResolve(bool folded)
    {
        RefreshActionButtons();
        // Flip dealer cards
        for (int i = 0; i < dealerCardGOs.Count; i++) Destroy(dealerCardGOs[i]);
        dealerCardGOs.Clear();
        const float step = 0.3f;
        for (int i = 0; i < currentRound.DealerHand.Cards.Count; i++)
        {
            RevealCard(currentRound.DealerHand.Cards[i], dealerCardGOs, new Vector2(-54 + i * 54, 170));
            soundManager?.PlayChip();
            yield return new WaitForSeconds(step);
        }
        dealerHandLabel.text = RankName(currentRound.DealerHand.Rank)
            + (currentRound.DealerHand.DealerQualifies ? "" : " (no qualify)");
        dealerHandLabel.color = currentRound.DealerHand.DealerQualifies ? UIFactory.TextDim : UIFactory.Accent;

        yield return new WaitForSeconds(0.2f);

        ThreeCardPokerRoundResult result = folded ? currentRound.Fold() : currentRound.Play();
        FinishRound(result);
    }

    void FinishRound(ThreeCardPokerRoundResult result)
    {
        bankroll.Deposit(result.TotalReturned);
        long net = result.NetChange;

        string outcomeStr;
        if (result.PlayerFolded)
            outcomeStr = result.PairPlusReturn > 0 ? "Folded — Pair Plus pays!" : "Folded";
        else
            outcomeStr = result.Outcome switch
            {
                ThreeCardPokerOutcome.PlayerWins      => "You win!",
                ThreeCardPokerOutcome.DealerWins      => "Dealer wins",
                ThreeCardPokerOutcome.Tie             => "Tie",
                ThreeCardPokerOutcome.DealerNoQualify => "Dealer no qualify — Ante paid",
                _                                     => ""
            };

        // Don't show win flavors when player chose to fold (net may still be positive from Pair Plus)
        string flavor = result.PlayerFolded ? "Press DEAL again"
            : net > 0 ? WinFlavors[UnityEngine.Random.Range(0, WinFlavors.Length)]
            : net < 0 ? LoseFlavors[UnityEngine.Random.Range(0, LoseFlavors.Length)]
            : "Press DEAL again";
        statusText.color = net > 0 ? UIFactory.Positive : net < 0 ? UIFactory.Negative : UIFactory.Accent;
        statusText.text  = $"{outcomeStr}  ({(net >= 0 ? "+" : "")}{UIFactory.FormatMoney(net)})  — {flavor}";

        if (net > 0 && result.PlayerFolded)
        {
            // Folded but Pair Plus still paid — quiet acknowledgement, no win streak
            floatingText?.Show($"PP +{UIFactory.FormatMoney(result.PairPlusReturn)}", UIFactory.Positive);
        }
        else if (net > 0)
        {
            soundManager?.PlayWin();
            bool bigWin = result.PlayerHand.Rank >= ThreeCardPokerRank.Straight;
            if (bigWin)
            {
                juiceManager?.Shake(0.5f, 4f); juiceManager?.Flash(new Color(1f, 0.85f, 0.2f, 0.25f), 0.6f);
                juiceManager?.PlayConfetti(2f); juiceManager?.PulseLight(0.9f, 0.7f);
                juiceManager?.PlayMoneyFountain(Vector2.zero);
                floatingText?.Show($"{RankName(result.PlayerHand.Rank)}! +{UIFactory.FormatMoney(net)}", new Color(1f, 0.85f, 0.2f), fontSize: 38);
            }
            else if (net >= ChipDenominations.Values[2]) // $500+ non-special hand
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

        long staked = anteLastBet + ppLastBet + (result.PlayerFolded ? 0 : anteLastBet); // ante+pp+play
        var record = new ThreeCardPokerRoundRecord(roundIndex,
            result.PlayerHand.Rank, result.DealerHand.Rank, result.Outcome,
            result.DealerQualified, result.TotalStaked, result.TotalReturned, bankroll.Balance);
        onRoundResolved?.Invoke(record);
        roundIndex++;
        int[] handTargets = { 50, 100, 250, 500, 1000 };
        foreach (var t in handTargets)
            if (roundIndex == t && roundMilestonesFired.Add(t))
                milestoneToast?.Show($"{t} Hands This Session", UIFactory.Accent, fontSize: 26);

        roundActive = false;
        waitingForDecision = false;
        ClearChipVisuals(anteArea); ClearChipVisuals(ppArea); ClearChipVisuals(playArea);
        playArea.Root.SetActive(false);
        RefreshBetDisplay(); RefreshActionButtons();
    }

    void BuildActionButtons()
    {
        const float y = -398f;
        clearBaseColor  = UIFactory.RedBet;
        dealBaseColor   = UIFactory.Positive;
        repeatBaseColor = UIFactory.AccentDim;

        clearBetButton = UIFactory.MakeButton(tableRoot, "ClearBetBtn",  new Vector2(-183f, y), new Vector2(161, 53), "CLEAR BET",  clearBaseColor,  OnClearBetClicked, 13, pixelFont: true);
        dealButton     = UIFactory.MakeButton(tableRoot, "DealBtn",      new Vector2(   0f, y), new Vector2(184, 62), "DEAL",        dealBaseColor,   OnDealClicked, 20, pixelFont: true);
        repeatButton   = UIFactory.MakeButton(tableRoot, "RepeatBetBtn", new Vector2( 183f, y), new Vector2(161, 53), "REPEAT BET", repeatBaseColor, OnRepeatBetClicked, 12, pixelFont: true);
        undoButton     = UIFactory.MakeButton(tableRoot, "UndoBtn",      new Vector2(-357f, y), new Vector2(138, 53), "UNDO",        UIFactory.AccentDim, UndoLastBetAction, 13, pixelFont: true);
    }

    void BuildDecisionButtons()
    {
        // Play/Fold buttons appear only when waitingForDecision
        playButton = UIFactory.MakeButton(tableRoot, "PlayBtn", new Vector2(-97f, -398f), new Vector2(184, 62),
            "PLAY", UIFactory.Positive, OnPlayClicked, 20, pixelFont: true);
        foldButton = UIFactory.MakeButton(tableRoot, "FoldBtn", new Vector2( 97f, -398f), new Vector2(184, 62),
            "FOLD", UIFactory.RedBet,   OnFoldClicked, 20, pixelFont: true);
        playButton.gameObject.SetActive(false);
        foldButton.gameObject.SetActive(false);
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

    void RefreshBetDisplay()
    {
        // During decision phase all 3 slots are managed by EnterDecisionPhase — don't touch them
        if (waitingForDecision) return;
        bool betPhase = !roundActive;
        anteArea.Root.SetActive(betPhase);
        ppArea.Root.SetActive(betPhase);
        // playArea is hidden except during decision
        if (betPhase)
        {
            anteArea.AmountText.text = antePending > 0 ? UIFactory.FormatMoney(antePending) : "ANTE";
            anteArea.AmountText.color = antePending > 0 ? UIFactory.TextLight : UIFactory.TextDim;
            anteArea.PayoutText.gameObject.SetActive(antePending <= 0);
            ppArea.AmountText.text = ppPending > 0 ? UIFactory.FormatMoney(ppPending) : "PAIR+";
            ppArea.AmountText.color = ppPending > 0 ? UIFactory.TextLight : UIFactory.TextDim;
            ppArea.PayoutText.gameObject.SetActive(ppPending <= 0);
        }
    }

    void RefreshActionButtons()
    {
        bool canDeal = !roundActive && antePending > 0;
        UIFactory.SetButtonState(dealButton,      dealBaseColor,   canDeal);
        UIFactory.SetButtonState(clearBetButton,  clearBaseColor,  !roundActive && (antePending > 0 || ppPending > 0));
        UIFactory.SetButtonState(repeatButton,    repeatBaseColor, !roundActive && anteLastBet > 0);
        UIFactory.SetButtonState(undoButton,      UIFactory.AccentDim, !roundActive && undoStack.Count > 0);
        if (canDeal && !dealWasEnabled && !waitingForDecision) JuiceTweens.Pulse(this, dealButton.GetComponent<RectTransform>(), peakScale: 1.15f, duration: 0.25f);
        dealWasEnabled = canDeal;

        dealButton.gameObject.SetActive(!waitingForDecision);
        clearBetButton.gameObject.SetActive(!roundActive);
        repeatButton.gameObject.SetActive(!roundActive);
        undoButton.gameObject.SetActive(!roundActive);

        playButton.gameObject.SetActive(waitingForDecision);
        foldButton.gameObject.SetActive(waitingForDecision);
    }

    public void SetRoundIndex(int index) => roundIndex = index;

    public void ResetRound()
    {
        currentRound = null;
        roundActive = false;
        waitingForDecision = false;
        winStreak = 0;
        bestRoundNet = 0;
        doubledMilestoneFired = false;
        roundMilestonesFired.Clear();
        streakBadgeGO?.SetActive(false);
        undoStack.Clear();
        antePending = ppPending = 0;
        ClearCardDisplays();
        ClearChipVisuals(anteArea); ClearChipVisuals(ppArea); ClearChipVisuals(playArea);
        playArea.Root.SetActive(false);
        statusText.color = UIFactory.Accent;
        statusText.text  = "Place ANTE to start";
        RefreshBetDisplay(); RefreshActionButtons();
    }
}
