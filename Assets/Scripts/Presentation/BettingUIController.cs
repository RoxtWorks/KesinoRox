using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Febucci.UI;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

// Owns bet placement + the spin button. Talks only to Core types (Bankroll,
// BetResolver, SpinResultGenerator) for anything that affects money or odds —
// this class just captures clicks and displays state.
public class BettingUIController : MonoBehaviour
{
    Bankroll bankroll;
    ChipSelectorUI chipSelector;
    SpinResultGenerator generator;
    ConveyorBeltUI belt;
    IRouletteWheel wheel;
    PastSpinsStripUI pastSpinsStrip;
    SoundManager soundManager;
    JuiceManager juiceManager;
    FloatingTextUI floatingText;
    FloatingTextUI milestoneToast;
    Action<SpinRecord> onSpinResolved;
    Action onBankrollChanged;
    int spinIndex;

    // The spin currently rolling: its result is decided the moment SPIN is pressed, so
    // leaving mid-reveal can still pay it out (see RefundTableBets).
    bool spinInFlight;
    List<Bet> spinBets;
    int spinWinningNumber;
    int winStreak;

    // Rotated randomly so the same phrase doesn't repeat every single spin across a
    // long session — plain, neutral/encouraging, nothing manipulative or urgency-baiting.
    static readonly string[] WinFlavors = { "Nice hit!", "There it is!", "Press SPIN again", "Keep it going!", "Well played" };
    static readonly string[] LoseFlavors = { "Press SPIN again", "Try again", "Onward", "Next one's yours", "Spin again" };
    bool doubledMilestoneFired;
    readonly HashSet<int> spinMilestonesFired = new HashSet<int>();
    long bestSpinNet;

    // Used when restoring a save — continues numbering spins from where the saved
    // session left off instead of restarting at #1.
    public void SetSpinIndex(int index) => spinIndex = index;

    // Keyed by Bet.TargetKey() so re-clicking the same spot accumulates chips instead
    // of creating a duplicate bet on it.
    readonly Dictionary<string, Bet> pendingBets = new Dictionary<string, Bet>();
    // What was actually spun last time (captured before pendingBets clears) — feeds
    // the REPEAT BET button so a strategy can be re-fired without re-clicking it all.
    List<Bet> lastBets = new List<Bet>();

    // Full pendingBets snapshots taken before every mutating action (place/clear/
    // repeat/double) — Undo just restores the most recent one wholesale rather than
    // tracking per-click deltas, so it uniformly handles every action that can touch
    // the tray instead of only single chip placements.
    readonly List<Dictionary<string, Bet>> undoStack = new List<Dictionary<string, Bet>>();
    const int MaxUndoDepth = 30;
    Text betTrayText;
    Text statusText;
    TextMeshProUGUI streakText;
    TextAnimator_TMP streakAnimator;
    GameObject streakBadgeGO;
    TextMeshProUGUI achievementText;
    TextAnimator_TMP achievementAnimator;
    GameObject achievementBadgeGO;
    Coroutine achievementHideRoutine;
    Button spinButton;
    Button repeatButton;
    Button undoButton;
    Button clearBetsButton;
    Button doubleAllButton;

    Transform canvasTransform;
    // Everything that makes up the betting felt lives under this one root so it can
    // be hidden as a unit during the spin — with it out of the way, the 3D wheel
    // underneath (previously always obscured by this opaque panel) becomes the
    // dramatic centerpiece of the reveal instead of a nameplate behind a UI wall.
    Transform tableRoot;
    CanvasGroup tableRootGroup;

    // Canvas position of every clickable bet spot, keyed the same way as pendingBets,
    // so a placed bet can drop a chip marker exactly on top of the spot that was
    // clicked — the way a real table shows what's been bet without reading a list.
    readonly Dictionary<string, Vector2> betSpotPositions = new Dictionary<string, Vector2>();
    readonly Dictionary<string, GameObject> chipVisuals = new Dictionary<string, GameObject>();

    // Corner/street/six-line spots are much smaller than a full number cell — a chip
    // marker sized for a 76x70 straight-up cell spills past a 24x24 corner spot into
    // the neighbouring cell and gets visually clipped there. Track each spot's size
    // so the marker (and its amount text) can be shrunk to fit where it's placed.
    readonly Dictionary<string, float> betSpotChipSizes = new Dictionary<string, float>();

    // Small per-number overlay showing net bankroll change if that number hits, given
    // every bet currently on the table — lets the player see the full win/lose spread
    // of a bet (or combo of bets) before spinning, not just the tray total. Each has
    // its own dark badge behind the text so it stays legible over red AND black
    // cells instead of the text color blending into a same-hued button.
    readonly Dictionary<int, Text> potentialLabels = new Dictionary<int, Text>();
    readonly Dictionary<int, Image> potentialBadges = new Dictionary<int, Image>();
    // The number cell buttons themselves, so the winning one can pulse after a spin.
    readonly Dictionary<int, RectTransform> numberCellRects = new Dictionary<int, RectTransform>();

    // Real-table orientation: 3 short rows x 12 wide columns, not a tall 12x3 strip —
    // uses the screen's aspect ratio properly and lets every cell be big. Column c
    // (0-indexed) holds the 3 numbers {3c+1, 3c+2, 3c+3}; displayRow 0 (top) shows
    // the ≡0 mod 3 number, matching a real felt's top-to-bottom order.
    const int Cols = 12, Rows = 3;
    const float CellW = 76, CellH = 70, ColGap = 6, RowGap = 6;
    // "2:1" column-bet markers sit to the right of the grid, one per row — the same
    // spot they occupy on a real felt once you rotate that felt 90 degrees to match
    // our horizontal layout (each of our ROWS is one of the felt's three columns).
    const float ColBetW = 46, ColBetGap = 10;
    const float GridWidth = Cols * CellW + (Cols - 1) * ColGap;
    // Whole felt (zero + grid + column-bet markers) centered on x=0 instead of
    // offset left to make room for a bet tray that used to live inside this panel.
    const float TotalWidth = CellW + ColGap + GridWidth + ColBetGap + ColBetW;
    const float LeftEdge = -TotalWidth / 2f;
    const float GridLeftEdge = LeftEdge + CellW + ColGap;
    const float GridLeft = GridLeftEdge + CellW / 2f;
    const float GridTop = 76;
    const float PanelCenterX = 0f;

