using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Febucci.UI;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

// Sic Bo betting surface in the Macau / Asian table layout: pair+single and three-number grids on the left,
// the main felt (Even/Big, doubles, triples, Odd/Small, totals, combos, singles, four-number bets) on the right.
// Chips go straight into SicBoRound and leave the wallet when placed (same as craps); every bet is one-roll,
// so the felt clears after each roll and REPEAT BET re-places it. Right-click a spot to take its bet down.
public class SicBoBettingUIController : MonoBehaviour
{
    Bankroll bankroll;
    ChipSelectorUI chipSelector;
    IRandomSource rng;
    SoundManager soundManager;
    JuiceManager juiceManager;
    FloatingTextUI floatingText;
    FloatingTextUI milestoneToast;
    Action<SicBoRoundRecord> onRoundResolved;
    Action onBankrollChanged;
    Action<string, Color> onRollLogged;

    SicBoRound currentRound;
    SicBoRollResult pendingResult;
    bool rolling;
    int roundIndex;
    int winStreak;
    long bestRoundNet;
    bool doubledMilestoneFired;
    bool rollWasEnabled;
    readonly HashSet<int> roundMilestonesFired = new HashSet<int>();
    readonly Dictionary<SicBoBetType, long> lastBets = new Dictionary<SicBoBetType, long>();
    readonly List<Dictionary<SicBoBetType, long>> undoStack = new List<Dictionary<SicBoBetType, long>>();
    const int MaxUndoDepth = 10;

    Transform tableRoot, feltRoot, chipLayer;
    Text statusText;
    Button rollButton, clearBetButton, repeatButton, undoButton;
    Color rollBaseColor, clearBaseColor, repeatBaseColor;

    TextMeshProUGUI streakText;
    TextAnimator_TMP streakAnimator;
    GameObject streakBadgeGO;

    SicBoDome dome;

    class BetTile
    {
        public SicBoBetType Type;
        public GameObject Root;
        public Vector2 ChipAnchor;
        public Image FillImg;
        public Color BaseColor;
        public readonly List<GameObject> ChipVisuals = new List<GameObject>();
    }

    readonly Dictionary<SicBoBetType, BetTile> tiles = new Dictionary<SicBoBetType, BetTile>();

    // Casino-sim house style: felt green main area, craps-style dark zone tints, gold headings
    static readonly Color FeltColor    = UIFactory.FeltGreen;
    static readonly Color FeltLine     = new Color(0.32f, 0.58f, 0.40f);
    static readonly Color OrangeColor  = new Color(0.30f, 0.22f, 0.06f);
    static readonly Color BlueColor    = new Color(0.10f, 0.17f, 0.38f);
    static readonly Color GridLine     = new Color(0.62f, 0.52f, 0.25f);
    static readonly Color BlueGridLine = new Color(0.36f, 0.46f, 0.74f);
    static readonly Color GridText     = UIFactory.TextLight;
    static readonly Color TitleGold    = new Color(1f, 0.85f, 0.1f);
    static readonly Color WinsText     = new Color(0.82f, 0.86f, 0.80f);
    static readonly Color PipDark      = new Color(0.08f, 0.08f, 0.1f);
    static readonly Color PipRed       = new Color(0.86f, 0.1f, 0.12f);
    static readonly Color WinGlow      = new Color(1f, 0.84f, 0.2f);
    const string RedHex = "#FF5A4E";

    static readonly Color[] ChipColors =
    {
        new Color(0.65f, 0.12f, 0.12f),
        new Color(0.1f, 0.35f, 0.6f),
        UIFactory.Chip500White
    };

    static readonly string[] WinFlavors  = { "Collect your winnings!", "Dice don't lie!", "Nice read!", "Keep riding!", "There it is!" };
    static readonly string[] LoseFlavors = { "Shake those dice", "Try a different spread", "Roll again", "Onward", "Next roll's yours" };

    public void Build(Transform canvas, Bankroll bankroll, ChipSelectorUI chipSelector, IRandomSource rng,
        SoundManager soundManager, JuiceManager juiceManager, FloatingTextUI floatingText,
        FloatingTextUI milestoneToast, SicBoDome dome,
        Action<SicBoRoundRecord> onRoundResolved, Action onBankrollChanged,
        Action<string, Color> onRollLogged)
    {
        this.bankroll = bankroll;
        this.chipSelector = chipSelector;
        this.rng = rng;
        this.soundManager = soundManager;
        this.juiceManager = juiceManager;
        this.floatingText = floatingText;
        this.milestoneToast = milestoneToast;
        this.dome = dome;
        this.onRoundResolved = onRoundResolved;
        this.onBankrollChanged = onBankrollChanged;
        this.onRollLogged = onRollLogged;

        currentRound = new SicBoRound(rng);

        var rootGO = new GameObject("SicBoUIRoot");
        rootGO.transform.SetParent(canvas, false);
        var rootRt = rootGO.AddComponent<RectTransform>();
        rootRt.anchorMin = rootRt.anchorMax = rootRt.pivot = new Vector2(0.5f, 0.5f);
        rootRt.anchoredPosition = Vector2.zero;
        tableRoot = rootGO.transform;

        // Top band under the HUD: status bar, action buttons, right-click tip
        var statusPanelBg = UIFactory.MakePanel(tableRoot, "StatusPanelBg", new Vector2(0, 350), new Vector2(600, 40), UIFactory.PanelDark, shadow: false);
        UIFactory.AddSharpFrame(statusPanelBg, UIFactory.AccentDim, square: true);
        statusText = UIFactory.MakeText(tableRoot, "StatusText", new Vector2(0, 350), 19,
            sizeDelta: new Vector2(590, 36), color: UIFactory.Accent, style: FontStyle.Bold);
        statusText.text = "Place bets, then ROLL";

        // The whole betting table lives under feltRoot, scaled down so the top band has room to breathe
        var feltGO = new GameObject("SBFeltRoot", typeof(RectTransform));
        feltGO.transform.SetParent(tableRoot, false);
        ((RectTransform)feltGO.transform).anchoredPosition = new Vector2(FeltOffsetX, FeltOffsetY);
        feltGO.transform.localScale = Vector3.one * FeltScale;
        feltRoot = feltGO.transform;
        BuildTable();
        chipLayer = new GameObject("SBChipLayer", typeof(RectTransform)).transform;
        chipLayer.SetParent(feltRoot, false);
        BuildActionButtons();
        BuildTakeDownTip();
        BuildStreakBadge();
        RefreshActionButtons();
        StartPresim();
    }

