using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

// Scrolling table of every spin this session — bet index, winning number, stake,
// win/loss, and running balance. Each field is its own Text column at a fixed x
// offset (not one formatted string) so values stay aligned no matter how many
// digits a number has — a plain proportional font can't fake column alignment
// with padding alone.
public class HistoryPanelUI : MonoBehaviour
{
    static readonly float[] ColX = { 0f, 32f, 78f, 150f, 216f };
    static readonly float[] ColW = { 32f, 46f, 72f, 66f, 69f };
    const float ContentWidth = 285f;
    const float RowHeight = 26f;
    const int MaxStored = 300;

    Transform content;
    RectTransform contentRt;
    ScrollRect scrollRect;
    readonly List<SpinRecord> records = new List<SpinRecord>();
    readonly List<GameObject> rowObjects = new List<GameObject>();

    public void Build(Transform canvas, Vector2 anchoredPos, Vector2 size)
    {
        UIFactory.MakePanel(canvas, "HistoryPanelBg", anchoredPos, size, UIFactory.PanelDark);
        UIFactory.MakeSectionHeader(canvas, "History", anchoredPos + new Vector2(0, size.y / 2f - 20f), new Vector2(size.x - 20, 20));

        var headerRow = new GameObject("HistoryHeaderRow");
        headerRow.transform.SetParent(canvas, false);
        var headerRt = headerRow.AddComponent<RectTransform>();
        headerRt.anchorMin = headerRt.anchorMax = new Vector2(0.5f, 0.5f);
        headerRt.pivot = new Vector2(0f, 0.5f);
        headerRt.sizeDelta = new Vector2(ContentWidth, RowHeight);
        headerRt.anchoredPosition = anchoredPos + new Vector2(-ContentWidth / 2f, size.y / 2f - 46f);
        MakeRowText(headerRow.transform, "#", 0, UIFactory.Accent, FontStyle.Bold);
        MakeRowText(headerRow.transform, "Num", 1, UIFactory.Accent, FontStyle.Bold);
        MakeRowText(headerRow.transform, "Stake", 2, UIFactory.Accent, FontStyle.Bold);
        MakeRowText(headerRow.transform, "+/-", 3, UIFactory.Accent, FontStyle.Bold);
        MakeRowText(headerRow.transform, "Bal", 4, UIFactory.Accent, FontStyle.Bold);

        var scrollGO = new GameObject("HistoryScroll");
        scrollGO.transform.SetParent(canvas, false);
        var scrollRt = scrollGO.AddComponent<RectTransform>();
        scrollRt.sizeDelta = new Vector2(ContentWidth, size.y - 76f);
        scrollRt.anchoredPosition = anchoredPos + new Vector2(0, -28f);
        scrollRect = scrollGO.AddComponent<ScrollRect>();
        scrollRect.horizontal = false;
        scrollRect.vertical = true;
        scrollRect.movementType = ScrollRect.MovementType.Clamped;
        scrollRect.scrollSensitivity = 26f;

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
        contentRt = contentGO.AddComponent<RectTransform>();
        contentRt.anchorMin = new Vector2(0, 1);
        contentRt.anchorMax = new Vector2(0, 1);
        contentRt.pivot = new Vector2(0, 1);
        contentRt.anchoredPosition = Vector2.zero;
        contentRt.sizeDelta = new Vector2(ContentWidth, RowHeight);
        content = contentGO.transform;

        scrollRect.viewport = vpRt;
        scrollRect.content = contentRt;
    }

    static Text MakeRowText(Transform row, string label, int col, Color? color = null, FontStyle style = FontStyle.Normal)
    {
        var t = UIFactory.MakeText(row, $"Col{col}", new Vector2(ColX[col] + ColW[col] / 2f, 0), 15,
            TextAnchor.MiddleRight, new Vector2(ColW[col] - 4f, RowHeight), color ?? UIFactory.TextDim, style);
        // Row's own rect is non-zero-sized (pivot top-left), so a default-anchored
        // child measures its position from the wrong reference point — same class of
        // bug as the Recent Spins badges. Anchor explicitly to the row's left edge,
        // vertically centered, so anchoredPosition.x is a clean offset from col 0.
        var rt = t.GetComponent<RectTransform>();
        rt.anchorMin = new Vector2(0f, 0.5f);
        rt.anchorMax = new Vector2(0f, 0.5f);
        rt.pivot = new Vector2(0.5f, 0.5f);
        rt.anchoredPosition = new Vector2(ColX[col] + ColW[col] / 2f, 0);
        t.resizeTextForBestFit = true;
        t.resizeTextMinSize = 10;
        t.resizeTextMaxSize = 15;
        t.text = label;
        return t;
    }

    public void AddRecord(SpinRecord record)
    {
        records.Add(record);
        if (records.Count > MaxStored) records.RemoveAt(0);
        Rebuild(animateNewest: true);

        Canvas.ForceUpdateCanvases();
        scrollRect.verticalNormalizedPosition = 0f;
    }

    public void Clear()
    {
        records.Clear();
        Rebuild(false);
    }

    void Rebuild(bool animateNewest)
    {
        foreach (var go in rowObjects) Destroy(go);
        rowObjects.Clear();

        contentRt.sizeDelta = new Vector2(ContentWidth, Mathf.Max(RowHeight, records.Count * RowHeight));

        for (int i = 0; i < records.Count; i++)
        {
            var rec = records[i];
            var rowGO = new GameObject($"Row_{i}");
            rowGO.transform.SetParent(content, false);
            var rowRt = rowGO.AddComponent<RectTransform>();
            rowRt.anchorMin = new Vector2(0, 1);
            rowRt.anchorMax = new Vector2(0, 1);
            rowRt.pivot = new Vector2(0, 1);
            rowRt.sizeDelta = new Vector2(ContentWidth, RowHeight);
            rowRt.anchoredPosition = new Vector2(0, -i * RowHeight);

            string sign = rec.NetChange >= 0 ? "+" : "";
            Color netColor = rec.NetChange > 0 ? UIFactory.Positive : rec.NetChange < 0 ? UIFactory.Negative : UIFactory.TextDim;
            Color numColor = rec.WinningNumber == 0 ? UIFactory.Positive : WheelLayout.IsRed(rec.WinningNumber) ? UIFactory.RedBet : UIFactory.TextLight;

            MakeRowText(rowGO.transform, $"{rec.SpinIndex + 1}", 0);
            MakeRowText(rowGO.transform, $"{rec.WinningNumber}", 1, numColor, FontStyle.Bold);
            MakeRowText(rowGO.transform, $"{rec.TotalStaked}", 2);
            MakeRowText(rowGO.transform, $"{sign}{rec.NetChange}", 3, netColor);
            MakeRowText(rowGO.transform, $"{rec.BalanceAfter}", 4);

            rowObjects.Add(rowGO);
            if (animateNewest && i == records.Count - 1) JuiceTweens.PopIn(this, rowRt, overshoot: 1.06f, duration: 0.2f);
        }

        if (records.Count == 0)
        {
            var emptyGO = new GameObject("EmptyLabel");
            emptyGO.transform.SetParent(content, false);
            var t = UIFactory.MakeText(emptyGO.transform, "Text", Vector2.zero, 14,
                TextAnchor.UpperLeft, new Vector2(ContentWidth, RowHeight), UIFactory.TextDim);
            t.text = "No spins yet";
            rowObjects.Add(emptyGO);
        }
    }
}
