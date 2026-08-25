using UnityEngine;
using UnityEngine.SceneManagement;

// Entry-point scene — placeholder title + one button per game. Same procedural-at-
// runtime construction pattern as GameManager/BlackjackGameManager (built entirely
// in Start(), no prefabs/serialized fields), just much smaller since there's no
// gameplay here.
public class MainMenuManager : MonoBehaviour
{
    void Start()
    {
        SetupCamera();
        SetupUI();
    }

    void SetupCamera()
    {
        var camGO = new GameObject("Main Camera");
        var cam = camGO.AddComponent<Camera>();
        camGO.tag = "MainCamera";
        cam.clearFlags = CameraClearFlags.SolidColor;
        cam.backgroundColor = new Color(0.015f, 0.02f, 0.03f);
        // Runtime-created cameras don't get an AudioListener automatically — only
        // the Editor's own "Create > Camera" menu does that.
        camGO.AddComponent<AudioListener>();
    }

    void SetupUI()
    {
        var esGO = new GameObject("EventSystem");
        esGO.AddComponent<UnityEngine.EventSystems.EventSystem>();
        esGO.AddComponent<UnityEngine.EventSystems.StandaloneInputModule>();

        var canvasGO = new GameObject("Canvas");
        var canvas = canvasGO.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        var scaler = canvasGO.AddComponent<UnityEngine.UI.CanvasScaler>();
        scaler.uiScaleMode = UnityEngine.UI.CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920, 1080);
        scaler.matchWidthOrHeight = 0.5f;
        canvasGO.AddComponent<UnityEngine.UI.GraphicRaycaster>();

        UIFactory.MakeButton(canvasGO.transform, "CloseAppBtn", new Vector2(880, 515), new Vector2(140, 32),
            "CLOSE APP", new Color(0.4f, 0.16f, 0.16f), () =>
            {
#if UNITY_EDITOR
                UnityEditor.EditorApplication.isPlaying = false;
#else
                Application.Quit();
#endif
            }, 13);

        // Placeholder title — swap the string whenever there's a real name, nothing
        // else needs to change.
        UIFactory.MakeFramedPanel(canvasGO.transform, "TitlePanelBg", new Vector2(0, 220), new Vector2(560, 140), Color.black);
        var title = UIFactory.MakeText(canvasGO.transform, "TitleText", new Vector2(0, 220), 42,
            sizeDelta: new Vector2(520, 100), color: UIFactory.Accent, style: FontStyle.Bold);
        title.text = "CASINO SIM";

        var subtitle = UIFactory.MakeText(canvasGO.transform, "SubtitleText", new Vector2(0, 90), 16,
            sizeDelta: new Vector2(400, 30), color: UIFactory.TextDim);
        subtitle.text = "Choose a game";

        var rouletteBtn = UIFactory.MakeButton(canvasGO.transform, "RouletteBtn", new Vector2(-160, -60), new Vector2(280, 90),
            "ROULETTE", UIFactory.Positive, () => SceneManager.LoadScene("Main"), 22);
        var blackjackBtn = UIFactory.MakeButton(canvasGO.transform, "BlackjackBtn", new Vector2(160, -60), new Vector2(280, 90),
            "BLACKJACK", UIFactory.AccentDim, () => SceneManager.LoadScene("Blackjack"), 22);

        JuiceTweens.PopIn(this, rouletteBtn.GetComponent<RectTransform>(), overshoot: 1.15f, duration: 0.3f);
        JuiceTweens.PopIn(this, blackjackBtn.GetComponent<RectTransform>(), overshoot: 1.15f, duration: 0.3f);
    }
}
