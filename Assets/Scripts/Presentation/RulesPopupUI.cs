using UnityEngine;
using UnityEngine.UI;

// In-scene "how to play" reference — same dark-scrim modal pattern as
// GameSwitcherPanel, just showing a block of rules/payout text instead of a
// list of buttons. Triggered by a "?" button next to MENU in each game scene.
public class RulesPopupUI : MonoBehaviour
{
    GameObject panelRoot;

    public void Build(Transform canvas, string title, string bodyText)
    {
        panelRoot = new GameObject("RulesPopupPanel");
        panelRoot.transform.SetParent(canvas, false);
        var rt = panelRoot.AddComponent<RectTransform>();
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = Vector2.zero;
        rt.offsetMax = Vector2.zero;
        panelRoot.transform.SetAsLastSibling();

        var scrimGO = new GameObject("Scrim");
        scrimGO.transform.SetParent(panelRoot.transform, false);
        var scrimRt = scrimGO.AddComponent<RectTransform>();
        scrimRt.anchorMin = Vector2.zero;
        scrimRt.anchorMax = Vector2.one;
        scrimRt.offsetMin = Vector2.zero;
        scrimRt.offsetMax = Vector2.zero;
        var scrimImg = scrimGO.AddComponent<Image>();
        scrimImg.color = new Color(0.02f, 0.02f, 0.03f, 0.97f);
        var scrimBtn = scrimGO.AddComponent<Button>();
        scrimBtn.transition = Selectable.Transition.None;
        scrimBtn.onClick.AddListener(Hide);

        UIFactory.MakeHeroTitle(panelRoot.transform, "RulesTitle", new Vector2(0, 300), title, 30);

        var bodyPanel = UIFactory.MakeFramedPanel(panelRoot.transform, "RulesBodyBg", new Vector2(0, 40), new Vector2(760, 480), Color.black);
        var bodyText2 = UIFactory.MakeText(bodyPanel.transform, "RulesBody", Vector2.zero, 16,
            TextAnchor.UpperLeft, new Vector2(700, 440), UIFactory.TextLight);
        bodyText2.text = bodyText;

        UIFactory.MakeButton(panelRoot.transform, "RulesCloseBtn", new Vector2(0, -280), new Vector2(160, 46),
            "CLOSE", UIFactory.AccentDim, Hide, 14, pixelFont: true);

        panelRoot.SetActive(false);
    }

    public void Toggle()
    {
        if (panelRoot.activeSelf) Hide();
        else Show();
    }

    public void Show()
    {
        panelRoot.transform.SetAsLastSibling();
        panelRoot.SetActive(true);
    }

    public void Hide() => panelRoot.SetActive(false);
}
