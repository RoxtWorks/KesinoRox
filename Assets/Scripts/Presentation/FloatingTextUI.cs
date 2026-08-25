using System.Collections;
using Febucci.UI;
using TMPro;
using UnityEngine;

// "+250" / "-25" popup that rises and fades near the balance on every resolved
// spin — uses Text Animator (Febucci) for the per-character reveal instead of the
// text just snapping into place, on top of this class's own rise/fade motion.
public class FloatingTextUI : MonoBehaviour
{
    TextMeshProUGUI tmp;
    TextAnimator_TMP animator;
    CanvasGroup group;
    RectTransform rt;
    Vector2 basePos;
    Coroutine routine;

    public void Build(Transform canvas, Vector2 anchoredPos)
    {
        basePos = anchoredPos;
        var go = new GameObject("FloatingText");
        go.transform.SetParent(canvas, false);
        rt = go.AddComponent<RectTransform>();
        rt.sizeDelta = new Vector2(300, 50);
        rt.anchoredPosition = anchoredPos;

        tmp = go.AddComponent<TextMeshProUGUI>();
        tmp.alignment = TextAlignmentOptions.Center;
        tmp.fontSize = 30;
        tmp.fontStyle = FontStyles.Bold;
        tmp.raycastTarget = false;

        animator = go.AddComponent<TextAnimator_TMP>();

        group = go.AddComponent<CanvasGroup>();
        group.alpha = 0f;
    }

    public void Show(string text, Color color, int fontSize = 30)
    {
        if (routine != null) StopCoroutine(routine);
        routine = StartCoroutine(ShowRoutine(text, color, fontSize));
    }

    IEnumerator ShowRoutine(string text, Color color, int fontSize)
    {
        rt.anchoredPosition = basePos;
        group.alpha = 1f;
        tmp.fontSize = fontSize;
        string hex = ColorUtility.ToHtmlStringRGB(color);
        animator.SetText($"<color=#{hex}>{text}</color>");

        // Longer travel + duration than the original 40px/1.1s — that was barely
        // perceptible as "rising" before it faded, especially spawning this close to
        // the screen's top edge where there's little headroom to begin with.
        float duration = 1.3f;
        float t = 0f;
        while (t < duration)
        {
            t += Time.deltaTime;
            float u = Mathf.Clamp01(t / duration);
            rt.anchoredPosition = basePos + new Vector2(0f, 90f * u);
            group.alpha = 1f - Mathf.Pow(u, 3f);
            yield return null;
        }
        group.alpha = 0f;
    }
}
