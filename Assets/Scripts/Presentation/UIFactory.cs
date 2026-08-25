using TMPro;
using UnityEngine;
using UnityEngine.UI;

// Shared legacy-uGUI construction helpers. Generates a rounded-rect sprite at
// runtime (no art assets needed) so every panel/button gets soft corners and a
// drop shadow instead of flat unstyled rectangles.
public static class UIFactory
{
    // Shared silver/blue palette (matches the Kenney frame art) so every panel
    // controller reads consistently.
    public static readonly Color PanelDark = new Color(0.07f, 0.08f, 0.1f, 0.88f);
    public static readonly Color PanelDarker = new Color(0.05f, 0.055f, 0.07f, 0.92f);
    public static readonly Color Accent = new Color(0.72f, 0.79f, 0.88f);
    public static readonly Color AccentDim = new Color(0.4f, 0.46f, 0.56f);
    public static readonly Color TextLight = new Color(0.93f, 0.92f, 0.88f);
    public static readonly Color TextDim = new Color(0.68f, 0.66f, 0.6f);
    public static readonly Color Positive = new Color(0.35f, 0.78f, 0.4f);
    public static readonly Color Negative = new Color(0.85f, 0.32f, 0.3f);
    public static readonly Color FeltGreen = new Color(0.09f, 0.32f, 0.18f);
    public static readonly Color FeltGreenDark = new Color(0.06f, 0.22f, 0.12f);
    public static readonly Color RedBet = new Color(0.68f, 0.14f, 0.14f);
    public static readonly Color BlackBet = new Color(0.1f, 0.1f, 0.12f);
    // Lightened from (0.22,0.22,0.24) — that shade sat too close to the near-black
    // felt/panel backgrounds to read as "disabled" rather than "invisible."
    public static readonly Color DisabledButton = new Color(0.3f, 0.3f, 0.34f, 0.85f);

    // Swaps a button's own Image color directly between its base color and a flat
    // grey — Unity's built-in ColorBlock.disabledColor tint is too subtle to read
    // as "disabled" against this project's palette. One shared helper instead of
    // every controller keeping its own copy (blackjack and baccarat each had one).
    public static void SetButtonState(Button btn, Color baseColor, bool enabled)
    {
        btn.interactable = enabled;
        var img = btn.GetComponent<Image>();
        if (img != null) img.color = enabled ? baseColor : DisabledButton;
    }

    // Thousands separators on every balance/bet/history number in the project —
    // "11000" reads slower than "11,000" once a session's run a while.
    public static string FormatMoney(long amount) => amount.ToString("N0", System.Globalization.CultureInfo.InvariantCulture);

    // Compact form for tight spaces (P/L grid cells, ~30px) where "+1,250" would
    // overflow or get shrunk unreadably small — "1.3k" instead. Full FormatMoney is
    // still used everywhere there's room (HUD, history tables) since it's more precise.
    public static string FormatMoneyCompact(long amount)
    {
        long abs = System.Math.Abs(amount);
        string sign = amount < 0 ? "-" : "";
        if (abs < 1000) return $"{sign}{abs}";
        if (abs < 1000000) return $"{sign}{abs / 1000f:0.#}k";
        return $"{sign}{abs / 1000000f:0.#}m";
    }

    static Sprite roundedSprite;
    static Font cachedFont;
    static TMP_FontAsset pixelFont;
    static bool pixelFontLoadAttempted;

    public static Font Font => cachedFont ??= Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");

    // Drop-in art location: whatever pre-rendered border/panel/shape PNGs the project
    // is currently using — Heat UI's for now, source-agnostic by design. Swapping to
    // a different art pack (or hand-provided files) means replacing the files in
    // Assets/Resources/UI/ and, if the shapes changed meaningfully, retuning the
    // import settings below (see CircleFrameSprite/SquareFrameSprite for what each
    // file needs) — nothing else in this file, or any caller, has to change.
    // Required files: RadialFilled.png (solid filled circle — see Circle()),
    // RadialRing.png (circular ring/outline), SquareRing.png (rect ring/outline).
    static Sprite circleFrameSprite, squareFrameSprite;

