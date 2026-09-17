using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Febucci.UI;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

// Normal (standard) Craps betting surface.
// Rules vs Crapless: 7/11 = natural (Pass wins), 2/3/12 = craps (Pass loses),
// Don't Pass is available, Place bets on 4/5/6/8/9/10 only.
// Bets withdraw from bankroll on placement; ROLL resolves all active bets.
public class NormalCrapsBettingUIController : MonoBehaviour
{
    static readonly string[] WinFlavors  = { "Roll again!", "Dice are hot!", "Keep shooting!", "There it is!", "Nice roll!" };
    static readonly string[] LoseFlavors = { "Roll again", "Next roll's yours", "Reload and go", "Come back swinging", "Shake it off" };
    Bankroll bankroll;
    ChipSelectorUI chipSelector;
    IRandomSource rng;
    SoundManager soundManager;
    JuiceManager juiceManager;
    FloatingTextUI floatingText;
    FloatingTextUI milestoneToast;
    Action<NormalCrapsRoundRecord> onRoundResolved;
    Action onBankrollChanged;
    Action<string, Color> onRollResolved;
    Action<NormalCrapsRoundRecord> onRollLogged;

    NormalCrapsRound currentRound;
    int roundIndex;
    int rollLogIndex;
    int pointBeforeRoll;
    long roundTotalStaked;
    long roundTotalReturned;
    int rollCount;
    int winStreak;
    long bestRoundNet;
    bool doubledMilestoneFired;
    readonly HashSet<int> roundMilestonesFired = new HashSet<int>();
    readonly Dictionary<NormalCrapsBetType, long> lastRollBets = new Dictionary<NormalCrapsBetType, long>();
    long onTableAtRoll;
    NormalCrapsRollResult pendingResult;
    readonly List<Action> undoStack = new List<Action>();
    const int MaxUndoDepth = 10;
    bool rolling;

    Button clearBetButton, repeatBetButton, undoButton;
    Color clearBaseColor, repeatBaseColor;

    Dice3D die1UI, die2UI;
    Dice3D shadowDie1, shadowDie2;
    PreSimResult pendingPresim;
    Coroutine presimRoutine;

    TextMeshProUGUI streakText;
    TextAnimator_TMP streakAnimator;
    GameObject streakBadgeGO;

    Transform tableRoot;
    Text statusText;
    Button rollButton, betsToggleButton;
    TextMeshProUGUI betsToggleLabel;
    Color rollBaseColor;

    class FlatSpot
    {
        public GameObject Root;
        public Text AmountText;
        public Text OddsText;   // optional — only passSpot / dontPassSpot
        public string DefaultLabel = "";
        public readonly List<GameObject> ChipVisuals = new List<GameObject>();
        public readonly List<GameObject> OddsChipVisuals = new List<GameObject>();
    }

    FlatSpot passSpot, dontPassSpot, fieldSpot;

    // Come / Don't Come interactive bars
    Transform comeBgT, dcBgT;
    Text comeAmtText, dcAmtText;
    readonly Dictionary<NormalComeWager, GameObject> comeChips = new Dictionary<NormalComeWager, GameObject>();
    readonly Dictionary<NormalDontComeWager, GameObject> dcChips = new Dictionary<NormalDontComeWager, GameObject>();

    // ATS / Lucky Roller
    FlatSpot atsLowsSpot, atsHighsSpot, atsAllSpot;
    readonly Dictionary<int, Image> atsDotImages = new Dictionary<int, Image>();
    static readonly int[] AtsNumbers = { 2, 3, 4, 5, 6, 8, 9, 10, 11, 12 };
    readonly Dictionary<int, FlatSpot>   placeSpots   = new Dictionary<int, FlatSpot>();
    readonly Dictionary<int, FlatSpot>   laySpots     = new Dictionary<int, FlatSpot>();
    readonly Dictionary<int, RectTransform> numberCellRoots = new Dictionary<int, RectTransform>();
    readonly Dictionary<int, FlatSpot>   hardSpots    = new Dictionary<int, FlatSpot>();
    readonly Dictionary<NormalCrapsBetType, FlatSpot> propSpots = new Dictionary<NormalCrapsBetType, FlatSpot>();

    static readonly NormalCrapsBetType[] PropTypes = { NormalCrapsBetType.AnyCraps, NormalCrapsBetType.AnySeven, NormalCrapsBetType.AnyEleven, NormalCrapsBetType.Horn, NormalCrapsBetType.CAndE };
    static readonly int[] PlaceNumbers = { 4, 5, 6, 8, 9, 10 };
    static readonly int[] HardNumbers  = { 4, 6, 8, 10 };
    static readonly Color PassColor     = new Color(0.05f, 0.30f, 0.10f);
    static readonly Color DontColor     = new Color(0.30f, 0.05f, 0.05f);
    static readonly Color FieldColor    = new Color(0.25f, 0.20f, 0.05f);
    static readonly Color PlaceColor    = new Color(0.10f, 0.15f, 0.35f);
    static readonly Color HardColor     = new Color(0.30f, 0.10f, 0.30f);
    static readonly Color PropColor     = new Color(0.25f, 0.25f, 0.05f);
    static readonly Color PointColor    = new Color(1f, 0.85f, 0.2f);
    // Pass-Line family green, dark enough for the white BETS ON text to read clearly
    static readonly Color BetsOnColor   = new Color(0.12f, 0.42f, 0.18f);

    // Chip stack X offsets chosen to sit beside each spot's label instead of on top of it
    const float LineChipX     = -130f;
    const float LineOddsChipX = -70f;
    const float AtsChipX      = 84f;
    const float FieldChipX    = -45f;

    static readonly Color[] ChipColors = { new Color(0.65f, 0.12f, 0.12f), new Color(0.1f, 0.35f, 0.6f), UIFactory.Chip500White };

    GameObject shooterPromptRoot;
    Text shooterPromptTitleText, shooterPromptBetsText;

    GameObject oddsModalRoot;
    Text oddsModalTitleText, oddsModalOddsText, oddsModalAmountText, oddsModalCapText;
    Button[] multiplierButtons;
    Text[] multiplierLabels;
    NormalCrapsBetType oddsModalBetType;
    int oddsModalPoint;
    bool oddsModalIsDontSide;
    long oddsModalCap;
    long oddsModalBaseAmount;
    long oddsModalPendingAmount;
    NormalComeWager oddsModalComeWager;
    NormalDontComeWager oddsModalDcWager;

    GameObject pointPuck;

    public void Build(Transform canvas, Bankroll bankroll, ChipSelectorUI chipSelector, IRandomSource rng,
        SoundManager soundManager, JuiceManager juiceManager, FloatingTextUI floatingText,
        FloatingTextUI milestoneToast, Dice3D die1, Dice3D die2, Dice3D shadowDie1, Dice3D shadowDie2,
        Action<NormalCrapsRoundRecord> onRoundResolved, Action onBankrollChanged,
        Action<string, Color> onRollResolved,
        Action<NormalCrapsRoundRecord> onRollLogged)
    {
        this.bankroll = bankroll;
        this.chipSelector = chipSelector;
        this.rng = rng;
        this.soundManager = soundManager;
        this.juiceManager = juiceManager;
        this.floatingText = floatingText;
        this.milestoneToast = milestoneToast;
        this.die1UI = die1; this.die2UI = die2;
        this.shadowDie1 = shadowDie1; this.shadowDie2 = shadowDie2;
        this.onRoundResolved = onRoundResolved;
        this.onBankrollChanged = onBankrollChanged;
        this.onRollResolved = onRollResolved;
        this.onRollLogged = onRollLogged;

        currentRound = new NormalCrapsRound(rng);
        currentRound.PlaceBetsWorking = false;

        var rootGO = new GameObject("NCrapsUIRoot");
        rootGO.transform.SetParent(canvas, false);
        var rootRt = rootGO.AddComponent<RectTransform>();
        rootRt.anchorMin = rootRt.anchorMax = rootRt.pivot = new Vector2(0.5f, 0.5f);
        rootRt.anchoredPosition = Vector2.zero;
        tableRoot = rootGO.transform;

        // Panel tall enough to contain buttons inside
        UIFactory.MakePanel(tableRoot, "FeltBg", new Vector2(0, -30), new Vector2(1100, 800), UIFactory.PanelDark);
        UIFactory.MakeHeroTitle(tableRoot, "Header", new Vector2(0, 335), "CRAPS", 26);

        var statusBg = UIFactory.MakePanel(tableRoot, "StatusBg", new Vector2(0, 290), new Vector2(700, 48), UIFactory.PanelDark, shadow: false);
        UIFactory.AddSharpFrame(statusBg, UIFactory.AccentDim, square: true);
        statusText = UIFactory.MakeText(tableRoot, "StatusText", new Vector2(0, 290), 18,
            sizeDelta: new Vector2(680, 42), color: UIFactory.Accent, style: FontStyle.Bold);
        statusText.text = "Place bets — come-out roll";

        BuildFelt();
        BuildActionButtons();
        BuildStreakBadge();
        BuildOddsModal(canvas);
        BuildShooterPrompt(canvas);
        RefreshActionButtons();
    }

