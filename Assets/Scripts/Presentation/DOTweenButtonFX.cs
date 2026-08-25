using DG.Tweening;
using UnityEngine;
using UnityEngine.EventSystems;

// Drop-on-any-button hover/press juice via DOTween — scale up slightly on hover,
// punch down on click. Self-contained (reads its own RectTransform's starting
// scale as the baseline), so it can be added to any button without the caller
// wiring anything else up.
public class DOTweenButtonFX : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler, IPointerDownHandler, IPointerUpHandler
{
    public float hoverScale = 1.06f;
    public float pressScale = 0.94f;
    public float duration = 0.15f;

    RectTransform rt;
    Vector3 baseScale;

    void Awake()
    {
        rt = (RectTransform)transform;
        baseScale = rt.localScale;
    }

    public void OnPointerEnter(PointerEventData eventData)
    {
        rt.DOKill();
        rt.DOScale(baseScale * hoverScale, duration).SetEase(Ease.OutBack);
    }

    public void OnPointerExit(PointerEventData eventData)
    {
        rt.DOKill();
        rt.DOScale(baseScale, duration).SetEase(Ease.OutQuad);
    }

    public void OnPointerDown(PointerEventData eventData)
    {
        rt.DOKill();
        rt.DOScale(baseScale * pressScale, duration * 0.5f).SetEase(Ease.OutQuad);
    }

    public void OnPointerUp(PointerEventData eventData)
    {
        rt.DOKill();
        rt.DOScale(baseScale * hoverScale, duration).SetEase(Ease.OutBack);
    }
}