    static readonly Color RailColor = new Color(0.30f, 0.22f, 0.10f);
    static readonly Color ZeroGreen = new Color(0.10f, 0.50f, 0.22f);
    static readonly Color OutsideFelt = new Color(0.07f, 0.30f, 0.16f); // dozens, columns, even-money boxes
    // Split / street / corner / six-line spots: faint white lines and dots on the felt, like a real layout
    static readonly Color InsideSpotColor = new Color(1f, 1f, 1f, 0.22f);
    static readonly Color[] ChipColors =
    {
        new Color(0.65f, 0.12f, 0.12f),
        new Color(0.1f, 0.35f, 0.6f),
        UIFactory.Chip500White,
    };

    public void Build(Transform canvas, Bankroll bankroll, ChipSelectorUI chipSelector,
        SpinResultGenerator generator, ConveyorBeltUI belt, IRouletteWheel wheel,
        PastSpinsStripUI pastSpinsStrip, Vector2 betTrayPos, Vector2 betTraySize, SoundManager soundManager,
        JuiceManager juiceManager, FloatingTextUI floatingText, FloatingTextUI milestoneToast, Action<SpinRecord> onSpinResolved,
        Action onBankrollChanged)
    {
        this.onBankrollChanged = onBankrollChanged;
        this.bankroll = bankroll;
        this.chipSelector = chipSelector;
        this.generator = generator;
        this.belt = belt;
        this.wheel = wheel;
        this.pastSpinsStrip = pastSpinsStrip;
        this.soundManager = soundManager;
        this.juiceManager = juiceManager;
        this.floatingText = floatingText;
        this.milestoneToast = milestoneToast;
        this.onSpinResolved = onSpinResolved;
        this.canvasTransform = canvas;

        var tableRootGO = new GameObject("BettingUIRoot");
        tableRootGO.transform.SetParent(canvas, false);
        var tableRootRT = tableRootGO.AddComponent<RectTransform>();
        tableRootRT.anchorMin = new Vector2(0.5f, 0.5f);
        tableRootRT.anchorMax = new Vector2(0.5f, 0.5f);
        tableRootRT.pivot = new Vector2(0.5f, 0.5f);
        tableRootRT.anchoredPosition = Vector2.zero;
        tableRootGroup = tableRootGO.AddComponent<CanvasGroup>();
        tableRoot = tableRootGO.transform;

        // Backdrop behind the whole betting felt so it reads as one panel against
        // the 3D table instead of buttons floating loose over the wheel graphic.
        // Grown taller on the top side only (center raised, bottom edge unchanged —
        // the number grid/outside bets/action row below all key off GridTop and
        // don't move) so the header at y=195 has real headroom below the panel's
        // own top edge instead of poking above it into the 3D wheel. Header/status
        // stay at their original 195/155 — that spacing was already correct against
        // the grid below (GridTop=76, top row edge ~111); the actual bug was the
        // panel being too short, not the header/status positions themselves.
        // Green felt with a wooden rail, same as the other tables
        var feltCenter = new Vector2(PanelCenterX, -90);
        var feltSize = new Vector2(TotalWidth + 60, 710);
        var rail = UIFactory.MakePanel(tableRoot, "FeltRail", feltCenter, feltSize + new Vector2(24f, 24f), RailColor);
        UIFactory.AddSharpFrame(rail, new Color(0.62f, 0.52f, 0.25f), square: true);
        UIFactory.MakePanel(tableRoot, "Felt", feltCenter, feltSize, UIFactory.FeltGreen, shadow: false);
        // Take-down tip where the "BETTING TABLE" title used to sit — same framed line as the other tables
        var tip = UIFactory.MakePanel(tableRoot, "TakeDownTipBg", new Vector2(PanelCenterX, 197), new Vector2(360f, 26f), UIFactory.PanelDarker, shadow: false);
        UIFactory.AddSharpFrame(tip, UIFactory.AccentDim, square: true);
        UIFactory.MakeText(tableRoot, "TakeDownTip", new Vector2(PanelCenterX, 197), 14, TextAnchor.MiddleCenter,
            new Vector2(350f, 24f), UIFactory.TextLight).text = "RIGHT-CLICK a bet to take it down";

        var statusPanelBg = UIFactory.MakePanel(tableRoot, "StatusPanelBg", new Vector2(PanelCenterX, 155), new Vector2(720, 40), UIFactory.PanelDark, shadow: false);
        UIFactory.AddSharpFrame(statusPanelBg, UIFactory.AccentDim, square: true);
        statusText = UIFactory.MakeText(tableRoot, "StatusText", new Vector2(PanelCenterX, 155), 19,
            sizeDelta: new Vector2(700, 34), color: UIFactory.Accent, style: FontStyle.Bold);
        statusText.text = "Place bets, then SPIN";

        // Standalone badge up by the balance HUD instead of buried in the betting
        // felt — was easy to miss down there, and small. Framed panel (black fill +
        // gold border) makes a dedicated backdrop for it instead of fighting the felt
        // graphic underneath for contrast. Parented to the canvas directly, not
        // tableRoot — tableRoot has zero offset from canvas center so the coordinate
        // space is identical, this just keeps it from getting swept up in any
        // tableRoot-wide fade/animation later.
        streakBadgeGO = new GameObject("StreakBadge");
        streakBadgeGO.transform.SetParent(canvas, false);
        var streakBadgeRt = streakBadgeGO.AddComponent<RectTransform>();
        streakBadgeRt.sizeDelta = new Vector2(260, 36);
        streakBadgeRt.anchoredPosition = new Vector2(0, 310); // between the number strip and the felt
        UIFactory.MakeFramedPanel(streakBadgeGO.transform, "StreakBadgeBg", Vector2.zero, new Vector2(260, 36), Color.black);

        var streakGO = new GameObject("StreakText");
        streakGO.transform.SetParent(streakBadgeGO.transform, false);
        var streakRt = streakGO.AddComponent<RectTransform>();
        streakRt.sizeDelta = new Vector2(250, 32);
        streakRt.anchoredPosition = Vector2.zero;
        streakText = streakGO.AddComponent<TextMeshProUGUI>();
        streakText.alignment = TextAlignmentOptions.Center;
        streakText.fontSize = 20;
        streakText.fontStyle = FontStyles.Bold;
        streakText.raycastTarget = false;
        streakText.outlineWidth = 0.25f;
        streakText.outlineColor = new Color32(0, 0, 0, 230);
        streakAnimator = streakGO.AddComponent<TextAnimator_TMP>();

        // Deactivate last, not before building — TMP's outlineWidth/outlineColor
        // setters lazily create a material instance off the font asset's shared
        // material, which is null until the object's first Awake/OnEnable; setting
        // them while the GameObject is already inactive throws ArgumentNullException
        // deep in TMP_Text.SetOutlineThickness and silently aborts the rest of Build().
        streakBadgeGO.SetActive(false);

        // Mirror of the streak badge on the right — one-off celebration pings
        // (bankroll doubled, hitting a win-streak threshold) instead of a persistent
        // counter, so it pulses in and auto-hides rather than staying up. Emoji
        // dropped here too — same missing-glyph problem as the streak badge's 🔥.
        achievementBadgeGO = new GameObject("AchievementBadge");
        achievementBadgeGO.transform.SetParent(canvas, false);
        var achievementBadgeRt = achievementBadgeGO.AddComponent<RectTransform>();
        achievementBadgeRt.sizeDelta = new Vector2(280, 90);
        achievementBadgeRt.anchoredPosition = new Vector2(480, 465);
        UIFactory.MakeFramedPanel(achievementBadgeGO.transform, "AchievementBadgeBg", Vector2.zero, new Vector2(280, 90), Color.black);

        var achievementGO = new GameObject("AchievementText");
        achievementGO.transform.SetParent(achievementBadgeGO.transform, false);
        var achievementRt = achievementGO.AddComponent<RectTransform>();
        achievementRt.sizeDelta = new Vector2(260, 70);
        achievementRt.anchoredPosition = Vector2.zero;
        achievementText = achievementGO.AddComponent<TextMeshProUGUI>();
        achievementText.alignment = TextAlignmentOptions.Center;
        achievementText.fontSize = 24;
        achievementText.fontStyle = FontStyles.Bold;
        achievementText.raycastTarget = false;
        achievementText.enableWordWrapping = true;
        achievementText.outlineWidth = 0.25f;
        achievementText.outlineColor = new Color32(0, 0, 0, 230);
        achievementAnimator = achievementGO.AddComponent<TextAnimator_TMP>();

        achievementBadgeGO.SetActive(false);

        BuildNumberGrid(tableRoot);
        BuildCornerBets(tableRoot);
        BuildSplitBets(tableRoot);
        BuildStreetAndSixLineBets(tableRoot);
        float bottomY = BuildOutsideBets(tableRoot);
        BuildColumnBets(tableRoot);

        // "Your Bets" now lives in the right-hand sidebar, alongside History and P/L,
        // instead of eating into the felt panel's own width.
        UIFactory.MakePanel(canvas, "BetTrayBg", betTrayPos, betTraySize, UIFactory.PanelDark);
        UIFactory.MakeSectionHeader(canvas, "Your Bets", betTrayPos + new Vector2(0, betTraySize.y / 2f - 20f), new Vector2(betTraySize.x - 20, 24));
        betTrayText = UIFactory.MakeText(canvas, "BetTray", betTrayPos + new Vector2(0, -10f), 15,
            TextAnchor.UpperLeft, new Vector2(betTraySize.x - 20, betTraySize.y - 60), UIFactory.TextLight);

        // Bottom row: UNDO / CLEAR / SPIN / REPEAT / DOUBLE ALL. UNDO and DOUBLE ALL
        // sit outside the original CLEAR/SPIN/REPEAT trio with room to spare — the
        // felt panel is wide enough that this doesn't crowd anything.
        undoButton = UIFactory.MakeButton(tableRoot, "UndoBtn", new Vector2(PanelCenterX - 378, bottomY), new Vector2(138, 53),
            "UNDO", UIFactory.AccentDim, UndoLastBetAction, 13, pixelFont: true);

        clearBetsButton = UIFactory.MakeButton(tableRoot, "ClearBetsBtn", new Vector2(PanelCenterX - 212, bottomY), new Vector2(173, 53),
            "CLEAR BETS", UIFactory.RedBet, ClearBets, 14, pixelFont: true);

        spinButton = UIFactory.MakeButton(tableRoot, "SpinButton", new Vector2(PanelCenterX, bottomY), new Vector2(230, 62),
            "SPIN", UIFactory.Positive, TrySpin, 22, pixelFont: true);

        repeatButton = UIFactory.MakeButton(tableRoot, "RepeatBetBtn", new Vector2(PanelCenterX + 212, bottomY), new Vector2(173, 53),
            "REPEAT BET", UIFactory.AccentDim, RepeatLastBet, 14, pixelFont: true);

        doubleAllButton = UIFactory.MakeButton(tableRoot, "DoubleAllBtn", new Vector2(PanelCenterX + 389, bottomY), new Vector2(161, 53),
            "DOUBLE ALL", UIFactory.AccentDim, DoubleAllBets, 13, pixelFont: true);

        RefreshBetTray();
    }

