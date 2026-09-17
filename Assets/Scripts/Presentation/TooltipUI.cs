using TMPro;
using UnityEngine;
using UnityEngine.UI;

// Shared singleton tooltip panel — hover-over text for any UI element that adds a
// TooltipTrigger. Created once in the scene; all triggers share the same panel.
public class TooltipUI : MonoBehaviour
{
    public static TooltipUI Instance { get; private set; }

    GameObject panelGO;
    TextMeshProUGUI label;
    RectTransform canvasRT;

    const float PadX = 12f, PadY = 8f, MaxWidth = 280f;

    public static TooltipUI Create(Transform canvasRoot)
    {
        if (Instance != null) return Instance;
        var go = new GameObject("TooltipUI");
        go.transform.SetParent(canvasRoot, false);
        var ui = go.AddComponent<TooltipUI>();
        ui.Build(canvasRoot);
        Instance = ui;
        return ui;
    }

    void Build(Transform canvasRoot)
    {
        canvasRT = canvasRoot.GetComponent<RectTransform>();

        panelGO = new GameObject("TooltipPanel");
        panelGO.transform.SetParent(canvasRoot, false);

        var rt = panelGO.AddComponent<RectTransform>();
        rt.pivot = new Vector2(0f, 0f);
        rt.anchorMin = rt.anchorMax = new Vector2(0f, 0f);

        var img = panelGO.AddComponent<Image>();
        img.sprite = UIFactory.RoundedRect();
        img.type = Image.Type.Sliced;
        img.color = new Color(0.06f, 0.07f, 0.1f, 0.96f);
        UIFactory.AddSharpFrame(panelGO, UIFactory.AccentDim, square: true);

        var shadow = panelGO.AddComponent<Shadow>();
        shadow.effectColor = new Color(0, 0, 0, 0.7f);
        shadow.effectDistance = new Vector2(0, -3);

        var textGO = new GameObject("TooltipText");
        textGO.transform.SetParent(panelGO.transform, false);
        label = textGO.AddComponent<TextMeshProUGUI>();
        if (UIFactory.PixelFont != null) label.font = UIFactory.PixelFont;
        label.fontSize = 14;
        label.color = UIFactory.TextLight;
        label.enableWordWrapping = true;
        label.alignment = TextAlignmentOptions.TopLeft;
        var textRT = textGO.GetComponent<RectTransform>();
        textRT.anchorMin = Vector2.zero;
        textRT.anchorMax = Vector2.one;
        textRT.offsetMin = new Vector2(PadX, PadY);
        textRT.offsetMax = new Vector2(-PadX, -PadY);

        // Sort above everything else.
        var canvas = panelGO.AddComponent<Canvas>();
        canvas.overrideSorting = true;
        canvas.sortingOrder = 999;
        panelGO.AddComponent<GraphicRaycaster>();

        panelGO.SetActive(false);
    }

    public void Show(string text, Vector2 screenPos)
    {
        label.text = text;
        // Force layout to measure text size.
        label.ForceMeshUpdate();
        float textHeight = label.preferredHeight + PadY * 2;
        float width = Mathf.Min(label.preferredWidth + PadX * 2, MaxWidth);

        var rt = panelGO.GetComponent<RectTransform>();
        rt.sizeDelta = new Vector2(width, textHeight);

        // Convert screen position to canvas local position and nudge above cursor.
        RectTransformUtility.ScreenPointToLocalPointInRectangle(canvasRT, screenPos, null, out Vector2 localPt);
        localPt += new Vector2(12f, 24f);
        // Clamp so tooltip stays inside canvas bounds.
        var canvas2 = canvasRT.sizeDelta;
        localPt.x = Mathf.Clamp(localPt.x, -canvas2.x / 2f, canvas2.x / 2f - width);
        localPt.y = Mathf.Clamp(localPt.y, -canvas2.y / 2f, canvas2.y / 2f - textHeight);

        rt.anchoredPosition = localPt;
        panelGO.SetActive(true);
    }

    public void Hide() => panelGO.SetActive(false);

    void OnDestroy() { if (Instance == this) Instance = null; }
}
