using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.UI;

// Craps' equivalent of the other three games' GameManager — thin orchestrator, same
// composition pattern: builds the scene/UI procedurally at runtime and wires
// Presentation controllers to Core session objects. Reuses BlackjackTableBuilder
// unchanged for the 3D felt backdrop — same precedent Baccarat already set, it's
// already generic set-dressing with zero blackjack-specific coupling.
public class CrapsGameManager : MonoBehaviour
{
    Bankroll bankroll;
    IRandomSource rng;
    BlackjackTableBuilder builder;

    BankrollHudUI hud;
    ChipSelectorUI chipSelector;
    CrapsBettingUIController bettingController;
    CrapsHistoryPanelUI historyPanel;
    ResultsStripUI resultsStrip;
    SoundManager soundManager;
    JuiceManager juiceManager;
    FloatingTextUI floatingText;
    FloatingTextUI milestoneToast;
    GameSwitcherPanel switcherPanel;
    RulesPopupUI rulesPanel;
    Transform cameraTransform;
    Camera cam;
    Light keyLight;

    readonly List<CrapsRoundRecord> sessionRecords = new List<CrapsRoundRecord>();
    int nextRoundIndex;

    // Shrunk and moved up from the other 3 games' (800,200)/(300,560) — Craps now
    // has a permanent "One-Roll Bets" side panel occupying that space below y=10
    // (mirroring Hardways on the left), so History is confined to the strip above
    // it instead, between the top corner buttons and that panel's top edge.
    static readonly Vector2 HistoryPos = new Vector2(800, 254);
    static readonly Vector2 HistorySize = new Vector2(300, 470);

    void Start()
    {
        Application.runInBackground = true;
        SoundManager.ApplyPersistedMuteState();

        builder = gameObject.AddComponent<BlackjackTableBuilder>();
        builder.Build();

        SetupCamera();
        SetupLight();

        bankroll = new Bankroll(1000);
        rng = new SystemRandomSource();

        SetupUI();
    }

    void SetupCamera()
    {
        var camGO = new GameObject("Main Camera");
        cam = camGO.AddComponent<Camera>();
        camGO.tag = "MainCamera";
        camGO.transform.position = new Vector3(0, 10.5f, -0.6f);
        camGO.transform.rotation = Quaternion.Euler(82f, 0f, 0f);
        cam.clearFlags = CameraClearFlags.SolidColor;
        cam.backgroundColor = new Color(0.015f, 0.02f, 0.03f);
        camGO.AddComponent<AudioListener>();
        cameraTransform = camGO.transform;
    }

