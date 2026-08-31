using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Febucci.UI;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

// Craps' bet surface is fundamentally different from the other three games: bets
// aren't staged locally and committed on one DEAL/SPIN click — they go straight into
// CrapsRound the moment they're placed (withdrawn from the bankroll immediately, same
// as a real felt), because Place/Hardway/Come bets persist and keep resolving across
// MANY rolls, not one. ROLL just asks CrapsRound to resolve the next physical roll
// against whatever's currently on the table. See Assets/Scripts/Core/CrapsRound.cs
// for the actual rules/state machine — this class only captures clicks and displays
// state, same Core-only-owns-the-money-and-odds split every other controller uses.
public class CrapsBettingUIController : MonoBehaviour
{
    Bankroll bankroll;
    ChipSelectorUI chipSelector;
    IRandomSource rng;
    SoundManager soundManager;
    JuiceManager juiceManager;
    FloatingTextUI floatingText;
    FloatingTextUI milestoneToast;
    Action<CrapsRoundRecord> onRoundResolved;
    Action onBankrollChanged;
    Action<string, Color> onRollResolved;
    Action<CrapsRoundRecord> onRollLogged;
    int rollLogIndex;

    CrapsRound currentRound;
    int roundIndex;
    long roundTotalStaked;
    long roundTotalReturned;
    int rollCount;
    int winStreak;
    bool rolling;

    long lastLineAmount;
    readonly Dictionary<CrapsBetType, long> lastOneRollBets = new Dictionary<CrapsBetType, long>();

    TextMeshProUGUI streakText;
    TextAnimator_TMP streakAnimator;
    GameObject streakBadgeGO;

    Transform tableRoot;
    Text statusText;
    Dice3D die1UI, die2UI;
    Dice3D shadowDie1, shadowDie2;
    PreSimResult pendingPresim;
    Coroutine presimRoutine;

    // Odds confirmation modal — auto-opens whenever a point is established on
    // Pass Line or a Come wager parks at its own point, so the player never has
    // to hunt for an ADD ODDS button. Discrete 1x/2x/3x/etc. multiplier buttons
    // match the real casino format (3-4-5x standard, extended to crapless's
    // extra point numbers via CrapsResolver.MaxOddsMultiplier). SKIP = explicit
    // decline, scrim click = same.
    GameObject oddsModalRoot;
    Text oddsModalTitleText, oddsModalOddsText, oddsModalAmountText, oddsModalCapText;
    Button[] multiplierButtons;
    Text[] multiplierLabels;
    CrapsBetType? oddsModalBetType;
    ComeWager oddsModalComeWager;
    int oddsModalPoint;
    bool oddsModalIsDontSide;
    long oddsModalCap;
    long oddsModalBaseAmount;
    long oddsModalPendingAmount;
    Button rollButton, clearBetButton, repeatButton, undoButton, betsToggleButton;
    TextMeshProUGUI betsToggleLabel;
    Color rollBaseColor, clearBaseColor, repeatBaseColor;

    class FlatBarSpot
    {
        public GameObject Root;
        public Text AmountText;
        public readonly List<GameObject> ChipVisuals = new List<GameObject>();
    }

    class NumberSpot
    {
        public int Number;
        public GameObject Root;
        public Text NumberText;
        public Text AmountText;
        public Text PayoutText;
        public Image FrameImg;
        public Color BaseFrameColor;
        public readonly List<GameObject> ChipVisuals = new List<GameObject>();
    }

    FlatBarSpot passSpot, comeSpot, fieldSpot;
    readonly Dictionary<int, NumberSpot> placeSpots = new Dictionary<int, NumberSpot>();
    readonly Dictionary<int, FlatBarSpot> hardSpots = new Dictionary<int, FlatBarSpot>();
    readonly Dictionary<CrapsBetType, FlatBarSpot> propSpots = new Dictionary<CrapsBetType, FlatBarSpot>();
    static readonly CrapsBetType[] PropTypes = { CrapsBetType.AnyCraps, CrapsBetType.AnySeven, CrapsBetType.AnyEleven, CrapsBetType.Horn };

    static readonly int[] PlaceNumbers = { 2, 3, 4, 5, 6, 8, 9, 10, 11, 12 };
    static readonly int[] HardNumbers = { 4, 6, 8, 10 };
    static readonly Color PointHighlight = new Color(1f, 0.75f, 0.2f);
    static readonly Color[] ChipStackColors =
    {
        new Color(0.65f, 0.12f, 0.12f),
        new Color(0.1f, 0.35f, 0.6f),
        new Color(0.1f, 0.1f, 0.1f),
    };

    // Each undo entry reverses exactly the one bet placement it was pushed for —
    // simpler and safer than snapshotting the whole bet state, since Place/Come bets
    // can keep mutating across many rolls in between clicks (unlike roulette/baccarat's
    // single pending-bet dictionary that only ever changes between DEAL clicks).
    readonly List<Action> undoStack = new List<Action>();
    const int MaxUndoDepth = 30;

    const float PanelCenterX = 0f;

