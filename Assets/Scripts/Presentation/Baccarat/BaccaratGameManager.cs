using System.Collections.Generic;
using System.Linq;
using UnityEngine;

// Baccarat's equivalent of GameManager/BlackjackGameManager — same thin-orchestrator
// composition pattern: builds the scene/UI procedurally at runtime and wires
// Presentation controllers to Core session objects.
public class BaccaratGameManager : MonoBehaviour
{
    Bankroll bankroll;
    Shoe shoe;

    BankrollHudUI hud;
    ChipSelectorUI chipSelector;
    BaccaratBettingUIController bettingController;
    BaccaratHistoryPanelUI historyPanel;
    SoundManager soundManager;
    JuiceManager juiceManager;
    FloatingTextUI floatingText;
    FloatingTextUI milestoneToast;
    GameSwitcherPanel switcherPanel;
    RulesPopupUI rulesPanel;
    Transform cameraTransform;
    Light keyLight;

    readonly List<BaccaratRoundRecord> sessionRecords = new List<BaccaratRoundRecord>();
    int nextRoundIndex;

    // Tall column on the right, same as the other table games
    static readonly Vector2 HistoryPos = new Vector2(820, -28);
    static readonly Vector2 HistorySize = new Vector2(270, 1016);

    void Start()
    {
        Application.runInBackground = true;
        SoundManager.ApplyPersistedMuteState();

        SetupCamera();
        SetupLight();

        bankroll = new Bankroll(1000);
        shoe = new Shoe(8, new SystemRandomSource()); // Vegas baccarat deals from an 8-deck shoe

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
        switcherPanel.Build(canvasGO.transform, "Baccarat");
        UIFactory.MakeButton(canvasGO.transform, "MenuNavBtn", new Vector2(-880, 515), new Vector2(180, 32),
            "MENU", UIFactory.PanelDarker, () => switcherPanel.Toggle(), 13, pixelFont: true);

        rulesPanel = gameObject.AddComponent<RulesPopupUI>();
        rulesPanel.Build(canvasGO.transform, "BACCARAT", new RulesContent()
            .Heading("HOW TO PLAY")
            .Text("Bet on PLAYER, BANKER or TIE, then DEAL. The hand closer to 9 wins. No decisions after DEAL.")
            .Text("Card values: Ace = 1, 2-9 face value, 10/J/Q/K = 0.")
            .Text("Only the last digit counts: 7 + 8 = 15 counts as 5.")
            .Text("A natural 8 or 9 on the first two cards ends the hand.")
            .Heading("THIRD CARD")
            .Pay("Player 0-5", "draws")
            .Pay("Player 6-7", "stands")
            .Pay("Banker 0-2", "draws")
            .Pay("Banker 3", "draws unless Player's 3rd is 8")
            .Pay("Banker 4", "draws if Player's 3rd is 2-7")
            .Pay("Banker 5", "draws if Player's 3rd is 4-7")
            .Pay("Banker 6", "draws if Player's 3rd is 6-7")
            .Pay("Banker 7", "stands")
            .Note("If Player stands, Banker draws on 0-5. Eight-deck shoe. RIGHT-CLICK a bet to take it down.")
            .Payouts()
            .Heading("PAYOUTS")
            .Pay("Player", "1 to 1")
            .Pay("Banker", "1 to 1 less 5%")
            .Pay("Tie", "8 to 1")
            .Note("On a tie, Player and Banker bets push."));
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
                BaccaratSaveSystem.Save(bankroll, nextRoundIndex, sessionRecords, bettingController.OnTableTotal());
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
                BaccaratSaveSystem.Save(bankroll, nextRoundIndex, sessionRecords, bettingController.OnTableTotal());
            });

        chipSelector = gameObject.AddComponent<ChipSelectorUI>();
        chipSelector.Build(canvasGO.transform, soundManager);

        historyPanel = gameObject.AddComponent<BaccaratHistoryPanelUI>();
        historyPanel.Build(canvasGO.transform, HistoryPos, HistorySize);

        bettingController = gameObject.AddComponent<BaccaratBettingUIController>();
        bettingController.Build(canvasGO.transform, bankroll, chipSelector, shoe, soundManager, juiceManager,
            floatingText, milestoneToast,
            record =>
            {
                hud.Refresh();
                historyPanel.AddRecord(record);
                sessionRecords.Add(record);
                if (sessionRecords.Count > BaccaratSaveSystem.MaxSavedRecords) sessionRecords.RemoveAt(0);
                nextRoundIndex = record.RoundIndex + 1;
                BaccaratSaveSystem.Save(bankroll, nextRoundIndex, sessionRecords, bettingController.OnTableTotal());
            },
            () => hud.Refresh());

        if (BaccaratSaveSystem.TryLoad(out long balance, out long startingBalance, out long totalFunded,
                out int loadedNextRoundIndex, out List<BaccaratRoundRecord> loadedRecords))
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

    // Leaving the table (quit or switching games): chips not yet dealt go back to the wallet;
    // a hand mid-reveal is already decided, so it's paid out before saving.
    void OnApplicationQuit() => LeaveTable();
    void OnDestroy() => LeaveTable();

    void LeaveTable()
    {
        if (bankroll == null || bettingController == null) return;
        bettingController.RefundTableBets();
        BaccaratSaveSystem.Save(bankroll, nextRoundIndex, sessionRecords, bettingController.OnTableTotal());
    }
}
