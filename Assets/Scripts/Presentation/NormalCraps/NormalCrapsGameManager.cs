using System.Collections.Generic;
using System.Linq;
using UnityEngine;

public class NormalCrapsGameManager : MonoBehaviour
{
    Bankroll bankroll;
    IRandomSource rng;
    BlackjackTableBuilder builder;

    BankrollHudUI hud;
    ChipSelectorUI chipSelector;
    NormalCrapsBettingUIController bettingController;
    NormalCrapsHistoryPanelUI historyPanel;
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

    readonly List<NormalCrapsRoundRecord> sessionRecords = new List<NormalCrapsRoundRecord>();
    // Last per-roll History rows, persisted so History survives a restart
    readonly List<NormalCrapsRoundRecord> rollRecords = new List<NormalCrapsRoundRecord>();
    int nextRoundIndex;

    static readonly Vector2 HistoryPos  = new Vector2(780, 150);
    static readonly Vector2 HistorySize = new Vector2(300, 680);

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
        switcherPanel.Build(canvasGO.transform, "NormalCraps");
        UIFactory.MakeButton(canvasGO.transform, "MenuNavBtn", new Vector2(-880, 515), new Vector2(180, 32),
            "MENU", UIFactory.PanelDarker, () => switcherPanel.Toggle(), 13, pixelFont: true);

