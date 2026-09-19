using System.Collections.Generic;
using System.Linq;
using UnityEngine;

public class SicBoGameManager : MonoBehaviour
{
    Bankroll bankroll;
    IRandomSource rng;

    BankrollHudUI hud;
    ChipSelectorUI chipSelector;
    SicBoBettingUIController bettingController;
    SicBoHistoryPanelUI historyPanel;
    SoundManager soundManager;
    JuiceManager juiceManager;
    FloatingTextUI floatingText;
    FloatingTextUI milestoneToast;
    GameSwitcherPanel switcherPanel;
    RulesPopupUI rulesPanel;
    Transform cameraTransform;
    Camera cam;
    Light keyLight;

    readonly List<SicBoRoundRecord> sessionRecords = new List<SicBoRoundRecord>();
    int nextRoundIndex;

    static readonly Vector2 HistoryPos  = new Vector2(820, -28);
    static readonly Vector2 HistorySize = new Vector2(270, 1016);

    void Start()
    {
        Application.runInBackground = true;
        SoundManager.ApplyPersistedMuteState();

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
        switcherPanel.Build(canvasGO.transform, "SicBo");
        UIFactory.MakeButton(canvasGO.transform, "MenuNavBtn", new Vector2(-880, 515), new Vector2(180, 32),
            "MENU", UIFactory.PanelDarker, () => switcherPanel.Toggle(), 13, pixelFont: true);

        rulesPanel = gameObject.AddComponent<RulesPopupUI>();
        rulesPanel.Build(canvasGO.transform, "SIC BO", new RulesContent()
            .Heading("HOW TO PLAY")
            .Text("Place chips on any bets, then ROLL. Three dice are shaken; every bet is settled on that one roll.")
            .Text("Big, Small, Odd and Even all lose on any triple.")
            .Heading("SPECIAL BETS")
            .Pay("Single", "your number on 1, 2 or 3 dice")
            .Pay("Double", "your number on 2+ dice")
            .Pay("Combo", "both numbers show")
            .Pay("Orange grid", "pair + single, e.g. 4-4-1")
            .Pay("Blue grid", "3 different, e.g. 1-2-6")
            .Pay("Four numbers", "3 different from the 4")
            .Note("Macau pay table. RIGHT-CLICK a bet to take it down.")
            .Payouts()
            .Heading("PAYOUTS")
            .Pay("Big 11-17 / Small 4-10", "1 to 1")
            .Pay("Odd / Even", "1 to 1")
            .Pay("Total 4 or 17", "50 to 1")
            .Pay("Total 5 or 16", "30 to 1")
            .Pay("Total 6 or 15", "18 to 1")
            .Pay("Total 7 or 14", "12 to 1")
            .Pay("Total 8 or 13", "8 to 1")
            .Pay("Total 9 to 12", "6 to 1")
            .Pay("Single (1 / 2 / 3 dice)", "1 / 2 / 12 to 1")
            .Pay("Double", "8 to 1")
            .Pay("Combo", "5 to 1")
            .Pay("Specific triple", "150 to 1")
            .Pay("Any triple", "24 to 1")
            .Pay("Orange grid", "50 to 1")
            .Pay("Blue grid", "30 to 1")
            .Pay("Four numbers", "7 to 1"));
        UIFactory.MakeButton(canvasGO.transform, "RulesBtn", new Vector2(-880, 470), new Vector2(180, 32),
            "HOW TO PLAY", UIFactory.PanelDarker, () => rulesPanel.Toggle(), 13, pixelFont: true);

        soundManager = gameObject.AddComponent<SoundManager>();
        soundManager.Build();

        juiceManager = gameObject.AddComponent<JuiceManager>();
        juiceManager.Build(canvasGO.transform, cameraTransform, Vector3.up * 1f, keyLight);

        floatingText = gameObject.AddComponent<FloatingTextUI>();
        floatingText.Build(canvasGO.transform, new Vector2(0, 260));

        milestoneToast = gameObject.AddComponent<FloatingTextUI>();
        milestoneToast.Build(canvasGO.transform, new Vector2(0, 390));

        hud = gameObject.AddComponent<BankrollHudUI>();
        hud.Build(canvasGO.transform, bankroll,
            addAmount =>
            {
                bankroll.AddFunds(addAmount);
                hud.Refresh();
                soundManager.PlayAddMoney();
                SicBoSaveSystem.Save(bankroll, nextRoundIndex, sessionRecords, bettingController.OnTableTotal());
            },
            resetAmount =>
            {
                if (sessionRecords.Count > 0)
                {
                    int wins = sessionRecords.Count(r => r.NetChange > 0);
                    long biggest = sessionRecords.Max(r => r.NetChange);
                    string bestPart = biggest > 0 ? $", best +{UIFactory.FormatMoney(biggest)}" : "";
                    milestoneToast.Show($"Session: {sessionRecords.Count} rolls, {wins} won{bestPart}", UIFactory.Accent, fontSize: 26);
                }
                bankroll.Reset(resetAmount);
                hud.Refresh();
                historyPanel.Clear();
                bettingController.ResetRound();
                soundManager.PlayReset();
                sessionRecords.Clear();
                nextRoundIndex = 0;
                bettingController.SetRoundIndex(0);
                SicBoSaveSystem.Save(bankroll, nextRoundIndex, sessionRecords, bettingController.OnTableTotal());
            });

        chipSelector = gameObject.AddComponent<ChipSelectorUI>();
        chipSelector.Build(canvasGO.transform, soundManager);

        historyPanel = gameObject.AddComponent<SicBoHistoryPanelUI>();
        historyPanel.Build(canvasGO.transform, HistoryPos, HistorySize);

        var dome = SicBoDome.Create(transform, new Vector3(20f, 0f, 20f));

        bettingController = gameObject.AddComponent<SicBoBettingUIController>();
        bettingController.Build(canvasGO.transform, bankroll, chipSelector, rng, soundManager, juiceManager,
            floatingText, milestoneToast, dome,
            record =>
            {
                hud.Refresh();
                sessionRecords.Add(record);
                historyPanel.AddRecord(record);
                nextRoundIndex = record.RoundIndex + 1;
                SicBoSaveSystem.Save(bankroll, nextRoundIndex, sessionRecords, bettingController.OnTableTotal());
            },
            () => hud.Refresh(),
            null); // the History column shows every roll; no separate results strip

        if (SicBoSaveSystem.TryLoad(out long balance, out long startingBalance, out long totalFunded,
                out int loadedNextRoundIndex, out List<SicBoRoundRecord> loadedRecords))
        {
            bankroll.LoadState(balance, startingBalance, totalFunded);
            hud.Refresh();
            bettingController.SetRoundIndex(loadedNextRoundIndex);
            nextRoundIndex = loadedNextRoundIndex;
            sessionRecords.AddRange(loadedRecords);
            foreach (var r in loadedRecords) historyPanel.AddRecord(r);
        }

        soundManager.PlayMusic();
        BuildDiceOverlay(canvasGO.transform, dome);
        SceneTransition.Reveal();
    }

