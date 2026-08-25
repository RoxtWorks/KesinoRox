using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.UI;

// Rolling window of the last 30 spins as a color-coded grid — green square = that
// spin returned more than it staked, red = less, gray = broke even (including
// spins with no bet placed). Each block shows the actual net P/L for that spin
// (not the winning number — see HistoryPanelUI/PastSpinsStripUI for that), so a
// glance answers "how much did I win or lose," not just "win or lose."
public class ProfitLossTrackerUI : MonoBehaviour
{
    const int MaxRecords = 30;
    const int Cols = 6, Rows = 5;
    const float CellSize = 38f, CellGap = 6f;

    readonly List<SpinRecord> records = new List<SpinRecord>();
    readonly List<GameObject> blocks = new List<GameObject>();

    Transform canvas;
    Vector2 anchoredPos;
    Text summaryText;

    public void Build(Transform canvas, Vector2 anchoredPos, Vector2 size)
    {
        this.canvas = canvas;
        this.anchoredPos = anchoredPos;

        UIFactory.MakePanel(canvas, "PLTrackerBg", anchoredPos, size, UIFactory.PanelDark);
        UIFactory.MakeSectionHeader(canvas, "P/L (Last 30)", anchoredPos + new Vector2(0, size.y / 2f - 20f), new Vector2(size.x - 20, 20));

        summaryText = UIFactory.MakeText(canvas, "PLSummary", anchoredPos + new Vector2(0, -135), 18,
            TextAnchor.MiddleCenter, new Vector2(size.x - 20, 48), UIFactory.TextDim, FontStyle.Bold);

        Refresh(false);
    }

    public void AddRecord(SpinRecord record)
    {
        records.Add(record);
        if (records.Count > MaxRecords) records.RemoveAt(0);
        Refresh(animateNewest: true);
    }

    public void Clear()
    {
        records.Clear();
        Refresh(false);
    }

    void Refresh(bool animateNewest)
    {
        foreach (var b in blocks) Destroy(b);
        blocks.Clear();

        float gridW = Cols * CellSize + (Cols - 1) * CellGap;
        float startX = anchoredPos.x - gridW / 2f + CellSize / 2f;
        float startY = anchoredPos.y + 90f;

        for (int i = 0; i < records.Count; i++)
        {
            int col = i % Cols;
            int row = i / Cols;
            var rec = records[i];
            Color c = rec.NetChange > 0 ? UIFactory.Positive
                    : rec.NetChange < 0 ? UIFactory.Negative
                    : new Color(0.42f, 0.42f, 0.44f);

            var go = new GameObject($"PLBlock_{i}");
            go.transform.SetParent(canvas, false);
            var img = go.AddComponent<Image>();
            img.sprite = UIFactory.RoundedRect();
            img.type = Image.Type.Sliced;
            img.color = c;
            var rt = go.GetComponent<RectTransform>();
            rt.sizeDelta = new Vector2(CellSize, CellSize);
            rt.anchoredPosition = new Vector2(startX + col * (CellSize + CellGap), startY - row * (CellSize + CellGap));

            // The actual profit/loss for that spin, not the winning number — "was it
            // a win or a loss" is already the block's color; the number people
            // actually want here is how much.
            string sign = rec.NetChange > 0 ? "+" : "";
            var numText = UIFactory.MakeText(go.transform, "Num", Vector2.zero, 13,
                sizeDelta: new Vector2(CellSize - 4f, CellSize - 4f), color: Color.black, style: FontStyle.Bold);
            numText.resizeTextForBestFit = true;
            numText.resizeTextMinSize = 8;
            numText.resizeTextMaxSize = 13;
            numText.text = $"{sign}{rec.NetChange}";

            blocks.Add(go);
            if (animateNewest && i == records.Count - 1) JuiceTweens.PopIn(this, rt, overshoot: 1.3f, duration: 0.25f);
        }

        int wins = records.Count(r => r.NetChange > 0);
        int losses = records.Count(r => r.NetChange < 0);
        int flats = records.Count - wins - losses;
        long net = records.Sum(r => r.NetChange);

        summaryText.color = net >= 0 ? UIFactory.Positive : UIFactory.Negative;
        summaryText.text = $"W:{wins}  L:{losses}  D:{flats}\nNet: {(net >= 0 ? "+" : "")}{net}";
    }
}
