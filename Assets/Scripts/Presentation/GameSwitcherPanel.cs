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
        const float buttonWidth = 280f, gap = 40f;
        float totalWidth = others.Count * buttonWidth + (others.Count - 1) * gap;
        float startX = -totalWidth / 2f + buttonWidth / 2f;

        for (int i = 0; i < others.Count; i++)
        {
            var entry = others[i];
            var pos = new Vector2(startX + i * (buttonWidth + gap), -20);
            UIFactory.MakeButton(panelRoot.transform, $"SwitchTo_{entry.SceneName}", pos, new Vector2(buttonWidth, 110),
                entry.DisplayName, entry.Color, () =>
                {
                    Hide();
                    SceneTransition.Load(entry.SceneName);
                }, 22, pixelFont: true);
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