    public void Build(Transform canvas, Bankroll bankroll, ChipSelectorUI chipSelector, IRandomSource rng,
        SoundManager soundManager, JuiceManager juiceManager, FloatingTextUI floatingText, FloatingTextUI milestoneToast,
        Dice3D die1, Dice3D die2, Dice3D shadow1, Dice3D shadow2,
        Action<CrapsRoundRecord> onRoundResolved, Action onBankrollChanged,
        Action<string, Color> onRollResolved, Action<CrapsRoundRecord> onRollLogged)
    {
        this.bankroll = bankroll;
        this.chipSelector = chipSelector;
        this.rng = rng;
        this.soundManager = soundManager;
        this.juiceManager = juiceManager;
        this.floatingText = floatingText;
        this.milestoneToast = milestoneToast;
        this.onRoundResolved = onRoundResolved;
        this.onBankrollChanged = onBankrollChanged;
        this.onRollResolved = onRollResolved;
        this.onRollLogged = onRollLogged;
        die1UI = die1;
        die2UI = die2;
        shadowDie1 = shadow1;
        shadowDie2 = shadow2;

        currentRound = new CrapsRound(rng);

        var tableRootGO = new GameObject("CrapsUIRoot");
        tableRootGO.transform.SetParent(canvas, false);
        var tableRootRT = tableRootGO.AddComponent<RectTransform>();
        tableRootRT.anchorMin = new Vector2(0.5f, 0.5f);
        tableRootRT.anchorMax = new Vector2(0.5f, 0.5f);
        tableRootRT.pivot = new Vector2(0.5f, 0.5f);
        tableRootRT.anchoredPosition = Vector2.zero;
        tableRoot = tableRootGO.transform;

        // Header/status live in the TOP half of the screen (near the HUD and the 3D
        // dice they describe) — the felt itself is confined to the BOTTOM half, per
        // the real-table reference the user pointed at. Every row below was placed
        // with an explicit, checked gap against its neighbor (and against the
        // resultsStrip CrapsGameManager places below this whole panel), verified as
        // one whole screenshot — the same "check the whole stack" discipline the
        // roulette header/status and results-strip-clipping fixes established
        // earlier this session, applied up front this time instead of after the fact.
        UIFactory.MakeHeroTitle(tableRoot, "Header_Craps", new Vector2(PanelCenterX, 340), "CRAPS TABLE", 24);
        var statusPanelBg = UIFactory.MakePanel(tableRoot, "StatusPanelBg", new Vector2(PanelCenterX, 300), new Vector2(600, 36), UIFactory.PanelDark, shadow: false);
        UIFactory.AddSharpFrame(statusPanelBg, UIFactory.AccentDim, square: true);
        statusText = UIFactory.MakeText(tableRoot, "StatusText", new Vector2(PanelCenterX, 300), 16,
            sizeDelta: new Vector2(580, 30), color: UIFactory.Accent, style: FontStyle.Bold);
        statusText.text = "Come out — place Pass Line, then ROLL";

        // Felt background — confined to the bottom half of the screen (top edge
        // ~10, bottom ~-424, well clear of the resultsStrip CrapsGameManager places
        // below it at -480) instead of the earlier version's near-full-screen panel.
        const float feltCenterY = -228f;
        UIFactory.MakePanel(tableRoot, "CrapsPanelBg", new Vector2(PanelCenterX, feltCenterY), new Vector2(1400, 476), UIFactory.PanelDark);

        BuildSideBetPanel();

        // Field — ONE unified bar (not 7 separate boxes) with its winning numbers
        // printed inside it, matching a real felt's field box: one printed rectangle,
        // not a row of buttons.
        fieldSpot = BuildBarSpot("FieldSpot", new Vector2(0, -86), new Vector2(760, 70), new Color(0.55f, 0.45f, 0.15f), OnFieldBetClicked);
        var fieldAmtRt = fieldSpot.AmountText.GetComponent<RectTransform>();
        fieldAmtRt.anchoredPosition = new Vector2(0, 20);
        fieldAmtRt.sizeDelta = new Vector2(720, 20);

        // Each number is its own positioned Text (not one shared string) so 2 and 12
        // can be individually bigger/bolder, evenly spaced, with their payout tags
        // sitting directly underneath them — not an approximate offset guessed
        // against one centered block of text — and pulled fully inside the box's own
        // bounds (y=-24, box half-height 35) instead of nearly poking past the edge.
        int[] fieldNumbers = { 2, 3, 4, 9, 10, 11, 12 };
        const float fieldSpacing = 100f;
        float fieldStartX = -(fieldNumbers.Length - 1) * fieldSpacing / 2f;
        for (int i = 0; i < fieldNumbers.Length; i++)
        {
            int n = fieldNumbers[i];
            bool bonus = n == 2 || n == 12;
            var numText = UIFactory.MakeText(fieldSpot.Root.transform, $"FieldNum_{n}", new Vector2(fieldStartX + i * fieldSpacing, -4), bonus ? 22 : 15,
                sizeDelta: new Vector2(fieldSpacing - 6, 30), color: UIFactory.TextLight, style: FontStyle.Bold);
            numText.text = n.ToString();
            if (bonus)
            {
                var payTag = UIFactory.MakeText(fieldSpot.Root.transform, $"FieldPay_{n}", new Vector2(fieldStartX + i * fieldSpacing, -24), 13,
                    sizeDelta: new Vector2(fieldSpacing + 10, 18), color: new Color(1f, 0.75f, 0.2f), style: FontStyle.Bold);
                payTag.text = n == 2 ? "PAYS 2X" : "PAYS 3X";
            }
        }

        // Come sits directly ABOVE the Place row, Pass Line directly BELOW it —
        // the numbers grid sits "wrapped" between them instead of both bars stacked
        // above the numbers, per the reference table's framing.
        comeSpot = BuildBarSpot("ComeSpot", new Vector2(0, -155), new Vector2(1280, 46), UIFactory.AccentDim, () => OnComeBetClicked());

        // Place — kept horizontal, but whichever number matches the live point gets
        // a bright amber ring (see UpdatePointHighlight, called from RefreshBetDisplay
        // whenever the point changes) so it's visible at a glance, not just in text.
        const float placeSpacing = 108f;
        float placeStartX = -PlaceNumbers.Length * placeSpacing / 2f + placeSpacing / 2f;
        for (int i = 0; i < PlaceNumbers.Length; i++)
        {
            int n = PlaceNumbers[i];
            var pos = new Vector2(placeStartX + i * placeSpacing, -225);
            placeSpots[n] = BuildNumberSpot(n, pos, 74, UIFactory.AccentDim, PlacePayoutLabel(n), () => OnPlaceBetClicked(n), square: true);
        }

        passSpot = BuildBarSpot("PassLineSpot", new Vector2(0, -325), new Vector2(1280, 54), UIFactory.Positive, () => OnLineBetClicked());

        const float actionY = -378;
        clearBaseColor = UIFactory.RedBet;
        rollBaseColor = UIFactory.Positive;
        repeatBaseColor = UIFactory.AccentDim;
        clearBetButton = UIFactory.MakeButton(tableRoot, "ClearBetBtn", new Vector2(-330f, actionY), new Vector2(150, 46),
            "CLEAR BET", clearBaseColor, OnClearBetClicked, 13, pixelFont: true);
        rollButton = UIFactory.MakeButton(tableRoot, "RollBtn", new Vector2(-110f, actionY), new Vector2(170, 54),
            "ROLL", rollBaseColor, OnRollClicked, 18, pixelFont: true);
        repeatButton = UIFactory.MakeButton(tableRoot, "RepeatBetBtn", new Vector2(110f, actionY), new Vector2(150, 46),
            "REPEAT BET", repeatBaseColor, OnRepeatBetClicked, 12, pixelFont: true);
        undoButton = UIFactory.MakeButton(tableRoot, "UndoBtn", new Vector2(330f, actionY), new Vector2(130, 46),
            "UNDO", UIFactory.AccentDim, OnUndoClicked, 13, pixelFont: true);
        // Real bubble-craps machines offer this as a standing "BETS ON/OFF" toggle
        // instead of the standard off-by-default-on-come-out house rule — forces
        // Place bets to work through the come-out roll too. Sits inside the felt,
        // just right of the Field bar (which ends at x=380) instead of floating up
        // near the header disconnected from the table — it's a persistent mode
        // toggle, not a one-shot action, so it still stays out of the action row.
        betsToggleButton = UIFactory.MakeButton(tableRoot, "BetsToggleBtn", new Vector2(545f, -86f), new Vector2(140, 46),
            "BETS OFF", UIFactory.AccentDim, OnBetsToggleClicked, 13, pixelFont: true);
        betsToggleLabel = betsToggleButton.GetComponentInChildren<TextMeshProUGUI>();

        BuildStreakBadge();
        BuildOddsModal(canvas);

        RefreshBetDisplay();
        RefreshActionButtons();

        // Kick off the first pre-sim immediately — by the time the player can click
        // ROLL, the shadow dice will already have a valid trajectory ready.
        StartPresim();
    }

    void StartPresim()
    {
        if (presimRoutine != null) StopCoroutine(presimRoutine);
        pendingPresim = null;
        presimRoutine = StartCoroutine(Dice3D.RunPreSim(shadowDie1, shadowDie2, result => { pendingPresim = result; presimRoutine = null; }));
    }

    // Same construction pattern as the other games' streak/achievement badges: framed
    // black panel + TMP + Text Animator, built while active (TMP's outline throws if
    // set on an already-inactive object) and deactivated only once configured.
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
        streakText.enableWordWrapping = false;
        streakText.fontSize = 20;
        streakText.outlineWidth = 0.25f;
        streakText.outlineColor = new Color32(0, 0, 0, 230);
        streakAnimator = textGO.AddComponent<TextAnimator_TMP>();