    void BuildDiceOverlay(Transform canvasParent, SicBoDome dome)
    {
        int diceLayer = LayerMask.NameToLayer("CrapsDice");
        SetLayerRecursive(dome.transform, diceLayer);
        Physics.IgnoreLayerCollision(diceLayer, diceLayer, false);
        Physics.IgnoreLayerCollision(diceLayer, 0, true);
        cam.cullingMask &= ~(1 << diceLayer);

        var rtDesc = new RenderTextureDescriptor(512, 512, RenderTextureFormat.ARGB32, 24) { sRGB = true };
        var rt = new RenderTexture(rtDesc) { name = "SicBoDiceRT" };
        rt.Create();

        Vector3 domeAim = dome.transform.position + Vector3.up * 0.9f;
        var diceCamGO = new GameObject("DiceOverlayCamera");
        var diceCam = diceCamGO.AddComponent<Camera>();
        diceCam.clearFlags = CameraClearFlags.SolidColor;
        diceCam.backgroundColor = new Color(0f, 0f, 0f, 0f);
        diceCam.cullingMask = 1 << diceLayer;
        diceCam.targetTexture = rt;
        diceCam.depth = cam.depth + 1;
        diceCam.fieldOfView = 50f;
        diceCam.farClipPlane = 60f;
        diceCamGO.transform.position = domeAim + new Vector3(0.4f, 3.6f, 3.6f);
        diceCamGO.transform.LookAt(domeAim, Vector3.up);

        var overlayGO = new GameObject("DiceOverlayImage", typeof(RectTransform));
        overlayGO.transform.SetParent(canvasParent, false);
        var raw = overlayGO.AddComponent<UnityEngine.UI.RawImage>();
        raw.texture = rt;
        raw.raycastTarget = false;
        var overlayRt = overlayGO.GetComponent<RectTransform>();
        overlayRt.anchorMin = overlayRt.anchorMax = new Vector2(0.5f, 0.5f);
        overlayRt.pivot = new Vector2(0.5f, 0.5f);
        overlayRt.anchoredPosition = new Vector2(-495f, 322f);
        overlayRt.sizeDelta = new Vector2(350f, 350f);
        overlayGO.transform.SetAsLastSibling();
    }

    static void SetLayerRecursive(Transform root, int layer)
    {
        root.gameObject.layer = layer;
        foreach (Transform child in root) SetLayerRecursive(child, layer);
    }

    // Leaving the table (quit or switching games) picks every chip up and returns it to the wallet before saving.
    void OnApplicationQuit() => LeaveTable();
    void OnDestroy() => LeaveTable();

    void LeaveTable()
    {
        if (bankroll == null || bettingController == null) return;
        bettingController.RefundTableBets();
        SicBoSaveSystem.Save(bankroll, nextRoundIndex, sessionRecords, bettingController.OnTableTotal());
    }
}

