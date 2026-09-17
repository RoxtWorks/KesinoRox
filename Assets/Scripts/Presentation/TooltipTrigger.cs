using UnityEngine;
using UnityEngine.EventSystems;

// Attach to any UI GameObject. On hover shows a tooltip via TooltipUI.Instance.
public class TooltipTrigger : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler, IPointerMoveHandler
{
    public string Text;

    public void OnPointerEnter(PointerEventData e) => TooltipUI.Instance?.Show(Text, e.position);
    public void OnPointerMove(PointerEventData e) => TooltipUI.Instance?.Show(Text, e.position);
    public void OnPointerExit(PointerEventData e) => TooltipUI.Instance?.Hide();
    void OnDisable() => TooltipUI.Instance?.Hide();
}
