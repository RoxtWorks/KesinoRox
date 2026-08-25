using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.UI;

// Frequency tracker over a rolling window of the last WindowSize spins — surfaces
// the 3 numbers hitting most (hot) and least (cold) often, each with its own hit
// count. Each spin is statistically independent so this has no predictive value,
// but it's the classic "streak" read players watch for, useful here as a
// strategy-testing signal like everything else.
public class HotColdTrackerUI : MonoBehaviour
{
    const int WindowSize = 500;
    const int SlotCount = 3;
    readonly Queue<int> window = new Queue<int>();
    readonly int[] counts = new int[37];

    Text[] hotNumTexts = new Text[SlotCount];
    Text[] hotCountTexts = new Text[SlotCount];
    Text[] coldNumTexts = new Text[SlotCount];
    Text[] coldCountTexts = new Text[SlotCount];
    Text summaryText;

    public void Build(Transform canvas, Vector2 anchoredPos)
    {
        var size = new Vector2(280, 340);
        UIFactory.MakePanel(canvas, "HotColdBg", anchoredPos, size, UIFactory.PanelDark);
        UIFactory.MakeSectionHeader(canvas, "Hot / Cold (500)", anchoredPos + new Vector2(0, size.y / 2f - 20f), new Vector2(size.x - 24, 22));

        UIFactory.MakeText(canvas, "HotLabel", anchoredPos + new Vector2(0, 112), 16, TextAnchor.MiddleCenter,
            new Vector2(size.x - 20, 22), UIFactory.Negative, FontStyle.Bold).text = "HOT";
        BuildSlots(canvas, anchoredPos, 70f, hotNumTexts, hotCountTexts);

        UIFactory.MakeText(canvas, "ColdLabel", anchoredPos + new Vector2(0, -8), 16, TextAnchor.MiddleCenter,
            new Vector2(size.x - 20, 22), new Color(0.42f, 0.72f, 0.95f), FontStyle.Bold).text = "COLD";
        BuildSlots(canvas, anchoredPos, -50f, coldNumTexts, coldCountTexts);

        summaryText = UIFactory.MakeText(canvas, "HotColdSummary", anchoredPos + new Vector2(0, -128), 15,
            TextAnchor.MiddleCenter, new Vector2(size.x - 20, 24), UIFactory.TextDim);

        Refresh();
    }

    void BuildSlots(Transform canvas, Vector2 anchoredPos, float rowY, Text[] numTexts, Text[] countTexts)
    {
        float[] xs = { -85f, 0f, 85f };
        for (int i = 0; i < SlotCount; i++)
        {
            numTexts[i] = UIFactory.MakeText(canvas, $"Num_{rowY}_{i}", anchoredPos + new Vector2(xs[i], rowY), 30,
                TextAnchor.MiddleCenter, new Vector2(78, 40), UIFactory.TextLight, FontStyle.Bold);
            countTexts[i] = UIFactory.MakeText(canvas, $"Count_{rowY}_{i}", anchoredPos + new Vector2(xs[i], rowY - 30f), 15,
                TextAnchor.MiddleCenter, new Vector2(78, 20), UIFactory.TextDim);
        }
    }

    public void AddSpin(int number)
    {
        window.Enqueue(number);
        counts[number]++;
        if (window.Count > WindowSize)
            counts[window.Dequeue()]--;
        Refresh();
    }

    public void Clear()
    {
        window.Clear();
        System.Array.Clear(counts, 0, counts.Length);
        Refresh();
    }

    // Bulk-load path for session restore — unlike the other three trackers this one
    // doesn't destroy/recreate GameObjects per AddSpin (Refresh only updates a fixed
    // set of Text components), so it was never the O(n^2) part of the slow-load
    // problem. Still wasteful to Refresh() once per saved spin when only the final
    // state matters, so this replays into the counts/window directly and refreshes
    // once at the end, same pattern as the other trackers' LoadRecords/LoadSpins.
    public void LoadSpins(IEnumerable<int> loadedOldestFirst)
    {
        window.Clear();
        System.Array.Clear(counts, 0, counts.Length);
        foreach (int number in loadedOldestFirst)
        {
            window.Enqueue(number);
            counts[number]++;
            if (window.Count > WindowSize)
                counts[window.Dequeue()]--;
        }
        Refresh();
    }

    static Color NumberColor(int n) => n == 0 ? UIFactory.FeltGreen : (WheelLayout.IsRed(n) ? UIFactory.RedBet : UIFactory.TextLight);

    void Refresh()
    {
        if (window.Count == 0)
        {
            for (int i = 0; i < SlotCount; i++)
            {
                hotNumTexts[i].text = "—"; hotCountTexts[i].text = "";
                coldNumTexts[i].text = "—"; coldCountTexts[i].text = "";
            }
            summaryText.text = "No spins yet";
            return;
        }

        // Hot only makes sense among numbers that have actually hit. Cold is judged
        // across ALL 37 numbers, not just ones seen so far — a number that hasn't
        // come up even once is colder than one that's hit only once, so it has to
        // win the "lowest count" ranking, not get excluded from consideration.
        var hot = Enumerable.Range(0, 37).Where(n => counts[n] > 0)
            .OrderByDescending(n => counts[n]).ThenBy(n => n).Take(SlotCount).ToList();
        var cold = Enumerable.Range(0, 37)
            .OrderBy(n => counts[n]).ThenBy(n => n).Take(SlotCount).ToList();

        for (int i = 0; i < SlotCount; i++)
        {
            if (i < hot.Count)
            {
                hotNumTexts[i].text = hot[i].ToString();
                hotNumTexts[i].color = NumberColor(hot[i]);
                hotCountTexts[i].text = $"{counts[hot[i]]}x";
            }
            else { hotNumTexts[i].text = "—"; hotCountTexts[i].text = ""; }

            coldNumTexts[i].text = cold[i].ToString();
            coldNumTexts[i].color = NumberColor(cold[i]);
            coldCountTexts[i].text = $"{counts[cold[i]]}x";
        }

        summaryText.text = $"{window.Count} spin{(window.Count == 1 ? "" : "s")} tracked";
    }
}