    void SetupLight()
    {
        var keyGO = new GameObject("Key Light");
        var key = keyGO.AddComponent<Light>();
        key.type = LightType.Directional;
        key.intensity = 1.1f;
        key.color = new Color(1f, 0.96f, 0.88f);
        key.shadows = LightShadows.Soft;
        keyGO.transform.rotation = Quaternion.Euler(55f, -35f, 0f);
        keyLight = key;

        var fillGO = new GameObject("Fill Light");
        var fill = fillGO.AddComponent<Light>();
        fill.type = LightType.Directional;
        fill.intensity = 0.35f;
        fill.color = new Color(0.55f, 0.65f, 0.85f);
        fill.shadows = LightShadows.None;
        fillGO.transform.rotation = Quaternion.Euler(35f, 150f, 0f);

        RenderSettings.ambientLight = new Color(0.1f, 0.11f, 0.1f);
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
            }, 13, pixelFont: true);
        UIFactory.MakeMuteButton(canvasGO.transform, new Vector2(700, 515));

        switcherPanel = gameObject.AddComponent<GameSwitcherPanel>();
        switcherPanel.Build(canvasGO.transform, "Craps");
        UIFactory.MakeButton(canvasGO.transform, "MenuNavBtn", new Vector2(-880, 515), new Vector2(180, 32),
            "MENU", UIFactory.PanelDarker, () => switcherPanel.Toggle(), 13, pixelFont: true);

        rulesPanel = gameObject.AddComponent<RulesPopupUI>();
        rulesPanel.Build(canvasGO.transform, "CRAPLESS CRAPS RULES",
            "Every total except 7 can be a point — 2, 3, 11, 12 included. On the\n" +
            "come-out roll, only a 7 resolves anything; every other total just\n" +
            "sets the point. No instant win or loss on 2, 3, 11, or 12.\n\n" +
            "PASS LINE / DON'T PASS — pays 1:1. Wins/loses on the point\n" +
            "repeating or a 7. Locked once the point is set.\n" +
            "COME / DON'T COME — like a second Pass Line, started anytime\n" +
            "after the point is set. Travels to its own point on the next roll.\n" +
            "FIELD — one-roll bet. Wins 3/4/9/10/11 (1:1), 2 (2:1), 12 (3:1).\n" +
            "PLACE — bet a number repeats before a 7. Only working once a\n" +
            "point is set. 4/10 pay 9:5, 5/9 pay 7:5, 6/8 pay 7:6,\n" +
            "2/12 pay 11:2, 3/11 pay 11:4.\n" +
            "HARDWAYS — bet a number rolls as a matching pair (e.g. 2+2)\n" +
            "before a 7 or the easy way. 4/10 pay 7:1, 6/8 pay 9:1.\n" +
            "ODDS — true-odds side bet behind a working point, once set,\n" +
            "capped at 3x that bet. Use ADD ODDS once a point is eligible.\n\n" +
            "SEVEN OUT (a 7 during the point phase) ends the shooter's\n" +
            "turn and clears every Place/Hardway/Come bet at once.");
        UIFactory.MakeButton(canvasGO.transform, "RulesBtn", new Vector2(-880, 470), new Vector2(180, 32),
            "HOW TO PLAY", UIFactory.PanelDarker, () => rulesPanel.Toggle(), 13, pixelFont: true);

        soundManager = gameObject.AddComponent<SoundManager>();
        soundManager.Build();

        juiceManager = gameObject.AddComponent<JuiceManager>();
        juiceManager.Build(canvasGO.transform, cameraTransform, Vector3.up * 1f, keyLight);

        floatingText = gameObject.AddComponent<FloatingTextUI>();
        floatingText.Build(canvasGO.transform, new Vector2(0, 260));

        milestoneToast = gameObject.AddComponent<FloatingTextUI>();
        milestoneToast.Build(canvasGO.transform, new Vector2(0, 250));

        hud = gameObject.AddComponent<BankrollHudUI>();
        hud.Build(canvasGO.transform, bankroll,
            addAmount =>
            {
                bankroll.AddFunds(addAmount);
                hud.Refresh();
                soundManager.PlayAddMoney();
                CrapsSaveSystem.Save(bankroll, nextRoundIndex, sessionRecords);
            },
            resetAmount =>
            {
                if (sessionRecords.Count > 0)
                {
                    int wins = sessionRecords.Count(r => r.NetChange > 0);
                    long biggest = sessionRecords.Max(r => r.NetChange);
                    string bestPart = biggest > 0 ? $", best +{biggest}" : "";
                    milestoneToast.Show($"Session: {sessionRecords.Count} shooters, {wins} winning{bestPart}", UIFactory.Accent, fontSize: 26);
                }

                bankroll.Reset(resetAmount);
                hud.Refresh();
                historyPanel.Clear();
                resultsStrip.Clear();
                bettingController.ResetRound();
                soundManager.PlayReset();
                sessionRecords.Clear();
                nextRoundIndex = 0;
                bettingController.SetRoundIndex(0);
                CrapsSaveSystem.Save(bankroll, nextRoundIndex, sessionRecords);
            });

        chipSelector = gameObject.AddComponent<ChipSelectorUI>();
        chipSelector.Build(canvasGO.transform, soundManager);

        historyPanel = gameObject.AddComponent<CrapsHistoryPanelUI>();
        historyPanel.Build(canvasGO.transform, HistoryPos, HistorySize);

        resultsStrip = gameObject.AddComponent<ResultsStripUI>();
        // Felt panel bottom edge sits at -424 (see CrapsBettingUIController's layout
        // comment) — placed clear of it and within the visible canvas at this
        // reference resolution. A first pass here put this at -590/-500 without
        // checking the actual felt height, clipping it off the bottom of the
        // canvas — caught by checking the whole rendered layout in one screenshot
        // instead of just the element being placed.
        // Felt bottom edge grew to -466 (Pass Line moved below the Place row to
        // wrap it, per the reference table's framing) — pushed down to stay clear,
        // same margin discipline as before.
        resultsStrip.Build(canvasGO.transform, new Vector2(0, -505));

        // Dome placed at a fixed world position well away from the main table
        // geometry (which lives near world origin). The DiceOverlayCamera renders
        // it independently at its own 3/4 view angle, so the dome's world position
        // has no bearing on where it appears on screen.
        var dome = BubbleCrapsDome.Create(transform, new Vector3(20f, 0f, 20f));
        var die1 = dome.Die1;
        var die2 = dome.Die2;

        bettingController = gameObject.AddComponent<CrapsBettingUIController>();
        bettingController.Build(canvasGO.transform, bankroll, chipSelector, rng, soundManager, juiceManager,
            floatingText, milestoneToast, die1, die2, dome.ShadowDie1, dome.ShadowDie2,
            record =>
            {
                // Shooter-turn end only (seven-out): session persistence still
                // tracks per-turn summaries, unrelated to what the History panel
                // now displays (see onRollLogged below).
                hud.Refresh();
                sessionRecords.Add(record);
                nextRoundIndex = record.RoundIndex + 1;
                CrapsSaveSystem.Save(bankroll, nextRoundIndex, sessionRecords);
            },
            () => hud.Refresh(),
            // Per-ROLL results strip (the actual number, colored by that roll's
            // outcome) — every reference app's roll strip works this way, not per
            // shooter turn. Not persisted, so it starts empty after a reload, same
            // as a real machine's session strip.
            (label, color) => resultsStrip.AddResult(label, color),
            // Detailed History panel — also per roll now (was per shooter-turn,
            // which made a mid-turn payout like a Place bet hit look like nothing
            // happened). Not persisted either, same reasoning as the results strip.
            record => historyPanel.AddRecord(record));

        if (CrapsSaveSystem.TryLoad(out long balance, out long startingBalance, out long totalFunded,
                out int loadedNextRoundIndex, out List<CrapsRoundRecord> loadedRecords))
        {
            bankroll.LoadState(balance, startingBalance, totalFunded);
            hud.Refresh();
            bettingController.SetRoundIndex(loadedNextRoundIndex);
            nextRoundIndex = loadedNextRoundIndex;
            sessionRecords.AddRange(loadedRecords);
            // historyPanel is per-roll now, not per-turn (see Build() above) and
            // isn't persisted — same as resultsStrip, it starts empty after a
            // reload rather than replaying old per-turn summaries into a per-roll
            // view where they wouldn't mean the same thing.
        }

        soundManager.PlayMusic();

        BuildDiceOverlay(canvasGO.transform, dome);

        SceneTransition.Reveal();
    }

    // Renders the dome to its own 512×512 RenderTexture via a dedicated camera
    // positioned at a 3/4 angle (~40° elevation, slightly to the side). This lets
    // the player see the dome floor clearly — they can verify the dice actually
    // landed flat and read the face-up value, which the original top-down view made
    // impossible. The RawImage is placed at a fixed canvas position (upper-left,
    // out of the way of the betting felt) rather than spanning the full screen.
    void BuildDiceOverlay(Transform canvasParent, BubbleCrapsDome dome)
    {
        int diceLayer = LayerMask.NameToLayer("CrapsDice");
        SetLayerRecursive(dome.transform, diceLayer);
        Physics.IgnoreLayerCollision(diceLayer, diceLayer, false); // dome walls + dice collide
        Physics.IgnoreLayerCollision(diceLayer, 0, true);          // ignore Default-layer table geo
        cam.cullingMask &= ~(1 << diceLayer);

        // Square RT — 512×512 at the ~420-canvas-unit display size is sharp enough.
        var rtDesc = new RenderTextureDescriptor(512, 512, RenderTextureFormat.ARGB32, 24) { sRGB = true };
        var rt = new RenderTexture(rtDesc) { name = "DiceOverlayRT" };
        rt.Create();

        // Aim at dome mid-height so the full cylinder (floor + ceiling) fits in frame.
        Vector3 domeAim = dome.transform.position + Vector3.up * 1.25f;
        var diceCamGO = new GameObject("DiceOverlayCamera");
        var diceCam = diceCamGO.AddComponent<Camera>();
        diceCam.clearFlags = CameraClearFlags.SolidColor;
        diceCam.backgroundColor = new Color(0f, 0f, 0f, 0f);
        diceCam.cullingMask = 1 << diceLayer;
        diceCam.targetTexture = rt;
        diceCam.depth = cam.depth + 1;
        diceCam.fieldOfView = 54f;
        diceCam.farClipPlane = 60f;
        // ~40° elevation, slight rightward offset — shows dome floor clearly so the
        // player can see where each die landed and verify the face-up result.
        diceCamGO.transform.position = domeAim + new Vector3(0.6f, 2.6f, 3.6f);
        diceCamGO.transform.LookAt(domeAim, Vector3.up);

        // Upper-left canvas position — clear of the felt, HUD, and history panel.
        var overlayGO = new GameObject("DiceOverlayImage", typeof(RectTransform));
        overlayGO.transform.SetParent(canvasParent, false);
        var raw = overlayGO.AddComponent<RawImage>();
        raw.texture = rt;
        raw.raycastTarget = false;
        var overlayRt = overlayGO.GetComponent<RectTransform>();
        overlayRt.anchorMin = overlayRt.anchorMax = new Vector2(0.5f, 0.5f);
        overlayRt.pivot = new Vector2(0.5f, 0.5f);
        overlayRt.anchoredPosition = new Vector2(-530f, 210f);
        overlayRt.sizeDelta = new Vector2(420f, 420f);
        overlayGO.transform.SetAsLastSibling();
    }

    static void SetLayerRecursive(Transform root, int layer)
    {
        root.gameObject.layer = layer;
        foreach (Transform child in root) SetLayerRecursive(child, layer);
    }

    // Casts a ray from the camera through a viewport-space point (0-1 range, origin
    // bottom-left) and intersects it with the horizontal plane at the given world Y —
    // the way to place a 3D object at a specific spot on screen when there's no
    // render-texture/second-camera setup, just this scene's single top-down camera.
    static Vector3 ViewportToGround(Camera camera, Vector2 viewportPos, float groundY)
    {
        Ray ray = camera.ViewportPointToRay(viewportPos);
        float t = (groundY - ray.origin.y) / ray.direction.y;
        return ray.origin + ray.direction * t;
    }

    void OnApplicationQuit()
    {
        if (bankroll != null) CrapsSaveSystem.Save(bankroll, nextRoundIndex, sessionRecords);
    }
}
