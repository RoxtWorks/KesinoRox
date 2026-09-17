using System;
using UnityEngine;
using UnityEngine.EventSystems;

// Uses right-click to take a bet down; Button on the same object keeps handling left-click placement.
public class RightClickRelay : MonoBehaviour, IPointerClickHandler
{
    public Action OnRightClick;

    public void OnPointerClick(PointerEventData eventData)
    {
        if (eventData.button == PointerEventData.InputButton.Right) OnRightClick?.Invoke();
    }

    public static void Attach(GameObject go, Action onRightClick) =>
        go.AddComponent<RightClickRelay>().OnRightClick = onRightClick;
}