    void BuildFelt()
    {
        // ── Layout constants (panel: centre y=-30, height=800, y range -430..+370) ──
        const float leftCx  = -162f;
        const float feltW   = 700f;
        const float rightCx = +382f;
        const float rightPW = 280f;

        // Right props panel
        var propBg = UIFactory.MakePanel(tableRoot, "PropsBg",
            new Vector2(rightCx, -20f), new Vector2(rightPW, 540f),
            new Color(0.04f, 0.04f, 0.07f, 0.95f), shadow: false);
        UIFactory.AddSharpFrame(propBg, UIFactory.AccentDim, square: true);

        // ── ATS / LUCKY ROLLER PANEL (top of left felt) ──────────────────
        BuildAtsPanel(leftCx, feltW);

        // ── PLACE / LAY CELLS (shifted down 125px vs earlier layout) ─────
        MakeSectionLabel("PlaceHdr", new Vector2(leftCx, 105f), feltW, "LAY  ·  PLACE");

        const float cellSpacing = 100f;
        const float halfCellW   = 50f;
        const float placeY      = 20f;
        float placeStartX = leftCx - feltW / 2f + halfCellW;
        for (int i = 0; i < PlaceNumbers.Length; i++)
        {
            int n = PlaceNumbers[i];
            float cx = placeStartX + i * cellSpacing;
            BuildPlaceLayCell(n, new Vector2(cx, placeY));
        }

        // 7th slot: BETS ON/OFF toggle — moves with the LAY/PLACE row
        float betsX = placeStartX + PlaceNumbers.Length * cellSpacing;
        betsToggleButton = UIFactory.MakeButton(tableRoot, "BetsToggleBtn",
            new Vector2(betsX, placeY), new Vector2(100f, 140f),
            "BETS\nOFF", UIFactory.AccentDim, OnBetsToggleClicked, 11, pixelFont: true);
        betsToggleLabel = betsToggleButton.GetComponentInChildren<TextMeshProUGUI>();
        if (betsToggleLabel != null) betsToggleLabel.raycastTarget = false;

        // ── FIELD BAR ────────────────────────────────────────────────────
        fieldSpot = BuildFieldBar(new Vector2(leftCx, -92f), new Vector2(feltW, 80f));

        // ── COME | DON'T COME BAR band (below field) ─────────────────────
        float halfW = feltW / 2f;
        float comeCx = leftCx - halfW / 2f;   // -337
        float dcCx   = leftCx + halfW / 2f;   // +13
        const float cdY = -167f, cdH = 55f;

        // COME bar — clickable, tracks unparked Come wager chips
        var comeGO = new GameObject("ComeBg");
        comeGO.transform.SetParent(tableRoot, false);
        var comeRt = comeGO.AddComponent<RectTransform>();
        comeRt.sizeDelta = new Vector2(halfW, cdH);
        comeRt.anchoredPosition = new Vector2(comeCx, cdY);
        var comeImg = comeGO.AddComponent<Image>();
        comeImg.sprite = UIFactory.RoundedRect();
        comeImg.type = Image.Type.Sliced;
        comeImg.color = new Color(0.03f, 0.18f, 0.06f, 0.55f);
        UIFactory.AddSharpFrame(comeGO, new Color(0.25f, 0.55f, 0.25f, 0.8f), square: true);
        var comeBtn = comeGO.AddComponent<Button>();
        comeBtn.targetGraphic = comeImg;
        comeBtn.onClick.AddListener(OnComeClicked);
        RightClickRelay.Attach(comeGO, TakeDownUnparkedCome);
        UIFactory.MakeText(comeGO.transform, "ComeLabel", new Vector2(0, 8f), 13,
            TextAnchor.MiddleCenter, new Vector2(halfW - 8f, 22f), UIFactory.TextLight, FontStyle.Bold).text = "COME";
        comeAmtText = UIFactory.MakeText(comeGO.transform, "ComeAmt", new Vector2(0, -9f), 11,
            TextAnchor.MiddleCenter, new Vector2(halfW - 8f, 18f), UIFactory.TextDim);
        comeAmtText.text = "1:1";
        comeBgT = comeGO.transform;

        // DON'T COME BAR — clickable, tracks unparked DC wager chips
        var dcGO = new GameObject("DontComeBg");
        dcGO.transform.SetParent(tableRoot, false);
        var dcRt = dcGO.AddComponent<RectTransform>();
        dcRt.sizeDelta = new Vector2(halfW, cdH);
        dcRt.anchoredPosition = new Vector2(dcCx, cdY);
        var dcImg = dcGO.AddComponent<Image>();
        dcImg.sprite = UIFactory.RoundedRect();
        dcImg.type = Image.Type.Sliced;
        dcImg.color = new Color(0.18f, 0.03f, 0.03f, 0.55f);
        UIFactory.AddSharpFrame(dcGO, new Color(0.55f, 0.25f, 0.25f, 0.8f), square: true);
        var dcBtn = dcGO.AddComponent<Button>();
        dcBtn.targetGraphic = dcImg;
        dcBtn.onClick.AddListener(OnDontComeClicked);
        RightClickRelay.Attach(dcGO, TakeDownUnparkedDontCome);
        UIFactory.MakeText(dcGO.transform, "DontComeLabel", new Vector2(0, 8f), 11,
            TextAnchor.MiddleCenter, new Vector2(halfW - 8f, 22f), UIFactory.TextLight, FontStyle.Bold).text = "DON'T COME BAR";
        dcAmtText = UIFactory.MakeText(dcGO.transform, "DontComeAmt", new Vector2(0, -9f), 11,
            TextAnchor.MiddleCenter, new Vector2(halfW - 8f, 18f), UIFactory.TextDim);
        dcAmtText.text = "1:1";
        dcBgT = dcGO.transform;

        // ── PASS LINE | DON'T PASS combined band ─────────────────────────
        const float pdY = -255f, pdH = 72f;
        float passW = feltW * 0.50f;   // 350
        float dpW   = feltW * 0.50f;   // 350
        float passCx = leftCx - (feltW - passW) / 2f;  // -337
        float dpCx   = leftCx + (feltW - dpW)  / 2f;   //  +13

        passSpot     = BuildFlatBar(NormalCrapsBetType.PassLine,
            new Vector2(passCx, pdY), new Vector2(passW, pdH),
            "PASS LINE", "1:1", PassColor, OnPassLineClicked);
        passSpot.OddsText = UIFactory.MakeText(passSpot.Root.transform, "PassOddsAmt",
            new Vector2(passW * 0.25f, -9f), 11, TextAnchor.MiddleRight,
            new Vector2(passW * 0.45f, 18f), UIFactory.Accent);
        passSpot.OddsText.text = "";

        dontPassSpot = BuildFlatBar(NormalCrapsBetType.DontPass,
            new Vector2(dpCx, pdY), new Vector2(dpW, pdH),
            "DON'T PASS", "1:1", DontColor, OnDontPassClicked);
        dontPassSpot.OddsText = UIFactory.MakeText(dontPassSpot.Root.transform, "DPOddsAmt",
            new Vector2(dpW * 0.25f, -9f), 11, TextAnchor.MiddleRight,
            new Vector2(dpW * 0.45f, 18f), UIFactory.Accent);
        dontPassSpot.OddsText.text = "";

        // ── RIGHT PANEL: HARDWAYS (shifted 20px down vs earlier) ─────────
        MakeSectionLabel("HardHdr", new Vector2(rightCx, 232f), rightPW - 10f, "HARDWAYS");

        float hcW = 116f, hcH = 78f;
        float hColA = rightCx - 63f, hColB = rightCx + 63f;
        hardSpots[4]  = BuildHardSpot(4,  new Vector2(hColA, 170f), hcW, hcH, "7:1");
        hardSpots[6]  = BuildHardSpot(6,  new Vector2(hColB, 170f), hcW, hcH, "9:1");
        hardSpots[8]  = BuildHardSpot(8,  new Vector2(hColA,  80f), hcW, hcH, "9:1");
        hardSpots[10] = BuildHardSpot(10, new Vector2(hColB,  80f), hcW, hcH, "7:1");

        MakeSectionLabel("OneRollLbl", new Vector2(rightCx, 22f), rightPW - 10f, "ONE ROLL BETS");

        // ── RIGHT PANEL: SEVEN + CRAPS ────────────────────────────────────
        float pcW = 116f, pcH = 70f;
        propSpots[NormalCrapsBetType.AnySeven]  = BuildPropSpot(NormalCrapsBetType.AnySeven,
            new Vector2(hColA, -32f), pcW, pcH, "SEVEN",    "4:1");
        propSpots[NormalCrapsBetType.AnyCraps]  = BuildPropSpot(NormalCrapsBetType.AnyCraps,
            new Vector2(hColB, -32f), pcW, pcH, "ANY\nCRAPS", "7:1");

        // ── RIGHT PANEL: ELEVEN + HORN ────────────────────────────────────
        propSpots[NormalCrapsBetType.AnyEleven] = BuildPropSpot(NormalCrapsBetType.AnyEleven,
            new Vector2(hColA, -120f), pcW, pcH, "ELEVEN",  "15:1");
        propSpots[NormalCrapsBetType.Horn]      = BuildPropSpot(NormalCrapsBetType.Horn,
            new Vector2(hColB, -120f), pcW, pcH, "HORN",    "30:1/15:1");
        propSpots[NormalCrapsBetType.CAndE]     = BuildPropSpot(NormalCrapsBetType.CAndE,
            new Vector2(rightCx, -200f), pcW * 2f + 10f, 56f, "C & E", "3:1 / 7:1");

        // ── POINT PUCK ───────────────────────────────────────────────────
        pointPuck = new GameObject("PointPuck");
        pointPuck.transform.SetParent(tableRoot, false);
        var puckRt = pointPuck.AddComponent<RectTransform>();
        puckRt.sizeDelta = new Vector2(38, 38);
        var puckImg = pointPuck.AddComponent<Image>();
        puckImg.sprite = UIFactory.Circle();
        puckImg.color = PointColor;
        UIFactory.MakeText(pointPuck.transform, "ON", Vector2.zero, 12, TextAnchor.MiddleCenter,
            new Vector2(36, 36), Color.black, FontStyle.Bold).text = "ON";
        pointPuck.SetActive(false);
    }

    // One style for every bet-section header so the table's areas read the same way
    static readonly Color SectionHeaderColor = new Color(1f, 0.85f, 0.1f);

    void MakeSectionLabel(string name, Vector2 pos, float width, string text) =>
        UIFactory.MakeText(tableRoot, name, pos, 13, TextAnchor.MiddleCenter,
            new Vector2(width, 18f), SectionHeaderColor, FontStyle.Bold).text = text;

    void BuildAtsPanel(float leftCx, float feltW)
    {
        // Panel sits between status bar (bottom≈266) and LAY/PLACE row (top≈114)
        // Center y=188, height=140 → top=258, bottom=118
        const float panelY = 188f, panelH = 140f;
        var bg = UIFactory.MakePanel(tableRoot, "AtsBg", new Vector2(leftCx, panelY),
            new Vector2(feltW, panelH), new Color(0.04f, 0.06f, 0.12f, 0.92f), shadow: false);
        UIFactory.AddSharpFrame(bg, new Color(0.8f, 0.7f, 0.1f, 0.7f), square: true);

        // Title sits well inside the frame (panel top = 258) so the frame line doesn't clip it
        MakeSectionLabel("AtsTitle", new Vector2(leftCx, 241f), feltW - 20f, "LUCKY ROLLER");

        // Three bet buttons: LOWS | ROLL ALL | HIGHS
        // Each 200px wide, 8px gap → total 616px centred in 700px felt
        const float btnW = 200f, btnH = 52f, btnGap = 8f;
        float btnRowY = 202f;
        float b0x = leftCx - btnW - btnGap;   // LOWS centre
        float b1x = leftCx;                    // ROLL ALL centre
        float b2x = leftCx + btnW + btnGap;    // HIGHS centre

        atsLowsSpot  = BuildFlatBar(NormalCrapsBetType.AtsLows,  new Vector2(b0x, btnRowY),
            new Vector2(btnW, btnH), "LOWS  (2-3-4-5-6)", "30:1",
            new Color(0.1f, 0.4f, 0.85f), OnAtsLowsClicked);
        atsAllSpot   = BuildFlatBar(NormalCrapsBetType.AtsAll,   new Vector2(b1x, btnRowY),
            new Vector2(btnW, btnH), "ROLL ALL", "155:1",
            new Color(0.7f, 0.55f, 0.05f), OnAtsAllClicked);
        atsHighsSpot = BuildFlatBar(NormalCrapsBetType.AtsHighs, new Vector2(b2x, btnRowY),
            new Vector2(btnW, btnH), "HIGHS (8-9-10-11-12)", "30:1",
            new Color(0.7f, 0.15f, 0.15f), OnAtsHighsClicked);

        // Number tracker dots: 2 3 4 5 6 · [7] · 8 9 10 11 12
        // 11 slots at 36px spacing centred on leftCx
        const float dotSpacing = 36f, dotSize = 20f, dotY = 162f, lblY = 148f;
        int[] slots = { 2, 3, 4, 5, 6, 0, 8, 9, 10, 11, 12 }; // 0 = 7 label slot
        float startX = leftCx - slots.Length / 2f * dotSpacing + dotSpacing / 2f;
        for (int i = 0; i < slots.Length; i++)
        {
            float cx = startX + i * dotSpacing;
            int n = slots[i];
            if (n == 0)
            {
                UIFactory.MakeText(tableRoot, "AtsSeven", new Vector2(cx, dotY), 14,
                    TextAnchor.MiddleCenter, new Vector2(dotSpacing, dotSize + 4f),
                    new Color(0.8f, 0.2f, 0.2f, 0.9f), FontStyle.Bold).text = "7";
                continue;
            }
            var dotGO = new GameObject($"AtsDot_{n}");
            dotGO.transform.SetParent(tableRoot, false);
            var img = dotGO.AddComponent<Image>();
            img.sprite = UIFactory.Circle();
            img.raycastTarget = false;
            var rt = dotGO.GetComponent<RectTransform>();
            rt.sizeDelta = new Vector2(dotSize, dotSize);
            rt.anchoredPosition = new Vector2(cx, dotY);
            atsDotImages[n] = img;

            UIFactory.MakeText(tableRoot, $"AtsDotLbl_{n}", new Vector2(cx, lblY), 10,
                TextAnchor.MiddleCenter, new Vector2(dotSpacing, 14f), UIFactory.TextDim).text = n.ToString();
        }
        RefreshAtsDots();
    }

    void RefreshAtsDots()
    {
        if (atsDotImages.Count == 0) return;
        var lows  = currentRound.AtsLowsCollected;
        var highs = currentRound.AtsHighsCollected;
        int[] lowNums  = { 2, 3, 4, 5, 6 };
        int[] highNums = { 8, 9, 10, 11, 12 };
        foreach (var n in lowNums)
        {
            if (atsDotImages.TryGetValue(n, out var img))
                img.color = lows.Contains(n)
                    ? new Color(0.2f, 0.75f, 1f, 1f)    // collected — bright blue
                    : new Color(0.2f, 0.25f, 0.4f, 0.6f); // uncollected — dim
        }
        foreach (var n in highNums)
        {
            if (atsDotImages.TryGetValue(n, out var img))
                img.color = highs.Contains(n)
                    ? new Color(1f, 0.4f, 0.3f, 1f)     // collected — bright red
                    : new Color(0.35f, 0.2f, 0.2f, 0.6f); // uncollected — dim
        }
    }

