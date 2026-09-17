using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Febucci.UI;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

// Crapless Craps betting surface — same layout, flow and visuals as Normal Craps.
// Crapless rules (see CrapsRound): every total except 7 is a point, nothing craps out,
// Place bets on 2-12, no Don't Pass / Don't Come / Lay.
// Bets withdraw from bankroll on placement; ROLL resolves all active bets.
public class CrapsBettingUIController : MonoBehaviour
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
    Action<CrapsRoundRecord> onRoundResolved;
    Action onBankrollChanged;
    Action<string, Color> onRollResolved;
    Action<CrapsRoundRecord> onRollLogged;

    CrapsRound currentRound;
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
    readonly Dictionary<CrapsBetType, long> lastRollBets = new Dictionary<CrapsBetType, long>();
    long onTableAtRoll;
    CrapsRollResult pendingResult;
    readonly List<Action> undoStack = new List<Action>();
    const int MaxUndoDepth = 10;
    bool rolling;

    Button clearBetButton, repeatBetButton, undoButton;
    Color clearBaseColor, repeatBaseColor;

    Dice3D die1UI, die2UI;
    Dice3D shadowDie1, shadowDie2;
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
        public Text OddsText;   // optional — only passSpot
        public string DefaultLabel = "";
        public readonly List<GameObject> ChipVisuals = new List<GameObject>();
        public readonly List<GameObject> OddsChipVisuals = new List<GameObject>();
    }

    FlatSpot passSpot, fieldSpot;

    // Come bar
    Transform comeBgT;
    Text comeAmtText;
    readonly Dictionary<ComeWager, GameObject> comeChips = new Dictionary<ComeWager, GameObject>();

    // ATS / Lucky Roller
    FlatSpot atsLowsSpot, atsHighsSpot, atsAllSpot;
    readonly Dictionary<int, Image> atsDotImages = new Dictionary<int, Image>();
    readonly Dictionary<int, FlatSpot>   placeSpots   = new Dictionary<int, FlatSpot>();
    readonly Dictionary<int, RectTransform> numberCellRoots = new Dictionary<int, RectTransform>();
    readonly Dictionary<int, FlatSpot>   hardSpots    = new Dictionary<int, FlatSpot>();
    readonly Dictionary<CrapsBetType, FlatSpot> propSpots = new Dictionary<CrapsBetType, FlatSpot>();

    static readonly CrapsBetType[] PropTypes = { CrapsBetType.AnyCraps, CrapsBetType.AnySeven, CrapsBetType.AnyEleven, CrapsBetType.Horn, CrapsBetType.CAndE };
    static readonly int[] PlaceNumbers = { 2, 3, 4, 5, 6, 8, 9, 10, 11, 12 };
    static readonly int[] HardNumbers  = { 4, 6, 8, 10 };
    static readonly Color PassColor     = new Color(0.05f, 0.30f, 0.10f);
    static readonly Color FieldColor    = new Color(0.25f, 0.20f, 0.05f);
    static readonly Color PlaceColor    = new Color(0.10f, 0.15f, 0.35f);
    static readonly Color HardColor     = new Color(0.30f, 0.10f, 0.30f);
    static readonly Color PropColor     = new Color(0.25f, 0.25f, 0.05f);
    static readonly Color PointColor    = new Color(1f, 0.85f, 0.2f);
    // Pass-Line family green, dark enough for the white BETS ON text to read clearly
    static readonly Color BetsOnColor   = new Color(0.12f, 0.42f, 0.18f);

    // Chip stack offsets chosen to sit beside each spot's label instead of on top of it
    const float LineChipX     = -130f;
    const float LineOddsChipX = -70f;
    const float AtsChipX      = 84f;
    const float FieldChipX    = -45f;
    const float PlaceChipY    = -36f;

    static readonly Color[] ChipColors = { new Color(0.65f, 0.12f, 0.12f), new Color(0.1f, 0.35f, 0.6f), UIFactory.Chip500White };

    GameObject shooterPromptRoot;
    Text shooterPromptTitleText, shooterPromptBetsText;

    GameObject oddsModalRoot;
    Text oddsModalTitleText, oddsModalOddsText, oddsModalAmountText, oddsModalCapText;
    Button[] multiplierButtons;
    Text[] multiplierLabels;
    CrapsBetType oddsModalBetType;
    int oddsModalPoint;
    long oddsModalCap;
    long oddsModalBaseAmount;
    long oddsModalPendingAmount;
    ComeWager oddsModalComeWager;

    GameObject pointPuck;

    public void Build(Transform canvas, Bankroll bankroll, ChipSelectorUI chipSelector, IRandomSource rng,
        SoundManager soundManager, JuiceManager juiceManager, FloatingTextUI floatingText,
        FloatingTextUI milestoneToast, Dice3D die1, Dice3D die2, Dice3D shadowDie1, Dice3D shadowDie2,
        Action<CrapsRoundRecord> onRoundResolved, Action onBankrollChanged,
        Action<string, Color> onRollResolved,
        Action<CrapsRoundRecord> onRollLogged)
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

        currentRound = new CrapsRound(rng);
        currentRound.PlaceBetsWorking = false;

        var rootGO = new GameObject("CCrapsUIRoot");
        rootGO.transform.SetParent(canvas, false);
        var rootRt = rootGO.AddComponent<RectTransform>();
        rootRt.anchorMin = rootRt.anchorMax = rootRt.pivot = new Vector2(0.5f, 0.5f);
        rootRt.anchoredPosition = Vector2.zero;
        tableRoot = rootGO.transform;

        // Panel tall enough to contain buttons inside
        UIFactory.MakePanel(tableRoot, "FeltBg", new Vector2(0, -30), new Vector2(1100, 800), UIFactory.PanelDark);
        UIFactory.MakeHeroTitle(tableRoot, "Header", new Vector2(0, 335), "CRAPLESS CRAPS", 26);

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
        // ── Layout constants (panel: centre y=-30, height=800, x range -550..+550) ──
        // Left felt is wider than Normal Craps' to fit ten Place numbers plus the BETS toggle.
        const float leftCx  = -135f;
        const float feltW   = 790f;
        const float rightCx = +405f;
        const float rightPW = 250f;

        // Right props panel
        var propBg = UIFactory.MakePanel(tableRoot, "PropsBg",
            new Vector2(rightCx, -20f), new Vector2(rightPW, 540f),
            new Color(0.04f, 0.04f, 0.07f, 0.95f), shadow: false);
        UIFactory.AddSharpFrame(propBg, UIFactory.AccentDim, square: true);

        // ── ATS / LUCKY ROLLER PANEL (top of left felt) ──────────────────
        BuildAtsPanel(leftCx, feltW);

        // ── PLACE CELLS 2-12 ─────────────────────────────────────────────
        MakeSectionLabel("PlaceHdr", new Vector2(leftCx, 105f), feltW, "PLACE");

        const float cellSpacing = 72f;
        const float placeY      = 20f;
        float placeStartX = leftCx - feltW / 2f + cellSpacing / 2f - 1f;
        for (int i = 0; i < PlaceNumbers.Length; i++)
        {
            int n = PlaceNumbers[i];
            float cx = placeStartX + i * cellSpacing;
            BuildPlaceCell(n, new Vector2(cx, placeY));
        }

        // 11th slot: BETS ON/OFF toggle — moves with the PLACE row
        float betsX = placeStartX + PlaceNumbers.Length * cellSpacing;
        betsToggleButton = UIFactory.MakeButton(tableRoot, "BetsToggleBtn",
            new Vector2(betsX, placeY), new Vector2(70f, 140f),
            "BETS\nOFF", UIFactory.AccentDim, OnBetsToggleClicked, 11, pixelFont: true);
        betsToggleLabel = betsToggleButton.GetComponentInChildren<TextMeshProUGUI>();
        if (betsToggleLabel != null) betsToggleLabel.raycastTarget = false;

        // ── FIELD BAR ────────────────────────────────────────────────────
        fieldSpot = BuildFieldBar(new Vector2(leftCx, -92f), new Vector2(feltW, 80f));

        // ── COME BAR (full width — crapless has no Don't Come) ───────────
        const float cdY = -167f, cdH = 55f;
        var comeGO = new GameObject("ComeBg");
        comeGO.transform.SetParent(tableRoot, false);
        var comeRt = comeGO.AddComponent<RectTransform>();
        comeRt.sizeDelta = new Vector2(feltW, cdH);
        comeRt.anchoredPosition = new Vector2(leftCx, cdY);
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
            TextAnchor.MiddleCenter, new Vector2(feltW - 8f, 22f), UIFactory.TextLight, FontStyle.Bold).text = "COME";
        comeAmtText = UIFactory.MakeText(comeGO.transform, "ComeAmt", new Vector2(0, -9f), 11,
            TextAnchor.MiddleCenter, new Vector2(feltW - 8f, 18f), UIFactory.TextDim);
        comeAmtText.text = "1:1";
        comeBgT = comeGO.transform;

        // ── PASS LINE (full width) ───────────────────────────────────────
        const float pdY = -255f, pdH = 72f;
        passSpot = BuildFlatBar(CrapsBetType.PassLine,
            new Vector2(leftCx, pdY), new Vector2(feltW, pdH),
            "PASS LINE", "1:1", PassColor, OnPassLineClicked);
        passSpot.OddsText = UIFactory.MakeText(passSpot.Root.transform, "PassOddsAmt",
            new Vector2(feltW * 0.25f, -9f), 11, TextAnchor.MiddleRight,
            new Vector2(feltW * 0.45f, 18f), UIFactory.Accent);
        passSpot.OddsText.text = "";

        // ── RIGHT PANEL: HARDWAYS ────────────────────────────────────────
        MakeSectionLabel("HardHdr", new Vector2(rightCx, 232f), rightPW - 10f, "HARDWAYS");

        float hcW = 110f, hcH = 78f;
        float hColA = rightCx - 58f, hColB = rightCx + 58f;
        hardSpots[4]  = BuildHardSpot(4,  new Vector2(hColA, 170f), hcW, hcH, "7:1");
        hardSpots[6]  = BuildHardSpot(6,  new Vector2(hColB, 170f), hcW, hcH, "9:1");
        hardSpots[8]  = BuildHardSpot(8,  new Vector2(hColA,  80f), hcW, hcH, "9:1");
        hardSpots[10] = BuildHardSpot(10, new Vector2(hColB,  80f), hcW, hcH, "7:1");

        MakeSectionLabel("OneRollLbl", new Vector2(rightCx, 22f), rightPW - 10f, "ONE ROLL BETS");

        // ── RIGHT PANEL: SEVEN + CRAPS ────────────────────────────────────
        float pcW = 110f, pcH = 70f;
        propSpots[CrapsBetType.AnySeven]  = BuildPropSpot(CrapsBetType.AnySeven,
            new Vector2(hColA, -32f), pcW, pcH, "SEVEN",    "4:1");
        propSpots[CrapsBetType.AnyCraps]  = BuildPropSpot(CrapsBetType.AnyCraps,
            new Vector2(hColB, -32f), pcW, pcH, "ANY\nCRAPS", "7:1");

        // ── RIGHT PANEL: ELEVEN + HORN ────────────────────────────────────
        propSpots[CrapsBetType.AnyEleven] = BuildPropSpot(CrapsBetType.AnyEleven,
            new Vector2(hColA, -120f), pcW, pcH, "ELEVEN",  "15:1");
        propSpots[CrapsBetType.Horn]      = BuildPropSpot(CrapsBetType.Horn,
            new Vector2(hColB, -120f), pcW, pcH, "HORN",    "30:1/15:1");
        propSpots[CrapsBetType.CAndE]     = BuildPropSpot(CrapsBetType.CAndE,
            new Vector2(rightCx, -200f), pcW * 2f + 6f, 56f, "C & E", "3:1 / 7:1");

        // ── POINT PUCK ───────────────────────────────────────────────────
        pointPuck = new GameObject("PointPuck");
        pointPuck.transform.SetParent(tableRoot, false);
        var puckRt = pointPuck.AddComponent<RectTransform>();
        puckRt.sizeDelta = new Vector2(34, 34);
        var puckImg = pointPuck.AddComponent<Image>();
        puckImg.sprite = UIFactory.Circle();
        puckImg.color = PointColor;
        UIFactory.MakeText(pointPuck.transform, "ON", Vector2.zero, 11, TextAnchor.MiddleCenter,
            new Vector2(32, 32), Color.black, FontStyle.Bold).text = "ON";
        pointPuck.SetActive(false);
    }

    // One style for every bet-section header so the table's areas read the same way
    static readonly Color SectionHeaderColor = new Color(1f, 0.85f, 0.1f);

    void MakeSectionLabel(string name, Vector2 pos, float width, string text) =>
        UIFactory.MakeText(tableRoot, name, pos, 13, TextAnchor.MiddleCenter,
            new Vector2(width, 18f), SectionHeaderColor, FontStyle.Bold).text = text;

    void BuildAtsPanel(float leftCx, float feltW)
    {
        // Panel sits between status bar (bottom≈266) and PLACE row (top≈114)
        // Center y=188, height=140 → top=258, bottom=118
        const float panelY = 188f, panelH = 140f;
        var bg = UIFactory.MakePanel(tableRoot, "AtsBg", new Vector2(leftCx, panelY),
            new Vector2(feltW, panelH), new Color(0.04f, 0.06f, 0.12f, 0.92f), shadow: false);
        UIFactory.AddSharpFrame(bg, new Color(0.8f, 0.7f, 0.1f, 0.7f), square: true);

        // Title sits well inside the frame (panel top = 258) so the frame line doesn't clip it
        MakeSectionLabel("AtsTitle", new Vector2(leftCx, 241f), feltW - 20f, "LUCKY ROLLER");

        // Three bet buttons: LOWS | ROLL ALL | HIGHS
        const float btnW = 220f, btnH = 52f, btnGap = 10f;
        float btnRowY = 202f;
        float b0x = leftCx - btnW - btnGap;
        float b1x = leftCx;
        float b2x = leftCx + btnW + btnGap;

        atsLowsSpot  = BuildFlatBar(CrapsBetType.AtsLows,  new Vector2(b0x, btnRowY),
            new Vector2(btnW, btnH), "LOWS  (2-3-4-5-6)", "30:1",
            new Color(0.1f, 0.4f, 0.85f), OnAtsLowsClicked);
        atsAllSpot   = BuildFlatBar(CrapsBetType.AtsAll,   new Vector2(b1x, btnRowY),
            new Vector2(btnW, btnH), "ROLL ALL", "155:1",
            new Color(0.7f, 0.55f, 0.05f), OnAtsAllClicked);
        atsHighsSpot = BuildFlatBar(CrapsBetType.AtsHighs, new Vector2(b2x, btnRowY),
            new Vector2(btnW, btnH), "HIGHS (8-9-10-11-12)", "30:1",
            new Color(0.7f, 0.15f, 0.15f), OnAtsHighsClicked);

        // Number tracker dots: 2 3 4 5 6 · [7] · 8 9 10 11 12
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
        if (!TryPlaceBet(CrapsBetType.AtsLows, chip, () => RebuildFlatChips(atsLowsSpot, currentRound.GetBet(CrapsBetType.AtsLows), AtsChipX))) return;
        PushUndoBet(CrapsBetType.AtsLows, chip);
    }

    void OnAtsHighsClicked()
    {
        if (AtsBlocked()) return;
        long chip = chipSelector.SelectedChip;
        if (!TryPlaceBet(CrapsBetType.AtsHighs, chip, () => RebuildFlatChips(atsHighsSpot, currentRound.GetBet(CrapsBetType.AtsHighs), AtsChipX))) return;
        PushUndoBet(CrapsBetType.AtsHighs, chip);
    }

    void OnAtsAllClicked()
    {
        if (AtsBlocked()) return;
        long chip = chipSelector.SelectedChip;
        if (!TryPlaceBet(CrapsBetType.AtsAll, chip, () => RebuildFlatChips(atsAllSpot, currentRound.GetBet(CrapsBetType.AtsAll), AtsChipX))) return;
        PushUndoBet(CrapsBetType.AtsAll, chip);
    }

    FlatSpot BuildFlatBar(CrapsBetType type, Vector2 pos, Vector2 size, string label, string payout, Color col, Action onClick)
    {
        var go = new GameObject($"CCFlat_{type}");
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
        var go = new GameObject("CCFlat_Field");
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
        RightClickRelay.Attach(go, () => TakeDown(CrapsBetType.Field, "Field"));

        float hw = size.x / 2f;
        var gold = new Color(1f, 0.85f, 0.3f);
        var goldDim = new Color(1f, 0.85f, 0.3f, 0.82f);

        // Left zone: PAYS DOUBLE above, large "2" below
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

        // Right zone: large "12" above, PAYS TRIPLE below
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

    static string PlacePayoutLabel(int n) => n switch
    {
        4 or 10 => "9:5",
        5 or 9  => "7:5",
        6 or 8  => "7:6",
        2 or 12 => "11:2",
        _       => "11:4"
    };

    // One tall cell per number: big number on top, payout (or bet amount) under it, chips in the lower half
    void BuildPlaceCell(int n, Vector2 centerPos)
    {
        const float cellW = 70f, cellH = 140f;

        var go = new GameObject($"CCPlace_{n}");
        go.transform.SetParent(tableRoot, false);
        var rt = go.AddComponent<RectTransform>();
        rt.sizeDelta = new Vector2(cellW, cellH);
        rt.anchoredPosition = centerPos;
        numberCellRoots[n] = rt;
        var img = go.AddComponent<Image>();
        img.sprite = UIFactory.RoundedRect();
        img.type = Image.Type.Sliced;
        img.color = new Color(PlaceColor.r, PlaceColor.g, PlaceColor.b, 0.55f);
        UIFactory.AddSharpFrame(go, new Color(PlaceColor.r + 0.2f, PlaceColor.g + 0.2f, PlaceColor.b + 0.2f, 0.8f), square: true);
        var btn = go.AddComponent<Button>();
        btn.targetGraphic = img;
        btn.onClick.AddListener(() => OnPlaceClicked(n));
        RightClickRelay.Attach(go, () => TakeDown(NumberToPlaceType(n), $"Place {n}"));

        UIFactory.MakeText(go.transform, "Num", new Vector2(0, 38), 26,
            TextAnchor.MiddleCenter, new Vector2(cellW, 34), UIFactory.TextLight, FontStyle.Bold).text = $"{n}";
        UIFactory.MakeText(go.transform, "WinLbl", new Vector2(0, 16), 10,
            TextAnchor.MiddleCenter, new Vector2(cellW - 4f, 14f),
            new Color(0.3f, 0.9f, 0.35f, 0.9f), FontStyle.Bold).text = "WIN";
        string pay = PlacePayoutLabel(n);
        var amtT = UIFactory.MakeText(go.transform, "Amt", new Vector2(0, 1), 12,
            TextAnchor.MiddleCenter, new Vector2(cellW - 4f, 18f), UIFactory.TextDim);
        amtT.text = pay;
        placeSpots[n] = new FlatSpot { Root = go, AmountText = amtT, DefaultLabel = pay };
    }

    FlatSpot BuildHardSpot(int n, Vector2 pos, float w, float h, string payout)
    {
        var go = new GameObject($"CCHard_{n}");
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

    FlatSpot BuildPropSpot(CrapsBetType type, Vector2 pos, float w, float h, string label, string payout)
    {
        var go = new GameObject($"CCProp_{type}");
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

    bool TryPlaceBet(CrapsBetType type, long amount, Action onSuccess)
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
        if (currentRound.Phase == CrapsPhase.Point)
        {
            long passBase = currentRound.GetBet(CrapsBetType.PassLine);
            if (passBase > 0 && currentRound.Point.HasValue)
                OpenOddsModal(CrapsBetType.PassOdds, "PASS LINE ODDS", currentRound.Point.Value, passBase);
            else
            { statusText.text = "Pass Line locked once point is set"; juiceManager?.MicroShake(1f); }
            return;
        }
        long chip = chipSelector.SelectedChip;
        if (!TryPlaceBet(CrapsBetType.PassLine, chip, () => RebuildFlatChips(passSpot, currentRound.GetBet(CrapsBetType.PassLine), LineChipX))) return;
        JuiceTweens.Pulse(this, (RectTransform)passSpot.Root.transform, peakScale: 1.04f, duration: 0.16f);
        PushUndoBet(CrapsBetType.PassLine, chip);
    }

    void OnComeClicked()
    {
        if (rolling) return;
        if (currentRound.Phase != CrapsPhase.Point)
        {
            statusText.color = UIFactory.Accent;
            statusText.text = "Come opens once a point is set";
            FlashBlocked();
            return;
        }
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
        var go = SpawnWagerChip(comeBgT, chip, new Vector2(LineChipX, -6f));
        comeChips[w] = go;
        RefreshComeBarText();
        RefreshActionButtons();
    }

    GameObject SpawnWagerChip(Transform parent, long denomination, Vector2 pos)
    {
        Color fill = denomination >= 500 ? ChipColors[2] : denomination >= 100 ? ChipColors[1] : ChipColors[0];
        var go = new GameObject("WagerChip");
        go.transform.SetParent(parent, false);
        MakeChipImage(go, fill);
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
    }

    void OnFieldClicked()
    {
        long chip = chipSelector.SelectedChip;
        if (!TryPlaceBet(CrapsBetType.Field, chip, () => RebuildFlatChips(fieldSpot, currentRound.GetBet(CrapsBetType.Field), FieldChipX))) return;
        JuiceTweens.Pulse(this, (RectTransform)fieldSpot.Root.transform, peakScale: 1.04f, duration: 0.16f);
        PushUndoBet(CrapsBetType.Field, chip);
    }

    void OnPlaceClicked(int n)
    {
        long chip = chipSelector.SelectedChip;
        var btype = NumberToPlaceType(n);
        if (!TryPlaceBet(btype, chip, () => RebuildPlaceChips(n))) return;
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

    void OnPropClicked(CrapsBetType type)
    {
        long chip = chipSelector.SelectedChip;
        if (!TryPlaceBet(type, chip, () => { if (propSpots.TryGetValue(type, out var s)) RebuildFlatChips(s, currentRound.GetBet(type)); })) return;
        if (propSpots.TryGetValue(type, out var spot)) JuiceTweens.Pulse(this, (RectTransform)spot.Root.transform, peakScale: 1.08f, duration: 0.16f);
        PushUndoBet(type, chip);
    }

    static CrapsBetType NumberToPlaceType(int n) => n switch
    {
        2 => CrapsBetType.Place2, 3 => CrapsBetType.Place3, 4 => CrapsBetType.Place4,
        5 => CrapsBetType.Place5, 6 => CrapsBetType.Place6, 8 => CrapsBetType.Place8,
        9 => CrapsBetType.Place9, 10 => CrapsBetType.Place10, 11 => CrapsBetType.Place11,
        _ => CrapsBetType.Place12
    };

    static CrapsBetType NumberToHardType(int n) => n switch
    {
        4 => CrapsBetType.Hard4, 6 => CrapsBetType.Hard6,
        8 => CrapsBetType.Hard8, _ => CrapsBetType.Hard10
    };

    // --- Chip visuals ---

    void AddChipVisualAt(FlatSpot spot, long denomination, Vector2 pos)
    {
        Color fill = denomination >= 500 ? ChipColors[2] : denomination >= 100 ? ChipColors[1] : ChipColors[0];
        var go = new GameObject("Chip");
        go.transform.SetParent(spot.Root.transform, false);
        MakeChipImage(go, fill);
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

    void FlashBlocked() => juiceManager?.MicroShake(1.2f);

    void RebuildFlatChips(FlatSpot spot, long amt, float chipX = 36f, float chipY = 0f)
    {
        ClearChipVisuals(spot);
        if (amt <= 0) return;
        var denoms = ChipDenominations.Values;
        long remaining = amt;
        var chipDenoms = new List<long>();
        for (int d = denoms.Length - 1; d >= 0 && chipDenoms.Count < 5; d--)
        {
            long denom = denoms[d];
            for (long n = remaining / denom; n > 0 && chipDenoms.Count < 5; n--, remaining -= denom)
                chipDenoms.Add(denom);
        }
        if (chipDenoms.Count == 0) chipDenoms.Add(denoms[0]);
        const float spacing = 10f;
        float startY = chipY - ((chipDenoms.Count - 1) * spacing) / 2f;
        for (int i = 0; i < chipDenoms.Count; i++)
            AddChipVisualAt(spot, chipDenoms[i], new Vector2(chipX, startY + i * spacing));
        spot.AmountText.text = UIFactory.FormatMoney(amt);
    }

    void RebuildPlaceChips(int n)
    {
        if (placeSpots.TryGetValue(n, out var s))
            RebuildFlatChips(s, currentRound.GetBet(NumberToPlaceType(n)), 0f, PlaceChipY);
    }

    void RebuildOddsChips(FlatSpot spot, long oddsAmt)
    {
        if (oddsAmt <= 0) return;
        var denoms = ChipDenominations.Values;
        long remaining = oddsAmt;
        var chipDenoms = new List<long>();
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
        {
            Color fill = chipDenoms[i] >= 500 ? ChipColors[2] : chipDenoms[i] >= 100 ? ChipColors[1] : ChipColors[0];
            var go = new GameObject("OddsChip");
            go.transform.SetParent(spot.Root.transform, false);
            MakeChipImage(go, fill);
            var rt = go.GetComponent<RectTransform>();
            rt.sizeDelta = new Vector2(18, 18);
            rt.anchoredPosition = new Vector2(LineOddsChipX, startY + i * spacing);
            spot.OddsChipVisuals.Add(go);
            JuiceTweens.PopIn(this, rt, overshoot: 1.3f, duration: 0.15f);
        }
    }

    void RebuildAllChipVisuals()
    {
        RebuildFlatChips(passSpot,     currentRound.GetBet(CrapsBetType.PassLine), LineChipX);
        RebuildOddsChips(passSpot,     currentRound.GetBet(CrapsBetType.PassOdds));
        RebuildFlatChips(fieldSpot,    currentRound.GetBet(CrapsBetType.Field), FieldChipX);
        foreach (var n in PlaceNumbers) RebuildPlaceChips(n);
        foreach (var kv in hardSpots)  RebuildFlatChips(kv.Value, currentRound.GetBet(NumberToHardType(kv.Key)));
        foreach (var kv in propSpots)  RebuildFlatChips(kv.Value, currentRound.GetBet(kv.Key));
        RebuildFlatChips(atsLowsSpot,  currentRound.GetBet(CrapsBetType.AtsLows),  AtsChipX);
        RebuildFlatChips(atsHighsSpot, currentRound.GetBet(CrapsBetType.AtsHighs), AtsChipX);
        RebuildFlatChips(atsAllSpot,   currentRound.GetBet(CrapsBetType.AtsAll),   AtsChipX);
        RefreshPassOddsText();
        onBankrollChanged?.Invoke();
    }

    void PushUndoBet(CrapsBetType type, long amount)
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
        void Refund(CrapsBetType t)
        {
            long b = currentRound.GetBet(t);
            if (b > 0) { currentRound.ClearBet(t); refunded += b; }
        }
        Refund(CrapsBetType.Field);
        foreach (var t in PropTypes) Refund(t);
        foreach (int n in PlaceNumbers) Refund(NumberToPlaceType(n));
        foreach (int n in HardNumbers) Refund(NumberToHardType(n));
        if (currentRound.Phase == CrapsPhase.ComeOut) Refund(CrapsBetType.PassLine);
        if (currentRound.CanPlaceAts)
        {
            Refund(CrapsBetType.AtsLows);
            Refund(CrapsBetType.AtsHighs);
            Refund(CrapsBetType.AtsAll);
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
            if (kv.Key == CrapsBetType.PassLine && currentRound.Phase != CrapsPhase.ComeOut) continue;
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

    static bool IsAtsType(CrapsBetType t) =>
        t == CrapsBetType.AtsLows || t == CrapsBetType.AtsHighs || t == CrapsBetType.AtsAll;

    // Right-click take-down: returns a whole spot to the wallet, following Vegas rules on what may come down.
    void TakeDown(CrapsBetType type, string label)
    {
        if (rolling) return;
        if (type == CrapsBetType.PassLine && currentRound.Phase == CrapsPhase.Point)
        {
            type = CrapsBetType.PassOdds;
            label = "Pass Line odds";
        }

        if (IsAtsType(type) && !currentRound.CanPlaceAts)
        {
            statusText.color = UIFactory.Accent;
            statusText.text = "Lucky Roller can't come down mid-run";
            FlashBlocked();
            return;
        }

        long amount = currentRound.GetBet(type);
        if (amount <= 0)
        {
            if (type == CrapsBetType.PassOdds && currentRound.GetBet(CrapsBetType.PassLine) > 0)
            {
                statusText.color = UIFactory.Accent;
                statusText.text = "Pass Line is locked once the point is set";
            }
            FlashBlocked();
            return;
        }
        currentRound.ClearBet(type);
        ReturnToWallet(amount, label);
        RebuildAllChipVisuals();
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

    // Every chip currently on the felt, including odds behind Come wagers.
    public long OnTableTotal()
    {
        long total = Enum.GetValues(typeof(CrapsBetType)).Cast<CrapsBetType>().Sum(t => currentRound.GetBet(t));
        total += currentRound.ComeWagers.Sum(w => w.Amount + w.OddsAmount);
        return total;
    }

    // Leaving the table: pay out a roll still animating, then pick every chip up and return it to the wallet.
    // Pure bankroll/round math only — safe to call from OnDestroy / OnApplicationQuit.
    public void RefundTableBets()
    {
        if (pendingResult != null) { bankroll.Deposit(pendingResult.TotalReturned); pendingResult = null; }
        long onTable = OnTableTotal();
        if (onTable > 0) bankroll.Deposit(onTable);
        currentRound = new CrapsRound(rng);
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
        bool hasBets = OnTableTotal() > 0;
        if (!hasBets) { statusText.text = "Place at least one bet"; juiceManager?.MicroShake(1.2f); return; }

        // Save for REPEAT BET — every flat bet on the felt; odds and Come need a point so they're skipped
        lastRollBets.Clear();
        foreach (CrapsBetType t in Enum.GetValues(typeof(CrapsBetType)))
        {
            if (t == CrapsBetType.PassOdds) continue;
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

    void ApplyRollResult(CrapsRollResult result)
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
            verdict = " — SEVEN OUT";
        else if (result.PointEstablishedThisRoll)
            verdict = $" — POINT: {result.NewPoint}";
        else if (result.PassResolved && result.PassReturn > 0)
            verdict = pointBeforeRoll == 0 ? " — NATURAL!" : " — POINT MADE!";
        else if (pointBeforeRoll != 0 && currentRound.Phase == CrapsPhase.ComeOut)
            verdict = " — POINT MADE";

        // Supplement verdict when no pass-line event but other bets resolved
        string sideWin = "";
        if (result.HardwayHits.Count > 0)
            sideWin = $" — HARD {result.HardwayHits.Keys.First()}!";
        else if (result.PlaceHits.Count > 0)
            sideWin = $" — PLACE {result.PlaceHits.Keys.First()} PAYS";
        else if (result.FieldReturn > 0)
            sideWin = " — FIELD PAYS";
        else if (result.AnyCrapsReturn > 0 || result.AnySevenReturn > 0
              || result.AnyElevenReturn > 0 || result.HornReturn > 0 || result.CAndEReturn > 0)
            sideWin = " — PROP WINS";
        else if (result.AtsLowsReturn > 0 || result.AtsHighsReturn > 0 || result.AtsAllReturn > 0)
            sideWin = " — LUCKY ROLLER PAYS!";

        if (verdict == "" && net > 0)
            verdict = sideWin;
        else if (verdict == "" && net < 0)
            verdict = " — BETS LOSE";
        else if (verdict == "" && result.ComeParked.Count > 0)
            verdict = $" — COME MOVES TO {result.ComeParked[0].Point}";

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
        // Per-roll row: chips on the felt when the dice were thrown, and this roll's net
        int rowPoint = pointBeforeRoll != 0 ? pointBeforeRoll : result.NewPoint ?? 0;
        var rollRecord = new CrapsRoundRecord(rollLogIndex++, rowPoint, rollCount,
            onTableAtRoll, onTableAtRoll + net, bankroll.Balance, result.Total);
        onRollLogged?.Invoke(rollRecord);

        if (result.RoundOver)
        {
            // Shooter turn ended — record for history/save
            var record = new CrapsRoundRecord(roundIndex, pointBeforeRoll, rollCount,
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
        else if (pointBeforeRoll != 0 && currentRound.Phase == CrapsPhase.ComeOut)
        {
            TryOpenShooterPrompt($"POINT {pointBeforeRoll} MADE — COMING OUT");
        }
        else if (result.PointEstablishedThisRoll)
        {
            int pt = result.NewPoint.Value;
            long passBase = currentRound.GetBet(CrapsBetType.PassLine);
            if (passBase > 0)
                OpenOddsModal(CrapsBetType.PassOdds, "PASS LINE ODDS", pt, passBase);
            TryOpenShooterPrompt($"POINT {pt} IS ON");
            RefreshActionButtons();
        }

        // Clear chip visuals for bets that resolved
        if (result.PassResolved) ClearChipVisuals(passSpot);
        // Field and props are one-roll bets — clear regardless of outcome
        ClearChipVisuals(fieldSpot);
        foreach (var t in PropTypes) { if (propSpots.TryGetValue(t, out var s)) ClearChipVisuals(s); }

        // Hardways lose on 7 or easy way; Place bets only when working — sync to whatever the core still holds
        foreach (var kv in hardSpots)
            if (currentRound.GetBet(NumberToHardType(kv.Key)) == 0) ClearChipVisuals(kv.Value);
        foreach (var n in PlaceNumbers)
            if (currentRound.GetBet(NumberToPlaceType(n)) == 0 && placeSpots.TryGetValue(n, out var ps)) ClearChipVisuals(ps);

        // Come wagers: bankroll already credited via result.TotalReturned at top of method
        foreach (var kv in result.ComeReturns)
            if (comeChips.TryGetValue(kv.Key, out var chip)) { Destroy(chip); comeChips.Remove(kv.Key); }
        foreach (var w in result.ComeParked)
        {
            // Move the chip from the bar onto the number cell where it parked
            if (comeChips.TryGetValue(w, out var chip)) { Destroy(chip); comeChips.Remove(w); }
            if (w.Point.HasValue && placeSpots.TryGetValue(w.Point.Value, out var ps))
                comeChips[w] = SpawnWagerChip(ps.Root.transform, w.Amount, new Vector2(-24f, PlaceChipY));
            if (w.Point.HasValue)
                OpenComeOddsModal(w);
        }

        // Reconcile: destroy chips for any wager silently removed by the core (parked Come lost to a 7)
        var activeCome = new HashSet<ComeWager>(currentRound.ComeWagers);
        foreach (var key in comeChips.Keys.ToList())
            if (!activeCome.Contains(key)) { Destroy(comeChips[key]); comeChips.Remove(key); }

        RefreshComeBarText();

        // ATS chip visuals — clear on any 7 (bets lost) or on win (bet consumed by core)
        if (result.AtsSevenOut || result.AtsLowsReturn > 0) ClearChipVisuals(atsLowsSpot);
        if (result.AtsSevenOut || result.AtsHighsReturn > 0) ClearChipVisuals(atsHighsSpot);
        if (result.AtsSevenOut || result.AtsAllReturn > 0) ClearChipVisuals(atsAllSpot);
        RefreshAtsDots();
    }

    void RefreshPhaseVisuals()
    {
        bool hasPoint = currentRound.Phase == CrapsPhase.Point;
        pointPuck.SetActive(hasPoint);
        if (hasPoint && currentRound.Point.HasValue && numberCellRoots.TryGetValue(currentRound.Point.Value, out var cellRt))
        {
            var puckRt = pointPuck.GetComponent<RectTransform>();
            puckRt.anchoredPosition = cellRt.anchoredPosition + new Vector2(24, 58);
            pointPuck.transform.SetAsLastSibling();
        }
        RefreshActionButtons();
    }

    // --- Action buttons ---

    void BuildActionButtons()
    {
        clearBaseColor  = new Color(0.55f, 0.18f, 0.18f);
        repeatBaseColor = new Color(0.18f, 0.42f, 0.22f);
        rollBaseColor   = UIFactory.Positive;

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
        var tipBg = UIFactory.MakePanel(tableRoot, "TakeDownTipBg", new Vector2(+405f, btnY), new Vector2(240f, 52f),
            UIFactory.PanelDarker, shadow: false);
        UIFactory.AddSharpFrame(tipBg, SectionHeaderColor, square: true);
        UIFactory.MakeText(tableRoot, "TakeDownTip", new Vector2(+405f, btnY), 13, TextAnchor.MiddleCenter,
            new Vector2(226f, 46f), UIFactory.TextLight, FontStyle.Bold).text = "RIGHT-CLICK A BET\nTO TAKE IT DOWN";
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
        if (betsToggleLabel != null) betsToggleLabel.text = working ? "BETS\nON" : "BETS\nOFF";
    }

    void RefreshActionButtons()
    {
        RefreshBetsToggle();
        bool hasClearable = currentRound.GetBet(CrapsBetType.Field) > 0
            || PropTypes.Any(t => currentRound.GetBet(t) > 0)
            || PlaceNumbers.Any(n => currentRound.GetBet(NumberToPlaceType(n)) > 0)
            || HardNumbers.Any(n => currentRound.GetBet(NumberToHardType(n)) > 0)
            || (currentRound.Phase == CrapsPhase.ComeOut && currentRound.GetBet(CrapsBetType.PassLine) > 0)
            || (currentRound.CanPlaceAts && (currentRound.GetBet(CrapsBetType.AtsLows) > 0
                || currentRound.GetBet(CrapsBetType.AtsHighs) > 0 || currentRound.GetBet(CrapsBetType.AtsAll) > 0));
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
        oddsModalRoot = new GameObject("CCOddsModal");
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

        var panel = UIFactory.MakeFramedPanel(oddsModalRoot.transform, "CCOddsModalPanel", Vector2.zero, new Vector2(660, 460), Color.black);

        oddsModalTitleText = UIFactory.MakeText(panel.transform, "OddsModalTitle", new Vector2(0, 185), 26,
            sizeDelta: new Vector2(620, 36), color: UIFactory.TextLight, style: FontStyle.Bold);
        oddsModalOddsText = UIFactory.MakeText(panel.transform, "OddsModalOdds", new Vector2(0, 130), 18,
            sizeDelta: new Vector2(620, 62), color: UIFactory.Accent);
        oddsModalAmountText = UIFactory.MakeText(panel.transform, "OddsModalAmount", new Vector2(0, 58), 36,
            sizeDelta: new Vector2(620, 50), color: UIFactory.TextLight, style: FontStyle.Bold);
        oddsModalCapText = UIFactory.MakeText(panel.transform, "OddsModalCap", new Vector2(0, 20), 17,
            sizeDelta: new Vector2(620, 26), color: UIFactory.Accent);

        // 5 buttons: 1x–5x. Only buttons up to MaxOddsMultiplier(point) are shown.
        multiplierButtons = new Button[5];
        multiplierLabels = new Text[5];
        float[] btnX = { -224f, -112f, 0f, 112f, 224f };
        for (int i = 0; i < 5; i++)
        {
            var go = new GameObject($"CCOddsMultBtn_{i + 1}x");
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

        UIFactory.MakeButton(panel.transform, "CCOddsConfirm", new Vector2(-115, -155), new Vector2(220, 58),
            "CONFIRM ODDS", UIFactory.Positive, OnOddsModalConfirm, 16, pixelFont: true);
        UIFactory.MakeButton(panel.transform, "CCOddsSkip", new Vector2(115, -155), new Vector2(220, 58),
            "SKIP ODDS", UIFactory.AccentDim, HideOddsModal, 16, pixelFont: true);

        oddsModalRoot.SetActive(false);
    }

    // Dealer check before each come-out: Place bets and Hardways are the player's own, separate from the shooter's line bets
    void BuildShooterPrompt(Transform canvas)
    {
        shooterPromptRoot = new GameObject("CCShooterPrompt");
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

        var panel = UIFactory.MakeFramedPanel(shooterPromptRoot.transform, "CCShooterPromptPanel", Vector2.zero, new Vector2(640, 340), Color.black);

        shooterPromptTitleText = UIFactory.MakeText(panel.transform, "ShooterPromptTitle", new Vector2(0, 120), 26,
            sizeDelta: new Vector2(600, 36), color: UIFactory.TextLight, style: FontStyle.Bold);
        UIFactory.MakeText(panel.transform, "ShooterPromptQuestion", new Vector2(0, 62), 22,
            sizeDelta: new Vector2(600, 34), color: UIFactory.Accent).text = "Dealer: \"Do you want to turn off your bets?\"";
        shooterPromptBetsText = UIFactory.MakeText(panel.transform, "ShooterPromptBets", new Vector2(0, 5), 16,
            sizeDelta: new Vector2(600, 56), color: UIFactory.TextDim);

        UIFactory.MakeButton(panel.transform, "CCShooterTurnOff", new Vector2(-125, -100), new Vector2(220, 58),
            "TURN OFF", UIFactory.AccentDim, () => AnswerShooterPrompt(false), 16, pixelFont: true);
        UIFactory.MakeButton(panel.transform, "CCShooterKeepOn", new Vector2(125, -100), new Vector2(220, 58),
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

    void OpenComeOddsModal(ComeWager w)
    {
        int point = w.Point.Value;
        OpenOddsModal(CrapsBetType.PassOdds, $"COME ODDS — {point}", point, w.Amount);
        oddsModalComeWager = w; // set after OpenOddsModal clears it
    }

    void OpenOddsModal(CrapsBetType betType, string title, int point, long baseAmount)
    {
        oddsModalComeWager = null; // clear Come context when opening for Pass Line
        oddsModalBetType = betType;
        oddsModalPoint = point;
        oddsModalBaseAmount = baseAmount;
        int maxMult = CrapsResolver.MaxOddsMultiplier(point);
        oddsModalCap = baseAmount * maxMult;
        oddsModalPendingAmount = baseAmount;

        oddsModalTitleText.text = title;
        oddsModalCapText.text = $"Max: {maxMult}x odds  =  {UIFactory.FormatMoney(oddsModalCap)}";

        for (int i = 0; i < multiplierButtons.Length; i++)
        {
            int mult = i + 1;
            bool visible = mult <= maxMult;
            multiplierButtons[i].gameObject.SetActive(visible);
            if (!visible) continue;
            long capturedAmount = baseAmount * mult;
            multiplierLabels[i].text = $"{mult}x\n{UIFactory.FormatMoney(capturedAmount)}";
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
        for (int i = 0; i < multiplierButtons.Length; i++)
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
        var (num, den) = CrapsResolver.TrueOdds(oddsModalPoint);
        long payout = CrapsResolver.OddsPayout(oddsModalPendingAmount, oddsModalPoint);
        long profit = payout - oddsModalPendingAmount;

        oddsModalOddsText.text = $"Point is {oddsModalPoint}  —  True odds {num}:{den}\n" +
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
        else
        {
            currentRound.PlaceBet(oddsModalBetType, amount);
            PushUndoBet(oddsModalBetType, amount);
        }
        roundTotalStaked += amount;

        soundManager?.PlayChip();
        HideOddsModal();
        RebuildAllChipVisuals();
        RefreshActionButtons();
    }

    void RefreshPassOddsText()
    {
        long passOdds = currentRound.GetBet(CrapsBetType.PassOdds);
        if (passSpot.OddsText != null) passSpot.OddsText.text = passOdds > 0 ? $"+{UIFactory.FormatMoney(passOdds)} odds" : "";
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
        lastRollBets.Clear();
        undoStack.Clear();
        streakBadgeGO?.SetActive(false);
        pointPuck.SetActive(false);
        currentRound = new CrapsRound(rng);
        shooterPromptRoot.SetActive(false);
        foreach (var kv in comeChips) Destroy(kv.Value);
        comeChips.Clear();
        RebuildAllChipVisuals();
        RefreshComeBarText();
        RefreshAtsDots();
        statusText.color = UIFactory.Accent;
        statusText.text = "Place bets — come-out roll";
        rollButton.interactable = true;
        RefreshActionButtons();
    }
}