    // displayRow 0 = top (≡0 mod 3: 3,6,9...36), 1 = mid (≡2: 2,5,8...35), 2 = bottom (≡1: 1,4,7...34)
    static readonly int[] RowOffset = { 3, 2, 1 };
    static int NumberAt(int col, int displayRow) => col * 3 + RowOffset[displayRow];
    static float ColX(int c) => GridLeft + c * (CellW + ColGap);
    static float RowY(int r) => GridTop - r * (CellH + RowGap);

    void RegisterSpot(Button spot, BetType type, int[] numbers, Vector2 pos, float chipSize = 30f)
    {
        string key = new Bet(type, 0, numbers).TargetKey();
        betSpotPositions[key] = pos;
        betSpotChipSizes[key] = chipSize;
        RightClickRelay.Attach(spot.gameObject, () => TakeDown(key));

        // The split / street / corner / six-line spots are unlabeled lines and dots, like a real
        // felt — hovering one names the bet and what it pays.
        string tip = type switch
        {
            BetType.Split => $"Split {string.Join(" / ", numbers)}  ·  pays 17 to 1",
            BetType.Street => $"Street {string.Join(" / ", numbers.OrderBy(n => n))}  ·  pays 11 to 1",
            BetType.Corner => $"Corner {string.Join(" / ", numbers.OrderBy(n => n))}  ·  pays 8 to 1",
            BetType.SixLine => $"Six line {numbers.Min()}-{numbers.Max()}  ·  pays 5 to 1",
            _ => null
        };
        if (tip != null) spot.gameObject.AddComponent<TooltipTrigger>().Text = tip;
    }