    bool AtsBlocked()
    {
        if (currentRound.CanPlaceAts) return false;
        statusText.color = UIFactory.Accent;
        statusText.text = "Lucky Roller opens again after the next 7";
        FlashBlocked();
        return true;
    }

    void OnAtsLowsClicked()
    {
        if (AtsBlocked()) return;
        long chip = chipSelector.SelectedChip;
        if (!TryPlaceBet(NormalCrapsBetType.AtsLows, chip, () => RebuildFlatChips(atsLowsSpot, currentRound.GetBet(NormalCrapsBetType.AtsLows), AtsChipX))) return;
        PushUndoBet(NormalCrapsBetType.AtsLows, chip);
    }

    void OnAtsHighsClicked()
    {
        if (AtsBlocked()) return;
        long chip = chipSelector.SelectedChip;
        if (!TryPlaceBet(NormalCrapsBetType.AtsHighs, chip, () => RebuildFlatChips(atsHighsSpot, currentRound.GetBet(NormalCrapsBetType.AtsHighs), AtsChipX))) return;
        PushUndoBet(NormalCrapsBetType.AtsHighs, chip);
    }

    void OnAtsAllClicked()
    {
        if (AtsBlocked()) return;
        long chip = chipSelector.SelectedChip;
        if (!TryPlaceBet(NormalCrapsBetType.AtsAll, chip, () => RebuildFlatChips(atsAllSpot, currentRound.GetBet(NormalCrapsBetType.AtsAll), AtsChipX))) return;
        PushUndoBet(NormalCrapsBetType.AtsAll, chip);
    }

    FlatSpot BuildFlatBar(NormalCrapsBetType type, Vector2 pos, Vector2 size, string label, string payout, Color col, Action onClick)
    {
        var go = new GameObject($"NCFlat_{type}");
        go.transform.SetParent(tableRoot, false);
        var rt = go.AddComponent<RectTransform>();
        rt.sizeDelta = size;
        rt.anchoredPosition = pos;
        var img = go.AddComponent<Image>();
        img.sprite = UIFactory.RoundedRect();
        img.type   = Image.Type.Sliced;
        img.color  = new Color(col.r, col.g, col.b, 0.55f);
        UIFactory.AddSharpFrame(go, new Color(col.r + 0.2f, col.g + 0.2f, col.b + 0.2f, 0.8f), square: true);
        var btn = go.AddComponent<Button>();
        btn.targetGraphic = img;
        btn.onClick.AddListener(() => onClick());
        RightClickRelay.Attach(go, () => TakeDown(type, label.Replace("\n", " ")));
        var labelT = UIFactory.MakeText(go.transform, "Lbl", new Vector2(0, 8), 13, TextAnchor.MiddleCenter,
            new Vector2(size.x - 8, 22), UIFactory.TextLight, FontStyle.Bold);
        labelT.text = label;
        var amtT = UIFactory.MakeText(go.transform, "Amt", new Vector2(0, -9), 11, TextAnchor.MiddleCenter,
            new Vector2(size.x - 8, 18), UIFactory.TextDim);
        amtT.text = payout;
        return new FlatSpot { Root = go, AmountText = amtT, DefaultLabel = payout };
    }

    FlatSpot BuildFieldBar(Vector2 pos, Vector2 size)
    {
        var go = new GameObject("NCFlat_Field");
        go.transform.SetParent(tableRoot, false);
        var rt = go.AddComponent<RectTransform>();
        rt.sizeDelta     = size;
        rt.anchoredPosition = pos;
        var img = go.AddComponent<Image>();
        img.sprite = UIFactory.RoundedRect();
        img.type   = Image.Type.Sliced;
        img.color  = new Color(FieldColor.r, FieldColor.g, FieldColor.b, 0.55f);
        UIFactory.AddSharpFrame(go, new Color(FieldColor.r + 0.2f, FieldColor.g + 0.2f, FieldColor.b + 0.2f, 0.8f), square: true);
        var btn = go.AddComponent<Button>();
        btn.targetGraphic = img;
        btn.onClick.AddListener(OnFieldClicked);
        RightClickRelay.Attach(go, () => TakeDown(NormalCrapsBetType.Field, "Field"));

        float hw = size.x / 2f;
        var gold = new Color(1f, 0.85f, 0.3f);
        var goldDim = new Color(1f, 0.85f, 0.3f, 0.82f);

        // Left zone: PAYS DOUBLE above, large "2" below — pushed closer to centre
        UIFactory.MakeText(go.transform, "TwoDbl", new Vector2(-hw + 80f, 17f), 9,
            TextAnchor.MiddleCenter, new Vector2(88f, 15f), goldDim).text = "PAYS DOUBLE";
    UIFactory.MakeText(go.transform, "Two", new Vector2(-hw + 80f, -5f), 24,
            TextAnchor.MiddleCenter, new Vector2(52f, 30f), gold, FontStyle.Bold).text = "2";

        // Centre zone: 3 · 4 · 9 · 10 · 11 evenly spaced between the 2 and 12, on a gentle arch
        int[] fieldNums = { 3, 4, 9, 10, 11 };
        float edgeX = hw - 80f;
        float step = 2f * edgeX / (fieldNums.Length + 1);
        for (int i = 0; i < fieldNums.Length; i++)
        {
            float x = -edgeX + step * (i + 1);
            float t = x / edgeX;
            float y = 2f + 22f * (1f - t * t);
            UIFactory.MakeText(go.transform, $"FieldNum_{fieldNums[i]}", new Vector2(x, y), 17,
                TextAnchor.MiddleCenter, new Vector2(44f, 24f), UIFactory.TextLight, FontStyle.Bold).text = fieldNums[i].ToString();
        }
        UIFactory.MakeText(go.transform, "FieldLbl", new Vector2(0f, -12f), 14,
            TextAnchor.MiddleCenter, new Vector2(120f, 22f), UIFactory.TextLight, FontStyle.Bold).text = "FIELD";

        // Right zone: large "12" above, PAYS TRIPLE below — pushed closer to centre
        UIFactory.MakeText(go.transform, "Twelve", new Vector2(hw - 80f, -5f), 24,
            TextAnchor.MiddleCenter, new Vector2(52f, 30f), gold, FontStyle.Bold).text = "12";
        UIFactory.MakeText(go.transform, "TwelveDbl", new Vector2(hw - 80f, 17f), 9,
            TextAnchor.MiddleCenter, new Vector2(88f, 15f), goldDim).text = "PAYS TRIPLE";

        // Chip amount text (hidden by default, shown when bet placed)
        var amtT = UIFactory.MakeText(go.transform, "Amt", new Vector2(90f, -14f), 12,
            TextAnchor.MiddleLeft, new Vector2(90f, 18f), UIFactory.TextLight);
        amtT.text = "";
        return new FlatSpot { Root = go, AmountText = amtT, DefaultLabel = "" };
    }

    static NormalCrapsBetType NumberToLayType(int n) => n switch
    {
        4 => NormalCrapsBetType.Lay4, 5 => NormalCrapsBetType.Lay5, 6 => NormalCrapsBetType.Lay6,
        8 => NormalCrapsBetType.Lay8, 9 => NormalCrapsBetType.Lay9, _ => NormalCrapsBetType.Lay10
    };

    void BuildPlaceLayCell(int n, Vector2 centerPos)
    {
        const float cellW = 100f, cellH = 140f, halfH = 60f;
        float halfOffset = cellH / 2f - halfH / 2f;

        var cellGO = new GameObject($"NCCell_{n}");
        cellGO.transform.SetParent(tableRoot, false);
        var cellRt = cellGO.AddComponent<RectTransform>();
        cellRt.sizeDelta = new Vector2(cellW, cellH);
        cellRt.anchoredPosition = centerPos;
        numberCellRoots[n] = cellRt;

        // LAY zone — top half, bet wins when 7 comes before this number
        var layCol = new Color(0.28f, 0.05f, 0.05f);
        var layGO = new GameObject($"NCLay_{n}");
        layGO.transform.SetParent(cellGO.transform, false);
        var layRt = layGO.AddComponent<RectTransform>();
        layRt.sizeDelta = new Vector2(cellW, halfH);
        layRt.anchoredPosition = new Vector2(0, halfOffset);
        var layImg = layGO.AddComponent<Image>();
        layImg.sprite = UIFactory.RoundedRect();
        layImg.type = Image.Type.Sliced;
        layImg.color = new Color(layCol.r, layCol.g, layCol.b, 0.55f);
        UIFactory.AddSharpFrame(layGO, new Color(layCol.r + 0.2f, layCol.g + 0.15f, layCol.b + 0.15f, 0.8f), square: true);
        var layBtn = layGO.AddComponent<Button>();
        layBtn.targetGraphic = layImg;
        int capturedN = n;
        layBtn.onClick.AddListener(() => OnLayClicked(capturedN));
        RightClickRelay.Attach(layGO, () => TakeDown(NumberToLayType(capturedN), $"Lay {capturedN}"));
        UIFactory.MakeText(layGO.transform, "LoseLbl", new Vector2(0, 14), 11,
            TextAnchor.MiddleCenter, new Vector2(cellW - 4f, 16f),
            new Color(0.9f, 0.3f, 0.3f, 0.9f), FontStyle.Bold).text = "LOSE";
        string layPay = n == 6 || n == 8 ? "5:6" : n == 5 || n == 9 ? "2:3" : "1:2";
        var layAmtT = UIFactory.MakeText(layGO.transform, "Amt", new Vector2(0, -8), 12,
            TextAnchor.MiddleCenter, new Vector2(cellW - 4f, 18f), UIFactory.TextDim);
        layAmtT.text = layPay;
        laySpots[n] = new FlatSpot { Root = layGO, AmountText = layAmtT, DefaultLabel = layPay };

        // Number label centered in full cell (renders over the gap between halves)
        UIFactory.MakeText(cellGO.transform, "Num", Vector2.zero, 24,
            TextAnchor.MiddleCenter, new Vector2(cellW, 32), UIFactory.TextLight, FontStyle.Bold).text = $"{n}";

        // PLACE zone — bottom half, bet wins when this number hits before 7
        string pay = n == 6 || n == 8 ? "7:6" : n == 5 || n == 9 ? "7:5" : "9:5";
        var placeGO = new GameObject($"NCPlace_{n}");
        placeGO.transform.SetParent(cellGO.transform, false);
        var placeRt = placeGO.AddComponent<RectTransform>();
        placeRt.sizeDelta = new Vector2(cellW, halfH);
        placeRt.anchoredPosition = new Vector2(0, -halfOffset);
        var placeImg = placeGO.AddComponent<Image>();
        placeImg.sprite = UIFactory.RoundedRect();
        placeImg.type = Image.Type.Sliced;
        placeImg.color = new Color(PlaceColor.r, PlaceColor.g, PlaceColor.b, 0.55f);
        UIFactory.AddSharpFrame(placeGO, new Color(PlaceColor.r + 0.2f, PlaceColor.g + 0.2f, PlaceColor.b + 0.2f, 0.8f), square: true);
        var placeBtn = placeGO.AddComponent<Button>();
        placeBtn.targetGraphic = placeImg;
        placeBtn.onClick.AddListener(() => OnPlaceClicked(capturedN));
        RightClickRelay.Attach(placeGO, () => TakeDown(NumberToPlaceType(capturedN), $"Place {capturedN}"));
        UIFactory.MakeText(placeGO.transform, "WinLbl", new Vector2(0, 14), 11,
            TextAnchor.MiddleCenter, new Vector2(cellW - 4f, 16f),
            new Color(0.3f, 0.9f, 0.35f, 0.9f), FontStyle.Bold).text = "WIN";
        var placeAmtT = UIFactory.MakeText(placeGO.transform, "Amt", new Vector2(0, -8), 12,
            TextAnchor.MiddleCenter, new Vector2(cellW - 4f, 18f), UIFactory.TextDim);
        placeAmtT.text = pay;
        placeSpots[n] = new FlatSpot { Root = placeGO, AmountText = placeAmtT, DefaultLabel = pay };
    }