        rulesPanel = gameObject.AddComponent<RulesPopupUI>();
        rulesPanel.Build(canvasGO.transform, "CRAPS", new RulesContent()
            .Heading("HOW TO PLAY")
            .Text("Bet the PASS LINE, then ROLL.")
            .Pay("Come-out 7 or 11", "Pass wins")
            .Pay("Come-out 2, 3 or 12", "Pass loses")
            .Pay("Any other number", "becomes the POINT")
            .Text("Roll the point again before a 7 and Pass wins. A 7 first is SEVEN OUT — the turn ends.")
            .Heading("GOOD TO KNOW")
            .Pay("Don't Pass / Don't Come", "the opposite of Pass / Come")
            .Pay("Odds", "extra bet behind the line at true odds")
            .Pay("Bets ON / OFF", "Place, Lay and Hardways work or rest")
            .Pay("Lucky Roller", "hit every number on its side before a 7")
            .Note("RIGHT-CLICK a bet to take it down. The Pass Line locks once the point is set.")
            .Payouts()
            .Heading("PAYOUTS")
            .Pay("Pass / Come", "1 to 1")
            .Pay("Don't Pass / Don't Come", "1 to 1, 12 pushes")
            .Pay("Odds 4/10 · 5/9 · 6/8", "2:1 · 3:2 · 6:5")
            .Pay("Place 4/10 · 5/9 · 6/8", "9:5 · 7:5 · 7:6")
            .Pay("Lay", "true odds less 5%")
            .Pay("Hard 4 / Hard 10", "7 to 1")
            .Pay("Hard 6 / Hard 8", "9 to 1")
            .Pay("Field 3, 4, 9, 10, 11", "1 to 1")
            .Pay("Field 2 / 12", "2 to 1 / 3 to 1")
            .Pay("Any Craps", "7 to 1")
            .Pay("Seven", "4 to 1")
            .Pay("Eleven", "15 to 1")
            .Pay("Horn 2/12 · 3/11", "30:1 · 15:1")
            .Pay("C & E craps · eleven", "3:1 · 7:1")
            .Pay("Lucky Roller Lows / Highs", "30 to 1")
            .Pay("Lucky Roller All", "155 to 1"));
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
                NormalCrapsSaveSystem.Save(bankroll, nextRoundIndex, sessionRecords, rollRecords, bettingController.OnTableTotal());
            },
            resetAmount =>
            {
                if (sessionRecords.Count > 0)
                {
                    int wins = sessionRecords.Count(r => r.NetChange > 0);
                    long biggest = sessionRecords.Max(r => r.NetChange);
                    string bestPart = biggest > 0 ? $", best +{UIFactory.FormatMoney(biggest)}" : "";
                    milestoneToast.Show($"Session: {sessionRecords.Count} shooters, {wins} winning{bestPart}", UIFactory.Accent, fontSize: 26);
                }
                bankroll.Reset(resetAmount);
                hud.Refresh();
                historyPanel.Clear();
                resultsStrip.Clear();
                bettingController.ResetRound();
                soundManager.PlayReset();
                sessionRecords.Clear();
                rollRecords.Clear();
                bettingController.SetRollLogIndex(0);
                nextRoundIndex = 0;
                bettingController.SetRoundIndex(0);
                NormalCrapsSaveSystem.Save(bankroll, nextRoundIndex, sessionRecords, rollRecords, bettingController.OnTableTotal());
            });

        chipSelector = gameObject.AddComponent<ChipSelectorUI>();
        chipSelector.Build(canvasGO.transform, soundManager);

        historyPanel = gameObject.AddComponent<NormalCrapsHistoryPanelUI>();
        historyPanel.Build(canvasGO.transform, HistoryPos, HistorySize);

        resultsStrip = gameObject.AddComponent<ResultsStripUI>();
        resultsStrip.Build(canvasGO.transform, new Vector2(0, -505));

        var dome = BubbleCrapsDome.Create(transform, new Vector3(20f, 0f, 20f));
        var die1 = dome.Die1;
        var die2 = dome.Die2;

        bettingController = gameObject.AddComponent<NormalCrapsBettingUIController>();
        bettingController.Build(canvasGO.transform, bankroll, chipSelector, rng, soundManager, juiceManager,
            floatingText, milestoneToast, die1, die2, dome.ShadowDie1, dome.ShadowDie2,
            record =>
            {
                hud.Refresh();
                sessionRecords.Add(record);
                nextRoundIndex = record.RoundIndex + 1;
                NormalCrapsSaveSystem.Save(bankroll, nextRoundIndex, sessionRecords, rollRecords, bettingController.OnTableTotal());
            },
            () => hud.Refresh(),
            (label, color) => resultsStrip.AddResult(label, color),
            record =>
            {
                historyPanel.AddRecord(record);
                rollRecords.Add(record);
                if (rollRecords.Count > NormalCrapsSaveSystem.MaxSavedRolls) rollRecords.RemoveAt(0);
                NormalCrapsSaveSystem.Save(bankroll, nextRoundIndex, sessionRecords, rollRecords, bettingController.OnTableTotal());
            });

        if (NormalCrapsSaveSystem.TryLoad(out long balance, out long startingBalance, out long totalFunded,
                out int loadedNextRoundIndex, out List<NormalCrapsRoundRecord> loadedRecords, out List<NormalCrapsRoundRecord> loadedRolls))
        {
            bankroll.LoadState(balance, startingBalance, totalFunded);
            hud.Refresh();
            bettingController.SetRoundIndex(loadedNextRoundIndex);
            nextRoundIndex = loadedNextRoundIndex;
            sessionRecords.AddRange(loadedRecords);
            // Restore the last saved rolls into History and the results strip, oldest first
            rollRecords.AddRange(loadedRolls);
            foreach (var r in loadedRolls)
            {
                historyPanel.AddRecord(r);
                resultsStrip.AddResult(r.RollTotal.ToString(), r.NetChange > 0 ? UIFactory.Positive : r.NetChange < 0 ? UIFactory.Negative : UIFactory.Accent);
            }
            if (loadedRolls.Count > 0) bettingController.SetRollLogIndex(loadedRolls[loadedRolls.Count - 1].RoundIndex + 1);
        }

        soundManager.PlayMusic();

        BuildDiceOverlay(canvasGO.transform, dome);

        SceneTransition.Reveal();
    }

    void BuildDiceOverlay(Transform canvasParent, BubbleCrapsDome dome)
    {
        int diceLayer = LayerMask.NameToLayer("CrapsDice");
        SetLayerRecursive(dome.transform, diceLayer);
        Physics.IgnoreLayerCollision(diceLayer, diceLayer, false);
        Physics.IgnoreLayerCollision(diceLayer, 0, true);
        cam.cullingMask &= ~(1 << diceLayer);

        var rtDesc = new RenderTextureDescriptor(512, 512, RenderTextureFormat.ARGB32, 24) { sRGB = true };
        var rt = new RenderTexture(rtDesc) { name = "DiceOverlayRT" };
        rt.Create();

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
        diceCamGO.transform.position = domeAim + new Vector3(0.6f, 2.6f, 3.6f);
        diceCamGO.transform.LookAt(domeAim, Vector3.up);

        var overlayGO = new GameObject("DiceOverlayImage", typeof(RectTransform));
        overlayGO.transform.SetParent(canvasParent, false);
        var raw = overlayGO.AddComponent<UnityEngine.UI.RawImage>();
        raw.texture = rt;
        raw.raycastTarget = false;
        var overlayRt = overlayGO.GetComponent<RectTransform>();
        overlayRt.anchorMin = overlayRt.anchorMax = new Vector2(0.5f, 0.5f);
        overlayRt.pivot = new Vector2(0.5f, 0.5f);
        overlayRt.anchoredPosition = new Vector2(-700f, 0f);
        overlayRt.sizeDelta = new Vector2(340f, 340f);
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
        NormalCrapsSaveSystem.Save(bankroll, nextRoundIndex, sessionRecords, rollRecords, bettingController.OnTableTotal());
    }
}