    // --- Table ---

    // Whole table: y from TableTop down to TableBottom, x from TableLeft to TableRight.
    const float TableTop = 250f, TableBottom = -532f, TableLeft = -945f, TableRight = 945f;
    const float Gap = 4f;
    // Scaled table sits flush left (x -950..670), leaving a tall column on the right for History
    const float FeltScale = 0.85f, FeltOffsetX = -140f, FeltOffsetY = -76f;

    void BuildTable()
    {
        UIFactory.MakePanel(feltRoot, "SBTableBg", new Vector2((TableLeft + TableRight) / 2f, (TableTop + TableBottom) / 2f),
            new Vector2(TableRight - TableLeft + 16f, TableTop - TableBottom + 16f), UIFactory.PanelDarker);

        const float leftW = 480f, blockGap = 14f;
        BuildLeftGrids(TableLeft, TableLeft + leftW);
        BuildMainFelt(TableLeft + leftW + blockGap, TableRight);
    }

    // Orange: specific double + single (1 wins 50). Blue: three single numbers (1 wins 30).
    void BuildLeftGrids(float left, float right)
    {
        const float sideW = 66f, blueOrangeGap = 14f;
        float rowH = (TableTop - TableBottom - blueOrangeGap) / 10f;
        float cellW = (right - left - sideW - 5 * Gap) / 5f;

        float orangeTop = TableTop;
        for (int pair = 1; pair <= 6; pair++)
        {
            int col = 0;
            for (int single = 1; single <= 6; single++)
            {
                if (single == pair) continue;
                var type = SicBoBetType.Pair1Single2 + (pair - 1) * 5 + col;
                var tile = MakeTile(type, CellRect(left, orangeTop, cellW, rowH, col, pair - 1), OrangeColor, GridLine, 1.06f);
                AddText(tile, RedDigits($"{pair}{pair}{single}"), Vector2.zero, 34, GridText);
                col++;
            }
        }
        MakeSideLabel(new Vector2(right - sideW / 2f, orangeTop - 3 * rowH), new Vector2(sideW, 6 * rowH - Gap), OrangeColor, GridLine, "1\nWINS\n50");

        int[][] blueRows =
        {
            new[] { 126, 135, 234, 256, 346 },
            new[] { 123, 136, 145, 235, 356 },
            new[] { 124, 146, 236, 245, 456 },
            new[] { 125, 134, 156, 246, 345 }
        };
        float blueTop = orangeTop - 6 * rowH - blueOrangeGap;
        for (int r = 0; r < 4; r++)
            for (int c = 0; c < 5; c++)
            {
                int n = blueRows[r][c];
                var tile = MakeTile(ThreeType(n / 100, n / 10 % 10, n % 10), CellRect(left, blueTop, cellW, rowH, c, r), BlueColor, BlueGridLine, 1.06f);
                AddText(tile, RedDigits(n.ToString()), Vector2.zero, 34, GridText);
            }
        MakeSideLabel(new Vector2(right - sideW / 2f, blueTop - 2 * rowH), new Vector2(sideW, 4 * rowH - Gap), BlueColor, BlueGridLine, "1\nWINS\n30");
    }

    static Rect CellRect(float left, float top, float w, float h, int col, int row) =>
        new Rect(left + col * (w + Gap), top - (row + 1) * h + Gap, w, h - Gap);

    void MakeSideLabel(Vector2 center, Vector2 size, Color fill, Color line, string text)
    {
        var bg = UIFactory.MakePanel(feltRoot, "SideLabel", center, size, fill, shadow: false);
        UIFactory.AddSharpFrame(bg, line, square: true);
        UIFactory.MakeText(feltRoot, "SideLabelText", center, 15, TextAnchor.MiddleCenter, size, TitleGold, FontStyle.Bold).text = text;
    }