        streakBadgeGO.SetActive(false);
    }

    FlatBarSpot BuildBarSpot(string name, Vector2 pos, Vector2 size, Color accentColor, UnityEngine.Events.UnityAction onClick)
    {
        var go = new GameObject(name);
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
        btn.onClick.AddListener(onClick);

        var amountText = UIFactory.MakeText(go.transform, "AmountText", Vector2.zero, 15,
            sizeDelta: size - new Vector2(10, 10), color: UIFactory.TextDim, style: FontStyle.Bold);

        return new FlatBarSpot { Root = go, AmountText = amountText };
    }

    NumberSpot BuildNumberSpot(int number, Vector2 pos, float diameter, Color accentColor, string payoutLabel, UnityEngine.Events.UnityAction onClick, bool square = false, string namePrefix = "NumberSpot")
    {
        var go = new GameObject($"{namePrefix}_{number}");
        go.transform.SetParent(tableRoot, false);
        var rt = go.AddComponent<RectTransform>();
        rt.sizeDelta = new Vector2(diameter, diameter);
        rt.anchoredPosition = pos;
        var fill = go.AddComponent<Image>();
        fill.sprite = square ? UIFactory.RoundedRect() : UIFactory.Circle();
        if (square) fill.type = Image.Type.Sliced;
        fill.color = new Color(1f, 1f, 1f, 0.06f);
        var frameImg = UIFactory.AddSharpFrame(go, accentColor, square: square);
        var btn = go.AddComponent<Button>();
        btn.targetGraphic = fill;
        btn.onClick.AddListener(onClick);

        // The NUMBER is a permanent label — it never gets overwritten by the bet
        // amount (that was the actual bug: betting used to replace "9" with "25",
        // so every active spot just read as an anonymous "25" and the only way to
        // tell them apart was counting positions across the row). Amount and payout
        // are separate rows underneath, both visible at once — number + chips +
        // payout all readable together, matching a real table's felt printing.
        var numberText = UIFactory.MakeText(go.transform, "NumberText", new Vector2(0, diameter * 0.28f), 16,
            sizeDelta: new Vector2(diameter - 8, diameter * 0.3f), color: UIFactory.TextLight, style: FontStyle.Bold);
        numberText.text = $"{number}";
        var numberShadow = numberText.gameObject.AddComponent<Shadow>();
        numberShadow.effectColor = new Color(0, 0, 0, 0.85f);
        numberShadow.effectDistance = new Vector2(1, -1);

        var amountText = UIFactory.MakeText(go.transform, "AmountText", new Vector2(0, diameter * 0.02f), 13,
            sizeDelta: new Vector2(diameter - 8, diameter * 0.24f), color: UIFactory.Positive, style: FontStyle.Bold);
        var amountShadow = amountText.gameObject.AddComponent<Shadow>();
        amountShadow.effectColor = new Color(0, 0, 0, 0.85f);
        amountShadow.effectDistance = new Vector2(1, -1);

        var payoutText = UIFactory.MakeText(go.transform, "PayoutText", new Vector2(0, -diameter * 0.34f), 13,
            sizeDelta: new Vector2(diameter - 4, 16), color: UIFactory.TextDim);
        payoutText.text = payoutLabel;

        return new NumberSpot { Number = number, Root = go, NumberText = numberText, AmountText = amountText, PayoutText = payoutText, FrameImg = frameImg, BaseFrameColor = accentColor };
    }

    // Two permanent side panels flanking the felt, no toggle — Hardways on the left,
    // One-Roll Bets on the right, mirroring each other and both always visible at
    // once instead of sharing one tabbed column. Same vertical extent as the felt
    // itself (panelCenterY/height match CrapsPanelBg exactly). History moves up
    // into the top-right (shrunk to fit above this panel) to make room — see
    // CrapsGameManager.cs.
    void BuildSideBetPanel()
    {
        const float leftX = -815f;
        const float rightX = 815f;
        const float panelCenterY = -228f;
        UIFactory.MakePanel(tableRoot, "HardwaysPanelBg", new Vector2(leftX, panelCenterY), new Vector2(210, 476), UIFactory.PanelDark);
        UIFactory.MakeSectionHeader(tableRoot, "Hardways", new Vector2(leftX, panelCenterY + 218), new Vector2(190, 20));
        UIFactory.MakePanel(tableRoot, "PropPanelBg", new Vector2(rightX, panelCenterY), new Vector2(210, 476), UIFactory.PanelDark);
        UIFactory.MakeSectionHeader(tableRoot, "One-Roll Bets", new Vector2(rightX, panelCenterY + 218), new Vector2(190, 20));

        float[] rowY = { -105f, -187f, -269f, -351f };
        for (int i = 0; i < HardNumbers.Length; i++)
        {
            int n = HardNumbers[i];
            hardSpots[n] = BuildSideBetRow($"HardSpot_{n}", new Vector2(leftX, rowY[i]), new Color(0.55f, 0.3f, 0.55f),
                HardPayoutLabel(n), () => OnHardwayBetClicked(n));
        }
        for (int i = 0; i < PropTypes.Length; i++)
        {
            var t = PropTypes[i];
            propSpots[t] = BuildSideBetRow($"PropSpot_{t}", new Vector2(rightX, rowY[i]), new Color(0.2f, 0.45f, 0.55f),
                PropPayoutLabel(t), () => OnPropBetClicked(t));
        }
    }

    FlatBarSpot BuildSideBetRow(string name, Vector2 pos, Color accentColor, string payoutLabel, UnityEngine.Events.UnityAction onClick)
    {
        var spot = BuildBarSpot(name, pos, new Vector2(190, 70), accentColor, onClick);
        var amtRt = spot.AmountText.GetComponent<RectTransform>();
        amtRt.anchoredPosition = new Vector2(0, 12);
        var payoutText = UIFactory.MakeText(spot.Root.transform, "PayoutText", new Vector2(0, -16), 15,
            sizeDelta: new Vector2(170, 20), color: UIFactory.TextDim);
        payoutText.text = payoutLabel;
        return spot;
    }

    static string PropLabel(CrapsBetType t) => t switch
    {
        CrapsBetType.AnyCraps => "ANY CRAPS",
        CrapsBetType.AnySeven => "ANY SEVEN",
        CrapsBetType.AnyEleven => "ELEVEN",
        _ => "HORN"
    };

    static string PropPayoutLabel(CrapsBetType t) => t switch
    {
        CrapsBetType.AnyCraps => "7:1",
        CrapsBetType.AnySeven => "4:1",
        CrapsBetType.AnyEleven => "15:1",
        _ => "SPLIT 4-WAY"
    };

    static string PlacePayoutLabel(int n) => n switch
    {
        4 or 10 => "9:5",
        5 or 9 => "7:5",
        6 or 8 => "7:6",
        2 or 12 => "11:2",
        3 or 11 => "11:4",
        _ => ""
    };

    static string HardPayoutLabel(int n) => n is 4 or 10 ? "7:1" : "9:1";

    static CrapsBetType PlaceTypeFor(int n) => n switch
    {
        2 => CrapsBetType.Place2,
        3 => CrapsBetType.Place3,
        4 => CrapsBetType.Place4,
        5 => CrapsBetType.Place5,
        6 => CrapsBetType.Place6,
        8 => CrapsBetType.Place8,
        9 => CrapsBetType.Place9,
        10 => CrapsBetType.Place10,
        11 => CrapsBetType.Place11,
        _ => CrapsBetType.Place12
    };

    static CrapsBetType HardTypeFor(int n) => n switch
    {
        4 => CrapsBetType.Hard4,
        6 => CrapsBetType.Hard6,
        8 => CrapsBetType.Hard8,
        _ => CrapsBetType.Hard10
    };

    // ---- Betting ----

    void FlashBlocked() => juiceManager?.MicroShake(1.2f);

    void PushUndoBet(CrapsBetType type, long amount)
    {
        undoStack.Add(() =>
        {
            currentRound.PlaceBet(type, -amount);
            bankroll.Deposit(amount);
            roundTotalStaked -= amount;
            RefreshBetDisplay();
            RefreshActionButtons();
        });
        if (undoStack.Count > MaxUndoDepth) undoStack.RemoveAt(0);
    }

    void OnLineBetClicked()
    {
        if (currentRound.Phase != CrapsPhase.ComeOut)
        {
            statusText.text = "Point already set — Pass Line locks until it resolves";
            FlashBlocked();
            return;
        }
        long chip = chipSelector.SelectedChip;
        if (!bankroll.TryWithdraw(chip))
        {
            statusText.text = bankroll.Balance < ChipDenominations.Values[0]
                ? "Out of chips — use ADD FUNDS above to keep playing"
                : "Not enough balance for that bet";
            FlashBlocked();
            return;
        }
        currentRound.PlaceBet(CrapsBetType.PassLine, chip);
        roundTotalStaked += chip;
        soundManager?.PlayChip();
        PushUndoBet(CrapsBetType.PassLine, chip);
        RefreshBetDisplay();
        RefreshActionButtons();
    }

    void OnComeBetClicked()
    {
        if (currentRound.Phase != CrapsPhase.Point)
        {
            statusText.text = "Come only available once a point is set";
            FlashBlocked();
            return;
        }
        long chip = chipSelector.SelectedChip;
        if (!bankroll.TryWithdraw(chip))
        {
            statusText.text = "Not enough balance for that bet";
            FlashBlocked();
            return;
        }
        var wager = currentRound.PlaceComeBet(chip);
        roundTotalStaked += chip;
        soundManager?.PlayChip();
        // Only undoable while still "traveling" (no point yet) — once parked it's a
        // contract bet like everything else, same rule real craps uses.
        undoStack.Add(() =>
        {
            if (wager.Point == null && currentRound.RemoveComeWager(wager))
            {
                bankroll.Deposit(chip);
                roundTotalStaked -= chip;
                RefreshBetDisplay();
                RefreshActionButtons();
            }
        });
        if (undoStack.Count > MaxUndoDepth) undoStack.RemoveAt(0);
        RefreshBetDisplay();
        RefreshActionButtons();
    }

    void OnFieldBetClicked()
    {
        long chip = chipSelector.SelectedChip;
        if (!bankroll.TryWithdraw(chip))
        {
            statusText.text = "Not enough balance for that bet";
            FlashBlocked();
            return;
        }
        currentRound.PlaceBet(CrapsBetType.Field, chip);
        roundTotalStaked += chip;
        soundManager?.PlayChip();
        PushUndoBet(CrapsBetType.Field, chip);
        RefreshBetDisplay();
        RefreshActionButtons();
    }

    void OnPlaceBetClicked(int number)
    {
        long chip = chipSelector.SelectedChip;
        if (!bankroll.TryWithdraw(chip))
        {
            statusText.text = "Not enough balance for that bet";
            FlashBlocked();
            return;
        }
        var type = PlaceTypeFor(number);
        currentRound.PlaceBet(type, chip);
        roundTotalStaked += chip;
        soundManager?.PlayChip();
        JuiceTweens.Pulse(this, (RectTransform)placeSpots[number].Root.transform, peakScale: 1.12f, duration: 0.18f);
        PushUndoBet(type, chip);
        RefreshBetDisplay();
        RefreshActionButtons();
    }

    void OnHardwayBetClicked(int number)
    {
        long chip = chipSelector.SelectedChip;
        if (!bankroll.TryWithdraw(chip))
        {
            statusText.text = "Not enough balance for that bet";
            FlashBlocked();
            return;
        }
        var type = HardTypeFor(number);
        currentRound.PlaceBet(type, chip);
        roundTotalStaked += chip;
        soundManager?.PlayChip();
        JuiceTweens.Pulse(this, (RectTransform)hardSpots[number].Root.transform, peakScale: 1.12f, duration: 0.18f);
        PushUndoBet(type, chip);
        RefreshBetDisplay();
        RefreshActionButtons();
    }

    // Any Craps/Any Seven/Eleven/Horn — one-roll props, same "clickable anytime,
    // resolves every roll, no phase gating" pattern Field already uses.
    void OnPropBetClicked(CrapsBetType type)
    {
        long chip = chipSelector.SelectedChip;
        if (!bankroll.TryWithdraw(chip))
        {
            statusText.text = "Not enough balance for that bet";
            FlashBlocked();
            return;
        }
        currentRound.PlaceBet(type, chip);
        roundTotalStaked += chip;
        soundManager?.PlayChip();
        JuiceTweens.Pulse(this, (RectTransform)propSpots[type].Root.transform, peakScale: 1.12f, duration: 0.18f);
        PushUndoBet(type, chip);
        RefreshBetDisplay();
        RefreshActionButtons();
    }

    void BuildOddsModal(Transform canvas)
    {
        oddsModalRoot = new GameObject("OddsModal");
        oddsModalRoot.transform.SetParent(canvas, false);
        var rt = oddsModalRoot.AddComponent<RectTransform>();
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = Vector2.zero;
        rt.offsetMax = Vector2.zero;

        var scrimGO = new GameObject("Scrim");
        scrimGO.transform.SetParent(oddsModalRoot.transform, false);
        var scrimRt = scrimGO.AddComponent<RectTransform>();
        scrimRt.anchorMin = Vector2.zero;
        scrimRt.anchorMax = Vector2.one;
        scrimRt.offsetMin = Vector2.zero;
        scrimRt.offsetMax = Vector2.zero;
        var scrimImg = scrimGO.AddComponent<Image>();
        scrimImg.color = new Color(0.02f, 0.02f, 0.03f, 0.93f);
        var scrimBtn = scrimGO.AddComponent<Button>();
        scrimBtn.transition = Selectable.Transition.None;
        scrimBtn.onClick.AddListener(HideOddsModal);

        var panel = UIFactory.MakeFramedPanel(oddsModalRoot.transform, "OddsModalPanel", Vector2.zero, new Vector2(660, 460), Color.black);

        oddsModalTitleText = UIFactory.MakeText(panel.transform, "OddsModalTitle", new Vector2(0, 185), 26,
            sizeDelta: new Vector2(620, 36), color: UIFactory.TextLight, style: FontStyle.Bold);
        oddsModalOddsText = UIFactory.MakeText(panel.transform, "OddsModalOdds", new Vector2(0, 130), 18,
            sizeDelta: new Vector2(620, 62), color: UIFactory.Accent);
        oddsModalAmountText = UIFactory.MakeText(panel.transform, "OddsModalAmount", new Vector2(0, 58), 36,
            sizeDelta: new Vector2(620, 50), color: UIFactory.TextLight, style: FontStyle.Bold);
        oddsModalCapText = UIFactory.MakeText(panel.transform, "OddsModalCap", new Vector2(0, 20), 17,
            sizeDelta: new Vector2(620, 26), color: UIFactory.Accent);

        // Discrete multiplier buttons (1x–5x) — real casino format. Only buttons up
        // to MaxOddsMultiplier(point) are shown; the rest are hidden per OpenOddsModal.
        multiplierButtons = new Button[5];
        multiplierLabels = new Text[5];
        float[] btnX = { -224f, -112f, 0f, 112f, 224f };
        for (int i = 0; i < 5; i++)
        {
            var go = new GameObject($"OddsMultBtn_{i + 1}x");
            go.transform.SetParent(panel.transform, false);
            var btnRt = go.AddComponent<RectTransform>();
            btnRt.sizeDelta = new Vector2(102, 66);
            btnRt.anchorMin = btnRt.anchorMax = new Vector2(0.5f, 0.5f);
            btnRt.pivot = new Vector2(0.5f, 0.5f);
            btnRt.anchoredPosition = new Vector2(btnX[i], -48f);
            var img = go.AddComponent<Image>();
            img.sprite = UIFactory.RoundedRect();
            img.type = Image.Type.Sliced;
            img.color = UIFactory.AccentDim;
            var btn = go.AddComponent<Button>();
            btn.targetGraphic = img;
            multiplierButtons[i] = btn;

            multiplierLabels[i] = UIFactory.MakeText(go.transform, "Label", Vector2.zero, 15,
                TextAnchor.MiddleCenter, new Vector2(98, 62), UIFactory.TextLight);
        }

        UIFactory.MakeButton(panel.transform, "OddsModalConfirm", new Vector2(-115, -155), new Vector2(220, 58),
            "CONFIRM ODDS", UIFactory.Positive, OnOddsModalConfirm, 16, pixelFont: true);
        UIFactory.MakeButton(panel.transform, "OddsModalSkip", new Vector2(115, -155), new Vector2(220, 58),
            "SKIP ODDS", UIFactory.AccentDim, HideOddsModal, 16, pixelFont: true);

        oddsModalRoot.SetActive(false);
    }

    void OpenOddsModal(CrapsBetType? betType, ComeWager comeWager, string title, int point, long baseAmount, bool isDontSide)
    {
        oddsModalBetType = betType;
        oddsModalComeWager = comeWager;
        oddsModalPoint = point;
        oddsModalIsDontSide = isDontSide;
        oddsModalBaseAmount = baseAmount;
        int maxMult = CrapsResolver.MaxOddsMultiplier(point);
        oddsModalCap = baseAmount * maxMult;
        oddsModalPendingAmount = baseAmount; // default to 1x

        oddsModalTitleText.text = title;
        oddsModalCapText.text = $"Max: {maxMult}x odds  =  {UIFactory.FormatMoney(oddsModalCap)}";

        // Configure multiplier buttons 1x..maxMult; hide the rest.
        for (int i = 0; i < 5; i++)
        {
            int mult = i + 1;
            bool visible = mult <= maxMult;
            multiplierButtons[i].gameObject.SetActive(visible);
            if (!visible) continue;
            long amount = baseAmount * mult;
            int capturedMult = mult;
            long capturedAmount = amount;
            multiplierLabels[i].text = $"{mult}x\n{UIFactory.FormatMoney(amount)}";
            multiplierButtons[i].onClick.RemoveAllListeners();
            multiplierButtons[i].onClick.AddListener(() => SetOddsModalAmount(capturedAmount));
        }

        RefreshOddsModalPreview();
        RefreshOddsModalButtonHighlights();

        oddsModalRoot.transform.SetAsLastSibling();
        oddsModalRoot.SetActive(true);
    }

    void SetOddsModalAmount(long amount)
    {
        oddsModalPendingAmount = amount;
        RefreshOddsModalPreview();
        RefreshOddsModalButtonHighlights();
    }

    void RefreshOddsModalButtonHighlights()
    {
        for (int i = 0; i < 5; i++)
        {
            if (!multiplierButtons[i].gameObject.activeSelf) continue;
            long amount = oddsModalBaseAmount * (i + 1);
            bool selected = amount == oddsModalPendingAmount;
            multiplierButtons[i].GetComponent<Image>().color = selected ? UIFactory.Accent : UIFactory.AccentDim;
            // Dark text on the bright amber selected state; light text on the dim unselected state.
            multiplierLabels[i].color = selected ? new Color(0.08f, 0.06f, 0f) : UIFactory.TextLight;
        }
    }

    void RefreshOddsModalPreview()
    {
        var (num, den) = CrapsResolver.TrueOdds(oddsModalPoint);
        string ratioText = oddsModalIsDontSide ? $"Lay odds {den}:{num}" : $"True odds {num}:{den}";
        long payout = oddsModalIsDontSide
            ? CrapsResolver.LayOddsPayout(oddsModalPendingAmount, oddsModalPoint)
            : CrapsResolver.OddsPayout(oddsModalPendingAmount, oddsModalPoint);
        long profit = payout - oddsModalPendingAmount;

        oddsModalOddsText.text = $"Point is {oddsModalPoint}  —  {ratioText}\n" +
            (oddsModalPendingAmount > 0
                ? $"Win {UIFactory.FormatMoney(profit)} on top of your stake back"
                : "Add chips below to build your odds stake");
        oddsModalAmountText.text = $"Stake: {UIFactory.FormatMoney(oddsModalPendingAmount)}";
        oddsModalAmountText.color = oddsModalPendingAmount > 0 ? UIFactory.TextLight : UIFactory.TextDim;
    }

    void OnOddsModalConfirm()
    {
        long amount = oddsModalPendingAmount;
        if (amount <= 0) { HideOddsModal(); return; }
        if (!bankroll.TryWithdraw(amount)) { FlashBlocked(); HideOddsModal(); return; }

        if (oddsModalComeWager != null)
        {
            currentRound.AddComeOdds(oddsModalComeWager, amount);
            // Not pushed to the undo stack — laid/taken odds behind a Come bet are a
            // contract bet like the base wager itself, same as Pass/Don't Pass Odds
            // being undoable only because we track them by flat bet type, not by a
            // per-wager amount ComeWager doesn't expose a way to subtract from.
        }
        else if (oddsModalBetType.HasValue)
        {
            currentRound.PlaceBet(oddsModalBetType.Value, amount);
            PushUndoBet(oddsModalBetType.Value, amount);
        }
        roundTotalStaked += amount;

        soundManager?.PlayChip();
        HideOddsModal();
        RefreshBetDisplay();
        RefreshActionButtons();
    }

    void HideOddsModal() => oddsModalRoot.SetActive(false);

    void OnClearBetClicked()
    {
        long refunded = 0;
        long field = currentRound.GetBet(CrapsBetType.Field);
        if (field > 0) { currentRound.PlaceBet(CrapsBetType.Field, -field); refunded += field; }
        foreach (int n in PlaceNumbers)
        {
            var t = PlaceTypeFor(n);
            long b = currentRound.GetBet(t);
            if (b > 0) { currentRound.PlaceBet(t, -b); refunded += b; }
        }
        foreach (int n in HardNumbers)
        {
            var t = HardTypeFor(n);
            long b = currentRound.GetBet(t);
            if (b > 0) { currentRound.PlaceBet(t, -b); refunded += b; }
        }
        foreach (var t in PropTypes)
        {
            long b = currentRound.GetBet(t);
            if (b > 0) { currentRound.PlaceBet(t, -b); refunded += b; }
        }
        // Pass Line is a contract bet once a point is established — only
        // clearable while still in the come-out phase, same real-table rule.
        if (currentRound.Phase == CrapsPhase.ComeOut)
        {
            long pass = currentRound.GetBet(CrapsBetType.PassLine);
            if (pass > 0) { currentRound.PlaceBet(CrapsBetType.PassLine, -pass); refunded += pass; }
        }

        if (refunded <= 0)
        {
            statusText.text = "Nothing to clear";
            FlashBlocked();
            return;
        }
        bankroll.Deposit(refunded);
        roundTotalStaked -= refunded;
        undoStack.Clear(); // bulk clear invalidates fine-grained undo history
        soundManager?.PlayClick();
        RefreshBetDisplay();
        RefreshActionButtons();
    }

    void OnRepeatBetClicked()
    {
        bool didAnything = false;

        // Line bet — come-out only, skip if already placed.
        if (currentRound.Phase == CrapsPhase.ComeOut && lastLineAmount > 0
            && currentRound.GetBet(CrapsBetType.PassLine) == 0)
        {
            if (bankroll.TryWithdraw(lastLineAmount))
            {
                currentRound.PlaceBet(CrapsBetType.PassLine, lastLineAmount);
                roundTotalStaked += lastLineAmount;
                PushUndoBet(CrapsBetType.PassLine, lastLineAmount);
                didAnything = true;
            }
        }

        // One-roll bets — re-place any that aren't already staked.
        foreach (var kv in lastOneRollBets)
        {
            if (currentRound.GetBet(kv.Key) > 0) continue;
            if (!bankroll.TryWithdraw(kv.Value)) continue;
            currentRound.PlaceBet(kv.Key, kv.Value);
            roundTotalStaked += kv.Value;
            PushUndoBet(kv.Key, kv.Value);
            didAnything = true;
        }

        if (!didAnything)
        {
            statusText.text = "Nothing to repeat";
            FlashBlocked();
            return;
        }
        soundManager?.PlayChip();
        RefreshBetDisplay();
        RefreshActionButtons();
    }

    void OnUndoClicked()
    {
        if (undoStack.Count == 0)
        {
            statusText.text = "Nothing to undo";
            FlashBlocked();
            return;
        }
        var action = undoStack[undoStack.Count - 1];
        undoStack.RemoveAt(undoStack.Count - 1);
        action();
        soundManager?.PlayClick();
    }

    // ---- Rolling ----

    // Real bubble-craps machines let you roll with only Place/Field/Hardway bets
    // down — a Pass Line bet is not required, matching that reference.
    void OnRollClicked()
    {
        if (rolling) return;
        rolling = true;
        rollButton.interactable = false;
        undoStack.Clear();
        StartCoroutine(RollSequence());
    }

    // "BETS ON/OFF" — forces Place bets to work through the come-out roll instead
    // of the standard off-by-default house rule, same toggle real bubble-craps
    // machines offer.
    void OnBetsToggleClicked()
    {
        currentRound.PlaceBetsWorking = !currentRound.PlaceBetsWorking;
        soundManager?.PlayClick();
        RefreshBetDisplay();
        RefreshActionButtons();
    }

    IEnumerator RollSequence()
    {
        rolling = true;
        RefreshActionButtons();

        long passBefore = currentRound.GetBet(CrapsBetType.PassLine);
        long passOddsBefore = currentRound.GetBet(CrapsBetType.PassOdds);
        int pointBefore = currentRound.Point ?? 0;
        // Place/Hardway/Come bets clear to zero on a seven-out with no payout field
        // of their own (a 7 never matches any of their numbers) — captured before
        // the roll so ApplyRollResult can show that loss on the seven-out's own
        // history row instead of it silently vanishing (the bankroll itself already
        // reflects it correctly from the original stake withdrawal; this is only
        // about which row gets credited with the loss the player actually felt).
        long placesBefore = 0;
        foreach (var n in PlaceNumbers) placesBefore += currentRound.GetBet(PlaceTypeFor(n));
        long activeBetsBefore = SumActiveClearableBets(); // Place + Hardway + Come wagers
        long nonPlaceActiveBefore = activeBetsBefore - placesBefore; // Hardway + Come wagers
        long oneRollStakesBefore = SumOneRollBets();
        bool placesWorkingBefore = currentRound.PlaceBetsWorking;
        // Per-hardway snapshot so ApplyRollResult can detect easy-way losses
        // (a Hardway cleared by its number hitting non-hard — not a 7, not a win).
        var hardBefore = new long[HardNumbers.Length];
        for (int i = 0; i < HardNumbers.Length; i++)
            hardBefore[i] = currentRound.GetBet(HardTypeFor(HardNumbers[i]));

        // Save per-bet amounts so REPEAT BET can re-place them next roll.
        lastOneRollBets.Clear();
        foreach (var t in PropTypes)
        {
            long b = currentRound.GetBet(t);
            if (b > 0) lastOneRollBets[t] = b;
        }
        long fieldBetNow = currentRound.GetBet(CrapsBetType.Field);
        if (fieldBetNow > 0) lastOneRollBets[CrapsBetType.Field] = fieldBetNow;

        // Wait for pre-sim if player pressed ROLL before it finished (rare edge case).
        while (pendingPresim == null) yield return null;
        var presim = pendingPresim;
        pendingPresim = null;

        var result = currentRound.Roll();
        rollCount++;

        yield return StartCoroutine(Dice3D.RollPair(die1UI, result.Die1, die2UI, result.Die2, presim));

        ApplyRollResult(result, passBefore, passOddsBefore, pointBefore, placesBefore, activeBetsBefore, nonPlaceActiveBefore, placesWorkingBefore, oneRollStakesBefore, hardBefore);

        rolling = false;
        RefreshActionButtons();

        // Pre-sim next roll immediately while player reads result and places bets.
        StartPresim();
    }

    long SumActiveClearableBets()
    {
        long sum = 0;
        foreach (var n in PlaceNumbers) sum += currentRound.GetBet(PlaceTypeFor(n));
        foreach (var n in HardNumbers) sum += currentRound.GetBet(HardTypeFor(n));
        foreach (var w in currentRound.ComeWagers) sum += w.Amount + w.OddsAmount;
        return sum;
    }

    // One-roll bets (Field + props) are ALWAYS consumed on the roll they're live for,
    // win or lose. Unlike Place/Hardway bets that persist until a hit or 7-out,
    // these stakes need to appear as a loss in the roll's history row whenever they
    // don't pay — otherwise losing prop rolls show as +0 with money silently gone.
    long SumOneRollBets()
    {
        return currentRound.GetBet(CrapsBetType.Field)
            + currentRound.GetBet(CrapsBetType.AnyCraps)
            + currentRound.GetBet(CrapsBetType.AnySeven)
            + currentRound.GetBet(CrapsBetType.AnyEleven)
            + currentRound.GetBet(CrapsBetType.Horn);
    }

    void ApplyRollResult(CrapsRollResult result, long passBefore, long passOddsBefore, int pointBefore, long placesBefore, long activeBetsBefore, long nonPlaceActiveBefore, bool placesWorkingBefore, long oneRollStakesBefore, long[] hardBefore)
    {
        long totalReturned = result.TotalReturned;
        // On seven-out: Hardway/Come wagers always lose; PassLine/PassOdds always lose.
        // Place bets only lose if they were WORKING (BETS ON) — BETS OFF protects them
        // even on a seven-out (dealer returns the chips marked "off").
        long lostOnSevenOut = result.RoundOver
            ? (placesWorkingBefore ? placesBefore : 0) + nonPlaceActiveBefore + passBefore + passOddsBefore
            : 0;
        // Come-out 7: Hardways always lose (no phase protection). Place bets lose only
        // if BETS ON. Use placesWorkingBefore — the auto-transitions in Roll() may have
        // already changed PlaceBetsWorking by the time we read it here.
        long lostOnComeOutSeven = !result.RoundOver && result.Total == 7 && pointBefore == 0
            ? nonPlaceActiveBefore + (placesWorkingBefore ? placesBefore : 0)
            : 0;
        // Easy-way loss: a Hardway clears when its number hits non-hard (e.g. Hard8
        // loses on 5+3=8). Not a 7, so none of the above trackers cover it. Detect by
        // comparing per-hardway snapshots: any Hardway that was > 0 before, is now 0,
        // and didn't appear in HardwayHits (no win) = silently cleared for no return.
        long lostOnEasyWay = 0;
        if (!result.RoundOver && result.Total != 7)
        {
            for (int i = 0; i < HardNumbers.Length; i++)
            {
                if (hardBefore[i] > 0
                    && currentRound.GetBet(HardTypeFor(HardNumbers[i])) == 0
                    && !result.HardwayHits.ContainsKey(HardNumbers[i]))
                    lostOnEasyWay += hardBefore[i];
            }
        }
        if (totalReturned > 0) bankroll.Deposit(totalReturned);
        roundTotalReturned += totalReturned;

        // The detailed History panel logs every physical roll now, not just the
        // roll that ends a shooter's turn — a Place bet paying mid-turn (like
        // "Place 3 paid!") previously produced no visible row at all, which read as
        // broken even though nothing was actually wrong. TotalStaked here is
        // reused as "cumulative staked this shooter's turn" (context, not a new
        // stake event on this roll) and TotalReturned is offset by that same
        // amount so CrapsRoundRecord.NetChange (TotalReturned - TotalStaked) comes
        // out to exactly (this roll's payout - anything wiped by a seven-out) —
        // the two fields are repurposed for a per-roll row rather than a per-turn
        // summary, but the UI only ever reads NetChange/TotalStaked/BalanceAfter,
        // so nothing downstream needs to change.
        // One-roll stakes (Field, props) are always consumed this roll. Subtract them
        // from the net so a losing prop roll shows -25 instead of silently +0.
        // Winning props already come back in totalReturned, so the net is correct:
        //   lose: 0 return - 25 stake = -25
        //   win:  50 return - 25 stake = +25
        int pointAfterRoll = currentRound.Point ?? 0;
        var rollRecord = new CrapsRoundRecord(rollLogIndex++, pointAfterRoll, rollCount,
            roundTotalStaked, roundTotalStaked + totalReturned - lostOnSevenOut - lostOnComeOutSeven - lostOnEasyWay - oneRollStakesBefore, bankroll.Balance, result.Total);
        onRollLogged?.Invoke(rollRecord);

        // Per-roll history — every reference app's roll strip shows the actual
        // number, once per physical roll, not once per shooter turn (a turn can
        // span many rolls). Green = something paid this roll; red = a seven-out
        // that paid nothing (an unambiguous loss moment); blue = a neutral roll,
        // nothing resolved either way.
        Color rollColor = totalReturned > 0 ? UIFactory.Positive
            : result.RoundOver ? UIFactory.Negative
            : new Color(0.3f, 0.55f, 0.95f);
        onRollResolved?.Invoke(result.Total.ToString(), rollColor);

        if (result.PassResolved && passBefore > 0)
            lastLineAmount = passBefore;

        var parts = new List<string> { $"Rolled {result.Die1}+{result.Die2} = {result.Total}" };
        if (result.PointEstablishedThisRoll) parts.Add($"Point is {result.NewPoint}");
        if (result.RoundOver) parts.Add("SEVEN OUT — new shooter coming up");
        if (result.PlaceHits.Count > 0) parts.Add($"Place {string.Join(",", result.PlaceHits.Keys)} paid!");
        if (result.HardwayHits.Count > 0) parts.Add("Hardway paid!");
        if (result.ComeReturns.Count > 0) parts.Add("Come bet paid!");
        if (result.AnyCrapsReturn > 0) parts.Add("Any Craps paid!");
        if (result.AnySevenReturn > 0) parts.Add("Any Seven paid!");
        if (result.AnyElevenReturn > 0) parts.Add("Eleven paid!");
        if (result.HornReturn > 0) parts.Add("Horn paid!");
        statusText.color = totalReturned > 0 ? UIFactory.Positive : (result.RoundOver ? UIFactory.Negative : UIFactory.Accent);
        statusText.text = string.Join("  —  ", parts);

        if (totalReturned > 0)
        {
            soundManager?.PlayWin();
            if (totalReturned >= 500)
            {
                juiceManager?.Shake(0.5f, 4f);
                juiceManager?.Flash(new Color(0.3f, 1f, 0.4f, 0.28f), 0.7f);
                juiceManager?.PlayConfetti(2f);
                juiceManager?.PulseLight(0.9f, 0.7f);
                juiceManager?.PlayMoneyFountain(Vector2.zero);
                floatingText?.Show($"HUGE HIT! +{UIFactory.FormatMoney(totalReturned)}", UIFactory.Positive, fontSize: 42);
            }
            else
            {
                juiceManager?.Shake(0.3f, 2f);
                juiceManager?.Flash(new Color(0.25f, 0.9f, 0.35f, 0.18f), 0.5f);
                juiceManager?.PlayConfetti();
                floatingText?.Show($"+{UIFactory.FormatMoney(totalReturned)}", UIFactory.Positive);
            }
            winStreak++;
        }
        else if (result.RoundOver)
        {
            soundManager?.PlayLose();
            juiceManager?.Shake(0.2f, 1f);
            juiceManager?.Flash(new Color(0.85f, 0.2f, 0.2f, 0.14f), 0.4f);
            floatingText?.Show("SEVEN OUT", UIFactory.Negative);
            winStreak = 0;
        }

        streakAnimator.SetText(winStreak >= 2 ? $"<wave><rainb>{winStreak} HIT STREAK</rainb></wave>" : "");
        streakBadgeGO.SetActive(winStreak >= 2);

        if (result.RoundOver)
        {
            // Carry BETS OFF Place bets to the new round — dealer marks chips "off",
            // seven-out doesn't take them, they stay on the layout for the next shooter.
            var carriedPlaceBets = new Dictionary<CrapsBetType, long>();
            if (!placesWorkingBefore)
            {
                foreach (var n in PlaceNumbers)
                {
                    long amt = currentRound.GetBet(PlaceTypeFor(n));
                    if (amt > 0) carriedPlaceBets[PlaceTypeFor(n)] = amt;
                }
            }
            var record = new CrapsRoundRecord(roundIndex, pointBefore, rollCount, roundTotalStaked, roundTotalReturned, bankroll.Balance);
            onRoundResolved?.Invoke(record);
            roundIndex++;
            currentRound = new CrapsRound(rng);
            foreach (var kv in carriedPlaceBets) currentRound.PlaceBet(kv.Key, kv.Value);
            roundTotalStaked = 0;
            roundTotalReturned = 0;
            rollCount = 0;
        }

        // Auto-open odds modal when a new odds opportunity is created this roll.
        // Pass Line gets its chance when a point is established on come-out;
        // Come wagers get theirs the roll they park at their own point.
        if (!result.RoundOver)
        {
            if (result.PointEstablishedThisRoll)
            {
                int pt = result.NewPoint.Value;
                long passBase = currentRound.GetBet(CrapsBetType.PassLine);
                if (passBase > 0)
                    OpenOddsModal(CrapsBetType.PassOdds, null, "PASS LINE ODDS", pt, passBase, isDontSide: false);
            }
            else if (result.ComeParked.Count > 0)
            {
                var w = result.ComeParked[0];
                OpenOddsModal(null, w, $"COME {w.Point} ODDS", w.Point.Value, w.Amount, isDontSide: false);
            }
        }

        RefreshBetDisplay();
        RefreshActionButtons();
    }

    // ---- Display refresh ----

    void RefreshBetDisplay()
    {
        // Bets withdraw from the bankroll immediately on click (see the class-level
        // comment on why craps can't stage bets like the other games do) — the HUD
        // needs to reflect that right away too, not just after a roll resolves.
        onBankrollChanged?.Invoke();

        SetBarAmountWithOdds(passSpot, currentRound.GetBet(CrapsBetType.PassLine), currentRound.GetBet(CrapsBetType.PassOdds), "PASS LINE");
        SetBarAmount(fieldSpot, currentRound.GetBet(CrapsBetType.Field), "FIELD");

        long comeTotal = currentRound.ComeWagers.Sum(w => w.Amount + w.OddsAmount);
        int comeCount = currentRound.ComeWagers.Count;
        comeSpot.AmountText.text = comeTotal > 0 ? $"COME ({comeCount})\n{UIFactory.FormatMoney(comeTotal)}" : "COME";
        comeSpot.AmountText.color = comeTotal > 0 ? UIFactory.TextLight : UIFactory.TextDim;
        RebuildChipVisuals(comeSpot.Root.transform, comeSpot.ChipVisuals, comeTotal, comeSpot.AmountText.transform);

        foreach (var kv in placeSpots) SetNumberSpot(kv.Value, currentRound.GetBet(PlaceTypeFor(kv.Key)));
        foreach (var kv in hardSpots) SetBarAmount(kv.Value, currentRound.GetBet(HardTypeFor(kv.Key)), $"HARD {kv.Key}");
        foreach (var kv in propSpots) SetBarAmount(kv.Value, currentRound.GetBet(kv.Key), PropLabel(kv.Key));
        UpdatePointHighlight();
    }

    // Bright amber ring on whichever Place spot matches the live point — the single
    // biggest legibility fix from the real-table reference photo: seeing which
    // number IS the point at a glance instead of only reading it in the status text.
    void UpdatePointHighlight()
    {
        int? point = currentRound.Phase == CrapsPhase.Point ? currentRound.Point : null;
        foreach (var kv in placeSpots)
            kv.Value.FrameImg.color = point.HasValue && kv.Key == point.Value ? PointHighlight : kv.Value.BaseFrameColor;
    }

    void SetBarAmount(FlatBarSpot spot, long amount, string idleLabel)
    {
        spot.AmountText.text = amount > 0 ? $"{idleLabel}\n{UIFactory.FormatMoney(amount)}" : idleLabel;
        spot.AmountText.color = amount > 0 ? UIFactory.TextLight : UIFactory.TextDim;
        RebuildChipVisuals(spot.Root.transform, spot.ChipVisuals, amount, spot.AmountText.transform);
    }

    // Odds were being staked (withdrawn from the bankroll, tracked in Core) but
    // never actually shown anywhere on the Pass Line/Don't Pass bar itself — the
    // bar's amount and chip count only ever reflected the base bet, so placing
    // odds looked like nothing happened even though real money moved. Chip count
    // now reflects base+odds together, and the odds portion gets its own explicit
    // line so it's not just a bigger number with no explanation.
    void SetBarAmountWithOdds(FlatBarSpot spot, long baseAmount, long oddsAmount, string idleLabel)
    {
        long total = baseAmount + oddsAmount;
        spot.AmountText.text = total <= 0 ? idleLabel
            : oddsAmount > 0 ? $"{idleLabel}\n{UIFactory.FormatMoney(baseAmount)} + {UIFactory.FormatMoney(oddsAmount)} odds"
            : $"{idleLabel}\n{UIFactory.FormatMoney(baseAmount)}";
        spot.AmountText.color = total > 0 ? UIFactory.TextLight : UIFactory.TextDim;
        RebuildChipVisuals(spot.Root.transform, spot.ChipVisuals, total, spot.AmountText.transform);
    }

    void SetNumberSpot(NumberSpot spot, long amount)
    {
        // Number label never changes — only the amount row (and the frame's point
        // highlight, handled separately) reflect bet state. Number+amount+payout all
        // stay visible together instead of the amount replacing the number.
        spot.NumberText.color = amount > 0 ? UIFactory.TextLight : UIFactory.TextDim;
        spot.AmountText.text = amount > 0 ? UIFactory.FormatMoney(amount) : "";
        RebuildChipVisuals(spot.Root.transform, spot.ChipVisuals, amount, spot.AmountText.transform);
    }

    void RebuildChipVisuals(Transform parent, List<GameObject> visuals, long amount, Transform amountTextTransform)
    {
        foreach (var go in visuals) Destroy(go);
        visuals.Clear();
        if (amount <= 0) return;

        // Break the amount into real chip denominations (largest first, same colors
        // as the CHIPS selector) instead of cycling ChipStackColors by loop index —
        // the old approach painted an arbitrary red/blue/black mix unrelated to the
        // actual stake (e.g. a single $100 bet showed as 4 mismatched chips).
        const int maxVisibleChips = 6;
        var denomIndices = new List<int>();
        long remaining = amount;
        for (int d = ChipDenominations.Values.Length - 1; d >= 0 && denomIndices.Count < maxVisibleChips; d--)
        {
            long value = ChipDenominations.Values[d];
            while (remaining >= value && denomIndices.Count < maxVisibleChips)
            {
                denomIndices.Add(d);
                remaining -= value;
            }
        }
        if (denomIndices.Count == 0) denomIndices.Add(0); // sub-minimum leftover still gets one chip

        for (int i = 0; i < denomIndices.Count; i++)
        {
            var go = new GameObject($"Chip_{i}");
            go.transform.SetParent(parent, false);
            var img = go.AddComponent<Image>();
            img.sprite = UIFactory.Circle();
            img.color = ChipStackColors[denomIndices[i]];
            img.raycastTarget = false;
            var rt = go.GetComponent<RectTransform>();
            rt.sizeDelta = new Vector2(18, 18);
            float fanX = (i % 2 == 0 ? -1f : 1f) * (6f + i * 2f);
            rt.anchoredPosition = new Vector2(fanX, -amountTextLowOffset);
            visuals.Add(go);
        }
        amountTextTransform.SetAsLastSibling();
    }

    // Fixed low band, well clear of every spot's amount text (which is always pinned
    // above center) — same "text pinned above, chip pile confined below" fix applied
    // to Baccarat's bet spots after chips were found covering the text there.
    const float amountTextLowOffset = 22f;

    void RefreshActionButtons()
    {
        bool hasLineBet = currentRound.GetBet(CrapsBetType.PassLine) > 0;
        UIFactory.SetButtonState(rollButton, rollBaseColor, !rolling);
        // Toggle always shows its ON/OFF color — it's a mode indicator, not just a
        // button. Only interactivity changes during rolling, not the color.
        betsToggleButton.interactable = !rolling;
        betsToggleButton.GetComponent<UnityEngine.UI.Image>().color =
            currentRound.PlaceBetsWorking ? UIFactory.Positive : UIFactory.AccentDim;
        betsToggleLabel.text = currentRound.PlaceBetsWorking ? "BETS ON" : "BETS OFF";

        bool hasClearable = currentRound.GetBet(CrapsBetType.Field) > 0
            || PlaceNumbers.Any(n => currentRound.GetBet(PlaceTypeFor(n)) > 0)
            || HardNumbers.Any(n => currentRound.GetBet(HardTypeFor(n)) > 0)
            || PropTypes.Any(t => currentRound.GetBet(t) > 0)
            || (currentRound.Phase == CrapsPhase.ComeOut && hasLineBet);
        UIFactory.SetButtonState(clearBetButton, clearBaseColor, !rolling && hasClearable);

        bool canRepeatLine = currentRound.Phase == CrapsPhase.ComeOut && lastLineAmount > 0 && !hasLineBet;
        bool canRepeatOneRoll = lastOneRollBets.Count > 0 && lastOneRollBets.Any(kv => currentRound.GetBet(kv.Key) == 0);
        bool canRepeat = !rolling && (canRepeatLine || canRepeatOneRoll);
        UIFactory.SetButtonState(repeatButton, repeatBaseColor, canRepeat);

        UIFactory.SetButtonState(undoButton, UIFactory.AccentDim, !rolling && undoStack.Count > 0);
    }

    public void SetRoundIndex(int index) => roundIndex = index;

    public void ResetRound()
    {
        currentRound = new CrapsRound(rng);
        roundTotalStaked = 0;
        roundTotalReturned = 0;
        rollCount = 0;
        winStreak = 0;
        lastLineAmount = 0;
        lastOneRollBets.Clear();
        undoStack.Clear();
        streakBadgeGO.SetActive(false);
        statusText.color = UIFactory.Accent;
        statusText.text = "Come out — place Pass Line, then ROLL";
        die1UI.SetFaceUp(1);
        die2UI.SetFaceUp(1);
        RefreshBetDisplay();
        RefreshActionButtons();
    }
}
