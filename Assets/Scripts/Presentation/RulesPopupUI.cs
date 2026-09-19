using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

// Content for the HOW TO PLAY popup: two columns — the basics (and any mechanic that needs
// explaining, like hand rankings or draw rules) on the left, a payout table on the right.
// Built with a small fluent API so every game's rules read the same way:
//   new RulesContent().Heading("HOW TO PLAY").Text("...").Payouts().Heading("PAYOUTS").Pay("Straight up", "35 to 1")
public class RulesContent
{
    public enum Kind { Heading, Text, Pay, Note }

    public readonly struct Item
    {
        public readonly Kind Kind;
        public readonly string Left;
        public readonly string Right;

        public Item(Kind kind, string left, string right = null)
        {
            Kind = kind;
            Left = left;
            Right = right;
        }
    }

    public readonly List<Item> LeftColumn = new List<Item>();
    public readonly List<Item> RightColumn = new List<Item>();
    List<Item> current;

    public RulesContent() => current = LeftColumn;

    public RulesContent Heading(string text) { current.Add(new Item(Kind.Heading, text)); return this; }
    public RulesContent Text(string text) { current.Add(new Item(Kind.Text, text)); return this; }
    // One payout / table row: what on the left, what it pays (or does) on the right
    public RulesContent Pay(string bet, string pays) { current.Add(new Item(Kind.Pay, bet, pays)); return this; }
    public RulesContent Note(string text) { current.Add(new Item(Kind.Note, text)); return this; }
    // Everything added after this goes in the right-hand column
    public RulesContent Payouts() { current = RightColumn; return this; }
}

// In-scene "how to play" reference — same dark-scrim modal pattern as
// GameSwitcherPanel. Opened only by the HOW TO PLAY button next to MENU.
public class RulesPopupUI : MonoBehaviour
{
    GameObject panelRoot;

    const float ColumnWidth = 600f, ColumnGap = 40f, MaxPanelHeight = 760f;
    static readonly Color HeadingGold = new Color(1f, 0.85f, 0.1f);
    static readonly Color PayGold = new Color(1f, 0.85f, 0.1f);
    static readonly Color RowShade = new Color(1f, 1f, 1f, 0.04f);

    // Plain text in one centred column (the main menu's CREDITS)
    public void Build(Transform canvas, string title, string bodyText) =>
        Build(canvas, title, new RulesContent().Text(bodyText));

