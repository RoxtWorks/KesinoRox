using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

// Spin feedback as a horizontal scrolling strip of pockets in real wheel order —
// like a slot-machine reel rolling to a stop, not a 3D ball. The winning number is
// already decided (SpinResultGenerator ran first); this just has to roll convincingly
// and land exactly on it. Always crisp 2D text, no camera-angle legibility problems,
// no physics to hang/tunnel/eject.
public class ConveyorBeltUI : MonoBehaviour
{
    const float CellPitch = 60f;      // center-to-center spacing between number cells
    const int Repeats = 10;           // copies of the 37-pocket sequence laid end to end
    const float Duration = 8f; // matches WheelSpinAnimator so both land together
    // Caps tick rate during the fast opening portion of the spin (many cells can cross
    // in a single frame there) without thinning out the tail — by the time the belt's
    // slow enough to matter, cells are already further apart than this on their own.
    const float MinTickInterval = 0.07f;

    RectTransform track;
    RectTransform viewport;
    SoundManager soundManager;
    JuiceManager juiceManager;

    // Every cell across all repeated copies of a given number — a highlight has to
    // update all of them, not just one, since the strip repeats the 37-pocket
    // sequence several times end to end.
    readonly Dictionary<int, List<Image>> cellsByNumber = new Dictionary<int, List<Image>>();
    static readonly Color HighlightBlue = new Color(0.25f, 0.55f, 1f);

    public bool IsPlaying { get; private set; }

    // Quintic ease-out: fast start, long smooth decelerating tail, never speeds back
    // up — mimics a wheel coasting to a stop from momentum/friction. The previous
    // 3-keyframe AnimationCurve had an unconstrained auto-tangent at its middle point,
    // which could wobble (speed up again before the end) and read as "random" rather
    // than a consistent decelerating roll.
    static float EaseOut(float u) => 1f - Mathf.Pow(1f - u, 5f);

    public void Build(Transform canvas, Vector2 anchoredPos, float viewportWidth, float viewportHeight,
        SoundManager soundManager = null, JuiceManager juiceManager = null)
    {
        this.soundManager = soundManager;
        this.juiceManager = juiceManager;
        var viewportGO = new GameObject("ConveyorViewport");
        viewportGO.transform.SetParent(canvas, false);
        viewport = viewportGO.AddComponent<RectTransform>();
        viewport.sizeDelta = new Vector2(viewportWidth, viewportHeight);
        viewport.anchoredPosition = anchoredPos;
        var bg = viewportGO.AddComponent<Image>();
        bg.sprite = UIFactory.RoundedRect();
        bg.type = Image.Type.Sliced;
        bg.color = UIFactory.PanelDarker;
        viewportGO.AddComponent<RectMask2D>();

        var trackGO = new GameObject("ConveyorTrack");
        trackGO.transform.SetParent(viewportGO.transform, false);
        track = trackGO.AddComponent<RectTransform>();
        track.anchorMin = new Vector2(0.5f, 0.5f);
        track.anchorMax = new Vector2(0.5f, 0.5f);
        track.pivot = new Vector2(0.5f, 0.5f);

        int total = WheelLayout.PocketCount * Repeats;
        track.sizeDelta = new Vector2(total * CellPitch, viewportHeight);

        for (int i = 0; i < total; i++)
        {
            int number = WheelLayout.PocketOrder[i % WheelLayout.PocketCount];
            var cellGO = new GameObject($"Cell_{i}_{number}");
            cellGO.transform.SetParent(trackGO.transform, false);
            var img = cellGO.AddComponent<Image>();
            img.color = number == 0 ? UIFactory.FeltGreen : (WheelLayout.IsRed(number) ? UIFactory.RedBet : UIFactory.BlackBet);
            var rt = cellGO.GetComponent<RectTransform>();
            rt.sizeDelta = new Vector2(CellPitch - 6, viewportHeight - 10);
            rt.anchoredPosition = new Vector2(i * CellPitch - track.sizeDelta.x / 2f + CellPitch / 2f, 0);

            var text = UIFactory.MakeText(cellGO.transform, "Num", Vector2.zero, 22, sizeDelta: rt.sizeDelta,
                color: Color.white, style: FontStyle.Bold);
            text.text = number.ToString();

            if (!cellsByNumber.TryGetValue(number, out var list))
            {
                list = new List<Image>();
                cellsByNumber[number] = list;
            }
            list.Add(img);
        }

        // Fixed marker over the viewport center — the winning number always lands here.
        var markerGO = new GameObject("Marker");
        markerGO.transform.SetParent(viewportGO.transform, false);
        var markerImg = markerGO.AddComponent<Image>();
        markerImg.color = UIFactory.Accent;
        var markerRt = markerGO.GetComponent<RectTransform>();
        markerRt.sizeDelta = new Vector2(4, viewportHeight + 12);
        markerRt.anchoredPosition = Vector2.zero;

        track.anchoredPosition = Vector2.zero;
    }