    FlatSpot BuildHardSpot(int n, Vector2 pos, float w, float h, string payout)
    {
        var go = new GameObject($"NCHard_{n}");
        go.transform.SetParent(tableRoot, false);
        var rt = go.AddComponent<RectTransform>();
        rt.sizeDelta = new Vector2(w, h);
        rt.anchoredPosition = pos;
        var img = go.AddComponent<Image>();
        img.sprite = UIFactory.RoundedRect();
        img.type   = Image.Type.Sliced;
        img.color  = new Color(HardColor.r, HardColor.g, HardColor.b, 0.55f);
        UIFactory.AddSharpFrame(go, new Color(HardColor.r + 0.15f, HardColor.g + 0.15f, HardColor.b + 0.15f, 0.8f), square: true);
        var btn = go.AddComponent<Button>();
        btn.targetGraphic = img;
        btn.onClick.AddListener(() => OnHardwayClicked(n));
        RightClickRelay.Attach(go, () => TakeDown(NumberToHardType(n), $"Hard {n}"));
        var numT = UIFactory.MakeText(go.transform, "Num", new Vector2(0, 13), 13, TextAnchor.MiddleCenter,
            new Vector2(w - 8f, 22f), UIFactory.TextLight, FontStyle.Bold);
        numT.text = $"HARD {n}";
        var amtT = UIFactory.MakeText(go.transform, "Amt", new Vector2(0, -8), 11, TextAnchor.MiddleCenter,
            new Vector2(w - 8f, 20f), UIFactory.TextDim);
        amtT.text = payout;
        return new FlatSpot { Root = go, AmountText = amtT, DefaultLabel = payout };
    }

    FlatSpot BuildPropSpot(NormalCrapsBetType type, Vector2 pos, float w, float h, string label, string payout)
    {
        var go = new GameObject($"NCProp_{type}");
        go.transform.SetParent(tableRoot, false);
        var rt = go.AddComponent<RectTransform>();
        rt.sizeDelta = new Vector2(w, h);
        rt.anchoredPosition = pos;
        var img = go.AddComponent<Image>();
        img.sprite = UIFactory.RoundedRect();
        img.type   = Image.Type.Sliced;
        img.color  = new Color(PropColor.r, PropColor.g, PropColor.b, 0.55f);
        UIFactory.AddSharpFrame(go, new Color(PropColor.r + 0.15f, PropColor.g + 0.15f, PropColor.b + 0.15f, 0.8f), square: true);
        var btn = go.AddComponent<Button>();
        btn.targetGraphic = img;
        btn.onClick.AddListener(() => OnPropClicked(type));
        RightClickRelay.Attach(go, () => TakeDown(type, label.Replace("\n", " ")));
        var lblT = UIFactory.MakeText(go.transform, "Lbl", new Vector2(0, 12), 12, TextAnchor.MiddleCenter,
            new Vector2(w - 8f, 28f), UIFactory.TextLight, FontStyle.Bold);
        lblT.text = label;
        var amtT = UIFactory.MakeText(go.transform, "Amt", new Vector2(0, -11), 11, TextAnchor.MiddleCenter,
            new Vector2(w - 8f, 20f), UIFactory.TextDim);
        amtT.text = payout;
        return new FlatSpot { Root = go, AmountText = amtT, DefaultLabel = payout };
    }

    // --- Bet placement handlers ---

    bool TryPlaceBet(NormalCrapsBetType type, long amount, Action onSuccess)
    {
        if (rolling) return false;
        if (!bankroll.TryWithdraw(amount))
        {
            statusText.text = bankroll.Balance < ChipDenominations.Values[0]
                ? "Out of chips — use ADD FUNDS above to keep playing"
                : "Not enough balance";
            juiceManager?.MicroShake(1.2f);
            return false;
        }
        currentRound.PlaceBet(type, amount);
        roundTotalStaked += amount;
        soundManager?.PlayChip();
        onBankrollChanged?.Invoke();
        onSuccess?.Invoke();
        RefreshActionButtons();
        return true;
    }

    void OnPassLineClicked()
    {
        if (currentRound.Phase == NormalCrapsPhase.Point)
        {
            long passBase = currentRound.GetBet(NormalCrapsBetType.PassLine);
            if (passBase > 0 && currentRound.Point.HasValue)
                OpenOddsModal(NormalCrapsBetType.PassOdds, "PASS LINE ODDS", currentRound.Point.Value, passBase, isDontSide: false);
            else
            { statusText.text = "Pass Line locked once point is set"; juiceManager?.MicroShake(1f); }
            return;
        }
        long chip = chipSelector.SelectedChip;
        if (!TryPlaceBet(NormalCrapsBetType.PassLine, chip, () => RebuildFlatChips(passSpot, currentRound.GetBet(NormalCrapsBetType.PassLine), LineChipX))) return;
        JuiceTweens.Pulse(this, (RectTransform)passSpot.Root.transform, peakScale: 1.08f, duration: 0.16f);
        PushUndoBet(NormalCrapsBetType.PassLine, chip);
    }

    void OnDontPassClicked()
    {
        if (currentRound.Phase == NormalCrapsPhase.Point)
        {
            long dpBase = currentRound.GetBet(NormalCrapsBetType.DontPass);
            if (dpBase > 0 && currentRound.Point.HasValue)
                OpenOddsModal(NormalCrapsBetType.DontPassOdds, "DON'T PASS LAY ODDS", currentRound.Point.Value, dpBase, isDontSide: true);
            else
            { statusText.text = "Don't Pass locked once point is set"; juiceManager?.MicroShake(1f); }
            return;
        }
        long chip = chipSelector.SelectedChip;
        if (!TryPlaceBet(NormalCrapsBetType.DontPass, chip, () => RebuildFlatChips(dontPassSpot, currentRound.GetBet(NormalCrapsBetType.DontPass), LineChipX))) return;
        JuiceTweens.Pulse(this, (RectTransform)dontPassSpot.Root.transform, peakScale: 1.08f, duration: 0.16f);
        PushUndoBet(NormalCrapsBetType.DontPass, chip);
    }

    void OnComeClicked()
    {
        if (rolling) return;
        if (currentRound.Phase != NormalCrapsPhase.Point) { FlashBlocked(); return; }
        long chip = chipSelector.SelectedChip;
        if (!bankroll.TryWithdraw(chip))
        {
            statusText.text = bankroll.Balance < ChipDenominations.Values[0]
                ? "Out of chips — use ADD FUNDS above to keep playing"
                : "Not enough balance";
            juiceManager?.MicroShake(1.2f);
            return;
        }
        var w = currentRound.PlaceComeBet(chip);
        roundTotalStaked += chip;
        soundManager?.PlayChip();
        onBankrollChanged?.Invoke();
        // Chip sits on the COME bar until it parks at a point
        var go = SpawnWagerChip(comeBgT, chip, new Vector2(0, -6f));
        comeChips[w] = go;
        RefreshComeBarText();
        RefreshActionButtons();
    }

    void OnDontComeClicked()
    {
        if (rolling) return;
        if (currentRound.Phase != NormalCrapsPhase.Point) { FlashBlocked(); return; }
        long chip = chipSelector.SelectedChip;
        if (!bankroll.TryWithdraw(chip))
        {
            statusText.text = bankroll.Balance < ChipDenominations.Values[0]
                ? "Out of chips — use ADD FUNDS above to keep playing"
                : "Not enough balance";
            juiceManager?.MicroShake(1.2f);
            return;
        }
        var w = currentRound.PlaceDontComeBet(chip);
        roundTotalStaked += chip;
        soundManager?.PlayChip();
        onBankrollChanged?.Invoke();
        var go = SpawnWagerChip(dcBgT, chip, new Vector2(0, -6f));
        dcChips[w] = go;
        RefreshComeBarText();
        RefreshActionButtons();
    }

    GameObject SpawnWagerChip(Transform parent, long denomination, Vector2 pos)
    {
        Color fill = denomination >= 500 ? ChipColors[2] : denomination >= 100 ? ChipColors[1] : ChipColors[0];
        var go = new GameObject("WagerChip");
        go.transform.SetParent(parent, false);
        var img = MakeChipImage(go, fill);
        var rt = go.GetComponent<RectTransform>();
        rt.sizeDelta = new Vector2(18, 18);
        rt.anchoredPosition = pos;
        JuiceTweens.PopIn(this, rt, overshoot: 1.3f, duration: 0.15f);
        return go;
    }

    void RefreshComeBarText()
    {
        long unparked = currentRound.ComeWagers.Where(w => w.Point == null).Sum(w => w.Amount);
        comeAmtText.text = unparked > 0 ? UIFactory.FormatMoney(unparked) : "1:1";
        long dcUnparked = currentRound.DontComeWagers.Where(w => w.Point == null).Sum(w => w.Amount);
        dcAmtText.text = dcUnparked > 0 ? UIFactory.FormatMoney(dcUnparked) : "1:1";
    }

    void OnFieldClicked()
    {
        long chip = chipSelector.SelectedChip;
        if (!TryPlaceBet(NormalCrapsBetType.Field, chip, () => RebuildFlatChips(fieldSpot, currentRound.GetBet(NormalCrapsBetType.Field), FieldChipX))) return;
        JuiceTweens.Pulse(this, (RectTransform)fieldSpot.Root.transform, peakScale: 1.08f, duration: 0.16f);
        PushUndoBet(NormalCrapsBetType.Field, chip);
    }

    void OnPlaceClicked(int n)
    {
        long chip = chipSelector.SelectedChip;
        var btype = NumberToPlaceType(n);
        if (!TryPlaceBet(btype, chip, () => { if (placeSpots.TryGetValue(n, out var s)) RebuildFlatChips(s, currentRound.GetBet(btype)); })) return;
        if (placeSpots.TryGetValue(n, out var spot)) JuiceTweens.Pulse(this, (RectTransform)spot.Root.transform, peakScale: 1.08f, duration: 0.16f);
        if (numberCellRoots.TryGetValue(n, out var cell)) JuiceTweens.Pulse(this, cell, peakScale: 1.06f, duration: 0.16f);
        PushUndoBet(btype, chip);
    }

    void OnLayClicked(int n)
    {
        long chip = chipSelector.SelectedChip;
        var btype = NumberToLayType(n);
        if (!TryPlaceBet(btype, chip, () => { if (laySpots.TryGetValue(n, out var s)) RebuildFlatChips(s, currentRound.GetBet(btype)); })) return;
        if (laySpots.TryGetValue(n, out var spot)) JuiceTweens.Pulse(this, (RectTransform)spot.Root.transform, peakScale: 1.08f, duration: 0.16f);
        if (numberCellRoots.TryGetValue(n, out var cell)) JuiceTweens.Pulse(this, cell, peakScale: 1.06f, duration: 0.16f);
        PushUndoBet(btype, chip);
    }

    void OnHardwayClicked(int n)
    {
        long chip = chipSelector.SelectedChip;
        var btype = NumberToHardType(n);
        if (!TryPlaceBet(btype, chip, () => { if (hardSpots.TryGetValue(n, out var s)) RebuildFlatChips(s, currentRound.GetBet(btype)); })) return;
        if (hardSpots.TryGetValue(n, out var spot)) JuiceTweens.Pulse(this, (RectTransform)spot.Root.transform, peakScale: 1.08f, duration: 0.16f);
        PushUndoBet(btype, chip);
    }

    void OnPropClicked(NormalCrapsBetType type)
    {
        long chip = chipSelector.SelectedChip;
        if (!TryPlaceBet(type, chip, () => { if (propSpots.TryGetValue(type, out var s)) RebuildFlatChips(s, currentRound.GetBet(type)); })) return;
        if (propSpots.TryGetValue(type, out var spot)) JuiceTweens.Pulse(this, (RectTransform)spot.Root.transform, peakScale: 1.08f, duration: 0.16f);
        PushUndoBet(type, chip);
    }

