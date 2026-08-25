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
    BlackjackTableBuilder builder;

    BankrollHudUI hud;
    ChipSelectorUI chipSelector;
    BlackjackBettingUIController bettingController;
    BlackjackHistoryPanelUI historyPanel;
    ResultsStripUI resultsStrip;
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

    static readonly Vector2 HistoryPos = new Vector2(800, 200);
    static readonly Vector2 HistorySize = new Vector2(300, 560);

    void Start()
    {
        // Same reasoning as roulette's GameManager: without this, losing OS focus
        // stalls every Time.deltaTime-driven coroutine (card pop-ins, camera shake),
        // which would freeze mid-animation instead of completing.
        Application.runInBackground = true;
        SoundManager.ApplyPersistedMuteState();

        builder = gameObject.AddComponent<BlackjackTableBuilder>();
        builder.Build();

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

        // Top-right: CLOSE APP (same as roulette). Top-left: nav button back to the
        // roulette scene — mirrored placement of roulette's own nav button to here.
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
        rulesPanel.Build(canvasGO.transform, "BLACKJACK RULES",
            "Beat the dealer's hand without going over 21.\n\n" +
            "Blackjack (natural 21 on your first two cards) pays 3:2.\n" +
            "A normal win pays 1:1. A push returns your bet.\n\n" +
            "Dealer hits on soft 17 and stands on hard 17+.\n\n" +
            "SPLIT — any pair, up to 4 hands total. Split aces get\n" +
            "exactly one more card each and can't be re-split or hit.\n" +
            "DOUBLE — double your bet for exactly one more card.\n" +
            "Double-after-split is allowed.\n" +
            "SURRENDER — forfeit half your bet before hitting.\n" +
            "INSURANCE — offered only when the dealer shows an Ace;\n" +
            "pays 2:1 if the dealer has blackjack.\n\n" +
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
                BlackjackSaveSystem.Save(bankroll, nextRoundIndex, sessionRecords);
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
                BlackjackSaveSystem.Save(bankroll, nextRoundIndex, sessionRecords);
            });

        chipSelector = gameObject.AddComponent<ChipSelectorUI>();
        chipSelector.Build(canvasGO.transform, soundManager);

        historyPanel = gameObject.AddComponent<BlackjackHistoryPanelUI>();
        historyPanel.Build(canvasGO.transform, HistoryPos, HistorySize);

        resultsStrip = gameObject.AddComponent<ResultsStripUI>();
        resultsStrip.Build(canvasGO.transform, new Vector2(0, -500));

        bettingController = gameObject.AddComponent<BlackjackBettingUIController>();
        bettingController.Build(canvasGO.transform, bankroll, chipSelector, shoe, soundManager, juiceManager,
            floatingText, milestoneToast, record =>
        {
            hud.Refresh();
            historyPanel.AddRecord(record);
            resultsStrip.AddResult(record.NetChange > 0 ? "W" : record.NetChange < 0 ? "L" : "P",
                record.NetChange > 0 ? UIFactory.Positive : record.NetChange < 0 ? UIFactory.Negative : UIFactory.Accent);

            sessionRecords.Add(record);
            nextRoundIndex = record.RoundIndex + 1;
            BlackjackSaveSystem.Save(bankroll, nextRoundIndex, sessionRecords);
        });

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
            foreach (var record in loadedRecords)
            {
                historyPanel.AddRecord(record);
                resultsStrip.AddResult(record.NetChange > 0 ? "W" : record.NetChange < 0 ? "L" : "P",
                    record.NetChange > 0 ? UIFactory.Positive : record.NetChange < 0 ? UIFactory.Negative : UIFactory.Accent);
            }
        }

        soundManager.PlayMusic();

        SceneTransition.Reveal();
    }

    void OnApplicationQuit()
    {
        if (bankroll != null) BlackjackSaveSystem.Save(bankroll, nextRoundIndex, sessionRecords);
    }
}
