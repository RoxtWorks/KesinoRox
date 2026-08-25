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
    WheelSpinAnimator wheelAnimator;
    RouletteTableBuilder tableBuilder;
    PastSpinsStripUI pastSpinsStrip;
    SoundManager soundManager;
    JuiceManager juiceManager;
    FloatingTextUI floatingText;
    FloatingTextUI milestoneToast;
    Action<SpinRecord> onSpinResolved;
    int spinIndex;
    int winStreak;

    // Rotated randomly so the same phrase doesn't repeat every single spin across a
    // long session — plain, neutral/encouraging, nothing manipulative or urgency-baiting.
    static readonly string[] WinFlavors = { "Nice hit!", "There it is!", "Press SPIN again", "Keep it going!", "Well played" };
    static readonly string[] LoseFlavors = { "Press SPIN again", "Try again", "Onward", "Next one's yours", "Spin again" };
    bool doubledMilestoneFired;
    readonly HashSet<int> spinMilestonesFired = new HashSet<int>();

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

    public void Build(Transform canvas, Bankroll bankroll, ChipSelectorUI chipSelector,
        SpinResultGenerator generator, ConveyorBeltUI belt, WheelSpinAnimator wheelAnimator, RouletteTableBuilder tableBuilder,
        PastSpinsStripUI pastSpinsStrip, Vector2 betTrayPos, Vector2 betTraySize, SoundManager soundManager,
        JuiceManager juiceManager, FloatingTextUI floatingText, FloatingTextUI milestoneToast, Action<SpinRecord> onSpinResolved)
    {
        this.bankroll = bankroll;
        this.chipSelector = chipSelector;
        this.generator = generator;
        this.belt = belt;
        this.wheelAnimator = wheelAnimator;
        this.tableBuilder = tableBuilder;
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
        UIFactory.MakePanel(tableRoot, "BettingPanelBg", new Vector2(PanelCenterX, -120), new Vector2(TotalWidth + 60, 650), UIFactory.PanelDark);
        UIFactory.MakeHeroTitle(tableRoot, "Header_BettingTable", new Vector2(PanelCenterX, 195), "BETTING TABLE", 26);

        UIFactory.MakePanel(tableRoot, "StatusPanelBg", new Vector2(PanelCenterX, 155), new Vector2(520, 40), UIFactory.PanelDark, shadow: false);
        statusText = UIFactory.MakeText(tableRoot, "StatusText", new Vector2(PanelCenterX, 155), 20,
            sizeDelta: new Vector2(500, 34), color: UIFactory.Accent, style: FontStyle.Bold);
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
        streakBadgeRt.sizeDelta = new Vector2(260, 90);
        streakBadgeRt.anchoredPosition = new Vector2(-480, 465);
        UIFactory.MakeFramedPanel(streakBadgeGO.transform, "StreakBadgeBg", Vector2.zero, new Vector2(260, 90), Color.black);

        var streakGO = new GameObject("StreakText");
        streakGO.transform.SetParent(streakBadgeGO.transform, false);
        var streakRt = streakGO.AddComponent<RectTransform>();
        streakRt.sizeDelta = new Vector2(240, 70);
        streakRt.anchoredPosition = Vector2.zero;
        streakText = streakGO.AddComponent<TextMeshProUGUI>();
        streakText.alignment = TextAlignmentOptions.Center;
        streakText.fontSize = 30;
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
        UIFactory.MakeButton(tableRoot, "UndoBtn", new Vector2(PanelCenterX - 390, bottomY), new Vector2(120, 46),
            "UNDO", UIFactory.AccentDim, UndoLastBetAction, 13, pixelFont: true);

        UIFactory.MakeButton(tableRoot, "ClearBetsBtn", new Vector2(PanelCenterX - 210, bottomY), new Vector2(150, 46),
            "CLEAR BETS", UIFactory.RedBet, ClearBets, 14, pixelFont: true);

        spinButton = UIFactory.MakeButton(tableRoot, "SpinButton", new Vector2(PanelCenterX, bottomY), new Vector2(200, 54),
            "SPIN", UIFactory.Positive, TrySpin, 20, pixelFont: true);

        repeatButton = UIFactory.MakeButton(tableRoot, "RepeatBetBtn", new Vector2(PanelCenterX + 210, bottomY), new Vector2(150, 46),
            "REPEAT BET", UIFactory.AccentDim, RepeatLastBet, 14, pixelFont: true);

        UIFactory.MakeButton(tableRoot, "DoubleAllBtn", new Vector2(PanelCenterX + 390, bottomY), new Vector2(140, 46),
            "DOUBLE ALL", UIFactory.AccentDim, DoubleAllBets, 13, pixelFont: true);

        RefreshBetTray();
    }

    // displayRow 0 = top (≡0 mod 3: 3,6,9...36), 1 = mid (≡2: 2,5,8...35), 2 = bottom (≡1: 1,4,7...34)
    static readonly int[] RowOffset = { 3, 2, 1 };
    static int NumberAt(int col, int displayRow) => col * 3 + RowOffset[displayRow];
    static float ColX(int c) => GridLeft + c * (CellW + ColGap);
    static float RowY(int r) => GridTop - r * (CellH + RowGap);

    void RegisterSpot(BetType type, int[] numbers, Vector2 pos, float chipSize = 30f)
    {
        string key = new Bet(type, 0, numbers).TargetKey();
        betSpotPositions[key] = pos;
        betSpotChipSizes[key] = chipSize;
    }

    void BuildNumberGrid(Transform canvas)
    {
        float zeroX = LeftEdge + CellW / 2f;
        float zeroY = (RowY(0) + RowY(Rows - 1)) / 2f;
        float zeroH = CellH * Rows + RowGap * (Rows - 1);
        var zeroBtn = UIFactory.MakeButton(canvas, "Num_0", new Vector2(zeroX, zeroY),
            new Vector2(CellW, zeroH), "0", UIFactory.FeltGreen, () => PlaceStraight(0), 26);
        RegisterSpot(BetType.Straight, new[] { 0 }, new Vector2(zeroX, zeroY));
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
                RegisterSpot(BetType.Straight, new[] { n }, pos);
                CreatePotentialLabel(n, pos, CellW, CellH);
                numberCellRects[n] = numBtn.GetComponent<RectTransform>();
            }
        }
    }

    // Centered directly under the number — sitting in the corner made it read as
    // attached to the neighbouring "C" corner-bet marker instead of to the number
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
                UIFactory.MakeButton(canvas, $"Corner_{numbers[0]}", new Vector2(x, y), new Vector2(24, 24),
                    "C", new Color(0.32f, 0.32f, 0.36f), () => PlaceMulti(BetType.Corner, numbers), 11);
                RegisterSpot(BetType.Corner, numbers, new Vector2(x, y), chipSize: 20f);
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
        Color splitColor = new Color(0.32f, 0.32f, 0.36f, 0.9f);

        for (int col = 0; col < Cols; col++)
        {
            for (int row = 0; row < Rows - 1; row++)
            {
                var numbers = new[] { NumberAt(col, row), NumberAt(col, row + 1) };
                var pos = new Vector2(ColX(col), (RowY(row) + RowY(row + 1)) / 2f);
                UIFactory.MakeButton(canvas, $"Split_{numbers[0]}_{numbers[1]}", pos, new Vector2(CellW - 14, 14),
                    "", splitColor, () => PlaceMulti(BetType.Split, numbers), 9);
                RegisterSpot(BetType.Split, numbers, pos, chipSize: 18f);
            }
        }

        for (int col = 0; col < Cols - 1; col++)
        {
            for (int row = 0; row < Rows; row++)
            {
                var numbers = new[] { NumberAt(col, row), NumberAt(col + 1, row) };
                var pos = new Vector2((ColX(col) + ColX(col + 1)) / 2f, RowY(row));
                UIFactory.MakeButton(canvas, $"Split_{numbers[0]}_{numbers[1]}", pos, new Vector2(14, CellH - 14),
                    "", splitColor, () => PlaceMulti(BetType.Split, numbers), 9);
                RegisterSpot(BetType.Split, numbers, pos, chipSize: 18f);
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
            UIFactory.MakeButton(canvas, $"Street_{numbers[0]}", pos, new Vector2(CellW - 10, 28),
                "S", new Color(0.32f, 0.32f, 0.36f), () => PlaceMulti(BetType.Street, numbers), 13);
            RegisterSpot(BetType.Street, numbers, pos, chipSize: 24f);
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
            UIFactory.MakeButton(canvas, $"SixLine_{numbers[0]}", pos, new Vector2(26, 26),
                "6L", new Color(0.32f, 0.32f, 0.36f), () => PlaceMulti(BetType.SixLine, numbers), 10);
            RegisterSpot(BetType.SixLine, numbers, pos, chipSize: 20f);
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
            UIFactory.MakeButton(canvas, $"Bet_{type}", pos, new Vector2(ColBetW, CellH),
                "2:1", new Color(0.24f, 0.26f, 0.29f), () => PlaceOutside(type), 15);
            RegisterSpot(type, null, pos, chipSize: 26f);
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
        UIFactory.MakeButton(canvas, $"Bet_{type}", pos, new Vector2(w, h), label,
            color ?? new Color(0.24f, 0.26f, 0.29f), () => PlaceOutside(type), 17, pixelFont: true);
        RegisterSpot(type, null, pos);
    }

    void PlaceStraight(int number) => PlaceMulti(BetType.Straight, new[] { number });
    void PlaceOutside(BetType type) => PlaceMulti(type, null);

    void PlaceMulti(BetType type, int[] numbers)
    {
        if (belt.IsPlaying) return;
        long chip = chipSelector.SelectedChip;
        long alreadyStaked = pendingBets.Values.Sum(b => b.Amount);
        if (!bankroll.CanAfford(alreadyStaked + chip))
        {
            statusText.text = bankroll.Balance < ChipDenominations.Values[0]
                ? "Out of chips — use ADD FUNDS above to keep playing"
                : "Not enough balance for that bet";
            return;
        }

        PushUndoSnapshot();
        var candidate = new Bet(type, chip, numbers);
        string key = candidate.TargetKey();
        long newAmount = pendingBets.TryGetValue(key, out var existing) ? existing.Amount + chip : chip;
        pendingBets[key] = new Bet(type, newAmount, numbers);

        UpdateChipVisual(key, newAmount);
        RefreshBetTray();
        RecalculatePotentials();
        soundManager?.PlayChip();
    }

    void ClearBets()
    {
        if (belt.IsPlaying) return;
        if (pendingBets.Count > 0) PushUndoSnapshot();
        pendingBets.Clear();
        ClearChipVisuals();
        RefreshBetTray();
        RecalculatePotentials();
        ClearWheelHighlights();
        soundManager?.PlayClick();
    }

    // Re-places whatever was actually spun last time, at the same amounts.
    void RepeatLastBet()
    {
        if (belt.IsPlaying) return;
        if (lastBets.Count == 0)
        {
            statusText.text = "No previous bet to repeat";
            return;
        }
        long total = lastBets.Sum(b => b.Amount);
        if (!bankroll.CanAfford(total))
        {
            statusText.text = "Not enough balance to repeat that bet";
            return;
        }

        PushUndoSnapshot();
        pendingBets.Clear();
        ClearChipVisuals();
        foreach (var b in lastBets)
        {
            string key = b.TargetKey();
            pendingBets[key] = new Bet(b.Type, b.Amount, b.Numbers);
            UpdateChipVisual(key, b.Amount);
        }
        RefreshBetTray();
        RecalculatePotentials();
        soundManager?.PlayChip();
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
            return;
        }
        long currentTotal = pendingBets.Values.Sum(b => b.Amount);
        if (!bankroll.CanAfford(currentTotal * 2))
        {
            statusText.text = "Not enough balance to double";
            return;
        }

        PushUndoSnapshot();
        foreach (var key in pendingBets.Keys.ToList())
        {
            var b = pendingBets[key];
            pendingBets[key] = new Bet(b.Type, b.Amount * 2, b.Numbers);
        }
        RebuildAllChipVisuals();
        RefreshBetTray();
        RecalculatePotentials();
        soundManager?.PlayChip();
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
            return;
        }
        var snapshot = undoStack[undoStack.Count - 1];
        undoStack.RemoveAt(undoStack.Count - 1);
        pendingBets.Clear();
        foreach (var kv in snapshot) pendingBets[kv.Key] = kv.Value;
        RebuildAllChipVisuals();
        RefreshBetTray();
        RecalculatePotentials();
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
        tableBuilder?.SetHighlightedNumbers(null);
    }

    // Used by the HUD's RESET button — same effect as CLEAR BETS, exposed publicly
    // since that button lives outside this controller.
    public void ResetBets()
    {
        ClearBets();
        lastBets.Clear();
        undoStack.Clear();
        winStreak = 0;
        streakAnimator?.SetText("");
        streakBadgeGO?.SetActive(false);
        if (achievementHideRoutine != null) { StopCoroutine(achievementHideRoutine); achievementHideRoutine = null; }
        achievementBadgeGO?.SetActive(false);
        doubledMilestoneFired = false;
        spinMilestonesFired.Clear();
    }

    // Drops (or updates) a small chip marker directly on the bet spot — the way a real
    // table shows what's been bet, instead of only listing it in the tray to the side.
    void UpdateChipVisual(string key, long amount)
    {
        if (!betSpotPositions.TryGetValue(key, out var pos)) return;

        if (chipVisuals.TryGetValue(key, out var existingGO))
        {
            existingGO.GetComponentInChildren<Text>().text = FormatChipAmount(amount);
            JuiceTweens.Pulse(this, existingGO.GetComponent<RectTransform>(), peakScale: 1.25f, duration: 0.18f);
            return;
        }

        float size = betSpotChipSizes.TryGetValue(key, out var s) ? s : 30f;

        var go = new GameObject($"ChipMarker_{key}");
        go.transform.SetParent(tableRoot, false);
        var img = go.AddComponent<Image>();
        img.sprite = UIFactory.Circle();
        img.color = UIFactory.Accent;
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

        chipVisuals[key] = go;
        JuiceTweens.PopIn(this, rt);
    }

    static string FormatChipAmount(long amount) => amount >= 1000 ? $"{amount / 1000f:0.#}k" : amount.ToString();

    void ClearChipVisuals()
    {
        foreach (var go in chipVisuals.Values) Destroy(go);
        chipVisuals.Clear();
    }

    void RefreshBetTray()
    {
        if (pendingBets.Count == 0)
        {
            betTrayText.text = "No bets placed";
            return;
        }
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
            return $"{label}: {b.Amount}";
        });
        long total = pendingBets.Values.Sum(b => b.Amount);
        betTrayText.text = string.Join("\n", lines) + $"\n\nTotal: {total}";
    }

    void TrySpin()
    {
        if (belt.IsPlaying) return;

        long totalStake = pendingBets.Values.Sum(b => b.Amount);
        if (totalStake > 0 && !bankroll.TryWithdraw(totalStake))
        {
            statusText.text = "Not enough balance to spin";
            return;
        }

        var bets = pendingBets.Values.ToList();
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
        tableBuilder?.SetHighlightedNumbers(highlighted);

        int winningNumber = generator.Spin();
        belt.PlaySpin(winningNumber, () => OnSpinComplete(winningNumber, bets, totalStake));
        wheelAnimator.PlaySpin(winningNumber);
    }

    void OnSpinComplete(int winningNumber, List<Bet> bets, long totalStake)
    {
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
        statusText.text = $"{winningNumber} {color}  ({(net >= 0 ? "+" : "")}{net})  — {flavor}";

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
                    floatingText?.Show($"HUGE WIN +{net}!", UIFactory.Positive, fontSize: 42);
                }
                else if (payoutRatio >= 4)
                {
                    juiceManager?.Shake(0.42f, 3f);
                    juiceManager?.Flash(new Color(0.28f, 0.95f, 0.38f, 0.22f), 0.55f);
                    juiceManager?.PlayConfetti(1.4f);
                    floatingText?.Show($"Big Win +{net}!", UIFactory.Positive, fontSize: 34);
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
                juiceManager?.Shake(0.2f, 1f);
                juiceManager?.Flash(new Color(0.85f, 0.2f, 0.2f, 0.14f), 0.4f);
                floatingText?.Show($"{net}", UIFactory.Negative);
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

        lastBets = bets;
        pendingBets.Clear();
        ClearChipVisuals();
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

    void SetTableVisible(bool visible)
    {
        tableRootGroup.alpha = visible ? 1f : 0f;
        tableRootGroup.interactable = visible;
        tableRootGroup.blocksRaycasts = visible;
    }
}