    static NormalCrapsBetType NumberToPlaceType(int n) => n switch
    {
        4 => NormalCrapsBetType.Place4, 5 => NormalCrapsBetType.Place5, 6 => NormalCrapsBetType.Place6,
        8 => NormalCrapsBetType.Place8, 9 => NormalCrapsBetType.Place9, _ => NormalCrapsBetType.Place10
    };

    static NormalCrapsBetType NumberToHardType(int n) => n switch
    {
        4 => NormalCrapsBetType.Hard4, 6 => NormalCrapsBetType.Hard6,
        8 => NormalCrapsBetType.Hard8, _ => NormalCrapsBetType.Hard10
    };

    // --- Chip visuals ---

    void AddChipVisual(FlatSpot spot, long denomination) =>
        AddChipVisualAt(spot, denomination, new Vector2(-32 + spot.ChipVisuals.Count * 16f, -12));

    void AddChipVisualAt(FlatSpot spot, long denomination, Vector2 pos)
    {
        Color fill = denomination >= 500 ? ChipColors[2] : denomination >= 100 ? ChipColors[1] : ChipColors[0];
        var go = new GameObject("Chip");
        go.transform.SetParent(spot.Root.transform, false);
        var img = MakeChipImage(go, fill);
        var rt = go.GetComponent<RectTransform>();
        rt.sizeDelta = new Vector2(18, 18);
        rt.anchoredPosition = pos;
        spot.ChipVisuals.Add(go);
        JuiceTweens.PopIn(this, rt, overshoot: 1.3f, duration: 0.15f);
    }

    static Image MakeChipImage(GameObject go, Color fill)
    {
        var img = go.AddComponent<Image>();
        img.sprite = UIFactory.Circle();
        img.color = fill;
        img.raycastTarget = false;
        if (fill == UIFactory.Chip500White)
        {
            var edge = go.AddComponent<Outline>();
            edge.effectColor = new Color(0f, 0f, 0f, 0.75f);
            edge.effectDistance = new Vector2(1f, -1f);
        }
        return img;
    }

    void ClearChipVisuals(FlatSpot spot)
    {
        foreach (var c in spot.ChipVisuals) Destroy(c);
        spot.ChipVisuals.Clear();
        foreach (var c in spot.OddsChipVisuals) Destroy(c);
        spot.OddsChipVisuals.Clear();
        spot.AmountText.text = spot.DefaultLabel;
        if (spot.OddsText != null) spot.OddsText.text = "";
    }

    void RefreshSpot(FlatSpot spot, long amt, long chip)
    {
        spot.AmountText.text = amt > 0 ? UIFactory.FormatMoney(amt) : spot.DefaultLabel;
    }

    void FlashBlocked() => juiceManager?.MicroShake(1.2f);

    void RebuildFlatChips(FlatSpot spot, long amt, float chipX = 36f)
    {
        ClearChipVisuals(spot);
        if (amt <= 0) return;
        var denoms = ChipDenominations.Values;
        long remaining = amt;
        var chipDenoms = new System.Collections.Generic.List<long>();
        for (int d = denoms.Length - 1; d >= 0 && chipDenoms.Count < 5; d--)
        {
            long denom = denoms[d];
            for (long n = remaining / denom; n > 0 && chipDenoms.Count < 5; n--, remaining -= denom)
                chipDenoms.Add(denom);
        }
        if (chipDenoms.Count == 0) chipDenoms.Add(denoms[0]);
        const float spacing = 10f;
        float startY = -((chipDenoms.Count - 1) * spacing) / 2f;
        for (int i = 0; i < chipDenoms.Count; i++)
            AddChipVisualAt(spot, chipDenoms[i], new Vector2(chipX, startY + i * spacing));
        spot.AmountText.text = UIFactory.FormatMoney(amt);
    }

    void RebuildOddsChips(FlatSpot spot, long oddsAmt)
    {
        if (oddsAmt <= 0) return;
        var denoms = ChipDenominations.Values;
        long remaining = oddsAmt;
        var chipDenoms = new System.Collections.Generic.List<long>();
        for (int d = denoms.Length - 1; d >= 0 && chipDenoms.Count < 5; d--)
        {
            long denom = denoms[d];
            for (long n = remaining / denom; n > 0 && chipDenoms.Count < 5; n--, remaining -= denom)
                chipDenoms.Add(denom);
        }
        if (chipDenoms.Count == 0) chipDenoms.Add(denoms[0]);
        const float spacing = 10f;
        const float chipX = LineOddsChipX;
        float startY = -((chipDenoms.Count - 1) * spacing) / 2f;
        for (int i = 0; i < chipDenoms.Count; i++)
        {
            Color fill = chipDenoms[i] >= 500 ? ChipColors[2] : chipDenoms[i] >= 100 ? ChipColors[1] : ChipColors[0];
            var go = new GameObject("OddsChip");
            go.transform.SetParent(spot.Root.transform, false);
            var img = MakeChipImage(go, fill);
            var rt = go.GetComponent<RectTransform>();
            rt.sizeDelta = new Vector2(18, 18);
            rt.anchoredPosition = new Vector2(chipX, startY + i * spacing);
            spot.OddsChipVisuals.Add(go);
            JuiceTweens.PopIn(this, rt, overshoot: 1.3f, duration: 0.15f);
        }
    }

    void RebuildAllChipVisuals()
    {
        RebuildFlatChips(passSpot,     currentRound.GetBet(NormalCrapsBetType.PassLine), LineChipX);
        RebuildOddsChips(passSpot,     currentRound.GetBet(NormalCrapsBetType.PassOdds));
        RebuildFlatChips(dontPassSpot, currentRound.GetBet(NormalCrapsBetType.DontPass), LineChipX);
        RebuildOddsChips(dontPassSpot, currentRound.GetBet(NormalCrapsBetType.DontPassOdds));
        RebuildFlatChips(fieldSpot,    currentRound.GetBet(NormalCrapsBetType.Field), FieldChipX);
        foreach (var kv in placeSpots) RebuildFlatChips(kv.Value, currentRound.GetBet(NumberToPlaceType(kv.Key)));
        foreach (var kv in laySpots)   RebuildFlatChips(kv.Value, currentRound.GetBet(NumberToLayType(kv.Key)));
        foreach (var kv in hardSpots)  RebuildFlatChips(kv.Value, currentRound.GetBet(NumberToHardType(kv.Key)));
        foreach (var kv in propSpots)  RebuildFlatChips(kv.Value, currentRound.GetBet(kv.Key));
        if (atsLowsSpot  != null) RebuildFlatChips(atsLowsSpot,  currentRound.GetBet(NormalCrapsBetType.AtsLows),  AtsChipX);
        if (atsHighsSpot != null) RebuildFlatChips(atsHighsSpot, currentRound.GetBet(NormalCrapsBetType.AtsHighs), AtsChipX);
        if (atsAllSpot   != null) RebuildFlatChips(atsAllSpot,   currentRound.GetBet(NormalCrapsBetType.AtsAll),   AtsChipX);
        onBankrollChanged?.Invoke();
    }

    void PushUndoBet(NormalCrapsBetType type, long amount)
    {
        undoStack.Add(() =>
        {
            currentRound.PlaceBet(type, -amount);
            bankroll.Deposit(amount);
            roundTotalStaked -= amount;
            RebuildAllChipVisuals();
        });
        if (undoStack.Count > MaxUndoDepth) undoStack.RemoveAt(0);
    }

    void OnClearBetClicked()
    {
        long refunded = 0;
        long field = currentRound.GetBet(NormalCrapsBetType.Field);
        if (field > 0) { currentRound.PlaceBet(NormalCrapsBetType.Field, -field); refunded += field; }
        foreach (var t in PropTypes)
        {
            long b = currentRound.GetBet(t);
            if (b > 0) { currentRound.PlaceBet(t, -b); refunded += b; }
        }
        foreach (int n in PlaceNumbers)
        {
            var bt = NumberToPlaceType(n); long b = currentRound.GetBet(bt);
            if (b > 0) { currentRound.PlaceBet(bt, -b); refunded += b; }
        }
        foreach (int n in PlaceNumbers)
        {
            var lt = NumberToLayType(n); long b = currentRound.GetBet(lt);
            if (b > 0) { currentRound.PlaceBet(lt, -b); refunded += b; }
        }
        foreach (int n in HardNumbers)
        {
            var bt = NumberToHardType(n); long b = currentRound.GetBet(bt);
            if (b > 0) { currentRound.PlaceBet(bt, -b); refunded += b; }
        }
        if (currentRound.Phase == NormalCrapsPhase.ComeOut)
        {
            long pass = currentRound.GetBet(NormalCrapsBetType.PassLine);
            if (pass > 0) { currentRound.PlaceBet(NormalCrapsBetType.PassLine, -pass); refunded += pass; }
            long dont = currentRound.GetBet(NormalCrapsBetType.DontPass);
            if (dont > 0) { currentRound.PlaceBet(NormalCrapsBetType.DontPass, -dont); refunded += dont; }
        }
        if (currentRound.CanPlaceAts)
        {
            long atsL = currentRound.GetBet(NormalCrapsBetType.AtsLows);
            if (atsL > 0) { currentRound.PlaceBet(NormalCrapsBetType.AtsLows, -atsL); refunded += atsL; }
            long atsH = currentRound.GetBet(NormalCrapsBetType.AtsHighs);
            if (atsH > 0) { currentRound.PlaceBet(NormalCrapsBetType.AtsHighs, -atsH); refunded += atsH; }
            long atsA = currentRound.GetBet(NormalCrapsBetType.AtsAll);
            if (atsA > 0) { currentRound.PlaceBet(NormalCrapsBetType.AtsAll, -atsA); refunded += atsA; }
        }
        if (refunded <= 0) { statusText.text = "Nothing to clear"; FlashBlocked(); return; }
        bankroll.Deposit(refunded);
        roundTotalStaked -= refunded;
        undoStack.Clear();
        soundManager?.PlayClick();
        RebuildAllChipVisuals();
        RefreshActionButtons();
    }

    void OnRepeatBetClicked()
    {
        if (rolling) return;
        bool didAnything = false;
        foreach (var kv in lastRollBets)
        {
            if (currentRound.GetBet(kv.Key) > 0) continue;
            bool lineBet = kv.Key == NormalCrapsBetType.PassLine || kv.Key == NormalCrapsBetType.DontPass;
            if (lineBet && currentRound.Phase != NormalCrapsPhase.ComeOut) continue;
            if (IsAtsType(kv.Key) && !currentRound.CanPlaceAts) continue;
            if (!bankroll.TryWithdraw(kv.Value)) continue;
            currentRound.PlaceBet(kv.Key, kv.Value);
            roundTotalStaked += kv.Value;
            PushUndoBet(kv.Key, kv.Value);
            didAnything = true;
        }
        if (!didAnything) { statusText.text = "Nothing to repeat"; FlashBlocked(); return; }
        soundManager?.PlayChip();
        JuiceTweens.Pulse(this, repeatBetButton.GetComponent<RectTransform>(), peakScale: 1.15f, duration: 0.2f);
        RebuildAllChipVisuals();
        RefreshActionButtons();
    }

    static bool IsAtsType(NormalCrapsBetType t) =>
        t == NormalCrapsBetType.AtsLows || t == NormalCrapsBetType.AtsHighs || t == NormalCrapsBetType.AtsAll;

    // Right-click take-down: returns a whole spot to the wallet, following Vegas rules on what may come down.
    void TakeDown(NormalCrapsBetType type, string label)
    {
        if (rolling) return;
        var types = new List<NormalCrapsBetType> { type };
        if (type == NormalCrapsBetType.PassLine && currentRound.Phase == NormalCrapsPhase.Point)
        {
            types[0] = NormalCrapsBetType.PassOdds;
            label = "Pass Line odds";
        }
        else if (type == NormalCrapsBetType.DontPass)
            types.Add(NormalCrapsBetType.DontPassOdds);

        if (IsAtsType(type) && !currentRound.CanPlaceAts)
        {
            statusText.color = UIFactory.Accent;
            statusText.text = "Lucky Roller can't come down mid-run";
            FlashBlocked();
            return;
        }

        long amount = types.Sum(t => currentRound.GetBet(t));
        if (amount <= 0)
        {
            if (type == NormalCrapsBetType.PassLine && currentRound.Phase == NormalCrapsPhase.Point
                && currentRound.GetBet(NormalCrapsBetType.PassLine) > 0)
            {
                statusText.color = UIFactory.Accent;
                statusText.text = "Pass Line is locked once the point is set";
            }
            FlashBlocked();
            return;
        }
        foreach (var t in types) currentRound.ClearBet(t);
        ReturnToWallet(amount, label);
        RebuildAllChipVisuals();
        RefreshPassDontPassOddsText();
    }

