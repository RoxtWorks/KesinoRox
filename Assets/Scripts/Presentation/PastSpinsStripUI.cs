using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

// Horizontal strip of small colored number badges for every spin this session —
// the compact "recent results" readout real roulette tables show, distinct from
// HistoryPanelUI's detailed bankroll log. Newest spin sits leftmost and the strip
// auto-snaps back there on every new spin; older spins scroll off to the right.
public class PastSpinsStripUI : MonoBehaviour
{
    Transform content;
    ScrollRect scrollRect;
    const int MaxStored = 500;
    const int VisibleCount = 14;
    const float Diameter = 34f;
    const float Spacing = 40f;

    readonly List<int> numbers = new List<int>();
    readonly List<GameObject> badgeObjects = new List<GameObject>();
    CanvasGroup group;

    public void Build(Transform canvas, Vector2 anchoredPos)
    {
        var rootGO = new GameObject("PastSpinsRoot");
        rootGO.transform.SetParent(canvas, false);
        var rt = rootGO.AddComponent<RectTransform>();
        rt.anchorMin = new Vector2(0.5f, 0.5f);
        rt.anchorMax = new Vector2(0.5f, 0.5f);
        rt.pivot = new Vector2(0.5f, 0.5f);
        rt.anchoredPosition = Vector2.zero;
        group = rootGO.AddComponent<CanvasGroup>();
        var root = rootGO.transform;

        float viewportWidth = Spacing * VisibleCount;
        UIFactory.MakePanel(root, "PastSpinsBg", anchoredPos, new Vector2(viewportWidth + 20, Diameter + 20), UIFactory.PanelDark);
        UIFactory.MakeSectionHeader(root, "Recent Spins", anchoredPos + new Vector2(-viewportWidth / 2f + 10, Diameter / 2f + 22), new Vector2(200, 20));

        var scrollGO = new GameObject("PastSpinsScroll");
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

    public void AddSpin(int number)
    {
        numbers.Insert(0, number);
        if (numbers.Count > MaxStored) numbers.RemoveRange(MaxStored, numbers.Count - MaxStored);
        Rebuild(animateNewest: true);
    }

    public void Clear()
    {
        numbers.Clear();
        Rebuild(false);
    }

    void Rebuild(bool animateNewest)
    {
        foreach (var go in badgeObjects) Destroy(go);
        badgeObjects.Clear();

        var contentRt = (RectTransform)content;
        float width = Mathf.Max(Spacing * VisibleCount, Spacing * numbers.Count + 10);
        contentRt.sizeDelta = new Vector2(width, Diameter + 6);

        for (int i = 0; i < numbers.Count; i++)
        {
            int n = numbers[i];
            Color fillColor = n == 0 ? UIFactory.FeltGreen : (WheelLayout.IsRed(n) ? UIFactory.RedBet : UIFactory.BlackBet);
            var pos = new Vector2(Diameter / 2f + 6 + i * Spacing, 0);

            // Ring is the OUTER graphic (bigger, drawn first) so the smaller colored
            // fill sits on top of it and stays visible — previously the ring was a
            // child sized bigger than its parent's fill circle, which meant it
            // rendered on top and completely hid the red/black/green color underneath,
            // leaving every badge showing as flat gold/gray no matter the number.
            var ringGO = new GameObject($"SpinBadge_{i}");
            ringGO.transform.SetParent(content, false);
            var ringImg = ringGO.AddComponent<Image>();
            ringImg.sprite = UIFactory.Circle();
            ringImg.color = i == 0 ? UIFactory.Accent : new Color(0.35f, 0.35f, 0.35f);
            var ringRt = ringGO.GetComponent<RectTransform>();
            // Content anchors its children from its own left-center edge (anchorMin/Max
            // (0,0.5)), not the default (0,0) — without matching that here, anchoredPosition
            // measures from the wrong reference point and every badge lands far outside
            // the viewport, clipped invisible by the scroll mask.
            ringRt.anchorMin = new Vector2(0f, 0.5f);
            ringRt.anchorMax = new Vector2(0f, 0.5f);
            ringRt.pivot = new Vector2(0.5f, 0.5f);
            ringRt.sizeDelta = new Vector2(Diameter + 4, Diameter + 4);
            ringRt.anchoredPosition = pos;

            var fillGO = new GameObject("Fill");
            fillGO.transform.SetParent(ringGO.transform, false);
            var fillImg = fillGO.AddComponent<Image>();
            fillImg.sprite = UIFactory.Circle();
            fillImg.color = fillColor;
            var fillRt = fillGO.GetComponent<RectTransform>();
            fillRt.sizeDelta = new Vector2(Diameter, Diameter);

            var text = UIFactory.MakeText(fillGO.transform, "Num", Vector2.zero, 14, sizeDelta: new Vector2(Diameter, Diameter),
                color: Color.white, style: FontStyle.Bold);
            text.text = n.ToString();

            badgeObjects.Add(ringGO);
            if (animateNewest && i == 0) JuiceTweens.PopIn(this, ringRt, overshoot: 1.3f, duration: 0.22f);
        }

        Canvas.ForceUpdateCanvases();
        scrollRect.horizontalNormalizedPosition = 0f;
    }
}