    // Same blue-tinge treatment as the 3D wheel's pockets, for the same reason —
    // shows which number(s) are actually covered by a bet while the belt is rolling,
    // without losing the red/black/green identity underneath.
    public void SetHighlightedNumbers(HashSet<int> numbers)
    {
        foreach (var kv in cellsByNumber)
        {
            Color baseColor = kv.Key == 0 ? UIFactory.FeltGreen : (WheelLayout.IsRed(kv.Key) ? UIFactory.RedBet : UIFactory.BlackBet);
            bool hi = numbers != null && numbers.Contains(kv.Key);
            Color final = hi ? Color.Lerp(baseColor, HighlightBlue, 0.55f) : baseColor;
            foreach (var img in kv.Value) img.color = final;
        }
    }

    public void PlaySpin(int winningNumber, Action onComplete)
    {
        if (IsPlaying) return;
        StartCoroutine(SpinRoutine(winningNumber, onComplete));
    }

    IEnumerator SpinRoutine(int winningNumber, Action onComplete)
    {
        IsPlaying = true;

        int pocketCount = WheelLayout.PocketCount;
        int pocketIndex = Array.IndexOf(WheelLayout.PocketOrder, winningNumber);
        if (pocketIndex < 0) pocketIndex = 0;

        float trackHalfWidth = track.sizeDelta.x / 2f;
        float lapWidth = pocketCount * CellPitch;

        // Renormalize by whole laps BEFORE each spin so drift never accumulates across
        // many spins. Every previous spin's target was computed from a fixed baseline
        // (middle of the strip) and then walked backward by whole laps to guarantee a
        // long roll — with nothing ever bringing it back, position drifted further from
        // that baseline with every spin. After enough spins it walked clean off the
        // front of the built strip onto a stretch with no cell GameObjects: a blank,
        // "not really rotating" belt. Shifting by an exact multiple of one full lap is
        // visually seamless (the sequence repeats identically), so this is invisible.
        int baselineIndex = pocketCount * (Repeats / 2);
        float baselineX = -(baselineIndex * CellPitch - trackHalfWidth + CellPitch / 2f);
        Vector2 pos = track.anchoredPosition;
        while (pos.x < baselineX - lapWidth) pos.x += lapWidth;
        while (pos.x > baselineX + lapWidth) pos.x -= lapWidth;
        track.anchoredPosition = pos;

        // Land on a copy roughly in the middle of the repeated strip so there's always
        // plenty of room to roll from wherever the track currently sits.
        int targetGlobalIndex = baselineIndex + pocketIndex;
        float targetX = -(targetGlobalIndex * CellPitch - trackHalfWidth + CellPitch / 2f);

        float startX = track.anchoredPosition.x;
        // The strip scrolls leftward (track.x decreases) as it moves forward through
        // the sequence. Always roll forward a substantial distance regardless of where
        // the track currently sits, so the animation never feels like a short hop —
        // subtracting whole laps keeps landing on the same winning number since the
        // sequence repeats every pocketCount cells.
        float minRollDistance = pocketCount * CellPitch * 2.5f;
        while (startX - targetX < minRollDistance)
            targetX -= pocketCount * CellPitch;

        // "Tsk" once per pocket crossed — since the move is eased out, ticks fire
        // rapidly at first and naturally spread out as the belt decelerates, the same
        // rattle-then-click feel as a real ball losing momentum against the frets.
        // Rate-limited by TIME rather than skipping every Nth cell: skipping cells
        // uniformly would also thin out the tail, right when the spin should feel
        // most distinct (near-silent right before landing). A time floor only bites
        // during the fast opening stretch — once cells are naturally more than
        // MinTickInterval apart, every single one still ticks.
        int lastCellIndex = Mathf.FloorToInt(-startX / CellPitch);
        float lastTickTime = -MinTickInterval;
        int tickCount = 0;

        float t = 0f;
        while (t < Duration)
        {
            t += Time.deltaTime;
            float u = Mathf.Clamp01(t / Duration);
            float eased = EaseOut(u);
            float x = Mathf.Lerp(startX, targetX, eased);
            track.anchoredPosition = new Vector2(x, 0f);

            int cellIndex = Mathf.FloorToInt(-x / CellPitch);
            if (cellIndex != lastCellIndex)
            {
                lastCellIndex = cellIndex;
                if (t - lastTickTime >= MinTickInterval)
                {
                    soundManager?.PlayTsk();
                    // Every tick gets the tick sound, but only every 4th gets a shake —
                    // a shake on all ~45 ticks of an 8s spin reads as constant vibration
                    // rather than juice, especially across many repeated spins in one
                    // session. Sound alone still sells "fast," the shake just accents it.
                    tickCount++;
                    if (tickCount % 4 == 0) juiceManager?.MicroShake(1f - u);
                    lastTickTime = t;
                }
            }
            yield return null;
        }

        track.anchoredPosition = new Vector2(targetX, 0f);
        IsPlaying = false;
        onComplete?.Invoke();
    }
}
