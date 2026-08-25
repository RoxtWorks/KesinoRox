using System.Collections;
using UnityEngine;

// Shared scale-tween coroutines — pop-in (overshoot once, settle) and pulse
// (grow-and-back) — used by chip markers, P/L blocks, and the winning-number cell
// so each feature isn't rewriting the same easing math.
public static class JuiceTweens
{
    public static void PopIn(MonoBehaviour host, RectTransform rt, float overshoot = 1.25f, float duration = 0.22f)
        => host.StartCoroutine(PopInRoutine(rt, overshoot, duration));

    static IEnumerator PopInRoutine(RectTransform rt, float overshoot, float duration)
    {
        float t = 0f;
        while (t < duration)
        {
            t += Time.deltaTime;
            float u = Mathf.Clamp01(t / duration);
            float scale = u < 0.6f
                ? Mathf.Lerp(0f, overshoot, u / 0.6f)
                : Mathf.Lerp(overshoot, 1f, (u - 0.6f) / 0.4f);
            if (rt == null) yield break;
            rt.localScale = Vector3.one * scale;
            yield return null;
        }
        if (rt != null) rt.localScale = Vector3.one;
    }

    public static void Pulse(MonoBehaviour host, RectTransform rt, float peakScale = 1.35f, float duration = 0.35f)
        => host.StartCoroutine(PulseRoutine(rt, peakScale, duration));

    static IEnumerator PulseRoutine(RectTransform rt, float peakScale, float duration)
    {
        float t = 0f;
        while (t < duration)
        {
            t += Time.deltaTime;
            float u = Mathf.Clamp01(t / duration);
            float scale = 1f + Mathf.Sin(u * Mathf.PI) * (peakScale - 1f);
            if (rt == null) yield break;
            rt.localScale = Vector3.one * scale;
            yield return null;
        }
        if (rt != null) rt.localScale = Vector3.one;
    }
}