    void TakeDownUnparkedCome()
    {
        if (rolling) return;
        var unparked = currentRound.ComeWagers.Where(w => w.Point == null).ToList();
        if (unparked.Count == 0) { FlashBlocked(); return; }
        long amount = 0;
        foreach (var w in unparked)
        {
            amount += w.Amount;
            currentRound.RemoveComeWager(w);
            if (comeChips.TryGetValue(w, out var chip)) { Destroy(chip); comeChips.Remove(w); }
        }
        ReturnToWallet(amount, "Come");
        RefreshComeBarText();
    }

    void TakeDownUnparkedDontCome()
    {
        if (rolling) return;
        var unparked = currentRound.DontComeWagers.Where(w => w.Point == null).ToList();
        if (unparked.Count == 0) { FlashBlocked(); return; }
        long amount = 0;
        foreach (var w in unparked)
        {
            amount += w.Amount;
            currentRound.RemoveDontComeWager(w);
            if (dcChips.TryGetValue(w, out var chip)) { Destroy(chip); dcChips.Remove(w); }
        }
        ReturnToWallet(amount, "Don't Come");
        RefreshComeBarText();
    }

    void ReturnToWallet(long amount, string label)
    {
        bankroll.Deposit(amount);
        roundTotalStaked -= amount;
        undoStack.Clear();
        soundManager?.PlayClick();
        statusText.color = UIFactory.Accent;
        statusText.text = $"{label} down — {UIFactory.FormatMoney(amount)} back to wallet";
        onBankrollChanged?.Invoke();
        RefreshActionButtons();
    }

    // Every chip currently on the felt, including odds behind Come / Don't Come wagers.
    public long OnTableTotal()
    {
        long total = Enum.GetValues(typeof(NormalCrapsBetType)).Cast<NormalCrapsBetType>().Sum(t => currentRound.GetBet(t));
        total += currentRound.ComeWagers.Sum(w => w.Amount + w.OddsAmount);
        total += currentRound.DontComeWagers.Sum(w => w.Amount + w.LayOddsAmount);
        return total;
    }

    // Leaving the table: pay out a roll still animating, then pick every chip up and return it to the wallet.
    // Pure bankroll/round math only — safe to call from OnDestroy / OnApplicationQuit.
    public void RefundTableBets()
    {
        if (pendingResult != null) { bankroll.Deposit(pendingResult.TotalReturned); pendingResult = null; }
        long onTable = OnTableTotal();
        if (onTable > 0) bankroll.Deposit(onTable);
        currentRound = new NormalCrapsRound(rng);
    }

    void OnUndoClicked()
    {
        if (undoStack.Count == 0) { statusText.text = "Nothing to undo"; FlashBlocked(); return; }
        var action = undoStack[undoStack.Count - 1];
        undoStack.RemoveAt(undoStack.Count - 1);
        action();
        soundManager?.PlayClick();
        RefreshActionButtons();
    }

    // --- Roll sequence ---

    void OnRollClicked()
    {
        if (rolling || shooterPromptRoot.activeSelf) return;
        bool hasBets = Enum.GetValues(typeof(NormalCrapsBetType)).Cast<NormalCrapsBetType>().Any(t => currentRound.GetBet(t) > 0)
            || currentRound.ComeWagers.Any()
            || currentRound.DontComeWagers.Any();
        if (!hasBets) { statusText.text = "Place at least one bet"; juiceManager?.MicroShake(1.2f); return; }

        // Save for REPEAT BET — every flat bet on the felt; odds and Come/Don't Come need a point so they're skipped
        lastRollBets.Clear();
        foreach (NormalCrapsBetType t in Enum.GetValues(typeof(NormalCrapsBetType)))
        {
            if (t == NormalCrapsBetType.PassOdds || t == NormalCrapsBetType.DontPassOdds) continue;
            long b = currentRound.GetBet(t);
            if (b > 0) lastRollBets[t] = b;
        }

        undoStack.Clear();
        rolling = true;
        rollButton.interactable = false;
        StartCoroutine(RollSequence());
    }

    IEnumerator RollSequence()
    {
        statusText.color = UIFactory.Accent;
        statusText.text = "Rolling...";

        // Pre-sim
        if (presimRoutine != null) StopCoroutine(presimRoutine);
        PreSimResult presim = null;
        bool presimDone = false;
        if (shadowDie1 != null && shadowDie2 != null)
        {
            presimRoutine = StartCoroutine(Dice3D.RunPreSim(shadowDie1, shadowDie2, r => { presim = r; presimDone = true; }));
            yield return new WaitUntil(() => presimDone);
        }

        pointBeforeRoll = currentRound.Point ?? 0;
        onTableAtRoll = OnTableTotal();
        var result = currentRound.Roll();
        pendingResult = result;

        if (die1UI != null && die2UI != null && presim != null)
            yield return StartCoroutine(Dice3D.RollPair(die1UI, result.Die1, die2UI, result.Die2, presim));
        else
            yield return new WaitForSeconds(1.5f);

        try { ApplyRollResult(result); }
        finally
        {
            rolling = false;
            rollButton.interactable = true;
            RefreshBetsToggle();
        }
    }

