using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

// Blackjack/Baccarat's equivalent of PastSpinsStripUI — same horizontal scrolling
// badge-strip mechanics, but badges carry a short text label (W/L/P, or P/B/T)
// instead of a roulette number, since there's no single numeric result to show.
public class ResultsStripUI : MonoBehaviour
{
    Transform content;
    ScrollRect scrollRect;
    const int MaxStored = 500;
    const int VisibleCount = 14;
    const float Diameter = 34f;
    const float Spacing = 40f;

    struct Entry { public string Label; public Color Color; }
    readonly List<Entry> entries = new List<Entry>();
    readonly List<GameObject> badgeObjects = new List<GameObject>();
    CanvasGroup group;

    public void Build(Transform canvas, Vector2 anchoredPos, string header = "Recent Results")
    {
        var rootGO = new GameObject("ResultsStripRoot");
        rootGO.transform.SetParent(canvas, false);
        var rt = rootGO.AddComponent<RectTransform>();
        rt.anchorMin = new Vector2(0.5f, 0.5f);
        rt.anchorMax = new Vector2(0.5f, 0.5f);
        rt.pivot = new Vector2(0.5f, 0.5f);
        rt.anchoredPosition = Vector2.zero;
        group = rootGO.AddComponent<CanvasGroup>();
        var root = rootGO.transform;

        float viewportWidth = Spacing * VisibleCount;
        UIFactory.MakePanel(root, "ResultsStripBg", anchoredPos, new Vector2(viewportWidth + 20, Diameter + 20), UIFactory.PanelDark);
        UIFactory.MakeSectionHeader(root, header, anchoredPos + new Vector2(-viewportWidth / 2f + 10, Diameter / 2f + 22), new Vector2(200, 20));

        var scrollGO = new GameObject("ResultsStripScroll");
        scrollGO.transform.SetParent(root, false);
        var scrollRt = scrollGO.AddComponent<RectTransform>();
        scrollRt.sizeDelta = new Vector2(viewportWidth, Diameter + 6);
        scrollRt.anchoredPosition = anchoredPos;
        scrollRect = scrollGO.AddComponent<ScrollRect>();
        scrollRect.horizontal = true;
        scrollRect.vertical = false;
        scrollRect.movementType = ScrollRect.MovementType.Clamped;
        scrollRect.scrollSensitivity = 24f;

        var viewportGO = new GameObject("Viewport");
        viewportGO.transform.SetParent(scrollGO.transform, false);
        var vpRt = viewportGO.AddComponent<RectTransform>();
        vpRt.anchorMin = Vector2.zero;
        vpRt.anchorMax = Vector2.one;
        vpRt.offsetMin = Vector2.zero;
        vpRt.offsetMax = Vector2.zero;
        viewportGO.AddComponent<RectMask2D>();
        viewportGO.AddComponent<Image>().color = new Color(0, 0, 0, 0.01f);

        var contentGO = new GameObject("Content");
        contentGO.transform.SetParent(viewportGO.transform, false);
        var contentRt = contentGO.AddComponent<RectTransform>();
        contentRt.anchorMin = new Vector2(0, 0.5f);
        contentRt.anchorMax = new Vector2(0, 0.5f);
        contentRt.pivot = new Vector2(0, 0.5f);
        contentRt.anchoredPosition = Vector2.zero;
        contentRt.sizeDelta = new Vector2(viewportWidth, Diameter + 6);
        content = contentGO.transform;

        scrollRect.viewport = vpRt;
        scrollRect.content = contentRt;
    }

    public void SetVisible(bool visible)
    {
        group.alpha = visible ? 1f : 0f;
        group.interactable = visible;
        group.blocksRaycasts = visible;
    }

    public void AddResult(string label, Color color)
    {
        entries.Insert(0, new Entry { Label = label, Color = color });
        if (entries.Count > MaxStored) entries.RemoveRange(MaxStored, entries.Count - MaxStored);
        Rebuild(animateNewest: true);
    }

    public void Clear()
    {
        entries.Clear();
        Rebuild(false);
    }

    void Rebuild(bool animateNewest)
    {
        foreach (var go in badgeObjects) Destroy(go);
        badgeObjects.Clear();

        var contentRt = (RectTransform)content;
        float width = Mathf.Max(Spacing * VisibleCount, Spacing * entries.Count + 10);
        contentRt.sizeDelta = new Vector2(width, Diameter + 6);

        for (int i = 0; i < entries.Count; i++)
        {
            var e = entries[i];
            var pos = new Vector2(Diameter / 2f + 6 + i * Spacing, 0);

            var ringGO = new GameObject($"ResultBadge_{i}");
            ringGO.transform.SetParent(content, false);
            var ringImg = ringGO.AddComponent<Image>();
            ringImg.sprite = UIFactory.Circle();
            ringImg.color = i == 0 ? UIFactory.Accent : new Color(0.35f, 0.35f, 0.35f);
            var ringRt = ringGO.GetComponent<RectTransform>();
            ringRt.anchorMin = new Vector2(0f, 0.5f);
            ringRt.anchorMax = new Vector2(0f, 0.5f);
            ringRt.pivot = new Vector2(0.5f, 0.5f);
            ringRt.sizeDelta = new Vector2(Diameter + 4, Diameter + 4);
            ringRt.anchoredPosition = pos;

            var fillGO = new GameObject("Fill");
            fillGO.transform.SetParent(ringGO.transform, false);
            var fillImg = fillGO.AddComponent<Image>();
            fillImg.sprite = UIFactory.Circle();
            fillImg.color = e.Color;
            var fillRt = fillGO.GetComponent<RectTransform>();
            fillRt.sizeDelta = new Vector2(Diameter, Diameter);

            var text = UIFactory.MakeText(fillGO.transform, "Label", Vector2.zero, 14, sizeDelta: new Vector2(Diameter, Diameter),
                color: Color.white, style: FontStyle.Bold);
            text.text = e.Label;

            badgeObjects.Add(ringGO);
            if (animateNewest && i == 0) JuiceTweens.PopIn(this, ringRt, overshoot: 1.3f, duration: 0.22f);
        }

        Canvas.ForceUpdateCanvases();
        scrollRect.horizontalNormalizedPosition = 0f;
    }
}