    void BuildNumberGrid(Transform canvas)
    {
        float zeroX = LeftEdge + CellW / 2f;
        float zeroY = (RowY(0) + RowY(Rows - 1)) / 2f;
        float zeroH = CellH * Rows + RowGap * (Rows - 1);
        var zeroBtn = UIFactory.MakeButton(canvas, "Num_0", new Vector2(zeroX, zeroY),
            new Vector2(CellW, zeroH), "0", ZeroGreen, () => PlaceStraight(0), 26);
        RegisterSpot(zeroBtn, BetType.Straight, new[] { 0 }, new Vector2(zeroX, zeroY));
        CreatePotentialLabel(0, new Vector2(zeroX, zeroY), CellW, zeroH);
        numberCellRects[0] = zeroBtn.GetComponent<RectTransform>();

        for (int col = 0; col < Cols; col++)
        {
            for (int row = 0; row < Rows; row++)
            {
                int n = NumberAt(col, row);
                Color c = WheelLayout.IsRed(n) ? UIFactory.RedBet : UIFactory.BlackBet;
                var pos = new Vector2(ColX(col), RowY(row));
                var numBtn = UIFactory.MakeButton(canvas, $"Num_{n}", pos, new Vector2(CellW, CellH),
                    n.ToString(), c, () => PlaceStraight(n), 26);
                RegisterSpot(numBtn, BetType.Straight, new[] { n }, pos);
                CreatePotentialLabel(n, pos, CellW, CellH);
                numberCellRects[n] = numBtn.GetComponent<RectTransform>();
            }
        }
    }

    // Centered directly under the number — sitting in the corner made it read as
    // attached to the neighbouring corner-bet marker instead of to the number
    // itself; dead-center underneath reads unambiguously as "this number's result."
    void CreatePotentialLabel(int number, Vector2 cellPos, float cellW, float cellH)
    {
        var pos = cellPos + new Vector2(0f, -cellH / 2f + 13f);

        var badgeGO = new GameObject($"PotentialBadge_{number}");
        badgeGO.transform.SetParent(tableRoot, false);
        var badgeImg = badgeGO.AddComponent<Image>();
        badgeImg.sprite = UIFactory.RoundedRect();
        badgeImg.type = Image.Type.Sliced;
        badgeImg.color = new Color(0f, 0f, 0f, 0.72f);
        badgeImg.raycastTarget = false;
        var badgeRt = badgeGO.GetComponent<RectTransform>();
        badgeRt.sizeDelta = new Vector2(cellW - 8f, 20);
        badgeRt.anchoredPosition = pos;
        badgeGO.SetActive(false);
        potentialBadges[number] = badgeImg;

        var text = UIFactory.MakeText(badgeGO.transform, "Text", Vector2.zero, 13,
            TextAnchor.MiddleCenter, new Vector2(cellW - 12f, 18), UIFactory.TextDim, FontStyle.Bold);
        text.raycastTarget = false;
        // Best-fit shrinks the font instead of clipping once bets get big enough that
        // "+17500" no longer fits at 13pt — stays fully readable rather than cutting
        // off past the badge's edge.
        text.resizeTextForBestFit = true;
        text.resizeTextMinSize = 8;
        text.resizeTextMaxSize = 13;
        text.text = "";
        potentialLabels[number] = text;
    }

    // Net bankroll change for every one of the 37 numbers if it hits, given whatever
    // bets are currently pending — a straight-up 25 on 0 shows +875 on 0's cell and
    // -25 on every other cell, exactly like counting out the felt by hand.
    void RecalculatePotentials()
    {
        bool any = pendingBets.Count > 0;
        for (int n = 0; n <= 36; n++)
        {
            if (!potentialLabels.TryGetValue(n, out var label)) continue;
            var badge = potentialBadges[n];
            badge.gameObject.SetActive(any);
            if (!any) continue;
            long net = pendingBets.Values.Sum(b => BetResolver.Resolve(b, n) - b.Amount);
            label.color = net > 0 ? UIFactory.Positive : net < 0 ? UIFactory.Negative : UIFactory.TextDim;
            label.text = net > 0 ? $"+{net}" : net.ToString();
        }
    }

    void BuildCornerBets(Transform canvas)
    {
        for (int col = 0; col < Cols - 1; col++)
        {
            for (int row = 0; row < Rows - 1; row++)
            {
                var numbers = new[] { NumberAt(col, row), NumberAt(col + 1, row), NumberAt(col, row + 1), NumberAt(col + 1, row + 1) };
                float x = (ColX(col) + ColX(col + 1)) / 2f;
                float y = (RowY(row) + RowY(row + 1)) / 2f;
                var spotBtn = UIFactory.MakeButton(canvas, $"Corner_{numbers[0]}", new Vector2(x, y), new Vector2(20, 20),
                    "", InsideSpotColor, () => PlaceMulti(BetType.Corner, numbers), 11, flatFill: true);
                spotBtn.image.sprite = UIFactory.Circle();
                RegisterSpot(spotBtn, BetType.Corner, numbers, new Vector2(x, y), chipSize: 20f);
            }
        }
    }

    // Split (2 adjacent numbers, 17:1) — was in BetResolver/BetType already but never
    // had a clickable spot on the felt. Thin unlabeled strips sitting right on the
    // shared border between two cells, same as a real table's split markers: vertical
    // strips between numbers in the same column (adjacent rows), horizontal strips
    // between numbers in the same row (adjacent columns).
    void BuildSplitBets(Transform canvas)
    {
        Color splitColor = InsideSpotColor;

        for (int col = 0; col < Cols; col++)
        {
            for (int row = 0; row < Rows - 1; row++)
            {
                var numbers = new[] { NumberAt(col, row), NumberAt(col, row + 1) };
                var pos = new Vector2(ColX(col), (RowY(row) + RowY(row + 1)) / 2f);
                var spotBtn = UIFactory.MakeButton(canvas, $"Split_{numbers[0]}_{numbers[1]}", pos, new Vector2(CellW - 14, 14),
                    "", splitColor, () => PlaceMulti(BetType.Split, numbers), 9, flatFill: true);
                RegisterSpot(spotBtn, BetType.Split, numbers, pos, chipSize: 18f);
            }
        }

        for (int col = 0; col < Cols - 1; col++)
        {
            for (int row = 0; row < Rows; row++)
            {
                var numbers = new[] { NumberAt(col, row), NumberAt(col + 1, row) };
                var pos = new Vector2((ColX(col) + ColX(col + 1)) / 2f, RowY(row));
                var spotBtn = UIFactory.MakeButton(canvas, $"Split_{numbers[0]}_{numbers[1]}", pos, new Vector2(14, CellH - 14),
                    "", splitColor, () => PlaceMulti(BetType.Split, numbers), 9, flatFill: true);
                RegisterSpot(spotBtn, BetType.Split, numbers, pos, chipSize: 18f);
            }
        }
    }

    float streetY, sixLineY;