    public void Build(Transform canvas, string title, RulesContent content)
    {
        panelRoot = new GameObject("RulesPopupPanel");
        panelRoot.transform.SetParent(canvas, false);
        var rt = panelRoot.AddComponent<RectTransform>();
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = Vector2.zero;
        rt.offsetMax = Vector2.zero;
        panelRoot.transform.SetAsLastSibling();

        var scrimGO = new GameObject("Scrim");
        scrimGO.transform.SetParent(panelRoot.transform, false);
        var scrimRt = scrimGO.AddComponent<RectTransform>();
        scrimRt.anchorMin = Vector2.zero;
        scrimRt.anchorMax = Vector2.one;
        scrimRt.offsetMin = Vector2.zero;
        scrimRt.offsetMax = Vector2.zero;
        var scrimImg = scrimGO.AddComponent<Image>();
        scrimImg.color = new Color(0.02f, 0.02f, 0.03f, 0.97f);
        var scrimBtn = scrimGO.AddComponent<Button>();
        scrimBtn.transition = Selectable.Transition.None;
        scrimBtn.onClick.AddListener(Hide);

        bool oneColumn = content.RightColumn.Count == 0;
        float panelWidth = oneColumn ? ColumnWidth + 60f : ColumnWidth * 2f + ColumnGap + 60f;

        // One scroll area holding both columns; the panel grows to fit and scrolls past MaxPanelHeight
        var scrollGO = new GameObject("RulesScroll");
        scrollGO.transform.SetParent(panelRoot.transform, false);
        var scrollRt = scrollGO.AddComponent<RectTransform>();
        var scroll = scrollGO.AddComponent<ScrollRect>();
        scroll.horizontal = false;
        scroll.movementType = ScrollRect.MovementType.Clamped;
        scroll.scrollSensitivity = 30f;
        scrollGO.AddComponent<RectMask2D>();
        scrollGO.AddComponent<Image>().color = new Color(0, 0, 0, 0.01f);

        var contentGO = new GameObject("Content");
        contentGO.transform.SetParent(scrollGO.transform, false);
        var contentRt = contentGO.AddComponent<RectTransform>();
        contentRt.anchorMin = contentRt.anchorMax = new Vector2(0.5f, 1f);
        contentRt.pivot = new Vector2(0.5f, 1f);
        scroll.viewport = scrollRt;
        scroll.content = contentRt;

        // Left column explains (plain rows), right column is the payout table (gold amounts)
        float leftHeight = BuildColumn(contentRt, content.LeftColumn, oneColumn ? 0f : -(ColumnWidth + ColumnGap) / 2f, UIFactory.TextLight);
        float rightHeight = BuildColumn(contentRt, content.RightColumn, (ColumnWidth + ColumnGap) / 2f, PayGold);
        float contentHeight = Mathf.Max(leftHeight, rightHeight) + 10f;
        contentRt.sizeDelta = new Vector2(panelWidth - 30f, contentHeight);
        contentRt.anchoredPosition = Vector2.zero;

        float panelHeight = Mathf.Clamp(contentHeight + 30f, 300f, MaxPanelHeight);
        var bodyPanel = UIFactory.MakeFramedPanel(panelRoot.transform, "RulesBodyBg", Vector2.zero, new Vector2(panelWidth, panelHeight), Color.black);
        scrollGO.transform.SetParent(bodyPanel.transform, false);
        scrollRt.sizeDelta = new Vector2(panelWidth - 30f, panelHeight - 30f);
        scrollRt.anchoredPosition = Vector2.zero;
        UIFactory.MakeHeroTitle(panelRoot.transform, "RulesTitle", new Vector2(0, panelHeight / 2f + 50f), title, 30);

        // Scroll bar down the right edge — only shown when the rules are taller than the panel
        var barGO = new GameObject("RulesScrollbar");
        barGO.transform.SetParent(bodyPanel.transform, false);
        var barRt = barGO.AddComponent<RectTransform>();
        barRt.sizeDelta = new Vector2(12f, panelHeight - 40f);
        barRt.anchoredPosition = new Vector2(panelWidth / 2f - 14f, 0f);
        barGO.AddComponent<Image>().color = new Color(1f, 1f, 1f, 0.08f);
        var handleArea = new GameObject("HandleArea");
        handleArea.transform.SetParent(barGO.transform, false);
        var areaRt = handleArea.AddComponent<RectTransform>();
        areaRt.anchorMin = Vector2.zero;
        areaRt.anchorMax = Vector2.one;
        areaRt.offsetMin = areaRt.offsetMax = Vector2.zero;
        var handle = new GameObject("Handle");
        handle.transform.SetParent(handleArea.transform, false);
        var handleRt = handle.AddComponent<RectTransform>();
        handleRt.offsetMin = handleRt.offsetMax = Vector2.zero;
        var handleImg = handle.AddComponent<Image>();
        handleImg.color = UIFactory.AccentDim;
        var bar = barGO.AddComponent<Scrollbar>();
        bar.direction = Scrollbar.Direction.BottomToTop;
        bar.handleRect = handleRt;
        bar.targetGraphic = handleImg;
        scroll.verticalScrollbar = bar;
        scroll.verticalScrollbarVisibility = ScrollRect.ScrollbarVisibility.AutoHide;
        scroll.verticalNormalizedPosition = 1f;

        UIFactory.MakeButton(panelRoot.transform, "RulesCloseBtn", new Vector2(0, -panelHeight / 2f - 45f), new Vector2(160, 46),
            "CLOSE", UIFactory.AccentDim, Hide, 14, pixelFont: true);

        panelRoot.SetActive(false);
    }

