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

    public static Font Font => cachedFont ??= Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");

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

    public static Text MakeSectionHeader(Transform parent, string label, Vector2 anchoredPos, Vector2 sizeDelta)
    {
        var t = MakeText(parent, $"Header_{label}", anchoredPos, 15, TextAnchor.MiddleLeft, sizeDelta, Accent, FontStyle.Bold);
        t.text = label.ToUpperInvariant();
        return t;
    }

    public static Button MakeButton(Transform parent, string name, Vector2 anchoredPos, Vector2 size,
        string label, Color color, UnityEngine.Events.UnityAction onClick, int fontSize = 18)
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

        MakeText(go.transform, "Label", Vector2.zero, fontSize, sizeDelta: size, color: TextLight, style: FontStyle.Bold);
        var labelText = go.GetComponentInChildren<Text>();
        labelText.text = label;
        return btn;
    }

    static Sprite circleSprite;
    public static Sprite Circle()
    {
        if (circleSprite != null) return circleSprite;
        // 64px was fine for roulette's ~40-64px badges/chips, but blackjack's bet
        // spot displays this same sprite at up to 160px — noticeably blurry at that
        // scale. 256px covers every current use with room to spare.
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
        circleSprite = Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 100f);
        return circleSprite;
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
        return btn;
    }

    // Plain MakePanel's fill color sits so close to the scene's near-black background
    // that it barely reads as a panel at all — this adds a thin gold border (a
    // slightly larger rect behind the fill) so it visibly frames its content instead
    // of blending into the backdrop.
    public static GameObject MakeFramedPanel(Transform parent, string name, Vector2 anchoredPos, Vector2 size, Color color, Color? borderColor = null, float borderThickness = 3f)
    {
        MakePanel(parent, name + "_Border", anchoredPos, size + new Vector2(borderThickness, borderThickness) * 2f, borderColor ?? AccentDim, shadow: true);
        return MakePanel(parent, name, anchoredPos, size, color, shadow: false);
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