    void BuildStreetAndSixLineBets(Transform canvas)
    {
        // Street = the 3 numbers in one column; sits at the column's outer (bottom)
        // edge, just like a real felt's street marker below each 3-number column.
        streetY = RowY(Rows - 1) - CellH / 2f - 8f - 15f;
        for (int col = 0; col < Cols; col++)
        {
            var numbers = new[] { NumberAt(col, 0), NumberAt(col, 1), NumberAt(col, 2) };
            var pos = new Vector2(ColX(col), streetY);
            var spotBtn = UIFactory.MakeButton(canvas, $"Street_{numbers[0]}", pos, new Vector2(CellW - 10, 18),
                "", InsideSpotColor, () => PlaceMulti(BetType.Street, numbers), 14, flatFill: true);
            RegisterSpot(spotBtn, BetType.Street, numbers, pos, chipSize: 24f);
        }

        // Six-line = two adjacent columns combined; sits at their shared boundary,
        // one row further out than the street markers.
        sixLineY = streetY - 15f - 6f - 13f;
        for (int col = 0; col < Cols - 1; col++)
        {
            var numbers = new[]
            {
                NumberAt(col, 0), NumberAt(col, 1), NumberAt(col, 2),
                NumberAt(col + 1, 0), NumberAt(col + 1, 1), NumberAt(col + 1, 2)
            };
            float x = (ColX(col) + ColX(col + 1)) / 2f;
            var pos = new Vector2(x, sixLineY);
            var spotBtn = UIFactory.MakeButton(canvas, $"SixLine_{numbers[0]}", pos, new Vector2(20, 20),
                "", InsideSpotColor, () => PlaceMulti(BetType.SixLine, numbers), 11, flatFill: true);
            spotBtn.image.sprite = UIFactory.Circle();
            RegisterSpot(spotBtn, BetType.SixLine, numbers, pos, chipSize: 20f);
        }
    }

    // Column ("2:1") bets sit to the right of the grid, one per row — row 0 is every
    // number ≡0 mod 3 (Column3 in standard numbering), row 1 is ≡2 mod 3 (Column2),
    // row 2 is ≡1 mod 3 (Column1). Matches a real felt once rotated to our horizontal
    // layout: each of our rows IS one of the felt's three vertical columns.
    void BuildColumnBets(Transform canvas)
    {
        float x = GridLeftEdge + GridWidth + ColBetGap + ColBetW / 2f;
        (int row, BetType type)[] rows = { (0, BetType.Column3), (1, BetType.Column2), (2, BetType.Column1) };
        foreach (var (row, type) in rows)
        {
            var pos = new Vector2(x, RowY(row));
            var spotBtn = UIFactory.MakeButton(canvas, $"Bet_{type}", pos, new Vector2(ColBetW, CellH),
                "2:1", OutsideFelt, () => PlaceOutside(type), 15);
            RegisterSpot(spotBtn, type, null, pos, chipSize: 26f);
        }
    }

    // Dozens/outside-bets. Returns the Y to place the bottom CLEAR/SPIN/REPEAT row at.
    float BuildOutsideBets(Transform canvas)
    {
        const float btnH = 44, rowGap = 12, colGap = 10;
        // Each dozen spans exactly 4 columns of the grid — 1st 12 sits directly under
        // numbers 1-12 (columns 0-3), 3rd 12 directly under 25-36 (columns 8-11) —
        // instead of the old even three-way split that didn't line up with the grid.
        float dozenW = (ColX(3) - ColX(0)) + CellW;
        float dozen1X = (ColX(0) + ColX(3)) / 2f;
        float dozen2X = (ColX(4) + ColX(7)) / 2f;
        float dozen3X = (ColX(8) + ColX(11)) / 2f;

        float y = sixLineY - 13f - 6f - 22f;
        AddOutsideBtnSized(canvas, "1st 12", BetType.Dozen1, new Vector2(dozen1X, y), dozenW, btnH);
        AddOutsideBtnSized(canvas, "2nd 12", BetType.Dozen2, new Vector2(dozen2X, y), dozenW, btnH);
        AddOutsideBtnSized(canvas, "3rd 12", BetType.Dozen3, new Vector2(dozen3X, y), dozenW, btnH);

        // 1-18/EVEN/RED/BLACK/ODD/19-36 as 6 equal segments spanning the same width
        // as the grid itself, so this row's edges line up with the grid's too.
        y -= btnH + rowGap;
        float sixW = (GridWidth - 5 * colGap) / 6f;
        for (int i = 0; i < 6; i++)
        {
            float x = GridLeftEdge + sixW / 2f + i * (sixW + colGap);
            switch (i)
            {
                case 0: AddOutsideBtnSized(canvas, "1-18", BetType.Low1to18, new Vector2(x, y), sixW, btnH); break;
                case 1: AddOutsideBtnSized(canvas, "EVEN", BetType.Even, new Vector2(x, y), sixW, btnH); break;
                case 2: AddOutsideBtnSized(canvas, "RED", BetType.Red, new Vector2(x, y), sixW, btnH, UIFactory.RedBet); break;
                case 3: AddOutsideBtnSized(canvas, "BLACK", BetType.Black, new Vector2(x, y), sixW, btnH, UIFactory.BlackBet); break;
                case 4: AddOutsideBtnSized(canvas, "ODD", BetType.Odd, new Vector2(x, y), sixW, btnH); break;
                case 5: AddOutsideBtnSized(canvas, "19-36", BetType.High19to36, new Vector2(x, y), sixW, btnH); break;
            }
        }

        return y - btnH / 2f - rowGap - 27f;
    }

    void AddOutsideBtnSized(Transform canvas, string label, BetType type, Vector2 pos, float w, float h, Color? color = null)
    {
        var spotBtn = UIFactory.MakeButton(canvas, $"Bet_{type}", pos, new Vector2(w, h), label,
            color ?? OutsideFelt, () => PlaceOutside(type), 17, pixelFont: true);
        RegisterSpot(spotBtn, type, null, pos);
    }

    void PlaceStraight(int number) => PlaceMulti(BetType.Straight, new[] { number });
    void PlaceOutside(BetType type) => PlaceMulti(type, null);

    // A blocked action (insufficient balance, nothing to undo, etc.) used to only
    // change the status text — easy to miss mid-click. A quick shake+flash makes it
    // felt, not just read, same tier as the small negative reaction to a loss.
    void FlashBlocked() => juiceManager?.MicroShake(1.2f);

