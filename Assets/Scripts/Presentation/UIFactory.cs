using TMPro;
using UnityEngine;
using UnityEngine.UI;

// Shared legacy-uGUI construction helpers. Generates a rounded-rect sprite at
// runtime (no art assets needed) so every panel/button gets soft corners and a
// drop shadow instead of flat unstyled rectangles.
public static class UIFactory
{
    // Shared dark/gold casino palette so every panel controller reads consistently.
    public static readonly Color PanelDark = new Color(0.08f, 0.09f, 0.09f, 0.88f);
    public static readonly Color PanelDarker = new Color(0.05f, 0.06f, 0.06f, 0.92f);
    public static readonly Color Accent = new Color(0.83f, 0.68f, 0.21f);
    public static readonly Color AccentDim = new Color(0.55f, 0.45f, 0.15f);
    public static readonly Color TextLight = new Color(0.93f, 0.92f, 0.88f);
    public static readonly Color TextDim = new Color(0.68f, 0.66f, 0.6f);
    public static readonly Color Positive = new Color(0.35f, 0.78f, 0.4f);
    public static readonly Color Negative = new Color(0.85f, 0.32f, 0.3f);
    public static readonly Color FeltGreen = new Color(0.09f, 0.32f, 0.18f);
    public static readonly Color FeltGreenDark = new Color(0.06f, 0.22f, 0.12f);
    public static readonly Color RedBet = new Color(0.68f, 0.14f, 0.14f);
    public static readonly Color BlackBet = new Color(0.1f, 0.1f, 0.12f);

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
        // Simple (stretch), not Sliced — this project's panels/buttons range from
        // tiny badges to huge felt backdrops, and Sliced's 9-slice border math blew
        // up into a solid fill on anything smaller than ~2x the source border. A
        // plain stretch scales the ring thickness proportionally with the element
        // instead, which reads fine across that whole size range.
        img.type = Image.Type.Simple;
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
                pixelFont = Resources.Load<TMP_FontAsset>("Fonts/ThaleahFat SDF");
                pixelFontLoadAttempted = true;
            }
            return pixelFont;
        }
    }

    static readonly VertexGradient GoldGradient = new VertexGradient(
        new Color(1f, 0.87f, 0.45f), new Color(1f, 0.87f, 0.45f),
        new Color(0.72f, 0.52f, 0.12f), new Color(0.72f, 0.52f, 0.12f));

    // Big pixel-font screen title with the same gold vertex gradient as the main
    // menu's "CASINO SIM" — used once per game screen for its hero header.
    public static TextMeshProUGUI MakeHeroTitle(Transform parent, string name, Vector2 anchoredPos, string text, float fontSize = 30)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        var t = go.AddComponent<TextMeshProUGUI>();
        if (PixelFont != null) t.font = PixelFont;
        t.fontSize = fontSize;
        t.alignment = TextAlignmentOptions.Center;
        t.enableVertexGradient = true;
        t.colorGradient = GoldGradient;
        t.text = text;
        var rt = go.GetComponent<RectTransform>();
        rt.sizeDelta = new Vector2(500, 60);
        rt.anchoredPosition = anchoredPos;
        return t;
    }

    // Procedural 9-sliced rounded rect — corner alpha falls off with a couple of
    // pixels of antialiasing so it doesn't look jagged when scaled up.
    public static Sprite RoundedRect()
    {
        if (roundedSprite != null) return roundedSprite;
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

    public static Button MakeButton(Transform parent, string name, Vector2 anchoredPos, Vector2 size,
        string label, Color color, UnityEngine.Events.UnityAction onClick, int fontSize = 18, bool pixelFont = false)
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

        var shadow = go.AddComponent<Shadow>();
        shadow.effectColor = new Color(0, 0, 0, 0.45f);
        shadow.effectDistance = new Vector2(0, -2);

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
            labelRt.offsetMin = new Vector2(4, 2);
            labelRt.offsetMax = new Vector2(-4, -2);
        }
        else
        {
            MakeText(go.transform, "Label", Vector2.zero, fontSize, sizeDelta: size, color: TextLight, style: FontStyle.Bold);
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