    public static Sprite CircleFrameSprite() => circleFrameSprite ??= Resources.Load<Sprite>("UI/RadialRing");
    public static Sprite SquareFrameSprite() => squareFrameSprite ??= Resources.Load<Sprite>("UI/SquareRing");

    // Adds a crisp pre-rendered ring/frame on top of an existing panel/chip/button,
    // replacing the soft, slightly hazy look of Unity's built-in Outline shader
    // component this project used everywhere before. square=true for rect panels
    // and buttons (SquareRing), false for circular elements (RadialRing).
    public static Image AddSharpFrame(GameObject target, Color color, bool square, float inset = 0f)
    {
        var frameGO = new GameObject("SharpFrame");
        frameGO.transform.SetParent(target.transform, false);
        var img = frameGO.AddComponent<Image>();
        img.sprite = square ? SquareFrameSprite() : CircleFrameSprite();
        // Square frames are Kenney's actual 9-slice art (proper sprite border baked
        // in via its import settings) so Sliced renders correctly across this
        // project's whole size range. Circular ones are still the older procedural/
        // Heat-derived sprite with no real border metadata, so those stay a plain
        // stretch (Simple) — 9-slicing them was what caused the solid-fill bug
        // earlier this session.
        img.type = square ? Image.Type.Sliced : Image.Type.Simple;
        img.color = color;
        img.raycastTarget = false;
        var targetRt = target.GetComponent<RectTransform>();
        var rt = frameGO.GetComponent<RectTransform>();
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = new Vector2(inset, inset);
        rt.offsetMax = new Vector2(-inset, -inset);
        return img;
    }

    // Hero/header pixel font — only used for titles and section headers (see
    // MakeHeroTitle/MakeSectionHeader). Dense functional text (number grids, card
    // ranks, live status messages) stays on the legacy font above for legibility at
    // small sizes.
    public static TMP_FontAsset PixelFont
    {
        get
        {
            if (pixelFont == null && !pixelFontLoadAttempted)
            {
                pixelFont = Resources.Load<TMP_FontAsset>("Fonts/BoldPixels SDF");
                pixelFontLoadAttempted = true;
            }
            return pixelFont;
        }
    }

    static readonly VertexGradient TitleGradient = new VertexGradient(
        new Color(0.88f, 0.94f, 1f), new Color(0.88f, 0.94f, 1f),
        new Color(0.55f, 0.65f, 0.8f), new Color(0.55f, 0.65f, 0.8f));