    void PlaceMulti(BetType type, int[] numbers)
    {
        if (belt.IsPlaying) return;
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
        var candidate = new Bet(type, chip, numbers);
        string key = candidate.TargetKey();
        long newAmount = pendingBets.TryGetValue(key, out var existing) ? existing.Amount + chip : chip;
        pendingBets[key] = new Bet(type, newAmount, numbers);
        // Chips leave the wallet the moment they're placed, same as every other table
        bankroll.TryWithdraw(chip);
        onBankrollChanged?.Invoke();

        UpdateChipVisual(key, newAmount);
        RefreshBetTray();
        RecalculatePotentials();
        soundManager?.PlayChip();
    }

    long OnFeltTotal() => pendingBets.Values.Sum(b => b.Amount);

    // Moves the felt to exactly these bets; the difference goes back to (or comes out of) the wallet.
    // Every undo / clear / repeat / double / take-down goes through here so money always matches the felt.
    bool SetPendingBets(IEnumerable<Bet> target)
    {
        var list = target.ToList();
        long current = OnFeltTotal();
        long wanted = list.Sum(b => b.Amount);
        if (wanted - current > bankroll.Balance) return false;
        bankroll.Deposit(current);
        pendingBets.Clear();
        foreach (var b in list) pendingBets[b.TargetKey()] = new Bet(b.Type, b.Amount, b.Numbers);
        bankroll.TryWithdraw(wanted);
        onBankrollChanged?.Invoke();
        RebuildAllChipVisuals();
        RefreshBetTray();
        RecalculatePotentials();
        return true;
    }

    void ClearBets()
    {
        if (belt.IsPlaying) return;
        if (pendingBets.Count == 0) { statusText.text = "Nothing to clear"; FlashBlocked(); return; }
        PushUndoSnapshot();
        SetPendingBets(new List<Bet>());
        ClearWheelHighlights();
        soundManager?.PlayClick();
    }

    // Right-click on a spot: that bet comes off the felt and back to the wallet
    void TakeDown(string key)
    {
        if (belt.IsPlaying || !pendingBets.TryGetValue(key, out var bet)) return;
        PushUndoSnapshot();
        SetPendingBets(pendingBets.Values.Where(b => b.TargetKey() != key));
        soundManager?.PlayClick();
        statusText.color = UIFactory.Accent;
        statusText.text = $"Bet down — {UIFactory.FormatMoney(bet.Amount)} back to wallet";
    }

    // Re-places whatever was actually spun last time, at the same amounts.
    void RepeatLastBet()
    {
        if (belt.IsPlaying) return;
        if (lastBets.Count == 0)
        {
            statusText.text = "No previous bet to repeat";
            FlashBlocked();
            return;
        }
        long total = lastBets.Sum(b => b.Amount);
        if (total - OnFeltTotal() > bankroll.Balance)
        {
            statusText.text = "Not enough balance to repeat that bet";
            FlashBlocked();
            return;
        }

        PushUndoSnapshot();
        SetPendingBets(lastBets);
        soundManager?.PlayChip();
        JuiceTweens.Pulse(this, repeatButton.GetComponent<RectTransform>(), peakScale: 1.15f, duration: 0.2f);
    }

    // Doubles every currently pending bet's stake in one click — a common progression
    // move (e.g. Martingale-style strategies) that would otherwise take re-clicking
    // every single spot again by hand.
    void DoubleAllBets()
    {
        if (belt.IsPlaying) return;
        if (pendingBets.Count == 0)
        {
            statusText.text = "No bets to double";
            FlashBlocked();
            return;
        }
        if (!bankroll.CanAfford(OnFeltTotal()))
        {
            statusText.text = "Not enough balance to double";
            FlashBlocked();
            return;
        }

        PushUndoSnapshot();
        SetPendingBets(pendingBets.Values.Select(b => new Bet(b.Type, b.Amount * 2, b.Numbers)));
        soundManager?.PlayChip();
        JuiceTweens.Pulse(this, doubleAllButton.GetComponent<RectTransform>(), peakScale: 1.15f, duration: 0.2f);
    }

    void PushUndoSnapshot()
    {
        undoStack.Add(new Dictionary<string, Bet>(pendingBets));
        if (undoStack.Count > MaxUndoDepth) undoStack.RemoveAt(0);
    }

    // Restores pendingBets to the snapshot taken just before the last place/clear/
    // repeat/double action, wholesale — simpler and more uniform than tracking a
    // delta for every individual action type.
    void UndoLastBetAction()
    {
        if (belt.IsPlaying) return;
        if (undoStack.Count == 0)
        {
            statusText.text = "Nothing to undo";
            FlashBlocked();
            return;
        }
        var snapshot = undoStack[undoStack.Count - 1];
        undoStack.RemoveAt(undoStack.Count - 1);
        if (!SetPendingBets(snapshot.Values))
        {
            undoStack.Add(snapshot);
            statusText.text = "Not enough balance to undo that";
            FlashBlocked();
            return;
        }
        soundManager?.PlayClick();
    }

    void RebuildAllChipVisuals()
    {
        ClearChipVisuals();
        foreach (var kv in pendingBets) UpdateChipVisual(kv.Key, kv.Value.Amount);
    }

    void ClearWheelHighlights()
    {
        belt.SetHighlightedNumbers(null);
        wheel?.SetHighlightedNumbers(null);
    }

    // Used by the HUD's RESET button, exposed publicly since that button lives outside
    // this controller. The bankroll is reset right before this, so chips on the felt just vanish.
    public void ResetBets()
    {
        pendingBets.Clear();
        ClearChipVisuals();
        RefreshBetTray();
        RecalculatePotentials();
        ClearWheelHighlights();
        lastBets.Clear();
        undoStack.Clear();
        winStreak = 0;
        streakAnimator?.SetText("");
        streakBadgeGO?.SetActive(false);
        if (achievementHideRoutine != null) { StopCoroutine(achievementHideRoutine); achievementHideRoutine = null; }
        achievementBadgeGO?.SetActive(false);
        doubledMilestoneFired = false;
        spinMilestonesFired.Clear();
        bestSpinNet = 0;
    }