    void BuildMainFelt(float left, float right)
    {
        float width = right - left;
        const float rowAH = 290f, rowBH = 150f, rowCH = 160f;
        float rowDH = TableTop - TableBottom - rowAH - rowBH - rowCH - 3 * 6f;
        float yA = TableTop, yB = yA - rowAH - 6f, yC = yB - rowBH - 6f, yD = yC - rowCH - 6f;

        // Row A: EVEN/BIG | doubles 4-6 | triples + ANY TRIPLE | doubles 1-3 | ODD/SMALL
        const float sideW = 250f, pairW = 130f;
        float tripleW = width - 2 * sideW - 2 * pairW - 4 * Gap;
        float x = left;
        BuildEvenOddBigSmall(x, yA, sideW, rowAH, true);
        x += sideW + Gap;
        BuildDoublesColumn(x, yA, pairW, rowAH, new[] { 4, 5, 6 });
        x += pairW + Gap;
        BuildTriples(x, yA, tripleW, rowAH);
        x += tripleW + Gap;
        BuildDoublesColumn(x, yA, pairW, rowAH, new[] { 1, 2, 3 });
        x += pairW + Gap;
        BuildEvenOddBigSmall(x, yA, sideW, rowAH, false);

        // Row B: totals 17 down to 4
        float totalW = (width - 13 * Gap) / 14f;
        for (int i = 0; i < 14; i++)
        {
            int t = 17 - i;
            var tile = MakeTile(TotalType(t), new Rect(left + i * (totalW + Gap), yB - rowBH, totalW, rowBH), FeltColor, FeltLine, 1.08f);
            AddText(tile, t.ToString(), new Vector2(0, 16), 58, Color.white);
            AddText(tile, $"1 WINS {SicBoResolver.TotalMultiplier(t)}", new Vector2(0, -50), 16, WinsText);
        }

        // Row C: "1 WINS 5" marker, then the 15 two-dice combinations, highest first
        const float markerW = 90f;
        var marker = UIFactory.MakePanel(feltRoot, "ComboMarker", new Vector2(left + markerW / 2f, yC - rowCH / 2f), new Vector2(markerW, rowCH), FeltColor, shadow: false);
        UIFactory.AddSharpFrame(marker, FeltLine, square: true);
        UIFactory.MakeText(feltRoot, "ComboMarkerText", new Vector2(left + markerW / 2f, yC - rowCH / 2f), 18, TextAnchor.MiddleCenter,
            new Vector2(markerW, rowCH), WinsText, FontStyle.Bold).text = "1\nWINS\n5";
        var combos = new List<(int a, int b)>();
        for (int a = 5; a >= 1; a--)
            for (int b = 6; b > a; b--)
                combos.Add((a, b));
        combos = combos.OrderByDescending(c => c.b).ThenByDescending(c => c.a).ToList();
        float comboW = (width - markerW - 15 * Gap) / 15f;
        for (int i = 0; i < combos.Count; i++)
        {
            var (a, b) = combos[i];
            var tile = MakeTile(ComboType(a, b), new Rect(left + markerW + Gap + i * (comboW + Gap), yC - rowCH, comboW, rowCH), FeltColor, FeltLine, 1.08f);
            MakeMiniDie(tile.Root.transform, new Vector2(0, 36), 58f, a);
            MakeMiniDie(tile.Root.transform, new Vector2(0, -36), 58f, b);
        }

        // Row D: six single-number spots (6 down to 1) with the payout caption, then the four-number bets
        const float singleW = 104f, captionH = 34f;
        float singleH = rowDH - captionH - Gap;
        for (int i = 0; i < 6; i++)
        {
            int f = 6 - i;
            var tile = MakeTile(SingleType(f), new Rect(left + i * (singleW + Gap), yD - singleH, singleW, singleH), FeltColor, FeltLine, 1.08f);
            MakeMiniDie(tile.Root.transform, Vector2.zero, 78f, f);
        }
        float capY = yD - singleH - Gap - captionH / 2f;
        string[] captions = { "1 : 1 ON ONE DICE", "1 : 2 ON TWO DICE", "1 : 12 ON THREE DICE" };
        for (int i = 0; i < 3; i++)
        {
            float cx = left + i * 2 * (singleW + Gap) + singleW + Gap / 2f;
            UIFactory.MakeText(feltRoot, "SingleCaption", new Vector2(cx, capY), 15, TextAnchor.MiddleCenter,
                new Vector2(2 * singleW + Gap, captionH), WinsText, FontStyle.Bold).text = captions[i];
        }

        float fourLeft = left + 6 * (singleW + Gap);
        float fourW = (right - fourLeft - 3 * Gap) / 4f;
        var fours = new[] { SicBoBetType.Four3456, SicBoBetType.Four2356, SicBoBetType.Four2345, SicBoBetType.Four1234 };
        for (int i = 0; i < 4; i++)
        {
            var faces = SicBoResolver.FourNumberFaces(fours[i]);
            string digits = string.Join(" ", faces.Reverse());
            var tile = MakeTile(fours[i], new Rect(fourLeft + i * (fourW + Gap), yD - rowDH, fourW, rowDH), FeltColor, FeltLine, 1.05f);
            AddText(tile, RedDigits(digits), new Vector2(0, 20), 50, Color.white);
            AddText(tile, "1 WINS 7", new Vector2(0, -50), 17, WinsText);
        }
    }

    void BuildEvenOddBigSmall(float left, float top, float w, float h, bool isLeft)
    {
        const float evenH = 80f;
        var evenTile = MakeTile(isLeft ? SicBoBetType.Even : SicBoBetType.Odd, new Rect(left, top - evenH, w, evenH), FeltColor, FeltLine, 1.05f);
        AddText(evenTile, isLeft ? "EVEN  1 : 1" : "ODD  1 : 1", Vector2.zero, 30, TitleGold);

        var bigTile = MakeTile(isLeft ? SicBoBetType.Big : SicBoBetType.Small, new Rect(left, top - h, w, h - evenH - Gap), FeltColor, FeltLine, 1.05f);
        AddText(bigTile, isLeft ? "BIG" : "SMALL", new Vector2(0, 42), 46, TitleGold);
        AddText(bigTile, isLeft ? "11 - 17" : "4 - 10", new Vector2(0, -12), 24, Color.white);
        AddText(bigTile, "1 WINS 1", new Vector2(0, -54), 17, WinsText);
    }