    void ApplyRollResult(NormalCrapsRollResult result)
    {
        pendingResult = null;
        long returned = result.TotalReturned;
        long net = returned - result.TotalStaked;
        bankroll.Deposit(returned);
        roundTotalReturned += returned;
        rollCount++;

        onBankrollChanged?.Invoke();

        // Status
        string dice  = $"{result.Die1} + {result.Die2} = {result.Total}";
        Color sc = result.RoundOver || net < 0 ? UIFactory.Negative : net > 0 ? UIFactory.Positive : UIFactory.Accent;
        string verdict = "";
        if (result.RoundOver)
        {
            verdict = " — SEVEN OUT";
        }
        else if (result.PointEstablishedThisRoll)
            verdict = $" — POINT: {result.NewPoint}";
        else if (pointBeforeRoll != 0 && currentRound.Phase == NormalCrapsPhase.ComeOut && !result.PassResolved)
            verdict = " — POINT MADE";
        else if (result.PassResolved && result.PassReturn > 0)
        {
            // Come-out natural (7 or 11) vs point made (total matches old point)
            verdict = (result.Total == 7 || result.Total == 11) ? " — NATURAL!" : " — POINT MADE!";
        }
        else if (result.PassResolved && result.PassReturn == 0)
        {
            // Come-out craps (2/3/12) loses pass line
            bool isCraps = result.Total == 2 || result.Total == 3 || result.Total == 12;
            bool dontWon = result.DontPassReturn > 0 && !result.DontPassPushed;
            if (isCraps)
            {
                if (dontWon)                    verdict = " — CRAPS — DON'T PASS WINS";
                else if (result.DontPassPushed) verdict = " — CRAPS — push on 12";
                else                            verdict = " — CRAPS";
            }
            else verdict = " — PASS LOSES";
        }

        // Supplement verdict when no pass-line event but other bets resolved
        string sideWin = "";
        if (result.HardwayHits.Count > 0)
            sideWin = $" — HARD {result.HardwayHits.Keys.First()}!";
        else if (result.PlaceHits.Count > 0)
            sideWin = $" — PLACE {result.PlaceHits.Keys.First()} PAYS";
        else if (result.FieldReturn > 0)
            sideWin = " — FIELD PAYS";
        else if (result.AnyCrapsReturn > 0 || result.AnySevenReturn > 0
              || result.AnyElevenReturn > 0 || result.HornReturn > 0)
            sideWin = " — PROP WINS";

        if (verdict == "" && net > 0)
            verdict = sideWin;
        else if (verdict == " — CRAPS" && net > 0)
            verdict += sideWin;
        else if (verdict == "" && net < 0)
            verdict = result.LayLosses.Count > 0 ? $" — LAY {result.LayLosses.Keys.First()} LOSES" : " — BETS LOSE";
        else if (verdict == "" && result.DontComePushed.Count > 0)
            verdict = " — DON'T COME PUSH";
        else if (verdict == "" && result.ComeParked.Count > 0)
            verdict = $" — COME MOVES TO {result.ComeParked[0].Point}";
        else if (verdict == "" && result.DontComeParked.Count > 0)
            verdict = $" — DON'T COME MOVES TO {result.DontComeParked[0].Point}";

        string flavor = "";
        if (!result.RoundOver && net > 0 && verdict != "")
            flavor = "  " + WinFlavors[UnityEngine.Random.Range(0, WinFlavors.Length)];
        else if (result.RoundOver)
            flavor = "  " + LoseFlavors[UnityEngine.Random.Range(0, LoseFlavors.Length)];

        statusText.color = sc;
        statusText.text = dice + verdict + flavor;

        // Refresh UI state
        RefreshPhaseVisuals();

        // Juice
        if (result.RoundOver)
        {
            juiceManager?.Shake(0.25f, 1.5f);
            juiceManager?.Flash(new Color(0.85f, 0.2f, 0.2f, 0.14f), 0.4f);
            floatingText?.Show("SEVEN OUT", UIFactory.Negative, fontSize: 36);
            if (net > 0)
            {
                soundManager?.PlayWin();
                winStreak++;
                if (!doubledMilestoneFired && bankroll.TotalFunded > 0 && bankroll.Balance >= bankroll.TotalFunded * 2)
                { doubledMilestoneFired = true; milestoneToast?.Show("BANKROLL DOUBLED!", UIFactory.Accent, fontSize: 30); }
                if (winStreak == 5 || winStreak == 10 || winStreak == 15 || winStreak == 20)
                    milestoneToast?.Show($"{winStreak} WIN STREAK!", new Color(1f, 0.85f, 0.2f), fontSize: 30);
            }
            else
            {
                soundManager?.PlayLose();
                winStreak = 0;
            }
        }
        else if (net > 0)
        {
            soundManager?.PlayWin();
            if (returned >= ChipDenominations.Values[2]) // $500+
            {
                juiceManager?.Shake(0.5f, 4f);
                juiceManager?.Flash(new Color(0.3f, 1f, 0.4f, 0.28f), 0.7f);
                juiceManager?.PlayConfetti(2f);
                juiceManager?.PulseLight(0.9f, 0.7f);
                juiceManager?.PlayMoneyFountain(Vector2.zero);
                floatingText?.Show($"HUGE WIN! +{UIFactory.FormatMoney(net)}", UIFactory.Positive, fontSize: 42);
            }
            else if (returned >= ChipDenominations.Values[0] * 4L) // $100+
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
            winStreak = 0;
        }

        bool showStreak = winStreak >= 2;
        streakAnimator?.SetText(showStreak ? $"<wave><rainb>{winStreak} WIN STREAK</rainb></wave>" : "");
        streakBadgeGO?.SetActive(showStreak);
        if (showStreak) JuiceTweens.Pulse(this, (RectTransform)streakBadgeGO.transform, peakScale: 1.15f, duration: 0.3f);

        onRollResolved?.Invoke(result.Total.ToString(), net > 0 ? UIFactory.Positive : net < 0 ? UIFactory.Negative : UIFactory.Accent);
        // Per-roll row: Stake column shows the turn's stake so far; NetChange is this roll's net only
        int rowPoint = pointBeforeRoll != 0 ? pointBeforeRoll : result.NewPoint ?? 0;
        var rollRecord = new NormalCrapsRoundRecord(rollLogIndex++, rowPoint, rollCount,
            onTableAtRoll, onTableAtRoll + net, bankroll.Balance, result.Total);
        onRollLogged?.Invoke(rollRecord);

        if (result.RoundOver)
        {
            // Shooter turn ended — record for history/save
            var record = new NormalCrapsRoundRecord(roundIndex, pointBeforeRoll, rollCount,
                roundTotalStaked, roundTotalReturned, bankroll.Balance, result.Total);
            onRoundResolved?.Invoke(record);
            roundIndex++;
            int[] turnTargets = { 50, 100, 250, 500, 1000 };
            foreach (var t in turnTargets)
                if (roundIndex == t && roundMilestonesFired.Add(t))
                    milestoneToast?.Show($"{t} Shooter Turns This Session", UIFactory.Accent, fontSize: 26);
            roundTotalStaked = 0;
            roundTotalReturned = 0;
            rollCount = 0;
            if (!TryOpenShooterPrompt("NEW SHOOTER COMING OUT"))
                currentRound.PlaceBetsWorking = false;
            RefreshBetsToggle();
        }
        else if (pointBeforeRoll != 0 && currentRound.Phase == NormalCrapsPhase.ComeOut)
        {
            TryOpenShooterPrompt($"POINT {pointBeforeRoll} MADE — COMING OUT");
        }
        else if (result.PointEstablishedThisRoll)
        {
            int pt = result.NewPoint.Value;
            long passBase = currentRound.GetBet(NormalCrapsBetType.PassLine);
            long dontBase = currentRound.GetBet(NormalCrapsBetType.DontPass);
            if (passBase > 0)
                OpenOddsModal(NormalCrapsBetType.PassOdds, "PASS LINE ODDS", pt, passBase, isDontSide: false);
            else if (dontBase > 0)
                OpenOddsModal(NormalCrapsBetType.DontPassOdds, "DON'T PASS LAY ODDS", pt, dontBase, isDontSide: true);
            TryOpenShooterPrompt($"POINT {pt} IS ON");
            RefreshActionButtons();
        }

        // Clear chip visuals for bets that resolved
        if (result.PassResolved) ClearChipVisuals(passSpot);
        if (result.DontPassResolved) ClearChipVisuals(dontPassSpot);
        // Field is always one-roll — clear it regardless of outcome
        ClearChipVisuals(fieldSpot);
        // Props are always one-roll — clear each after the roll
        foreach (var t in PropTypes) { if (propSpots.TryGetValue(t, out var s)) ClearChipVisuals(s); }

        // Sync hardway visuals: hardways lose on 7 or easy-way mid-round (bet goes to 0)
        foreach (var kv in hardSpots)
        {
            if (currentRound.GetBet(NumberToHardType(kv.Key)) == 0 && kv.Value.ChipVisuals.Count > 0)
                ClearChipVisuals(kv.Value);
        }

        // Place bets lost to a working-7 (come-out natural or seven-out with BETS ON)
        foreach (var kv in result.PlaceLosses)
            if (placeSpots.TryGetValue(kv.Key, out var ps)) ClearChipVisuals(ps);

        // Lay bets: clear visuals on loss (number hit) — on 7-win, stake stays, chips stay
        foreach (var kv in result.LayLosses)
            if (laySpots.TryGetValue(kv.Key, out var ls)) ClearChipVisuals(ls);

        // Come wagers: bankroll already credited via result.TotalReturned at top of method
        foreach (var kv in result.ComeReturns)
        {
            if (comeChips.TryGetValue(kv.Key, out var chip)) { Destroy(chip); comeChips.Remove(kv.Key); }
        }
        foreach (var w in result.ComeCrapsOut)
        {
            if (comeChips.TryGetValue(w, out var chip)) { Destroy(chip); comeChips.Remove(w); }
        }
        foreach (var w in result.ComeParked)
        {
            // Destroy bar chip, place a new one on the number cell where it parked
            if (comeChips.TryGetValue(w, out var chip)) { Destroy(chip); comeChips.Remove(w); }
            if (w.Point.HasValue && placeSpots.TryGetValue(w.Point.Value, out var ps))
                comeChips[w] = SpawnWagerChip(ps.Root.transform, w.Amount, new Vector2(-30f, 0f));
            // Prompt for Come odds (same as Pass Line odds prompt on point establishment)
            if (w.Point.HasValue)
                OpenComeOddsModal(w);
        }

        // Pushed DC stakes are already in result.TotalReturned (deposited at top of method)
        foreach (var w in result.DontComePushed)
        {
            if (dcChips.TryGetValue(w, out var chip)) { Destroy(chip); dcChips.Remove(w); }
        }
        foreach (var kv in result.DontComeReturns)
        {
            if (dcChips.TryGetValue(kv.Key, out var chip)) { Destroy(chip); dcChips.Remove(kv.Key); }
        }
        foreach (var w in result.DontComeParked)
        {
            if (dcChips.TryGetValue(w, out var chip)) { Destroy(chip); dcChips.Remove(w); }
            if (w.Point.HasValue && placeSpots.TryGetValue(w.Point.Value, out var ps))
                dcChips[w] = SpawnWagerChip(ps.Root.transform, w.Amount, new Vector2(-30f, -20f));
            if (w.Point.HasValue)
                OpenDontComeOddsModal(w);
        }

        // Reconcile: destroy chips for any wager silently removed by the core
        // (e.g. parked Come lost on seven-out, parked DC lost when its number hit)
        var activeCome = new HashSet<NormalComeWager>(currentRound.ComeWagers);
        foreach (var key in comeChips.Keys.ToList())
            if (!activeCome.Contains(key)) { Destroy(comeChips[key]); comeChips.Remove(key); }
        var activeDc = new HashSet<NormalDontComeWager>(currentRound.DontComeWagers);
        foreach (var key in dcChips.Keys.ToList())
            if (!activeDc.Contains(key)) { Destroy(dcChips[key]); dcChips.Remove(key); }

        RefreshComeBarText();

        if (result.RoundOver)
        {
            // Places/hardways are lost only when working; Lay bets win on 7 and always stay on the table
            foreach (var kv in placeSpots)
                if (currentRound.GetBet(NumberToPlaceType(kv.Key)) == 0) ClearChipVisuals(kv.Value);
            foreach (var kv in hardSpots)
                if (currentRound.GetBet(NumberToHardType(kv.Key)) == 0) ClearChipVisuals(kv.Value);
        }

        // ATS chip visuals — clear on seven-out (bets lost) or on win (bet consumed by core)
        if (result.AtsSevenOut)
        {
            if (atsLowsSpot  != null) ClearChipVisuals(atsLowsSpot);
            if (atsHighsSpot != null) ClearChipVisuals(atsHighsSpot);
            if (atsAllSpot   != null) ClearChipVisuals(atsAllSpot);
        }
        else
        {
            if (result.AtsLowsReturn  > 0 && atsLowsSpot  != null) ClearChipVisuals(atsLowsSpot);
            if (result.AtsHighsReturn > 0 && atsHighsSpot != null) ClearChipVisuals(atsHighsSpot);
            if (result.AtsAllReturn   > 0 && atsAllSpot   != null) ClearChipVisuals(atsAllSpot);
        }
        RefreshAtsDots();
    }

    void RefreshPhaseVisuals()
    {
        bool hasPoint = currentRound.Phase == NormalCrapsPhase.Point;
        pointPuck.SetActive(hasPoint);
        if (hasPoint && currentRound.Point.HasValue)
        {
            int pt = currentRound.Point.Value;
            if (numberCellRoots.TryGetValue(pt, out var cellRt))
            {
                var puckRt = pointPuck.GetComponent<RectTransform>();
                puckRt.anchoredPosition = cellRt.anchoredPosition + new Vector2(35, 50);
            }
        }
        RefreshActionButtons();
    }

    // --- Bets ON/OFF toggle (Place bets working toggle) ---

    void BuildActionButtons()
    {
        clearBaseColor  = new Color(0.55f, 0.18f, 0.18f);
        repeatBaseColor = new Color(0.18f, 0.42f, 0.22f);
        rollBaseColor   = UIFactory.Positive;

        // Horizontal row — buttons at panel bottom; BETS ON/OFF is the 7th number cell slot
        const float btnY = -380f;
        undoButton      = UIFactory.MakeButton(tableRoot, "UndoBtn",      new Vector2(-370f, btnY), new Vector2(120, 48), "UNDO",
            UIFactory.AccentDim, OnUndoClicked, 12, pixelFont: true);
        clearBetButton  = UIFactory.MakeButton(tableRoot, "ClearBetBtn",  new Vector2(-230f, btnY), new Vector2(120, 48), "CLEAR",
            clearBaseColor, OnClearBetClicked, 12, pixelFont: true);
        rollButton      = UIFactory.MakeButton(tableRoot, "RollBtn",      new Vector2( -50f, btnY), new Vector2(184, 56), "ROLL",
            rollBaseColor, OnRollClicked, 20, pixelFont: true);
        repeatBetButton = UIFactory.MakeButton(tableRoot, "RepeatBetBtn", new Vector2(+140f, btnY), new Vector2(138, 48), "REPEAT BET",
            repeatBaseColor, OnRepeatBetClicked, 13, pixelFont: true);

        // Take-down tip — bottom-right corner, own frame, bright text so it reads against the dark panel
        var tipBg = UIFactory.MakePanel(tableRoot, "TakeDownTipBg", new Vector2(+395f, btnY), new Vector2(250f, 52f),
            UIFactory.PanelDarker, shadow: false);
        UIFactory.AddSharpFrame(tipBg, SectionHeaderColor, square: true);
        UIFactory.MakeText(tableRoot, "TakeDownTip", new Vector2(+395f, btnY), 13, TextAnchor.MiddleCenter,
            new Vector2(236f, 46f), UIFactory.TextLight, FontStyle.Bold).text = "RIGHT-CLICK A BET\nTO TAKE IT DOWN";
    }

    void OnBetsToggleClicked()
    {
        currentRound.PlaceBetsWorking = !currentRound.PlaceBetsWorking;
        soundManager?.PlayClick();
        RefreshActionButtons();
    }

    void RefreshBetsToggle()
    {
        bool working = currentRound.PlaceBetsWorking;
        betsToggleButton.interactable = !rolling;
        betsToggleButton.GetComponent<Image>().color = working ? BetsOnColor : UIFactory.AccentDim;
        if (betsToggleLabel != null) betsToggleLabel.text = working ? "BETS ON" : "BETS OFF";
    }

    void RefreshActionButtons()
    {
        RefreshBetsToggle();
        bool hasClearable = currentRound.GetBet(NormalCrapsBetType.Field) > 0
            || PropTypes.Any(t => currentRound.GetBet(t) > 0)
            || PlaceNumbers.Any(n => currentRound.GetBet(NumberToPlaceType(n)) > 0)
            || PlaceNumbers.Any(n => currentRound.GetBet(NumberToLayType(n)) > 0)
            || HardNumbers.Any(n => currentRound.GetBet(NumberToHardType(n)) > 0)
            || (currentRound.Phase == NormalCrapsPhase.ComeOut
                && (currentRound.GetBet(NormalCrapsBetType.PassLine) > 0
                    || currentRound.GetBet(NormalCrapsBetType.DontPass) > 0));
        UIFactory.SetButtonState(clearBetButton, clearBaseColor, !rolling && hasClearable);
    }

