using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

// Pixel-art pictures behind the game names on the menu buttons — a wheel for roulette, dice for
// craps, cards for the card games — so a game is recognisable without reading its name. Drawn in
// code at 120x40 art pixels (3 screen pixels each on a 360x120 button), point-filtered.
public static class GameIcons
{
    const int W = 120, H = 40;
    const int Lift = 5; // pictures sit a few rows up, clear of the name plate
    static readonly Dictionary<string, Texture2D> cache = new Dictionary<string, Texture2D>();

    // Puts the picture behind the button's name, and moves the name onto a dark plate along the
    // bottom so it stays readable over the picture
    public static void AddArt(GameObject button, string sceneName)
    {
        var tex = Get(sceneName);
        if (tex == null) return;
        var label = button.GetComponentInChildren<TextMeshProUGUI>();
        int labelIndex = label != null ? label.transform.GetSiblingIndex() : button.transform.childCount;

        var artGO = new GameObject("Art");
        artGO.transform.SetParent(button.transform, false);
        var art = artGO.AddComponent<RawImage>();
        art.texture = tex;
        art.raycastTarget = false;
        Stretch(art.rectTransform, 0f, 1f);

        var plateGO = new GameObject("NamePlate");
        plateGO.transform.SetParent(button.transform, false);
        var plate = plateGO.AddComponent<Image>();
        plate.color = new Color(0f, 0f, 0f, 0.62f);
        plate.raycastTarget = false;
        Stretch(plate.rectTransform, 0f, PlateHeight);

        artGO.transform.SetSiblingIndex(labelIndex);
        plateGO.transform.SetSiblingIndex(labelIndex + 1);
        if (label != null)
        {
            Stretch(label.rectTransform, 0f, PlateHeight);
            label.rectTransform.offsetMin += new Vector2(4f, 0f);
            label.rectTransform.offsetMax -= new Vector2(4f, 2f);
        }
    }

    const float PlateHeight = 0.36f; // share of the button's height

    static void Stretch(RectTransform rt, float yMin, float yMax)
    {
        rt.anchorMin = new Vector2(0f, yMin);
        rt.anchorMax = new Vector2(1f, yMax);
        rt.offsetMin = new Vector2(4f, 4f);
        rt.offsetMax = new Vector2(-4f, yMax >= 1f ? -4f : 0f);
    }

    public static Texture2D Get(string sceneName)
    {
        if (cache.TryGetValue(sceneName, out var t) && t != null) return t;
        var c = new Canvas2D();
        switch (sceneName)
        {
            case "Roulette": DrawRoulette(c); break;
            case "Blackjack": c.Card(38, 5, 'A', Suit.Spade); c.Card(62, 7, 'K', Suit.Heart); break;
            case "Baccarat":
                c.Disc(16, 20, 9, PlayerBlue); c.Disc(104, 20, 9, BankerRed);
                c.Card(38, 5, '4', Suit.Diamond); c.Card(62, 7, '5', Suit.Club); break;
            case "CraplessCraps": c.Die(34, 9, 22, 6, Gold, GoldDark); c.Die(64, 7, 22, 5, Gold, GoldDark); break;
            case "NormalCraps": c.Die(34, 9, 22, 3, Ivory, IvoryDark); c.Die(64, 7, 22, 4, Ivory, IvoryDark); break;
            case "SicBo":
                c.Dome(60, 38, 28);
                c.Die(29, 16, 18, 4, Ivory, IvoryDark); c.Die(51, 12, 18, 5, Ivory, IvoryDark); c.Die(73, 16, 18, 6, Ivory, IvoryDark); break;
            case "ThreeCardPoker":
                c.Card(26, 6, 'A', Suit.Spade); c.Card(50, 5, 'K', Suit.Spade); c.Card(74, 6, 'Q', Suit.Spade);
                c.Chip(103, 22, 8); break;
            case "ThreePictures":
                c.Card(26, 6, 'J', Suit.Crown); c.Card(50, 5, 'Q', Suit.Crown); c.Card(74, 6, 'K', Suit.Crown); break;
            default: return null;
        }
        var tex = c.ToTexture();
        cache[sceneName] = tex;
        return tex;
    }

