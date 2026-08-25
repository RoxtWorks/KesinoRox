using System.Collections.Generic;
using System.Linq;
using UnityEngine;

// Baccarat's equivalent of GameManager/BlackjackGameManager — same thin-orchestrator
// composition pattern: builds the scene/UI procedurally at runtime and wires
// Presentation controllers to Core session objects. Reuses BlackjackTableBuilder
// unchanged for the 3D felt backdrop — it's already generic set-dressing with no
// blackjack-specific coupling, so cloning it here would just be duplication.
public class BaccaratGameManager : MonoBehaviour
{
    Bankroll bankroll;
    Shoe shoe;
    BlackjackTableBuilder builder;

    BankrollHudUI hud;
    ChipSelectorUI chipSelector;
    BaccaratBettingUIController bettingController;
    BaccaratHistoryPanelUI historyPanel;
    ResultsStripUI resultsStrip;
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

    static readonly Vector2 HistoryPos = new Vector2(800, 200);
    static readonly Vector2 HistorySize = new Vector2(300, 560);

    void Start()
    {
        Application.runInBackground = true;
        SoundManager.ApplyPersistedMuteState();

        builder = gameObject.AddComponent<BlackjackTableBuilder>();
        builder.Build();

        SetupCamera();
        SetupLight();

        bankroll = new Bankroll(1000);
        shoe = new Shoe(8, new SystemRandomSource()); // baccarat traditionally deals from a larger 8-deck shoe

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
        rulesPanel.Build(canvasGO.transform, "BACCARAT RULES",
            "Bet on which hand scores closer to 9: PLAYER, BANKER,\n" +
            "or a TIE. Cards 2-9 are face value, 10/J/Q/K are worth 0,\n" +
            "and Aces are worth 1. Only the last digit of the total\n" +
            "counts (e.g. 7+8=15 counts as 5).\n\n" +
            "PLAYER pays 1:1. BANKER pays 0.95:1 (a 5% commission\n" +
            "applies since Banker has the statistical edge). TIE pays\n" +
            "8:1 — and a Tie also pushes any Player/Banker bet back.\n\n" +
            "A third card is drawn automatically for either hand\n" +
            "under fixed rules — there are no player decisions once\n" +
            "you hit DEAL.\n\n" +
            "Shoe reshuffles automatically once it runs low.");
        UIFactory.MakeButton(canvasGO.transform, "RulesBtn", new Vector2(-880, 470), new Vector2(180, 32),
            "HOW TO PLAY", UIFactory.PanelDarker, () => rulesPanel.Toggle(), 13, pixelFont: true);

        soundManager = gameObject.AddComponent<SoundManager>();
        soundManager.Build();

        juiceManager = gameObject.AddComponent<JuiceManager>();
        juiceManager.Build(canvasGO.transform, cameraTransform, Vector3.up * 1f, keyLight);

        floatingText = gameObject.AddComponent<FloatingTextUI>();
        // Was pinned at 460, almost against the HUD panel above — floating win/loss
        // text landed way up at the top edge instead of near the action. Centered
        // over the table, just above its header, instead.
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
                BaccaratSaveSystem.Save(bankroll, nextRoundIndex, sessionRecords);
            },
            resetAmount =>
            {
                if (sessionRecords.Count > 0)
                {
                    int wins = sessionRecords.Count(r => r.NetChange > 0);
                    long biggest = sessionRecords.Max(r => r.NetChange);
                    string bestPart = biggest > 0 ? $", best +{biggest}" : "";
                    milestoneToast.Show($"Session: {sessionRecords.Count} hands, {wins} wins{bestPart}", UIFactory.Accent, fontSize: 26);
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
                BaccaratSaveSystem.Save(bankroll, nextRoundIndex, sessionRecords);
            });

        chipSelector = gameObject.AddComponent<ChipSelectorUI>();
        chipSelector.Build(canvasGO.transform, soundManager);

        historyPanel = gameObject.AddComponent<BaccaratHistoryPanelUI>();
        historyPanel.Build(canvasGO.transform, HistoryPos, HistorySize);

        resultsStrip = gameObject.AddComponent<ResultsStripUI>();
        resultsStrip.Build(canvasGO.transform, new Vector2(0, -500));

        bettingController = gameObject.AddComponent<BaccaratBettingUIController>();
        bettingController.Build(canvasGO.transform, bankroll, chipSelector, shoe, soundManager, juiceManager,
            floatingText, milestoneToast, record =>
        {
            hud.Refresh();
            historyPanel.AddRecord(record);
            resultsStrip.AddResult(OutcomeLabel(record.Outcome), OutcomeColor(record.Outcome));

            sessionRecords.Add(record);
            nextRoundIndex = record.RoundIndex + 1;
            BaccaratSaveSystem.Save(bankroll, nextRoundIndex, sessionRecords);
        });

        if (BaccaratSaveSystem.TryLoad(out long balance, out long startingBalance, out long totalFunded,
                out int loadedNextRoundIndex, out List<BaccaratRoundRecord> loadedRecords))
        {
            bankroll.LoadState(balance, startingBalance, totalFunded);
            hud.Refresh();
            bettingController.SetRoundIndex(loadedNextRoundIndex);
            nextRoundIndex = loadedNextRoundIndex;
            sessionRecords.AddRange(loadedRecords);
            foreach (var record in loadedRecords)
            {
                historyPanel.AddRecord(record);
                resultsStrip.AddResult(OutcomeLabel(record.Outcome), OutcomeColor(record.Outcome));
            }
        }

        soundManager.PlayMusic();

        SceneTransition.Reveal();
    }

    static string OutcomeLabel(BaccaratOutcome outcome) => outcome switch
    {
        BaccaratOutcome.PlayerWin => "P",
        BaccaratOutcome.BankerWin => "B",
        _ => "T",
    };

    static Color OutcomeColor(BaccaratOutcome outcome) => outcome switch
    {
        BaccaratOutcome.PlayerWin => UIFactory.Positive,
        BaccaratOutcome.BankerWin => UIFactory.Negative,
        _ => new Color(0.55f, 0.45f, 0.15f),
    };

    void OnApplicationQuit()
    {
        if (bankroll != null) BaccaratSaveSystem.Save(bankroll, nextRoundIndex, sessionRecords);
    }
}