    void BuildDoublesColumn(float left, float top, float w, float h, int[] faces)
    {
        float cellH = (h - 2 * Gap) / 3f;
        for (int i = 0; i < 3; i++)
        {
            var tile = MakeTile(DoubleType(faces[i]), new Rect(left, top - (i + 1) * cellH - i * Gap, w, cellH), FeltColor, FeltLine, 1.08f);
            MakeMiniDie(tile.Root.transform, new Vector2(-26, 12), 46f, faces[i]);
            MakeMiniDie(tile.Root.transform, new Vector2(26, 12), 46f, faces[i]);
            AddText(tile, "1 WINS 8", new Vector2(0, -33), 15, WinsText);
        }
    }

    // Top: six specific triples (6 down to 1). Below: ANY TRIPLE across the full width.
    void BuildTriples(float left, float top, float w, float h)
    {
        const float specificH = 142f;
        float cellW = (w - 5 * Gap) / 6f;
        for (int i = 0; i < 6; i++)
        {
            int f = 6 - i;
            var tile = MakeTile(TripleType(f), new Rect(left + i * (cellW + Gap), top - specificH, cellW, specificH), FeltColor, FeltLine, 1.08f);
            MakeDiceTriangle(tile.Root.transform, new Vector2(0, 16), 38f, f);
            AddText(tile, "1 WINS 150", new Vector2(0, -55), 15, WinsText);
        }

        var any = MakeTile(SicBoBetType.AnyTriple, new Rect(left, top - h, w, h - specificH - Gap), FeltColor, FeltLine, 1.03f);
        AddText(any, "ANY TRIPLE   1 WINS 24", new Vector2(0, 38), 36, TitleGold);
        float stepX = w / 6f;
        for (int i = 0; i < 6; i++)
            MakeDiceTriangle(any.Root.transform, new Vector2(-w / 2f + stepX * (i + 0.5f), -26), 28f, 6 - i);
    }

    // Every bet spot: a filled, framed button with right-click take-down. rect is in table coordinates (bottom-left origin).
    BetTile MakeTile(SicBoBetType type, Rect rect, Color fillColor, Color lineColor, float pulseScale)
    {
        var go = new GameObject($"SBTile_{type}");
        go.transform.SetParent(feltRoot, false);
        var rt = go.AddComponent<RectTransform>();
        rt.sizeDelta = rect.size;
        rt.anchoredPosition = rect.center;
        var fill = go.AddComponent<Image>();
        fill.color = fillColor;
        UIFactory.AddSharpFrame(go, lineColor, square: true);
        var btn = go.AddComponent<Button>();
        btn.targetGraphic = fill;
        var colors = btn.colors;
        colors.highlightedColor = new Color(1.15f, 1.15f, 1.15f);
        colors.pressedColor = new Color(0.85f, 0.85f, 0.85f);
        btn.colors = colors;
        btn.onClick.AddListener(() => OnTileClicked(type));
        RightClickRelay.Attach(go, () => TakeDown(type));

        var tile = new BetTile
        {
            Type = type, Root = go, FillImg = fill, BaseColor = fillColor,
            ChipAnchor = new Vector2(rect.xMax - 8f, rect.yMax - 8f)
        };
        tilePulse[type] = pulseScale;
        tiles[type] = tile;
        return tile;
    }

    readonly Dictionary<SicBoBetType, float> tilePulse = new Dictionary<SicBoBetType, float>();

    static void AddText(BetTile tile, string text, Vector2 pos, int size, Color color)
    {
        var rt = (RectTransform)tile.Root.transform;
        var t = UIFactory.MakeText(tile.Root.transform, "Text", pos, size, TextAnchor.MiddleCenter,
            new Vector2(rt.sizeDelta.x - 4f, size + 16f), color, FontStyle.Bold);
        t.supportRichText = true;
        t.raycastTarget = false;
        t.text = text;
    }

    // Asian dice paint the 1 and 4 red; the table numbers follow the same rule.
    static string RedDigits(string s) =>
        string.Concat(s.Select(c => c == '1' || c == '4' ? $"<color={RedHex}>{c}</color>" : c.ToString()));