    // Drops (or updates) a small chip marker directly on the bet spot — the way a real
    // table shows what's been bet, instead of only listing it in the tray to the side.
    void UpdateChipVisual(string key, long amount)
    {
        if (!betSpotPositions.TryGetValue(key, out var pos)) return;

        if (chipVisuals.TryGetValue(key, out var existingGO))
        {
            var existingText = existingGO.GetComponentInChildren<Text>();
            existingText.text = FormatChipAmount(amount);
            PaintChip(existingGO.GetComponent<Image>(), existingText, amount);
            JuiceTweens.Pulse(this, existingGO.GetComponent<RectTransform>(), peakScale: 1.25f, duration: 0.18f);
            return;
        }

        float size = betSpotChipSizes.TryGetValue(key, out var s) ? s : 30f;

        var go = new GameObject($"ChipMarker_{key}");
        go.transform.SetParent(tableRoot, false);
        var img = go.AddComponent<Image>();
        img.sprite = UIFactory.Circle();
        var edge = go.AddComponent<Outline>();
        edge.effectColor = new Color(0f, 0f, 0f, 0.8f);
        edge.effectDistance = new Vector2(1f, -1f);
        // Purely a visual overlay — without this it sits on top of the button it
        // marks and swallows every click after the first, silently capping the bet
        // at one chip no matter how many more times you click the spot.
        img.raycastTarget = false;
        var rt = go.GetComponent<RectTransform>();
        rt.sizeDelta = new Vector2(size, size);
        rt.anchoredPosition = pos;
        go.transform.SetAsLastSibling(); // draw on top of the grid/buttons beneath it

        // Font scales with the marker so a squeezed corner/street spot doesn't try to
        // cram a 12pt "500" into a 20px circle — it clipped past the circle's edge
        // into whatever cell happened to be underneath, reading as off-center.
        int fontSize = size >= 28f ? 12 : size >= 22f ? 10 : 8;
        var text = UIFactory.MakeText(go.transform, "Amount", Vector2.zero, fontSize, sizeDelta: new Vector2(size, size),
            color: Color.black, style: FontStyle.Bold);
        text.resizeTextForBestFit = true;
        text.resizeTextMinSize = 6;
        text.resizeTextMaxSize = fontSize;
        text.text = FormatChipAmount(amount);
        text.raycastTarget = false;
        PaintChip(img, text, amount);

        chipVisuals[key] = go;
        JuiceTweens.PopIn(this, rt);
    }

    // Chip colored like the biggest chip in the stack (red 25 / blue 100 / white 500), as on the other tables
    static void PaintChip(Image img, Text text, long amount)
    {
        var denoms = ChipDenominations.Values;
        int d = 0;
        for (int i = denoms.Length - 1; i >= 0; i--)
            if (amount >= denoms[i]) { d = i; break; }
        img.color = ChipColors[d];
        text.color = d == denoms.Length - 1 ? Color.black : Color.white;
    }

    static string FormatChipAmount(long amount) => amount >= 1000 ? $"{amount / 1000f:0.#}k" : amount.ToString();

    void ClearChipVisuals()
    {
        foreach (var go in chipVisuals.Values) Destroy(go);
        chipVisuals.Clear();
    }

    void RefreshBetTray()
    {
        // UNDO/CLEAR/REPEAT/DOUBLE ALL only make sense in specific states — dim
        // them the rest of the time instead of leaving them clickable-but-no-op.
        // SPIN is deliberately excluded: spinning with no bet placed is valid.
        UIFactory.SetButtonState(undoButton, UIFactory.AccentDim, undoStack.Count > 0);
        UIFactory.SetButtonState(clearBetsButton, UIFactory.RedBet, pendingBets.Count > 0);
        UIFactory.SetButtonState(repeatButton, UIFactory.AccentDim, lastBets.Count > 0);
        UIFactory.SetButtonState(doubleAllButton, UIFactory.AccentDim, pendingBets.Count > 0);

        if (pendingBets.Count == 0)
        {
            betTrayText.color = UIFactory.TextDim;
            betTrayText.text = "Place a chip on the felt to see your bets here";
            return;
        }
        betTrayText.color = UIFactory.TextLight;
        var lines = pendingBets.Values.Select(b =>
        {
            string label = b.Type switch
            {
                BetType.Straight => $"Straight {b.Numbers[0]}",
                BetType.Split => $"Split {string.Join("/", b.Numbers)}",
                BetType.Street => $"Street {string.Join("/", b.Numbers)}",
                BetType.Corner => $"Corner {string.Join("/", b.Numbers)}",
                BetType.SixLine => $"6-Line {string.Join("/", b.Numbers)}",
                _ => b.Type.ToString()
            };
            return $"{label}: {UIFactory.FormatMoney(b.Amount)}";
        });
        long total = pendingBets.Values.Sum(b => b.Amount);
        betTrayText.text = string.Join("\n", lines) + $"\n\nTotal: {UIFactory.FormatMoney(total)}";
    }

    void TrySpin()
    {
        if (belt.IsPlaying) return;

        // Chips already left the wallet when they were placed
        long totalStake = OnFeltTotal();
        var bets = pendingBets.Values.ToList();
        undoStack.Clear();
        spinButton.interactable = false;
        soundManager?.PlayClick();

        // Hide the felt/chips/recent-spins for the reveal — the 3D wheel and the
        // conveyor belt (which stay visible) become the whole show instead of
        // competing with a wall of buttons for attention.
        SetTableVisible(false);
        chipSelector.SetVisible(false);
        pastSpinsStrip.SetVisible(false);

        // Blue-tinge whichever numbers any pending bet actually covers, on both the
        // belt and the 3D wheel, so it's visible which number(s) to watch for once
        // the felt itself is hidden for the reveal — reuses BetResolver the same way
        // RecalculatePotentials does, so it's correct for every bet type uniformly
        // (straight-up numbers as well as splits/streets/corners/outside bets).
        var highlighted = new HashSet<int>();
        for (int n = 0; n <= 36; n++)
            if (bets.Any(b => BetResolver.Resolve(b, n) > 0)) highlighted.Add(n);
        belt.SetHighlightedNumbers(highlighted);
        wheel?.SetHighlightedNumbers(highlighted);

        int winningNumber = generator.Spin();
        spinInFlight = true;
        spinBets = bets;
        spinWinningNumber = winningNumber;
        belt.PlaySpin(winningNumber, () => OnSpinComplete(winningNumber, bets, totalStake), wheel);
        wheel.PlaySpin(winningNumber);
    }