    // Big pixel-font screen title with the same silver-blue vertex gradient as the
    // main menu's "CASINO SIM" — used once per game screen for its hero header.
    public static TextMeshProUGUI MakeHeroTitle(Transform parent, string name, Vector2 anchoredPos, string text, float fontSize = 30)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        var t = go.AddComponent<TextMeshProUGUI>();
        if (PixelFont != null) t.font = PixelFont;
        t.fontSize = fontSize;
        t.alignment = TextAlignmentOptions.Center;
        t.enableVertexGradient = true;
        t.colorGradient = TitleGradient;
        t.text = text;
        var rt = go.GetComponent<RectTransform>();
        rt.sizeDelta = new Vector2(500, 60);
        rt.anchoredPosition = anchoredPos;
        return t;
    }

    // Procedural 9-sliced rounded rect — corner alpha falls off with a couple of
    // pixels of antialiasing so it doesn't look jagged when scaled up.
    // Kenney's ornate panel shape (Assets/Resources/UI/PanelFill.png) — pure white
    // alpha-masked art, same as the procedural sprite this replaces, so every
    // existing Image.color tint (bet-cell red/black, chip colors, per-game button
    // colors) still applies cleanly with zero muddying. Falls back to the old
    // procedural rounded rect if the file's ever missing.
    public static Sprite RoundedRect()
    {
        if (roundedSprite != null) return roundedSprite;
        var kenneyPanel = Resources.Load<Sprite>("UI/PanelFill");
        if (kenneyPanel != null)
        {
            roundedSprite = kenneyPanel;
            return roundedSprite;
        }
        const int size = 64, radius = 18;
        var tex = new Texture2D(size, size, TextureFormat.RGBA32, false) { filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp };
        var pixels = new Color[size * size];
        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                int cx = Mathf.Clamp(x, radius, size - radius - 1);
                int cy = Mathf.Clamp(y, radius, size - radius - 1);
                float dist = Vector2.Distance(new Vector2(x, y), new Vector2(cx, cy));
                float alpha = Mathf.Clamp01(radius - dist + 0.5f);
                pixels[y * size + x] = new Color(1f, 1f, 1f, alpha);
            }
        }
        tex.SetPixels(pixels);
        tex.Apply();
        roundedSprite = Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 100f, 0,
            SpriteMeshType.FullRect, new Vector4(radius, radius, radius, radius));
        return roundedSprite;
    }

    public static Text MakeText(Transform parent, string name, Vector2 anchoredPos, int size,
        TextAnchor alignment = TextAnchor.MiddleCenter, Vector2? sizeDelta = null, Color? color = null, FontStyle style = FontStyle.Normal)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        var t = go.AddComponent<Text>();
        t.font = Font;
        t.alignment = alignment;
        t.fontSize = size;
        t.fontStyle = style;
        t.color = color ?? TextLight;
        var rt = go.GetComponent<RectTransform>();
        rt.sizeDelta = sizeDelta ?? new Vector2(400, 60);
        rt.anchoredPosition = anchoredPos;
        return t;
    }

    public static TextMeshProUGUI MakeSectionHeader(Transform parent, string label, Vector2 anchoredPos, Vector2 sizeDelta)
    {
        var go = new GameObject($"Header_{label}");
        go.transform.SetParent(parent, false);
        var t = go.AddComponent<TextMeshProUGUI>();
        if (PixelFont != null) t.font = PixelFont;
        t.fontSize = 16;
        t.color = Accent;
        t.alignment = TextAlignmentOptions.MidlineLeft;
        t.text = label.ToUpperInvariant();
        var rt = go.GetComponent<RectTransform>();
        rt.sizeDelta = sizeDelta;
        rt.anchoredPosition = anchoredPos;
        return t;
    }

    // One shared mute toggle every scene's top bar can drop in — SoundManager holds
    // the actual state (AudioListener.volume, persisted via PlayerPrefs), this just
    // builds the button and keeps its own label in sync with it.
    public static Button MakeMuteButton(Transform parent, Vector2 anchoredPos)
    {
        var btn = MakeButton(parent, "MuteBtn", anchoredPos, new Vector2(90, 32), "", PanelDarker, null, 12, pixelFont: true);
        var label = btn.GetComponentInChildren<TMPro.TextMeshProUGUI>();
        void Refresh() => label.text = global::SoundManager.IsMuted ? "MUTED" : "SOUND";
        Refresh();
        btn.onClick.AddListener(() => { global::SoundManager.ToggleMute(); Refresh(); });
        return btn;
    }

    // flatFill skips the Kenney sliced-border sprite entirely — for small cells
    // (roulette's corner/split/street/six-line spots, ~14-28px), the frame's fixed
    // ~14px border eats nearly the whole button, leaving text sitting on top of
    // border art instead of a clean fill. Flat color reads far better at that size.
    public static Button MakeButton(Transform parent, string name, Vector2 anchoredPos, Vector2 size,
        string label, Color color, UnityEngine.Events.UnityAction onClick, int fontSize = 18, bool pixelFont = false, bool flatFill = false)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        var img = go.AddComponent<Image>();
        if (flatFill)
        {
            img.sprite = null;
            img.type = Image.Type.Simple;
        }
        else
        {
            img.sprite = RoundedRect();
            img.type = Image.Type.Sliced;
        }
        img.color = color;
        var rt = go.GetComponent<RectTransform>();
        rt.sizeDelta = size;
        rt.anchoredPosition = anchoredPos;

        var shadow = go.AddComponent<Shadow>();
        shadow.effectColor = new Color(0, 0, 0, 0.45f);
        shadow.effectDistance = new Vector2(0, -2);

        // The sliced border art above is tinted by the same fill color as the rest
        // of the button, so a dark fill (PanelDarker, and DisabledButton once
        // SetButtonState swaps to it later) renders a dark border too — invisible
        // against this project's near-black backgrounds ("black on black"). A
        // separate frame overlay, tinted a fixed light silver instead of the fill
        // color, keeps the edge readable no matter what color the fill is now or
        // becomes later. Skipped only for flatFill (tiny cells already crowded).
        if (!flatFill)
            AddSharpFrame(go, AccentDim, square: true);

        var btn = go.AddComponent<Button>();
        btn.targetGraphic = img;
        var colors = btn.colors;
        colors.normalColor = Color.white;
        colors.highlightedColor = new Color(1.15f, 1.15f, 1.15f);
        colors.pressedColor = new Color(0.75f, 0.75f, 0.75f);
        colors.selectedColor = Color.white;
        colors.colorMultiplier = 1f;
        btn.colors = colors;
        if (onClick != null) btn.onClick.AddListener(onClick);

        if (pixelFont && PixelFont != null)
        {
            var labelGO = new GameObject("Label");
            labelGO.transform.SetParent(go.transform, false);
            var tmp = labelGO.AddComponent<TextMeshProUGUI>();
            tmp.font = PixelFont;
            tmp.text = label;
            tmp.color = TextLight;
            tmp.alignment = TextAlignmentOptions.Center;
            // Autosize instead of a fixed fontSize — the pixel font's glyph metrics
            // differ from the legacy font this replaced, so a fixed size risks
            // clipping/overflow on buttons whose box was tuned for the old font.
            tmp.enableAutoSizing = true;
            tmp.fontSizeMin = 8;
            tmp.fontSizeMax = fontSize + 4;
            var labelRt = labelGO.GetComponent<RectTransform>();
            labelRt.anchorMin = Vector2.zero;
            labelRt.anchorMax = Vector2.one;
            labelRt.offsetMin = new Vector2(10, 6);
            labelRt.offsetMax = new Vector2(-10, -6);
        }
        else
        {
            // Same margin reasoning as the pixel-font path above — full button size
            // let text sit flush against (or under) the sliced border art.
            var inset = new Vector2(Mathf.Max(size.x - 16f, 4f), Mathf.Max(size.y - 10f, 4f));
            MakeText(go.transform, "Label", Vector2.zero, fontSize, sizeDelta: inset, color: TextLight, style: FontStyle.Bold);
            var labelText = go.GetComponentInChildren<Text>();
            labelText.text = label;
        }

        go.AddComponent<DOTweenButtonFX>();
        return btn;
    }

    static Sprite circleSprite;
    // A pre-rendered filled circle from Assets/Resources/UI/RadialFilled.png —
    // sharper, better-antialiased than the procedural version this used to generate
    // at runtime. Falls back to that procedural circle if the file is ever missing
    // (e.g. mid-swap to a new art pack), so nothing breaks in the meantime.
    public static Sprite Circle()
    {
        if (circleSprite != null) return circleSprite;
        circleSprite = Resources.Load<Sprite>("UI/RadialFilled");
        if (circleSprite == null) circleSprite = ProceduralCircle();
        return circleSprite;
    }

    static Sprite ProceduralCircle()
    {
        const int size = 256;
        var tex = new Texture2D(size, size, TextureFormat.RGBA32, false) { filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp };
        var pixels = new Color[size * size];
        Vector2 center = new Vector2(size / 2f, size / 2f);
        float radius = size / 2f - 1f;
        for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float dist = Vector2.Distance(new Vector2(x, y), center);
                pixels[y * size + x] = new Color(1f, 1f, 1f, Mathf.Clamp01(radius - dist + 0.5f));
            }
        tex.SetPixels(pixels);
        tex.Apply();
        return Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 100f);
    }

    // Casino chip look: outer accent ring + inner fill disc + value label, built from
    // two stacked circle sprites rather than a flat button — reads as a chip, not a box.
    public static Button MakeChip(Transform parent, string name, Vector2 anchoredPos, float diameter,
        string label, Color fillColor, UnityEngine.Events.UnityAction onClick)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        var ringImg = go.AddComponent<Image>();
        ringImg.sprite = Circle();
        ringImg.color = Accent;
        var rt = go.GetComponent<RectTransform>();
        rt.sizeDelta = new Vector2(diameter, diameter);
        rt.anchoredPosition = anchoredPos;

        var shadow = go.AddComponent<Shadow>();
        shadow.effectColor = new Color(0, 0, 0, 0.5f);
        shadow.effectDistance = new Vector2(0, -2);

        var fillGO = new GameObject("Fill");
        fillGO.transform.SetParent(go.transform, false);
        var fillImg = fillGO.AddComponent<Image>();
        fillImg.sprite = Circle();
        fillImg.color = fillColor;
        var fillRt = fillGO.GetComponent<RectTransform>();
        fillRt.sizeDelta = new Vector2(diameter - 10, diameter - 10);
        fillRt.anchoredPosition = Vector2.zero;

        var btn = go.AddComponent<Button>();
        btn.targetGraphic = ringImg;
        var colors = btn.colors;
        colors.highlightedColor = new Color(1.15f, 1.15f, 1.15f);
        colors.pressedColor = new Color(0.8f, 0.8f, 0.8f);
        btn.colors = colors;
        if (onClick != null) btn.onClick.AddListener(onClick);

        var text = MakeText(fillGO.transform, "Label", Vector2.zero, 16, sizeDelta: new Vector2(diameter - 10, diameter - 10),
            color: TextLight, style: FontStyle.Bold);
        text.text = label;

        // No extra sharp-frame overlay here (unlike other chip-shaped elements) —
        // the outer ring IS the selected/unselected signal (ChipSelectorUI recolors
        // it directly), and a fixed-color overlay on top would wash that out.
        go.AddComponent<DOTweenButtonFX>();
        return btn;
    }

    // Plain MakePanel's fill color sits so close to the scene's near-black background
    // that it barely reads as a panel at all — this adds a thin gold border (a
    // slightly larger rect behind the fill) so it visibly frames its content instead
    // of blending into the backdrop.
    // Crisp Heat-UI frame overlay instead of the plain slightly-larger colored rect
    // this used to draw behind the panel to fake a border — same call signature/
    // return value, every existing caller gets the sharper look for free.
    public static GameObject MakeFramedPanel(Transform parent, string name, Vector2 anchoredPos, Vector2 size, Color color, Color? borderColor = null, float borderThickness = 3f)
    {
        var panel = MakePanel(parent, name, anchoredPos, size, color, shadow: true);
        AddSharpFrame(panel, borderColor ?? AccentDim, square: true, inset: -borderThickness);
        return panel;
    }

    public static GameObject MakePanel(Transform parent, string name, Vector2 anchoredPos, Vector2 size, Color color, bool shadow = true)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        var img = go.AddComponent<Image>();
        img.sprite = RoundedRect();
        img.type = Image.Type.Sliced;
        img.color = color;
        var rt = go.GetComponent<RectTransform>();
        rt.sizeDelta = size;
        rt.anchoredPosition = anchoredPos;

        if (shadow)
        {
            var sh = go.AddComponent<Shadow>();
            sh.effectColor = new Color(0, 0, 0, 0.5f);
            sh.effectDistance = new Vector2(0, -3);
        }
        return go;
    }
}
