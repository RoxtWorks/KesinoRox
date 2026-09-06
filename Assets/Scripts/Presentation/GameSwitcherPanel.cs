using System.Linq;
using UnityEngine;
using UnityEngine.UI;

// In-scene "switch game" popup, shown by clicking MENU — there's no more navigating
// back to an empty MainMenu scene mid-session. Lists every game in GameCatalog
// except whichever one is currently loaded; picking one routes straight there via
// SceneTransition. A dark scrim behind the panel dismisses it on click, same as a
// standard modal.
public class GameSwitcherPanel : MonoBehaviour
{
    GameObject panelRoot;

    public void Build(Transform canvas, string currentSceneName)
    {
        panelRoot = new GameObject("GameSwitcherPanel");
        panelRoot.transform.SetParent(canvas, false);
        var rt = panelRoot.AddComponent<RectTransform>();
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = Vector2.zero;
        rt.offsetMax = Vector2.zero;
        // Above everything else built into the scene so far.
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

        UIFactory.MakeHeroTitle(panelRoot.transform, "SwitcherTitle", new Vector2(0, 260), "CHOOSE A GAME", 30);

        var others = GameCatalog.Games.Where(g => g.SceneName != currentSceneName).ToList();
        const float buttonWidth = 260f, gap = 32f;
        const int perRow = 4;
        int rows = Mathf.CeilToInt(others.Count / (float)perRow);
        float rowHeight = 120f;
        float startY = (rows - 1) * rowHeight / 2f - 20f;

        for (int i = 0; i < others.Count; i++)
        {
            int row = i / perRow;
            int col = i % perRow;
            int rowCount = Mathf.Min(perRow, others.Count - row * perRow);
            float rowWidth = rowCount * buttonWidth + (rowCount - 1) * gap;
            float startX = -rowWidth / 2f + buttonWidth / 2f;
            var pos = new Vector2(startX + col * (buttonWidth + gap), startY - row * rowHeight);
            var entry = others[i];
            UIFactory.MakeButton(panelRoot.transform, $"SwitchTo_{entry.SceneName}", pos, new Vector2(buttonWidth, 100),
                entry.DisplayName, entry.Color, () =>
                {
                    Hide();
                    SceneTransition.Load(entry.SceneName);
                }, 20, pixelFont: true);
        }

        panelRoot.SetActive(false);
    }

    // Re-asserts top-most order every time it's shown, not just once at build time —
    // Build() runs early in each GameManager's setup, before the betting controller
    // and its bet spots/buttons get added to the same canvas, which would otherwise
    // stack visually on top of this panel.
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