    // Stacks one column's items top-down; returns the height used
    float BuildColumn(RectTransform content, List<RulesContent.Item> items, float centerX, Color rowValueColor)
    {
        float y = -10f;
        bool shade = false;
        var previous = RulesContent.Kind.Heading;
        foreach (var item in items)
        {
            // A paragraph right after table rows gets a little air
            if ((item.Kind == RulesContent.Kind.Text || item.Kind == RulesContent.Kind.Note) && previous == RulesContent.Kind.Pay) y -= 6f;
            previous = item.Kind;
            switch (item.Kind)
            {
                case RulesContent.Kind.Heading:
                    if (y < -10f) y -= 14f;
                    y -= Place(MakeLine(content, item.Left, 20, HeadingGold, FontStyle.Bold, TextAnchor.MiddleLeft), centerX, y, 30f);
                    shade = false;
                    break;
                case RulesContent.Kind.Text:
                {
                    var t = MakeLine(content, item.Left, 16, UIFactory.TextLight, FontStyle.Normal, TextAnchor.UpperLeft);
                    y -= Place(t, centerX, y, Wrapped(t)) + 6f;
                    break;
                }
                case RulesContent.Kind.Note:
                {
                    var t = MakeLine(content, item.Left, 14, UIFactory.TextDim, FontStyle.Italic, TextAnchor.UpperLeft);
                    y -= Place(t, centerX, y, Wrapped(t)) + 6f;
                    break;
                }
                case RulesContent.Kind.Pay:
                {
                    const float rowH = 28f;
                    if (shade)
                    {
                        var bg = new GameObject("RowShade");
                        bg.transform.SetParent(content, false);
                        var img = bg.AddComponent<Image>();
                        img.color = RowShade;
                        img.raycastTarget = false;
                        Place(bg.GetComponent<RectTransform>(), centerX, y, rowH);
                    }
                    shade = !shade;
                    var left = MakeLine(content, item.Left, 16, UIFactory.TextLight, FontStyle.Normal, TextAnchor.MiddleLeft);
                    var right = MakeLine(content, item.Right, 16, rowValueColor, rowValueColor == PayGold ? FontStyle.Bold : FontStyle.Normal, TextAnchor.MiddleRight);
                    Place(left, centerX, y, rowH, inset: 8f);
                    Place(right, centerX, y, rowH, inset: 8f);
                    y -= rowH;
                    break;
                }
            }
        }
        return -y;
    }

    static Text MakeLine(Transform parent, string text, int size, Color color, FontStyle style, TextAnchor anchor)
    {
        var t = UIFactory.MakeText(parent, "Line", Vector2.zero, size, anchor, new Vector2(ColumnWidth, 30f), color, style);
        t.horizontalOverflow = HorizontalWrapMode.Wrap;
        t.verticalOverflow = VerticalWrapMode.Overflow;
        t.supportRichText = true;
        t.raycastTarget = false;
        t.text = text;
        return t;
    }

    // Height the text needs at the column width
    static float Wrapped(Text t)
    {
        var settings = t.GetGenerationSettings(new Vector2(ColumnWidth, 0f));
        return Mathf.Ceil(t.cachedTextGeneratorForLayout.GetPreferredHeight(t.text, settings) / t.pixelsPerUnit) + 2f;
    }

    static float Place(Component c, float centerX, float top, float height, float inset = 0f)
    {
        var rt = (RectTransform)c.transform;
        rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 1f);
        rt.pivot = new Vector2(0.5f, 1f);
        rt.sizeDelta = new Vector2(ColumnWidth - inset * 2f, height);
        rt.anchoredPosition = new Vector2(centerX, top);
        return height;
    }

    void Update()
    {
        if (panelRoot != null && panelRoot.activeSelf && Input.GetKeyDown(KeyCode.Escape)) Hide();
    }

    public void Toggle()
    {
        if (panelRoot.activeSelf) Hide();
        else Show();
    }

    public void Show()
    {
        panelRoot.transform.SetAsLastSibling();
        panelRoot.SetActive(true);
    }

    public void Hide() => panelRoot.SetActive(false);
}