    // White die with pips (1 and 4 in red), used for every dice picture on the table
    public static void MakeMiniDie(Transform parent, Vector2 pos, float size, int face)
    {
        var go = new GameObject($"MiniDie{face}");
        go.transform.SetParent(parent, false);
        var rt = go.AddComponent<RectTransform>();
        rt.sizeDelta = new Vector2(size, size);
        rt.anchoredPosition = pos;
        var bg = go.AddComponent<Image>();
        bg.sprite = UIFactory.RoundedRect();
        bg.type = Image.Type.Sliced;
        bg.pixelsPerUnitMultiplier = size < 40f ? 4f : 2f;
        bg.color = Color.white;
        bg.raycastTarget = false;
        var edge = go.AddComponent<Outline>();
        edge.effectColor = new Color(0f, 0f, 0f, 0.6f);
        edge.effectDistance = new Vector2(1f, -1f);

        float o = size * 0.26f;
        Vector2[] slots =
        {
            new Vector2(-o, o), new Vector2(o, o), new Vector2(-o, 0), new Vector2(o, 0),
            new Vector2(-o, -o), new Vector2(o, -o), Vector2.zero
        };
        int[][] facePips =
        {
            new int[0], new[] { 6 }, new[] { 0, 5 }, new[] { 0, 6, 5 },
            new[] { 0, 1, 4, 5 }, new[] { 0, 1, 6, 4, 5 }, new[] { 0, 1, 2, 3, 4, 5 }
        };
        Color pipColor = face == 1 || face == 4 ? PipRed : PipDark;
        float pipSize = size * (face == 1 ? 0.34f : 0.2f);
        foreach (int slot in facePips[face])
        {
            var pip = new GameObject("Pip");
            pip.transform.SetParent(go.transform, false);
            var img = pip.AddComponent<Image>();
            img.sprite = UIFactory.Circle();
            img.color = pipColor;
            img.raycastTarget = false;
            var prt = pip.GetComponent<RectTransform>();
            prt.sizeDelta = Vector2.one * pipSize;
            prt.anchoredPosition = slots[slot];
        }
    }

    // Three dice stacked like the table art: one on top, two below
    static void MakeDiceTriangle(Transform parent, Vector2 center, float size, int face)
    {
        float half = size * 0.55f;
        MakeMiniDie(parent, center + new Vector2(0, half), size, face);
        MakeMiniDie(parent, center + new Vector2(-half, -half), size, face);
        MakeMiniDie(parent, center + new Vector2(half, -half), size, face);
    }

    static SicBoBetType TotalType(int t)  => SicBoBetType.Total4 + (t - 4);
    static SicBoBetType SingleType(int f) => SicBoBetType.Single1 + (f - 1);
    static SicBoBetType DoubleType(int f) => SicBoBetType.Double1 + (f - 1);
    static SicBoBetType TripleType(int f) => SicBoBetType.Triple1 + (f - 1);

    static SicBoBetType ComboType(int a, int b)
    {
        for (var t = SicBoBetType.Combo12; t <= SicBoBetType.Combo56; t++)
            if (SicBoResolver.ComboFaces(t) == (a, b)) return t;
        throw new ArgumentException($"No combo {a}-{b}");
    }

    static SicBoBetType ThreeType(int a, int b, int c)
    {
        for (var t = SicBoBetType.Three123; t <= SicBoBetType.Three456; t++)
            if (SicBoResolver.ThreeNumberFaces(t).SequenceEqual(new[] { a, b, c })) return t;
        throw new ArgumentException($"No three-number bet {a}{b}{c}");
    }

    static string BetName(SicBoBetType t)
    {
        switch (t)
        {
            case SicBoBetType.Big: return "Big";
            case SicBoBetType.Small: return "Small";
            case SicBoBetType.Odd: return "Odd";
            case SicBoBetType.Even: return "Even";
            case SicBoBetType.AnyTriple: return "Any Triple";
        }
        int n = SicBoResolver.TotalForBet(t);
        if (n > 0) return $"Total {n}";
        if ((n = SicBoResolver.SingleFace(t)) > 0) return $"Single {n}";
        if ((n = SicBoResolver.DoubleFace(t)) > 0) return $"Double {n}";
        if ((n = SicBoResolver.TripleFace(t)) > 0) return $"Triple {n}";
        var (a, b) = SicBoResolver.ComboFaces(t);
        if (a > 0) return $"Combo {a}-{b}";
        var three = SicBoResolver.ThreeNumberFaces(t);
        if (three != null) return $"{three[0]}-{three[1]}-{three[2]}";
        var (pair, single) = SicBoResolver.PairSingleFaces(t);
        if (pair > 0) return $"{pair}-{pair}-{single}";
        return $"Four {string.Join("", SicBoResolver.FourNumberFaces(t).Reverse())}";
    }

    // --- Chips ---

    // A small stack on the spot's top-right corner, amount printed on the top chip.
    // Lives on chipLayer (built after every spot) so a neighbouring spot never draws over it.
    void RefreshTile(BetTile tile)
    {
        foreach (var go in tile.ChipVisuals) Destroy(go);
        tile.ChipVisuals.Clear();
        long amt = currentRound.GetBet(tile.Type);
        if (amt <= 0) return;

        // Largest denomination on top; up to 3 chips in the stack
        var colors = new List<Color>();
        long remaining = amt;
        var denoms = ChipDenominations.Values;
        for (int d = denoms.Length - 1; d >= 0 && colors.Count < 3; d--)
            while (remaining >= denoms[d] && colors.Count < 3)
            {
                remaining -= denoms[d];
                colors.Add(ChipColors[d]);
            }
        if (colors.Count == 0) colors.Add(ChipColors[0]);
        colors.Reverse();

        for (int i = 0; i < colors.Count; i++)
        {
            var chip = MakeChip(tile.ChipAnchor + new Vector2(0, i * 3f), colors[i]);
            tile.ChipVisuals.Add(chip);
            if (i == colors.Count - 1)
            {
                bool light = colors[i] == UIFactory.Chip500White;
                UIFactory.MakeText(chip.transform, "Amt", Vector2.zero, 12, TextAnchor.MiddleCenter,
                    new Vector2(ChipSize + 8f, ChipSize), light ? UIFactory.ChipDarkText : UIFactory.TextLight, FontStyle.Bold)
                    .text = UIFactory.FormatMoneyCompact(amt);
            }
        }
    }

