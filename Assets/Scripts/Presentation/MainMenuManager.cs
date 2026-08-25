using DG.Tweening;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

// Entry-point scene — placeholder title + one button per game. Same procedural-at-
// runtime construction pattern as GameManager/BlackjackGameManager (built entirely
// in Start(), no prefabs/serialized fields). First screen in the project to use the
// TMP pixel font + DOTween hover/press juice instead of UIFactory's legacy Text —
// a deliberately contained pilot before rolling the pattern out further.
public class MainMenuManager : MonoBehaviour
{
    static readonly Color BgColor = new Color(0.04f, 0.03f, 0.06f);
    static readonly Color ThemeAccent = new Color(0.75f, 0.82f, 0.9f);
    static readonly Color TextDim = new Color(0.72f, 0.7f, 0.66f);

    TMP_FontAsset pixelFont;

    void Start()
    {
        Application.runInBackground = true;
        pixelFont = UIFactory.PixelFont;
        SetupCamera();
        SetupUI();
    }

    void SetupCamera()
    {
        var camGO = new GameObject("Main Camera");
        var cam = camGO.AddComponent<Camera>();
        camGO.tag = "MainCamera";
        cam.clearFlags = CameraClearFlags.SolidColor;
        cam.backgroundColor = BgColor;
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
        var scaler = canvasGO.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920, 1080);
        scaler.matchWidthOrHeight = 0.5f;
        canvasGO.AddComponent<GraphicRaycaster>();

        UIFactory.MakeButton(canvasGO.transform, "CloseAppBtn", new Vector2(880, 515), new Vector2(140, 32),
            "CLOSE APP", new Color(0.4f, 0.16f, 0.16f), () =>
            {
#if UNITY_EDITOR
                UnityEditor.EditorApplication.isPlaying = false;
#else
                Application.Quit();
#endif
            }, 13, pixelFont: true);

        var title = MakePixelText(canvasGO.transform, "TitleText", new Vector2(0, 230), 96,
            new Vector2(1100, 160), ThemeAccent, FontStyles.Bold);
        title.text = "CASINO SIM";
        title.enableVertexGradient = true;
        title.colorGradient = new VertexGradient(
            new Color(0.9f, 0.95f, 1f), new Color(0.9f, 0.95f, 1f),
            new Color(0.5f, 0.6f, 0.75f), new Color(0.5f, 0.6f, 0.75f));

        var titleRt = title.GetComponent<RectTransform>();
        titleRt.DOAnchorPosY(240, 1.6f).SetEase(Ease.InOutSine).SetLoops(-1, LoopType.Yoyo)
            .SetLink(titleRt.gameObject, LinkBehaviour.KillOnDestroy);

        var subtitle = MakePixelText(canvasGO.transform, "SubtitleText", new Vector2(0, 110), 28,
            new Vector2(500, 50), TextDim, FontStyles.Normal);
        subtitle.text = "choose a game";

        // Reads GameCatalog instead of one hardcoded button per game — adding a
        // fourth game later means adding one catalog entry, not a new call here too.
        const float buttonWidth = 260f, gap = 40f;
        var games = GameCatalog.Games;
        float totalWidth = games.Count * buttonWidth + (games.Count - 1) * gap;
        float startX = -totalWidth / 2f + buttonWidth / 2f;
        for (int i = 0; i < games.Count; i++)
        {
            var entry = games[i];
            var pos = new Vector2(startX + i * (buttonWidth + gap), -80);
            MakeGameButton(canvasGO.transform, $"{entry.SceneName}Btn", pos, new Vector2(buttonWidth, 100),
                entry.DisplayName, entry.Color, () => SceneTransition.Load(entry.SceneName));
        }
    }

    TextMeshProUGUI MakePixelText(Transform parent, string name, Vector2 anchoredPos, float fontSize,
        Vector2 sizeDelta, Color color, FontStyles style)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        var t = go.AddComponent<TextMeshProUGUI>();
        if (pixelFont != null) t.font = pixelFont;
        t.fontSize = fontSize;
        t.color = color;
        t.fontStyle = style;
        t.alignment = TextAlignmentOptions.Center;
        var rt = go.GetComponent<RectTransform>();
        rt.sizeDelta = sizeDelta;
        rt.anchoredPosition = anchoredPos;
        return t;
    }

    void MakeGameButton(Transform parent, string name, Vector2 anchoredPos, Vector2 size,
        string label, Color color, UnityEngine.Events.UnityAction onClick)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        var img = go.AddComponent<Image>();
        img.sprite = UIFactory.RoundedRect();
        img.type = Image.Type.Sliced;
        img.color = color;
        var rt = go.GetComponent<RectTransform>();
        rt.sizeDelta = size;
        rt.anchoredPosition = anchoredPos;

        var shadow = go.AddComponent<Shadow>();
        shadow.effectColor = new Color(0, 0, 0, 0.5f);
        shadow.effectDistance = new Vector2(0, -3);

        UIFactory.AddSharpFrame(go, ThemeAccent, square: true);

        var btn = go.AddComponent<Button>();
        btn.targetGraphic = img;
        btn.transition = Selectable.Transition.None;
        if (onClick != null) btn.onClick.AddListener(onClick);
        go.AddComponent<DOTweenButtonFX>();

        var labelGO = new GameObject("Label");
        labelGO.transform.SetParent(go.transform, false);
        var labelText = labelGO.AddComponent<TextMeshProUGUI>();
        if (pixelFont != null) labelText.font = pixelFont;
        labelText.color = Color.white;
        labelText.alignment = TextAlignmentOptions.Center;
        labelText.text = label;
        // Autosize instead of a fixed 34pt — narrower buttons (3-across instead of
        // 2-across once Baccarat joined) would otherwise let long labels overflow.
        labelText.enableAutoSizing = true;
        labelText.fontSizeMin = 16;
        labelText.fontSizeMax = 34;
        var labelRt = labelGO.GetComponent<RectTransform>();
        labelRt.anchorMin = Vector2.zero;
        labelRt.anchorMax = Vector2.one;
        labelRt.offsetMin = new Vector2(6, 2);
        labelRt.offsetMax = new Vector2(-6, -2);
    }
}