    void OnSpinComplete(int winningNumber, List<Bet> bets, long totalStake)
    {
        if (!spinInFlight) return; // already settled by RefundTableBets
        spinInFlight = false;
        SetTableVisible(true);
        chipSelector.SetVisible(true);
        pastSpinsStrip.SetVisible(true);

        long totalReturned = bets.Sum(b => BetResolver.Resolve(b, winningNumber));
        bankroll.Deposit(totalReturned);

        string color = winningNumber == 0 ? "GREEN" : (WheelLayout.IsRed(winningNumber) ? "RED" : "BLACK");
        long net = totalReturned - totalStake;
        statusText.color = net >= 0 ? UIFactory.Positive : UIFactory.Negative;
        string flavor = totalStake == 0 ? "Press SPIN again"
            : net > 0 ? WinFlavors[UnityEngine.Random.Range(0, WinFlavors.Length)]
            : net < 0 ? LoseFlavors[UnityEngine.Random.Range(0, LoseFlavors.Length)]
            : "Press SPIN again";
        statusText.text = $"{winningNumber} {color}  ({(net >= 0 ? "+" : "")}{UIFactory.FormatMoney(net)})  — {flavor}";

        // The spun chips are settled — take them off the felt before the save below, or it counts them
        // as still on the table and adds them back to the saved balance
        lastBets = bets;
        pendingBets.Clear();
        ClearChipVisuals();

        var record = new SpinRecord(spinIndex++, winningNumber, totalStake, totalReturned, bankroll.Balance);
        onSpinResolved?.Invoke(record);

        // Only a real bet gets a win/lose stinger — nothing to celebrate or mourn on
        // a spin with no stake, where net is always exactly 0.
        if (totalStake > 0)
        {
            if (net > 0)
            {
                soundManager?.PlayWin();

                // Scale the celebration to how big the win actually was relative to
                // stake — a 35:1 straight-up hit should feel nothing like a min
                // even-money win, not get the identical shake/flash/confetti either
                // way. Ratios land straight bets (35:1) in "huge", street/corner
                // (11:1/8:1) in "big", everything else (1:1-6:1) as a normal win.
                double payoutRatio = (double)net / totalStake;
                if (payoutRatio >= 15)
                {
                    juiceManager?.Shake(0.5f, 4f);
                    juiceManager?.Flash(new Color(0.3f, 1f, 0.4f, 0.28f), 0.7f);
                    juiceManager?.PlayConfetti(2f);
                    juiceManager?.PulseLight(0.9f, 0.7f);
                    juiceManager?.PlayMoneyFountain(Vector2.zero);
                    floatingText?.Show($"HUGE WIN +{UIFactory.FormatMoney(net)}!", UIFactory.Positive, fontSize: 42);
                }
                else if (payoutRatio >= 4)
                {
                    juiceManager?.Shake(0.42f, 3f);
                    juiceManager?.Flash(new Color(0.28f, 0.95f, 0.38f, 0.22f), 0.55f);
                    juiceManager?.PlayConfetti(1.4f);
                    floatingText?.Show($"Big Win +{UIFactory.FormatMoney(net)}!", UIFactory.Positive, fontSize: 34);
                }
                else
                {
                    juiceManager?.Shake(0.35f, 2.5f);
                    juiceManager?.Flash(new Color(0.25f, 0.9f, 0.35f, 0.18f), 0.5f);
                    juiceManager?.PlayConfetti();
                    floatingText?.Show($"+{UIFactory.FormatMoney(net)}", UIFactory.Positive);
                }
                winStreak++;
                if (net > bestSpinNet && spinIndex >= 2)
                { ShowAchievement($"BEST WIN: +{UIFactory.FormatMoney(net)}!"); bestSpinNet = net; }
                else if (net > bestSpinNet) { bestSpinNet = net; }
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
                winStreak = 0;
            }
            // Rainbow's actual registered tag is "rainb", not "rainbow" — confirmed
            // by dumping TagID off each entry in the Behaviors Database; the readable
            // name is just the asset's display name, not the parser key. Dropped the
            // fire emoji — the default TMP font has no glyph for it, so it was just
            // rendering as a blank/tofu box regardless of color or effect.
            bool showStreak = winStreak >= 2;
            streakBadgeGO.SetActive(showStreak);
            if (showStreak)
            {
                streakAnimator.SetText($"<wave><rainb>{winStreak} WIN STREAK</rainb></wave>");
                JuiceTweens.Pulse(this, (RectTransform)streakBadgeGO.transform, peakScale: 1.15f, duration: 0.3f);
            }
        }

        if (numberCellRects.TryGetValue(winningNumber, out var winCellRect))
            JuiceTweens.Pulse(this, winCellRect, peakScale: 1.3f, duration: 0.45f);

        CheckMilestones();

        RefreshBetTray();
        RecalculatePotentials();
        spinButton.interactable = true;
    }

    // Session milestones — purely celebratory, fire once each, no pressure/urgency
    // framing. Just "hey, that's a nice run" moments to keep a session feeling like
    // it's going somewhere instead of being an undifferentiated string of spins.
    void CheckMilestones()
    {
        if (!doubledMilestoneFired && bankroll.TotalFunded > 0 && bankroll.Balance >= bankroll.TotalFunded * 2)
        {
            doubledMilestoneFired = true;
            ShowAchievement("BANKROLL DOUBLED!");
        }

        int[] spinTargets = { 50, 100, 250, 500, 1000 };
        foreach (var target in spinTargets)
        {
            if (spinIndex == target && spinMilestonesFired.Add(target))
                milestoneToast?.Show($"{target} Spins This Session", UIFactory.Accent, fontSize: 30);
        }

        if (winStreak == 5 || winStreak == 10 || winStreak == 15 || winStreak == 20)
            ShowAchievement($"{winStreak} WIN STREAK!");
    }

    // Pulses the achievement badge in with new text and auto-hides it a few seconds
    // later — unlike the persistent streak badge, these are one-off celebration
    // pings, not ongoing state, so they shouldn't linger on screen indefinitely.
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

    // ---- Leaving the table ----

    public long OnTableTotal() => spinInFlight ? 0 : OnFeltTotal();

    // Leaving the table: chips not yet spun go back to the wallet; a spin mid-reveal is
    // already decided, so it's paid out and returned as a record for the history. Pure
    // bankroll math — safe from OnDestroy / OnApplicationQuit and safe to call twice.
    public SpinRecord RefundTableBets()
    {
        if (spinInFlight)
        {
            spinInFlight = false;
            long staked = spinBets.Sum(b => b.Amount);
            long returned = spinBets.Sum(b => BetResolver.Resolve(b, spinWinningNumber));
            bankroll.Deposit(returned);
            pendingBets.Clear();
            return new SpinRecord(spinIndex++, spinWinningNumber, staked, returned, bankroll.Balance);
        }
        long onFelt = OnFeltTotal();
        if (onFelt > 0) bankroll.Deposit(onFelt);
        pendingBets.Clear();
        return null;
    }

    void SetTableVisible(bool visible)
    {
        tableRootGroup.alpha = visible ? 1f : 0f;
        tableRootGroup.interactable = visible;
        tableRootGroup.blocksRaycasts = visible;
    }
}