    const float ChipSize = 30f;

    GameObject MakeChip(Vector2 pos, Color fill)
    {
        var go = new GameObject("Chip");
        go.transform.SetParent(chipLayer, false);
        var img = go.AddComponent<Image>();
        img.sprite = UIFactory.Circle();
        img.color = fill;
        img.raycastTarget = false;
        var edge = go.AddComponent<Outline>();
        edge.effectColor = new Color(0f, 0f, 0f, 0.8f);
        edge.effectDistance = new Vector2(1.5f, -1.5f);
        var rt = go.GetComponent<RectTransform>();
        rt.sizeDelta = new Vector2(ChipSize, ChipSize);
        rt.anchoredPosition = pos;
        return go;
    }

    void RefreshAllTiles()
    {
        foreach (var tile in tiles.Values) RefreshTile(tile);
    }

    // --- Bet actions ---

    Dictionary<SicBoBetType, long> Snapshot() =>
        tiles.Keys.Where(t => currentRound.GetBet(t) > 0).ToDictionary(t => t, t => currentRound.GetBet(t));

    void PushUndoSnapshot()
    {
        undoStack.Add(Snapshot());
        if (undoStack.Count > MaxUndoDepth) undoStack.RemoveAt(0);
    }

    // Puts the felt back to exactly these bets; money moves between felt and wallet to match.
    void ApplyBets(Dictionary<SicBoBetType, long> bets)
    {
        bankroll.Deposit(currentRound.TotalOnTable());
        currentRound.ClearAllBets();
        long total = bets.Values.Sum();
        if (total > 0 && bankroll.TryWithdraw(total))
            foreach (var kv in bets) currentRound.PlaceBet(kv.Key, kv.Value);
        RefreshAllTiles();
        onBankrollChanged?.Invoke();
    }

    void OnTileClicked(SicBoBetType type)
    {
        if (rolling) return;
        long chip = chipSelector.SelectedChip;
        if (!bankroll.CanAfford(chip))
        {
            statusText.color = UIFactory.Accent;
            statusText.text = bankroll.Balance < ChipDenominations.Values[0]
                ? "Out of chips — use ADD FUNDS above to keep playing"
                : "Not enough balance for that bet";
            FlashBlocked();
            return;
        }
        PushUndoSnapshot();
        bankroll.TryWithdraw(chip);
        currentRound.PlaceBet(type, chip);
        soundManager?.PlayChip();
        JuiceTweens.Pulse(this, (RectTransform)tiles[type].Root.transform, peakScale: tilePulse[type], duration: 0.18f);
        RefreshTile(tiles[type]);
        onBankrollChanged?.Invoke();
        RefreshActionButtons();
    }

    void TakeDown(SicBoBetType type)
    {
        if (rolling) return;
        long amt = currentRound.GetBet(type);
        if (amt <= 0) return;
        PushUndoSnapshot();
        currentRound.ClearBet(type);
        bankroll.Deposit(amt);
        soundManager?.PlayClick();
        statusText.color = UIFactory.Accent;
        statusText.text = $"{BetName(type)} down — {UIFactory.FormatMoney(amt)} back to wallet";
        RefreshTile(tiles[type]);
        onBankrollChanged?.Invoke();
        RefreshActionButtons();
    }

    void FlashBlocked() => juiceManager?.MicroShake(1.2f);

    void OnClearBetClicked()
    {
        if (rolling) return;
        if (currentRound.TotalOnTable() <= 0) { statusText.text = "Nothing to clear"; FlashBlocked(); return; }
        PushUndoSnapshot();
        ApplyBets(new Dictionary<SicBoBetType, long>());
        soundManager?.PlayClick();
        RefreshActionButtons();
    }

    void UndoLastBetAction()
    {
        if (rolling) return;
        if (undoStack.Count == 0) { statusText.text = "Nothing to undo"; FlashBlocked(); return; }
        var snap = undoStack[undoStack.Count - 1];
        undoStack.RemoveAt(undoStack.Count - 1);
        ApplyBets(snap);
        soundManager?.PlayClick();
        RefreshActionButtons();
    }

    // Re-places every bet from the last roll that isn't already on the felt.
    void OnRepeatBetClicked()
    {
        if (rolling) return;
        var missing = lastBets.Where(kv => currentRound.GetBet(kv.Key) == 0).ToList();
        long cost = missing.Sum(kv => kv.Value);
        if (lastBets.Count == 0) { statusText.text = "No previous bets to repeat"; FlashBlocked(); return; }
        if (cost == 0) { statusText.text = "Last roll's bets are already down"; FlashBlocked(); return; }
        if (!bankroll.CanAfford(cost))
        {
            statusText.color = UIFactory.Accent;
            statusText.text = $"Not enough balance to repeat ({UIFactory.FormatMoney(cost)})";
            FlashBlocked();
            return;
        }
        PushUndoSnapshot();
        bankroll.TryWithdraw(cost);
        foreach (var kv in missing) currentRound.PlaceBet(kv.Key, kv.Value);
        RefreshAllTiles();
        soundManager?.PlayChip();
        JuiceTweens.Pulse(this, repeatButton.GetComponent<RectTransform>(), peakScale: 1.15f, duration: 0.2f);
        onBankrollChanged?.Invoke();
        RefreshActionButtons();
    }

    public long OnTableTotal() => currentRound.TotalOnTable();