    void BuildStreakBadge()
    {
        streakBadgeGO = new GameObject("StreakBadge");
        streakBadgeGO.transform.SetParent(tableRoot, false);
        var rt = streakBadgeGO.AddComponent<RectTransform>();
        rt.sizeDelta = new Vector2(300, 90);
        rt.anchoredPosition = new Vector2(-520, 465);
        UIFactory.MakeFramedPanel(streakBadgeGO.transform, "StreakBg", Vector2.zero, new Vector2(300, 90), Color.black);
        var textGO = new GameObject("StreakText");
        textGO.transform.SetParent(streakBadgeGO.transform, false);
        textGO.AddComponent<RectTransform>().sizeDelta = new Vector2(280, 70);
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

    void BuildOddsModal(Transform canvas)
    {
        oddsModalRoot = new GameObject("NCOddsModal");
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

        var panel = UIFactory.MakeFramedPanel(oddsModalRoot.transform, "NCOddsModalPanel", Vector2.zero, new Vector2(660, 460), Color.black);

        oddsModalTitleText = UIFactory.MakeText(panel.transform, "OddsModalTitle", new Vector2(0, 185), 26,
            sizeDelta: new Vector2(620, 36), color: UIFactory.TextLight, style: FontStyle.Bold);
        oddsModalOddsText = UIFactory.MakeText(panel.transform, "OddsModalOdds", new Vector2(0, 130), 18,
            sizeDelta: new Vector2(620, 62), color: UIFactory.Accent);
        oddsModalAmountText = UIFactory.MakeText(panel.transform, "OddsModalAmount", new Vector2(0, 58), 36,
            sizeDelta: new Vector2(620, 50), color: UIFactory.TextLight, style: FontStyle.Bold);
        oddsModalCapText = UIFactory.MakeText(panel.transform, "OddsModalCap", new Vector2(0, 20), 17,
            sizeDelta: new Vector2(620, 26), color: UIFactory.Accent);

        // 6 buttons: 1x–6x. DontPassOdds always uses all 6 (max 6x). PassOdds shows only up to MaxOddsMultiplier(point).
        multiplierButtons = new Button[6];
        multiplierLabels = new Text[6];
        float[] btnX = { -262f, -157f, -52f, 52f, 157f, 262f };
        for (int i = 0; i < 6; i++)
        {
            var go = new GameObject($"NCOddsMultBtn_{i + 1}x");
            go.transform.SetParent(panel.transform, false);
            var btnRt = go.AddComponent<RectTransform>();
            btnRt.sizeDelta = new Vector2(96, 66);
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
                TextAnchor.MiddleCenter, new Vector2(92, 62), UIFactory.TextLight);
        }

        UIFactory.MakeButton(panel.transform, "NCOddsConfirm", new Vector2(-115, -155), new Vector2(220, 58),
            "CONFIRM ODDS", UIFactory.Positive, OnOddsModalConfirm, 16, pixelFont: true);
        UIFactory.MakeButton(panel.transform, "NCOddsSkip", new Vector2(115, -155), new Vector2(220, 58),
            "SKIP ODDS", UIFactory.AccentDim, HideOddsModal, 16, pixelFont: true);

        oddsModalRoot.SetActive(false);
    }

    // Dealer check before each come-out: Place/Lay bets are the player's own, separate from the shooter's line bets
    void BuildShooterPrompt(Transform canvas)
    {
        shooterPromptRoot = new GameObject("NCShooterPrompt");
        shooterPromptRoot.transform.SetParent(canvas, false);
        var rt = shooterPromptRoot.AddComponent<RectTransform>();
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = Vector2.zero;
        rt.offsetMax = Vector2.zero;

        var scrimGO = new GameObject("Scrim");
        scrimGO.transform.SetParent(shooterPromptRoot.transform, false);
        var scrimRt = scrimGO.AddComponent<RectTransform>();
        scrimRt.anchorMin = Vector2.zero;
        scrimRt.anchorMax = Vector2.one;
        scrimRt.offsetMin = Vector2.zero;
        scrimRt.offsetMax = Vector2.zero;
        scrimGO.AddComponent<Image>().color = new Color(0.02f, 0.02f, 0.03f, 0.85f);

        var panel = UIFactory.MakeFramedPanel(shooterPromptRoot.transform, "NCShooterPromptPanel", Vector2.zero, new Vector2(640, 340), Color.black);

        shooterPromptTitleText = UIFactory.MakeText(panel.transform, "ShooterPromptTitle", new Vector2(0, 120), 26,
            sizeDelta: new Vector2(600, 36), color: UIFactory.TextLight, style: FontStyle.Bold);
        UIFactory.MakeText(panel.transform, "ShooterPromptQuestion", new Vector2(0, 62), 22,
            sizeDelta: new Vector2(600, 34), color: UIFactory.Accent).text = "Dealer: \"Do you want to turn off your bets?\"";
        shooterPromptBetsText = UIFactory.MakeText(panel.transform, "ShooterPromptBets", new Vector2(0, 5), 16,
            sizeDelta: new Vector2(600, 56), color: UIFactory.TextDim);

        UIFactory.MakeButton(panel.transform, "NCShooterTurnOff", new Vector2(-125, -100), new Vector2(220, 58),
            "TURN OFF", UIFactory.AccentDim, () => AnswerShooterPrompt(false), 16, pixelFont: true);
        UIFactory.MakeButton(panel.transform, "NCShooterKeepOn", new Vector2(125, -100), new Vector2(220, 58),
            "KEEP ON", UIFactory.Positive, () => AnswerShooterPrompt(true), 16, pixelFont: true);

        shooterPromptRoot.SetActive(false);
    }

    bool TryOpenShooterPrompt(string title)
    {
        var parts = new List<string>();
        foreach (int n in PlaceNumbers)
        {
            long p = currentRound.GetBet(NumberToPlaceType(n));
            if (p > 0) parts.Add($"Place {n} {UIFactory.FormatMoney(p)}");
        }
        foreach (int n in PlaceNumbers)
        {
            long l = currentRound.GetBet(NumberToLayType(n));
            if (l > 0) parts.Add($"Lay {n} {UIFactory.FormatMoney(l)}");
        }
        foreach (int n in HardNumbers)
        {
            long h = currentRound.GetBet(NumberToHardType(n));
            if (h > 0) parts.Add($"Hard {n} {UIFactory.FormatMoney(h)}");
        }
        if (parts.Count == 0) return false;

        shooterPromptTitleText.text = title;
        shooterPromptBetsText.text = "Your bets on the table:\n" + string.Join("  ·  ", parts);
        shooterPromptRoot.transform.SetAsLastSibling();
        shooterPromptRoot.SetActive(true);
        return true;
    }

    void AnswerShooterPrompt(bool keepWorking)
    {
        currentRound.PlaceBetsWorking = keepWorking;
        shooterPromptRoot.SetActive(false);
        soundManager?.PlayClick();
        RefreshActionButtons();
    }

    void OpenComeOddsModal(NormalComeWager w)
    {
        int point = w.Point.Value;
        OpenOddsModal(NormalCrapsBetType.PassOdds, $"COME ODDS — {point}", point, w.Amount, isDontSide: false);
        oddsModalComeWager = w; // set after OpenOddsModal clears it
    }

    void OpenDontComeOddsModal(NormalDontComeWager w)
    {
        int point = w.Point.Value;
        OpenOddsModal(NormalCrapsBetType.DontPassOdds, $"DON'T COME LAY ODDS — {point}", point, w.Amount, isDontSide: true);
        oddsModalDcWager = w; // set after OpenOddsModal clears it
    }

    void OpenOddsModal(NormalCrapsBetType betType, string title, int point, long baseAmount, bool isDontSide)
    {
        oddsModalComeWager = null; // clear Come / Don't Come context when opening for Pass/DontPass
        oddsModalDcWager = null;
        oddsModalBetType = betType;
        oddsModalPoint = point;
        oddsModalIsDontSide = isDontSide;
        oddsModalBaseAmount = baseAmount;
        int maxMult = isDontSide
            ? (int)NormalCrapsResolver.MaxLayOddsMultiplier(point)
            : NormalCrapsResolver.MaxOddsMultiplier(point);
        oddsModalCap = baseAmount * maxMult;
        oddsModalPendingAmount = baseAmount;

        oddsModalTitleText.text = title;
        oddsModalCapText.text = $"Max: {maxMult}x odds  =  {UIFactory.FormatMoney(oddsModalCap)}";

        for (int i = 0; i < 6; i++)
        {
            int mult = i + 1;
            bool visible = mult <= maxMult;
            multiplierButtons[i].gameObject.SetActive(visible);
            if (!visible) continue;
            long amount = baseAmount * mult;
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
        for (int i = 0; i < 6; i++)
        {
            if (!multiplierButtons[i].gameObject.activeSelf) continue;
            long amount = oddsModalBaseAmount * (i + 1);
            bool selected = amount == oddsModalPendingAmount;
            multiplierButtons[i].GetComponent<Image>().color = selected ? UIFactory.Accent : UIFactory.AccentDim;
            multiplierLabels[i].color = selected ? new Color(0.08f, 0.06f, 0f) : UIFactory.TextLight;
        }
    }

    void RefreshOddsModalPreview()
    {
        var (num, den) = NormalCrapsResolver.TrueOdds(oddsModalPoint);
        string ratioText = oddsModalIsDontSide ? $"Lay odds {den}:{num}" : $"True odds {num}:{den}";
        long payout = oddsModalIsDontSide
            ? NormalCrapsResolver.LayOddsPayout(oddsModalPendingAmount, oddsModalPoint)
            : NormalCrapsResolver.OddsPayout(oddsModalPendingAmount, oddsModalPoint);
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
            oddsModalComeWager = null;
        }
        else if (oddsModalDcWager != null)
        {
            currentRound.AddDontComeLayOdds(oddsModalDcWager, amount);
            oddsModalDcWager = null;
        }
        else
        {
            currentRound.PlaceBet(oddsModalBetType, amount);
            PushUndoBet(oddsModalBetType, amount);
        }
        roundTotalStaked += amount;

        soundManager?.PlayChip();
        HideOddsModal();
        RebuildAllChipVisuals();
        RefreshPassDontPassOddsText();
        RefreshActionButtons();
    }

    void RefreshPassDontPassOddsText()
    {
        long passBase = currentRound.GetBet(NormalCrapsBetType.PassLine);
        long passOdds = currentRound.GetBet(NormalCrapsBetType.PassOdds);
        passSpot.AmountText.text = passBase > 0 ? UIFactory.FormatMoney(passBase) : passSpot.DefaultLabel;
        if (passSpot.OddsText != null) passSpot.OddsText.text = passOdds > 0 ? $"+{UIFactory.FormatMoney(passOdds)} odds" : "";

        long dpBase = currentRound.GetBet(NormalCrapsBetType.DontPass);
        long dpOdds = currentRound.GetBet(NormalCrapsBetType.DontPassOdds);
        dontPassSpot.AmountText.text = dpBase > 0 ? UIFactory.FormatMoney(dpBase) : dontPassSpot.DefaultLabel;
        if (dontPassSpot.OddsText != null) dontPassSpot.OddsText.text = dpOdds > 0 ? $"+{UIFactory.FormatMoney(dpOdds)} odds" : "";
    }

    void HideOddsModal() => oddsModalRoot.SetActive(false);

    public void SetRoundIndex(int index) => roundIndex = index;
    public void SetRollLogIndex(int index) => rollLogIndex = index;

    public void ResetRound()
    {
        rolling = false;
        winStreak = 0;
        bestRoundNet = 0;
        doubledMilestoneFired = false;
        roundMilestonesFired.Clear();
        rollCount = 0;
        roundTotalStaked = 0;
        roundTotalReturned = 0;
        streakBadgeGO?.SetActive(false);
        pointPuck.SetActive(false);
        currentRound = new NormalCrapsRound(rng);
        shooterPromptRoot.SetActive(false);
        ClearChipVisuals(passSpot);
        ClearChipVisuals(dontPassSpot);
        ClearChipVisuals(fieldSpot);
        foreach (var kv in placeSpots) ClearChipVisuals(kv.Value);
        foreach (var kv in laySpots)   ClearChipVisuals(kv.Value);
        foreach (var kv in hardSpots)  ClearChipVisuals(kv.Value);
        foreach (var kv in propSpots)  ClearChipVisuals(kv.Value);
        foreach (var kv in comeChips)  Destroy(kv.Value);
        comeChips.Clear();
        foreach (var kv in dcChips)    Destroy(kv.Value);
        dcChips.Clear();
        if (comeAmtText != null) comeAmtText.text = "1:1";
        if (dcAmtText   != null) dcAmtText.text   = "1:1";
        if (atsLowsSpot  != null) ClearChipVisuals(atsLowsSpot);
        if (atsHighsSpot != null) ClearChipVisuals(atsHighsSpot);
        if (atsAllSpot   != null) ClearChipVisuals(atsAllSpot);
        RefreshAtsDots();
        statusText.color = UIFactory.Accent;
        statusText.text = "Place bets — come-out roll";
        rollButton.interactable = true;
        RefreshActionButtons();
    }
}
