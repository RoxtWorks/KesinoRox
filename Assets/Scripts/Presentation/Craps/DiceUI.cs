using System.Collections;
using DG.Tweening;
using UnityEngine;
using UnityEngine.UI;

// One die — a white rounded-rect (same sprite every card/panel/button in this project
// already uses) with up to 6 pip dots toggled on/off per face value, standard layout.
// A brief "tumble" (rapid random-face cycling) plays before landing on the real
// result, same spin-then-settle beat WheelSpinAnimator uses for the roulette wheel.
public class DiceUI : MonoBehaviour
{
    Image bg;
    RectTransform rt;
    readonly GameObject[] pips = new GameObject[7]; // index 1..6 used, 0 unused

    static readonly Color DieWhite = Color.white;
    static readonly Color PipDark = new Color(0.08f, 0.08f, 0.1f);
    const float PipOffset = 15f;

    // Which of the 7 grid slots (TL,TR,ML,MR,BL,BR,C) light up per face value.
    static readonly int[][] FacePips =
    {
        new int[0],                      // 0 unused
        new[] { 6 },                     // 1: center
        new[] { 0, 5 },                  // 2: TL, BR
        new[] { 0, 6, 5 },               // 3: TL, center, BR
        new[] { 0, 1, 4, 5 },            // 4: corners
        new[] { 0, 1, 6, 4, 5 },         // 5: corners + center
        new[] { 0, 1, 2, 3, 4, 5 }       // 6: corners + mid-sides
    };

    public static DiceUI Create(Transform parent, Vector2 anchoredPos, Vector2 size)
    {
        var go = new GameObject("Die");
        go.transform.SetParent(parent, false);
        var die = go.AddComponent<DiceUI>();
        die.BuildSelf(anchoredPos, size);
        return die;
    }

    void BuildSelf(Vector2 anchoredPos, Vector2 size)
    {
        rt = gameObject.AddComponent<RectTransform>();
        rt.sizeDelta = size;
        rt.anchoredPosition = anchoredPos;

        bg = gameObject.AddComponent<Image>();
        bg.sprite = UIFactory.RoundedRect();
        bg.type = Image.Type.Sliced;
        bg.color = DieWhite;

        var shadow = gameObject.AddComponent<Shadow>();
        shadow.effectColor = new Color(0f, 0f, 0f, 0.4f);
        shadow.effectDistance = new Vector2(0f, -2f);

        // 7-slot grid: TL(0) TR(1) ML(2) MR(3) BL(4) BR(5) C(6).
        Vector2[] slotPos =
        {
            new Vector2(-PipOffset, PipOffset), new Vector2(PipOffset, PipOffset),
            new Vector2(-PipOffset, 0), new Vector2(PipOffset, 0),
            new Vector2(-PipOffset, -PipOffset), new Vector2(PipOffset, -PipOffset),
            Vector2.zero
        };
        for (int i = 0; i < 7; i++)
        {
            var pipGO = new GameObject($"Pip{i}");
            pipGO.transform.SetParent(transform, false);
            var img = pipGO.AddComponent<Image>();
            img.sprite = UIFactory.Circle();
            img.color = PipDark;
            var pipRt = pipGO.GetComponent<RectTransform>();
            pipRt.sizeDelta = new Vector2(9f, 9f);
            pipRt.anchoredPosition = slotPos[i];
            pips[i >= 6 ? 6 : i] = pipGO; // index 6 is the center slot regardless
            pipGO.SetActive(false);
        }
    }

    public void SetFace(int value)
    {
        for (int i = 0; i < 7; i++)
            if (pips[i] != null) pips[i].SetActive(false);
        if (value < 1 || value > 6) return;
        foreach (int slot in FacePips[value])
            pips[slot >= 6 ? 6 : slot].SetActive(true);
    }

    Coroutine tumbleRoutine;

    // Cycles random faces for a short beat, then settles on the real value with a
    // small pop — reads as "rolling" instead of the result just appearing.
    public void Roll(int finalValue, float duration = 0.45f)
    {
        if (tumbleRoutine != null) StopCoroutine(tumbleRoutine);
        tumbleRoutine = StartCoroutine(TumbleThenSettle(finalValue, duration));
    }

    IEnumerator TumbleThenSettle(int finalValue, float duration)
    {
        float t = 0f;
        const float frameTime = 0.06f;
        while (t < duration)
        {
            SetFace(UnityEngine.Random.Range(1, 7));
            yield return new WaitForSeconds(frameTime);
            t += frameTime;
        }
        SetFace(finalValue);
        rt.DOKill();
        rt.localScale = Vector3.one * 0.7f;
        rt.DOScale(1f, 0.15f).SetEase(Ease.OutBack).SetLink(gameObject, LinkBehaviour.KillOnDestroy);
        tumbleRoutine = null;
    }
}