    static readonly Color32 Ivory = new Color32(236, 232, 220, 255), IvoryDark = new Color32(170, 164, 150, 255);
    static readonly Color32 Gold = new Color32(240, 196, 70, 255), GoldDark = new Color32(170, 120, 30, 255);
    static readonly Color32 PipCol = new Color32(20, 20, 24, 255);
    static readonly Color32 PlayerBlue = new Color32(60, 120, 230, 255), BankerRed = new Color32(210, 50, 45, 255);
    static readonly Color32 Outline = new Color32(16, 16, 20, 255);

    static void DrawRoulette(Canvas2D c)
    {
        const int cx = 60, cy = 22; // drawn 5 rows lower so the whole wheel clears the top after the lift
        var wood = new Color32(110, 56, 24, 255);
        var woodLight = new Color32(150, 84, 38, 255);
        var red = new Color32(200, 30, 30, 255);
        var black = new Color32(24, 24, 28, 255);
        var green = new Color32(20, 150, 60, 255);
        for (int y = 0; y < H; y++)
        for (int x = 0; x < W; x++)
        {
            float dx = x + 0.5f - cx, dy = y + 0.5f - cy, r = Mathf.Sqrt(dx * dx + dy * dy);
            r /= 0.85f; // a touch smaller than the full height
            if (r > 19.5f) continue;
            float ang = Mathf.Atan2(dy, dx) * Mathf.Rad2Deg + 180f;
            if (r > 17.5f) c.Set(x, y, Outline);
            else if (r > 15f) c.Set(x, y, dy > 0 ? woodLight : wood);
            else if (r > 10f)
            {
                int seg = Mathf.FloorToInt(ang / 20f);
                c.Set(x, y, seg == 4 ? green : seg % 2 == 0 ? red : black);
            }
            else if (r > 9f) c.Set(x, y, new Color32(200, 170, 90, 255));
            else c.Set(x, y, r < 2.5f ? new Color32(230, 200, 110, 255) : wood);
        }
        // Turret arms and the ball
        for (int i = -5; i <= 5; i++) { c.Set(cx + i, cy, new Color32(210, 210, 220, 255)); c.Set(cx, cy + i, new Color32(210, 210, 220, 255)); }
        c.Disc(cx + 8, cy + 7, 1.6f, new Color32(250, 250, 250, 255));
    }

    enum Suit { Spade, Heart, Diamond, Club, Crown }

    // A tiny RGBA canvas with the few drawing helpers the pictures need; y = 0 is the top row
    class Canvas2D
    {
        readonly Color32[] px = new Color32[W * H];

        public void Set(int x, int y, Color32 c)
        {
            if (x < 0 || x >= W || y - Lift < 0 || y - Lift >= H) return;
            px[(H - 1 - (y - Lift)) * W + x] = c;
        }

        public void Rect(int x, int y, int w, int h, Color32 c)
        {
            for (int j = y; j < y + h; j++)
            for (int i = x; i < x + w; i++) Set(i, j, c);
        }

        public void Disc(float cx, float cy, float r, Color32 c)
        {
            for (int y = (int)(cy - r - 1); y <= cy + r + 1; y++)
            for (int x = (int)(cx - r - 1); x <= cx + r + 1; x++)
            {
                float dx = x + 0.5f - cx, dy = y + 0.5f - cy;
                if (dx * dx + dy * dy <= r * r) Set(x, y, c);
            }
        }

        // Die face-on: outline, body, lighter top-left edge, pips
        public void Die(int x, int y, int s, int face, Color32 body, Color32 shade)
        {
            Rect(x + 1, y, s - 2, s, Outline); Rect(x, y + 1, s, s - 2, Outline);
            Rect(x + 1, y + 1, s - 2, s - 2, body);
            Rect(x + 1, y + s - 3, s - 2, 2, shade);
            Rect(x + s - 3, y + 1, 2, s - 2, shade);
            int p = Mathf.Max(2, s / 7);
            int a = x + s / 4 - p / 2 + 1, m = x + s / 2 - p / 2, b = x + 3 * s / 4 - p / 2 - 1;
            int ay = y + s / 4 - p / 2 + 1, my = y + s / 2 - p / 2, by = y + 3 * s / 4 - p / 2 - 1;
            void Pip(int px0, int py0) => Rect(px0, py0, p, p, PipCol);
            if (face == 1 || face == 3 || face == 5) Pip(m, my);
            if (face >= 2) { Pip(a, ay); Pip(b, by); }
            if (face >= 4) { Pip(b, ay); Pip(a, by); }
            if (face == 6) { Pip(a, my); Pip(b, my); }
        }