    // Leaving the table: pay out a roll still animating, then return every chip on the felt.
    // Pure bankroll/round math only — safe to call from OnDestroy / OnApplicationQuit, and safe to call twice.
    public void RefundTableBets()
    {
        if (pendingResult != null) { bankroll.Deposit(pendingResult.TotalReturned); pendingResult = null; }
        long onTable = currentRound.TotalOnTable();
        if (onTable > 0) bankroll.Deposit(onTable);
        currentRound.ClearAllBets();
    }

    // --- Roll ---

    void OnRollClicked()
    {
        if (rolling) return;
        if (currentRound.TotalOnTable() <= 0) { statusText.text = "Place at least one bet before rolling"; FlashBlocked(); return; }

        lastBets.Clear();
        foreach (var kv in Snapshot()) lastBets[kv.Key] = kv.Value;

        rolling = true;
        undoStack.Clear();
        RefreshActionButtons();
        statusText.color = UIFactory.Accent;
        statusText.text = "Rolling...";
        StartCoroutine(RollSequence());
    }

    IEnumerator RollSequence()
    {
        StartPresim();
        if (dome != null) yield return new WaitUntil(() => readyPresim != null);
        var presim = readyPresim;
        readyPresim = null;

        var betsAtRoll = Snapshot();
        var result = currentRound.Roll();
        pendingResult = result;

        if (dome != null && presim != null)
            yield return StartCoroutine(Dice3D.RollGroup(dome.Dice, new[] { result.Die1, result.Die2, result.Die3 }, presim, dome.FloorVisual));
        else
            yield return new WaitForSeconds(1.5f);

        try { ApplyRollResult(result, betsAtRoll); }
        finally
        {
            rolling = false;
            RefreshActionButtons();
            StartPresim();
        }
    }

    // The shaker needs a few silent tosses before all three dice land flat and apart,
    // so the next toss is simulated while the player is still betting and ROLL starts instantly.
    PreSimGroupResult readyPresim;
    bool presimRunning;

    void StartPresim()
    {
        if (dome == null || presimRunning || readyPresim != null) return;
        presimRunning = true;
        StartCoroutine(Dice3D.RunPreSimGroup(dome.ShadowDice, dome.Dice, dome.FloorBody, r => { readyPresim = r; presimRunning = false; }));
    }

    // Pre-sim switches global physics to script mode while it runs; never leave it that way for the next scene.
    void OnDestroy()
    {
        if (!presimRunning) return;
        Physics.simulationMode = SimulationMode.FixedUpdate;
        Physics.gravity = new Vector3(0f, -9.81f, 0f);
    }

