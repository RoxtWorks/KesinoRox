using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

// Same scrolling-column-table approach as blackjack's BlackjackHistoryPanelUI, just
// with baccarat-shaped columns (round#, player point, banker point, winner, stake,
// +/-, balance). Newest hand on top.
public class BaccaratHistoryPanelUI : MonoBehaviour
{
    // Fits the 254px content area of the tall right-hand column
    static readonly float[] ColX = { 0f, 24f, 50f, 76f, 108f, 158f, 206f };
    static readonly float[] ColW = { 24f, 26f, 26f, 32f, 50f, 48f, 48f };
    const float ContentWidth = 254f;
    const float RowHeight = 30f;
    const int MaxStored = 60;

    static readonly Color PlayerBlue = new Color(0.35f, 0.60f, 1f);
    static readonly Color BankerRed = new Color(1f, 0.38f, 0.35f);
    static readonly Color TieGreen = new Color(0.45f, 0.85f, 0.45f);

    Transform content;
    RectTransform contentRt;
    ScrollRect scrollRect;
    readonly List<BaccaratRoundRecord> records = new List<BaccaratRoundRecord>();
    readonly List<GameObject> rowObjects = new List<GameObject>();

    public void Build(Transform canvas, Vector2 anchoredPos, Vector2 size)
    {
        UIFactory.MakePanel(canvas, "BcHistoryPanelBg", anchoredPos, size, UIFactory.PanelDark);
        UIFactory.MakeSectionHeader(canvas, "History", anchoredPos + new Vector2(0, size.y / 2f - 20f), new Vector2(size.x - 20, 20));

        var headerRow = new GameObject("BcHistoryHeaderRow");
        headerRow.transform.SetParent(canvas, false);
        var headerRt = headerRow.AddComponent<RectTransform>();
        headerRt.anchorMin = headerRt.anchorMax = new Vector2(0.5f, 0.5f);
        headerRt.pivot = new Vector2(0f, 0.5f);
        headerRt.sizeDelta = new Vector2(ContentWidth, RowHeight);
        headerRt.anchoredPosition = anchoredPos + new Vector2(-ContentWidth / 2f, size.y / 2f - 46f);
        MakeRowText(headerRow.transform, "#", 0, UIFactory.Accent, FontStyle.Bold);
        MakeRowText(headerRow.transform, "P", 1, UIFactory.Accent, FontStyle.Bold);
        MakeRowText(headerRow.transform, "B", 2, UIFactory.Accent, FontStyle.Bold);
        MakeRowText(headerRow.transform, "Win", 3, UIFactory.Accent, FontStyle.Bold);
        MakeRowText(headerRow.transform, "Stake", 4, UIFactory.Accent, FontStyle.Bold);
        MakeRowText(headerRow.transform, "+/-", 5, UIFactory.Accent, FontStyle.Bold);
        MakeRowText(headerRow.transform, "Bal", 6, UIFactory.Accent, FontStyle.Bold);

        var scrollGO = new GameObject("BcHistoryScroll");
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
        Rebuild(false);
    }

    static Text MakeRowText(Transform row, string label, int col, Color? color = null, FontStyle style = FontStyle.Normal)
    {
        var t = UIFactory.MakeText(row, $"Col{col}", new Vector2(ColX[col] + ColW[col] / 2f, 0), 14,
            TextAnchor.MiddleRight, new Vector2(ColW[col] - 4f, RowHeight), color ?? UIFactory.TextDim, style);
        var rt = t.GetComponent<RectTransform>();
        rt.anchorMin = new Vector2(0f, 0.5f);
        rt.anchorMax = new Vector2(0f, 0.5f);
        rt.pivot = new Vector2(0.5f, 0.5f);
        rt.anchoredPosition = new Vector2(ColX[col] + ColW[col] / 2f, 0);
        t.resizeTextForBestFit = true;
        t.resizeTextMinSize = 9;
        t.resizeTextMaxSize = 14;
        t.text = label;
        return t;
    }

    public void AddRecord(BaccaratRoundRecord record)
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

        for (int i = 0; i < records.Count; i++)
        {
            var rec = records[records.Count - 1 - i];
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
            (string winLabel, Color winColor) = rec.Outcome switch
            {
                BaccaratOutcome.PlayerWin => ("P", PlayerBlue),
                BaccaratOutcome.BankerWin => ("B", BankerRed),
                _ => ("T", TieGreen),
            };

            MakeRowText(rowGO.transform, $"{rec.RoundIndex + 1}", 0);
            MakeRowText(rowGO.transform, $"{rec.PlayerPoint}", 1);
            MakeRowText(rowGO.transform, $"{rec.BankerPoint}", 2);
            MakeRowText(rowGO.transform, winLabel, 3, winColor, FontStyle.Bold);
            MakeRowText(rowGO.transform, UIFactory.FormatMoneyCompact(rec.TotalStaked), 4);
            MakeRowText(rowGO.transform, $"{sign}{UIFactory.FormatMoneyCompact(rec.NetChange)}", 5, netColor);
            MakeRowText(rowGO.transform, UIFactory.FormatMoneyCompact(rec.BalanceAfter), 6);

            rowObjects.Add(rowGO);
            if (animateNewest && i == 0) JuiceTweens.PopIn(this, rowRt, overshoot: 1.06f, duration: 0.2f);
        }

        if (records.Count == 0)
        {
            var emptyGO = new GameObject("EmptyLabel");
            emptyGO.transform.SetParent(content, false);
            var t = UIFactory.MakeText(emptyGO.transform, "Text", Vector2.zero, 14,
                TextAnchor.UpperLeft, new Vector2(ContentWidth, RowHeight), UIFactory.TextDim);
            t.text = "No hands yet";
            rowObjects.Add(emptyGO);
        }
    }
}
