using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

// Tall right-hand column: one row per round, newest on top —
// # · dealer point (3P = three pictures) · hands 1-5 (W win, W6 win on 6 paid 1:2, P push, L lose, · not played) · +/- · balance.
public class ThreePicturesHistoryPanelUI : MonoBehaviour
{
    // Column left edges and widths inside the 254px content area: #, Dlr, H1..H5, +/-, Bal
    static readonly float[] ColX = { 0f, 24f, 56f, 80f, 104f, 128f, 152f, 178f, 216f };
    static readonly float[] ColW = { 24f, 32f, 24f, 24f, 24f, 24f, 24f, 38f, 38f };
    const float ContentWidth = 254f;
    const float RowHeight = 30f;
    const int MaxStored = 60;

    static readonly Color TitleGold = new Color(1f, 0.85f, 0.1f);

    Transform content;
    RectTransform contentRt;
    ScrollRect scrollRect;
    readonly List<ThreePicturesRoundRecord> records = new List<ThreePicturesRoundRecord>();
    readonly List<GameObject> rowObjects = new List<GameObject>();

    public void Build(Transform canvas, Vector2 anchoredPos, Vector2 size)
    {
        UIFactory.MakePanel(canvas, "TPHistoryPanelBg", anchoredPos, size, UIFactory.PanelDark);
        UIFactory.MakeSectionHeader(canvas, "History", anchoredPos + new Vector2(0, size.y / 2f - 20f), new Vector2(size.x - 20, 20));

        var headerRow = new GameObject("TPHistoryHeaderRow");
        headerRow.transform.SetParent(canvas, false);
        var headerRt = headerRow.AddComponent<RectTransform>();
        headerRt.anchorMin = headerRt.anchorMax = new Vector2(0.5f, 0.5f);
        headerRt.pivot = new Vector2(0f, 0.5f);
        headerRt.sizeDelta = new Vector2(ContentWidth, 24f);
        headerRt.anchoredPosition = anchoredPos + new Vector2(-ContentWidth / 2f, size.y / 2f - 46f);
        string[] headers = { "#", "Dlr", "1", "2", "3", "4", "5", "+/-", "Bal" };
        for (int i = 0; i < headers.Length; i++)
            MakeRowText(headerRow.transform, headers[i], i, UIFactory.Accent, FontStyle.Bold);

        var scrollGO = new GameObject("TPHistoryScroll");
        scrollGO.transform.SetParent(canvas, false);
        var scrollRt = scrollGO.AddComponent<RectTransform>();
        scrollRt.sizeDelta = new Vector2(ContentWidth, size.y - 72f);
        scrollRt.anchoredPosition = anchoredPos + new Vector2(0, -26f);
        scrollRect = scrollGO.AddComponent<ScrollRect>();
        scrollRect.horizontal = false;
        scrollRect.vertical = true;
        scrollRect.movementType = ScrollRect.MovementType.Clamped;
        scrollRect.scrollSensitivity = 30f;

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
        Rebuild(false);
    }

    static Text MakeRowText(Transform row, string label, int col, Color? color = null, FontStyle style = FontStyle.Normal)
    {
        var t = UIFactory.MakeText(row, $"Col{col}", Vector2.zero, 15,
            col >= 7 ? TextAnchor.MiddleRight : TextAnchor.MiddleCenter, new Vector2(ColW[col] - 2f, RowHeight), color ?? UIFactory.TextDim, style);
        var rt = t.GetComponent<RectTransform>();
        rt.anchorMin = new Vector2(0f, 0.5f);
        rt.anchorMax = new Vector2(0f, 0.5f);
        rt.pivot = new Vector2(0.5f, 0.5f);
        rt.anchoredPosition = new Vector2(ColX[col] + ColW[col] / 2f, 0);
        t.resizeTextForBestFit = true;
        t.resizeTextMinSize = 9;
        t.resizeTextMaxSize = 15;
        t.text = label;
        return t;
    }

    public void AddRecord(ThreePicturesRoundRecord record)
    {
        records.Add(record);
        if (records.Count > MaxStored) records.RemoveAt(0);
        Rebuild(animateNewest: true);
        Canvas.ForceUpdateCanvases();
        scrollRect.verticalNormalizedPosition = 1f;
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

        for (int row = 0; row < records.Count; row++)
        {
            var rec = records[records.Count - 1 - row];
            var rowGO = new GameObject($"Row_{row}");
            rowGO.transform.SetParent(content, false);
            var rowRt = rowGO.AddComponent<RectTransform>();
            rowRt.anchorMin = new Vector2(0, 1);
            rowRt.anchorMax = new Vector2(0, 1);
            rowRt.pivot = new Vector2(0, 1);
            rowRt.sizeDelta = new Vector2(ContentWidth, RowHeight);
            rowRt.anchoredPosition = new Vector2(0, -row * RowHeight);
            if (row % 2 == 0)
            {
                var stripe = rowGO.AddComponent<Image>();
                stripe.color = new Color(1f, 1f, 1f, 0.03f);
                stripe.raycastTarget = false;
            }

            Color netColor = rec.NetChange > 0 ? UIFactory.Positive : rec.NetChange < 0 ? UIFactory.Negative : UIFactory.TextDim;
            string sign = rec.NetChange >= 0 ? "+" : "";

            MakeRowText(rowGO.transform, $"{rec.RoundIndex + 1}", 0);
            MakeRowText(rowGO.transform, rec.DealerRoyal ? "3P" : $"{rec.DealerPoint}", 1,
                rec.DealerRoyal ? TitleGold : UIFactory.TextLight, FontStyle.Bold);
            for (int b = 0; b < rec.Boxes.Length; b++)
            {
                var e = rec.Boxes[b];
                if (e.Stake <= 0) { MakeRowText(rowGO.transform, "·", 2 + b); continue; }
                switch (e.Outcome)
                {
                    case ThreePicturesOutcome.PlayerWins: MakeRowText(rowGO.transform, e.HalfPay ? "W6" : "W", 2 + b, UIFactory.Positive, FontStyle.Bold); break;
                    case ThreePicturesOutcome.Tie:        MakeRowText(rowGO.transform, "P", 2 + b, UIFactory.Accent, FontStyle.Bold); break;
                    default:                              MakeRowText(rowGO.transform, "L", 2 + b, UIFactory.Negative, FontStyle.Bold); break;
                }
            }
            MakeRowText(rowGO.transform, $"{sign}{UIFactory.FormatMoneyCompact(rec.NetChange)}", 7, netColor);
            MakeRowText(rowGO.transform, UIFactory.FormatMoneyCompact(rec.BalanceAfter), 8);

            rowObjects.Add(rowGO);
            if (animateNewest && row == 0)
                JuiceTweens.PopIn(this, rowRt, overshoot: 1.06f, duration: 0.2f);
        }

        if (records.Count == 0)
        {
            var emptyGO = new GameObject("EmptyLabel");
            emptyGO.transform.SetParent(content, false);
            var t = UIFactory.MakeText(emptyGO.transform, "Text", Vector2.zero, 14,
                TextAnchor.UpperLeft, new Vector2(ContentWidth, RowHeight), UIFactory.TextDim);
            t.text = "No rounds yet";
            rowObjects.Add(emptyGO);
        }
    }
}
