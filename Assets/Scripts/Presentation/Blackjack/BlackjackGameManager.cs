using System.Collections.Generic;
using System.Linq;
using UnityEngine;

// Blackjack's equivalent of GameManager — thin orchestrator, same composition
// pattern: builds the scene/UI procedurally at runtime (no prefabs/serialized
// fields) and wires Presentation controllers to Core session objects (Bankroll,
// Shoe). Holds no game rules itself — see Assets/Scripts/Core for that.
public class BlackjackGameManager : MonoBehaviour
{
    Bankroll bankroll;
    Shoe shoe;

    BankrollHudUI hud;
    ChipSelectorUI chipSelector;
    BlackjackBettingUIController bettingController;
    BlackjackHistoryPanelUI historyPanel;
    SoundManager soundManager;
    JuiceManager juiceManager;
    FloatingTextUI floatingText;
    FloatingTextUI milestoneToast;
    GameSwitcherPanel switcherPanel;
    RulesPopupUI rulesPanel;
    Transform cameraTransform;
    Light keyLight;

    readonly List<BlackjackRoundRecord> sessionRecords = new List<BlackjackRoundRecord>();
    int nextRoundIndex;

    // Tall column on the right, same as the other table games
    static readonly Vector2 HistoryPos = new Vector2(820, -28);
    static readonly Vector2 HistorySize = new Vector2(270, 1016);

    void Start()
    {
        // Same reasoning as roulette's GameManager: without this, losing OS focus
        // stalls every Time.deltaTime-driven coroutine (card pop-ins, camera shake),
        // which would freeze mid-animation instead of completing.
        Application.runInBackground = true;
        SoundManager.ApplyPersistedMuteState();

        SetupCamera();
        SetupLight();

        bankroll = new Bankroll(1000);
        shoe = new Shoe(6, new SystemRandomSource());

        SetupUI();
    }

    void SetupCamera()
    {
        var camGO = new GameObject("Main Camera");
        var cam = camGO.AddComponent<Camera>();
        camGO.tag = "MainCamera";
        camGO.transform.position = new Vector3(0, 10.5f, -0.6f);
        camGO.transform.rotation = Quaternion.Euler(82f, 0f, 0f);
        cam.clearFlags = CameraClearFlags.SolidColor;
        cam.backgroundColor = new Color(0.015f, 0.02f, 0.03f);
        AnimatedBackground.Attach(cam); // tiled pixel-art floor drifting behind everything

        // A camera created via AddComponent at runtime does NOT get an AudioListener
        // automatically — same gotcha as the roulette scene.
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
        switcherPanel.Build(canvasGO.transform, "Blackjack");
        UIFactory.MakeButton(canvasGO.transform, "MenuNavBtn", new Vector2(-880, 515), new Vector2(180, 32),
            "MENU", UIFactory.PanelDarker, () => switcherPanel.Toggle(), 13, pixelFont: true);

        rulesPanel = gameObject.AddComponent<RulesPopupUI>();
        rulesPanel.Build(canvasGO.transform, "BLACKJACK", new RulesContent()
            .Heading("HOW TO PLAY")
            .Text("Place your bet, then DEAL. Get closer to 21 than the dealer without going over.")
            .Text("Card values: 2-10 face value, J/Q/K = 10, Ace = 1 or 11.")
            .Text("Dealer hits soft 17, stands on hard 17+, and checks for blackjack with an Ace or ten up.")
            .Heading("YOUR MOVES")
            .Pay("Hit", "take a card")
            .Pay("Stand", "keep your hand")
            .Pay("Double", "double the bet, one more card")
            .Pay("Split", "pairs, up to 4 hands")
            .Pay("Surrender", "give up half, first 2 cards")
            .Note("Split Aces get one card each. Six-deck shoe. RIGHT-CLICK your bet to take it down.")
            .Payouts()
            .Heading("PAYOUTS")
            .Pay("Blackjack", "3 to 2")
            .Pay("Win", "1 to 1")
            .Pay("Push", "bet returned")
            .Pay("Insurance (dealer Ace)", "2 to 1"));
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
                BlackjackSaveSystem.Save(bankroll, nextRoundIndex, sessionRecords, bettingController.OnTableTotal());
            },
            resetAmount =>
            {
                if (sessionRecords.Count > 0)
                {
                    int wins = sessionRecords.Count(r => r.NetChange > 0);
                    long biggest = sessionRecords.Max(r => r.NetChange);
                    string bestPart = biggest > 0 ? $", best +{UIFactory.FormatMoney(biggest)}" : "";
                    milestoneToast.Show($"Session: {sessionRecords.Count} hands, {wins} wins{bestPart}", UIFactory.Accent, fontSize: 26);
                }

                bankroll.Reset(resetAmount);
                hud.Refresh();
                historyPanel.Clear();
                bettingController.ResetRound();
                soundManager.PlayReset();
                sessionRecords.Clear();
                nextRoundIndex = 0;
                bettingController.SetRoundIndex(0);
                BlackjackSaveSystem.Save(bankroll, nextRoundIndex, sessionRecords, bettingController.OnTableTotal());
            });

        chipSelector = gameObject.AddComponent<ChipSelectorUI>();
        chipSelector.Build(canvasGO.transform, soundManager);

        historyPanel = gameObject.AddComponent<BlackjackHistoryPanelUI>();
        historyPanel.Build(canvasGO.transform, HistoryPos, HistorySize);

        bettingController = gameObject.AddComponent<BlackjackBettingUIController>();
        bettingController.Build(canvasGO.transform, bankroll, chipSelector, shoe, soundManager, juiceManager,
            floatingText, milestoneToast,
            record =>
            {
                hud.Refresh();
                historyPanel.AddRecord(record);
                sessionRecords.Add(record);
                nextRoundIndex = record.RoundIndex + 1;
                BlackjackSaveSystem.Save(bankroll, nextRoundIndex, sessionRecords, bettingController.OnTableTotal());
            },
            () => hud.Refresh());

        // Restore last session, if a save exists — bankroll first, then replay every
        // saved round through the same AddRecord call a live round uses.
        if (BlackjackSaveSystem.TryLoad(out long balance, out long startingBalance, out long totalFunded,
                out int loadedNextRoundIndex, out List<BlackjackRoundRecord> loadedRecords))
        {
            bankroll.LoadState(balance, startingBalance, totalFunded);
            hud.Refresh();
            bettingController.SetRoundIndex(loadedNextRoundIndex);
            nextRoundIndex = loadedNextRoundIndex;
            sessionRecords.AddRange(loadedRecords);
            foreach (var record in loadedRecords) historyPanel.AddRecord(record);
        }

        soundManager.PlayMusic();

        SceneTransition.Reveal();
    }

    // Leaving the table (quit or switching games): a bet not yet dealt goes back to the wallet;
    // a hand in progress stands where it is, the dealer plays out, and the result is paid before saving.
    void OnApplicationQuit() => LeaveTable();
    void OnDestroy() => LeaveTable();

    void LeaveTable()
    {
        if (bankroll == null || bettingController == null) return;
        bettingController.RefundTableBets();
        BlackjackSaveSystem.Save(bankroll, nextRoundIndex, sessionRecords, bettingController.OnTableTotal());
    }
}