        // Card: 22x30, rank top-left and bottom-right, big suit in the middle
        public void Card(int x, int y, char rank, Suit suit)
        {
            const int cw = 22, ch = 30;
            Rect(x + 1, y, cw - 2, ch, Outline); Rect(x, y + 1, cw, ch - 2, Outline);
            Rect(x + 1, y + 1, cw - 2, ch - 2, Ivory);
            var col = suit == Suit.Heart || suit == Suit.Diamond ? new Color32(200, 30, 30, 255)
                : suit == Suit.Crown ? new Color32(200, 150, 30, 255) : PipCol;
            Glyph(x + 3, y + 3, rank, col);
            Symbol(x + cw / 2 - 5, y + ch / 2 - 4, suit, col);
        }

        public void Chip(int cx, int cy, int r)
        {
            Disc(cx, cy, r + 1, Outline);
            Disc(cx, cy, r, new Color32(40, 90, 200, 255));
            for (int i = 0; i < 8; i++)
            {
                float a = i * Mathf.PI / 4f;
                Disc(cx + Mathf.Cos(a) * (r - 1.5f), cy + Mathf.Sin(a) * (r - 1.5f), 1.2f, Ivory);
            }
            Disc(cx, cy, r - 4, new Color32(60, 110, 220, 255));
        }

        // Clear shaker dome for Sic Bo: a pale arc over the dice
        public void Dome(int cx, int baseY, int r)
        {
            var glass = new Color32(150, 200, 230, 255);
            for (int y = baseY - r; y <= baseY; y++)
            for (int x = cx - r; x <= cx + r; x++)
            {
                float dx = x + 0.5f - cx, dy = y + 0.5f - baseY, d = Mathf.Sqrt(dx * dx + dy * dy);
                if (d <= r && d > r - 1.6f) Set(x, y, glass);
            }
            Rect(cx - r - 2, baseY, 2 * r + 5, 2, new Color32(90, 90, 100, 255));
        }

        // 10x10 suit symbols
        void Symbol(int x, int y, Suit s, Color32 c)
        {
            string[] art = s switch
            {
                Suit.Heart => new[] { ".XX..XX..", "XXXXXXXX.", "XXXXXXXX.", "XXXXXXXX.", ".XXXXXX..", "..XXXX...", "...XX....", "........." },
                Suit.Diamond => new[] { "....X....", "...XXX...", "..XXXXX..", ".XXXXXXX.", "..XXXXX..", "...XXX...", "....X....", "........." },
                Suit.Club => new[] { "...XXX...", "...XXX...", ".XX.X.XX.", "XXXXXXXXX", "XXXXXXXXX", ".XX.X.XX.", "....X....", "...XXX..." },
                Suit.Crown => new[] { "X...X...X", "XX.XXX.XX", "XXXXXXXXX", "XXXXXXXXX", "XXXXXXXXX", ".........", "XXXXXXXXX", "........." },
                _ => new[] { "....X....", "...XXX...", "..XXXXX..", ".XXXXXXX.", "XXXXXXXXX", "XX.XXX.XX", "....X....", "...XXX..." },
            };
            for (int j = 0; j < art.Length; j++)
            for (int i = 0; i < art[j].Length; i++)
                if (art[j][i] == 'X') Set(x + i, y + j, c);
        }

        // 3x5 rank glyphs
        void Glyph(int x, int y, char ch, Color32 c)
        {
            string[] g = ch switch
            {
                'A' => new[] { ".X.", "X.X", "XXX", "X.X", "X.X" },
                'K' => new[] { "X.X", "XX.", "X..", "XX.", "X.X" },
                'Q' => new[] { ".X.", "X.X", "X.X", "XX.", ".XX" },
                'J' => new[] { "..X", "..X", "..X", "X.X", ".X." },
                '4' => new[] { "X.X", "X.X", "XXX", "..X", "..X" },
                '5' => new[] { "XXX", "X..", "XX.", "..X", "XX." },
                _ => new[] { "XXX", "X.X", "X.X", "X.X", "XXX" },
            };
            for (int j = 0; j < 5; j++)
            for (int i = 0; i < 3; i++)
                if (g[j][i] == 'X') Set(x + i, y + j, c);
        }

        public Texture2D ToTexture()
        {
            var tex = new Texture2D(W, H, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp };
            tex.SetPixels32(px);
            tex.Apply(false);
            return tex;
        }
    }
}