    void ApplyRollResult(SicBoRollResult rollResult, Dictionary<SicBoBetType, long> betsAtRoll)
    {
        pendingResult = null;
        long returned = rollResult.TotalReturned;
        long net = returned - rollResult.TotalStaked;
        bankroll.Deposit(returned);
        RefreshAllTiles();

        string diceStr = $"{rollResult.Die1} · {rollResult.Die2} · {rollResult.Die3}  = {rollResult.Total}";
        string bigSmall = rollResult.IsTriple ? "TRIPLE!" : (rollResult.Total >= 11 ? "BIG" : "SMALL") + (rollResult.Total % 2 == 1 ? " · ODD" : " · EVEN");
        diceStr += $"  [{bigSmall}]";

        string flavor = net > 0 ? WinFlavors[UnityEngine.Random.Range(0, WinFlavors.Length)]
            : net < 0 ? LoseFlavors[UnityEngine.Random.Range(0, LoseFlavors.Length)]
            : "Broke even";
        statusText.color = net > 0 ? UIFactory.Positive : net < 0 ? UIFactory.Negative : UIFactory.Accent;
        statusText.text  = $"{diceStr}  ({(net >= 0 ? "+" : "")}{UIFactory.FormatMoney(net)})  — {flavor}";

        HighlightWinningTiles(rollResult, betsAtRoll);

        if (net > 0)
        {
            soundManager?.PlayWin();
            if (rollResult.IsTriple)
            {
                juiceManager?.Shake(0.6f, 5f); juiceManager?.Flash(new Color(0.8f, 0.5f, 1f, 0.3f), 0.7f);
                juiceManager?.PlayConfetti(2.5f); juiceManager?.PlayMoneyFountain(Vector2.zero);
                floatingText?.Show($"TRIPLE! +{UIFactory.FormatMoney(net)}", new Color(1f, 0.85f, 0.2f), fontSize: 42);
            }
            else if (net >= ChipDenominations.Values[2])
            {
                juiceManager?.Shake(0.5f, 4f); juiceManager?.Flash(new Color(0.3f, 1f, 0.4f, 0.28f), 0.7f);
                juiceManager?.PlayConfetti(2f); juiceManager?.PulseLight(0.9f, 0.7f);
                juiceManager?.PlayMoneyFountain(Vector2.zero);
                floatingText?.Show($"HUGE WIN! +{UIFactory.FormatMoney(net)}", UIFactory.Positive, fontSize: 42);
            }
            else if (net >= ChipDenominations.Values[0] * 4L)
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

        bool showStreak = winStreak >= 2;
        streakAnimator?.SetText(showStreak ? $"<wave><rainb>{winStreak} WIN STREAK</rainb></wave>" : "");
        streakBadgeGO?.SetActive(showStreak);
        if (showStreak) JuiceTweens.Pulse(this, (RectTransform)streakBadgeGO.transform, peakScale: 1.15f, duration: 0.3f);

        onBankrollChanged?.Invoke();

        onRollLogged?.Invoke(rollResult.Total.ToString(), NetColor(net));

        var record = new SicBoRoundRecord(roundIndex, rollResult.Die1, rollResult.Die2, rollResult.Die3,
            rollResult.TotalStaked, returned, bankroll.Balance);
        onRoundResolved?.Invoke(record);
        roundIndex++;
        int[] rollTargets = { 50, 100, 250, 500, 1000 };
        foreach (var t in rollTargets)
            if (roundIndex == t && roundMilestonesFired.Add(t))
                milestoneToast?.Show($"{t} Rolls This Session", UIFactory.Accent, fontSize: 26);
    }

    public static Color NetColor(long net) => net > 0 ? UIFactory.Positive : net < 0 ? UIFactory.Negative : UIFactory.Accent;

    void HighlightWinningTiles(SicBoRollResult result, Dictionary<SicBoBetType, long> betsAtRoll)
    {
        foreach (var kv in tiles)
        {
            bool wouldWin = SicBoResolver.WouldWin(kv.Key, result.Die1, result.Die2, result.Die3);
            bool playerBet = betsAtRoll.ContainsKey(kv.Key);
            Color bc = kv.Value.BaseColor;
            // Your winning bets glow gold; every other winning area lights up so the result reads at a glance
            kv.Value.FillImg.color = wouldWin && playerBet ? Color.Lerp(bc, WinGlow, 0.75f)
                : wouldWin ? Color.Lerp(bc, Color.white, 0.35f)
                : bc;
        }
        StopCoroutine(nameof(ResetTileHighlights));
        StartCoroutine(nameof(ResetTileHighlights));
    }

    IEnumerator ResetTileHighlights()
    {
        yield return new WaitForSeconds(3f);
        foreach (var kv in tiles) kv.Value.FillImg.color = kv.Value.BaseColor;
    }

    // --- Buttons / badges ---

    void BuildActionButtons()
    {
        const float y = 295f;
        clearBaseColor = UIFactory.RedBet;
        rollBaseColor  = UIFactory.Positive;
        repeatBaseColor = UIFactory.AccentDim;

        undoButton     = UIFactory.MakeButton(tableRoot, "UndoBtn",      new Vector2(-230f, y), new Vector2(110, 46), "UNDO",        UIFactory.AccentDim, UndoLastBetAction, 13, pixelFont: true);
        clearBetButton = UIFactory.MakeButton(tableRoot, "ClearBetBtn",  new Vector2(-105f, y), new Vector2(130, 46), "CLEAR BET",  clearBaseColor,  OnClearBetClicked, 13, pixelFont: true);
        rollButton     = UIFactory.MakeButton(tableRoot, "RollBtn",      new Vector2(  45f, y), new Vector2(150, 50), "ROLL",        rollBaseColor,   OnRollClicked, 20, pixelFont: true);
        repeatButton   = UIFactory.MakeButton(tableRoot, "RepeatBetBtn", new Vector2( 195f, y), new Vector2(130, 46), "REPEAT BET", repeatBaseColor, OnRepeatBetClicked, 12, pixelFont: true);
    }

    // One framed line under the action buttons
    void BuildTakeDownTip()
    {
        var tip = UIFactory.MakePanel(tableRoot, "TakeDownTipBg", new Vector2(0f, 238f), new Vector2(360f, 26f), UIFactory.PanelDarker, shadow: false);
        UIFactory.AddSharpFrame(tip, UIFactory.AccentDim, square: true);
        UIFactory.MakeText(tableRoot, "TakeDownTip", new Vector2(0f, 238f), 14, TextAnchor.MiddleCenter,
            new Vector2(350f, 24f), UIFactory.TextLight).text = "RIGHT-CLICK a bet to take it down";
    }

    // Slim badge under the HUD, only shown on a 2+ win streak
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
        textGO.AddComponent<RectTransform>().sizeDelta = new Vector2(250, 32);
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

    void RefreshActionButtons()
    {
        long onTable = currentRound.TotalOnTable();
        bool canRoll = !rolling && onTable > 0;
        UIFactory.SetButtonState(rollButton,      rollBaseColor,   canRoll);
        UIFactory.SetButtonState(clearBetButton,  clearBaseColor,  !rolling && onTable > 0);
        UIFactory.SetButtonState(repeatButton,    repeatBaseColor, !rolling && lastBets.Count > 0);
        UIFactory.SetButtonState(undoButton,      UIFactory.AccentDim, !rolling && undoStack.Count > 0);
        if (canRoll && !rollWasEnabled) JuiceTweens.Pulse(this, rollButton.GetComponent<RectTransform>(), peakScale: 1.15f, duration: 0.25f);
        rollWasEnabled = canRoll;
    }

    public void SetRoundIndex(int index) => roundIndex = index;

    // Bankroll reset wipes the wallet, so chips on the felt are dropped rather than refunded.
    public void ResetRound()
    {
        rolling = false;
        pendingResult = null;
        winStreak = 0;
        bestRoundNet = 0;
        doubledMilestoneFired = false;
        roundMilestonesFired.Clear();
        streakBadgeGO?.SetActive(false);
        undoStack.Clear();
        lastBets.Clear();
        currentRound.ClearAllBets();
        RefreshAllTiles();
        statusText.color = UIFactory.Accent;
        statusText.text  = "Place bets, then ROLL";
        RefreshActionButtons();
    }
}
