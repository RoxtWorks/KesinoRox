using System.Collections.Generic;
using System.Linq;
using UnityEngine;

public class ThreeCardPokerGameManager : MonoBehaviour
{
    Bankroll bankroll;
    Shoe shoe;

    BankrollHudUI hud;
    ChipSelectorUI chipSelector;
    ThreeCardPokerBettingUIController bettingController;
    ThreeCardPokerHistoryPanelUI historyPanel;
    SoundManager soundManager;
    JuiceManager juiceManager;
    FloatingTextUI floatingText;
    FloatingTextUI milestoneToast;
    GameSwitcherPanel switcherPanel;
    RulesPopupUI rulesPanel;
    Transform cameraTransform;
    Light keyLight;

    readonly List<ThreeCardPokerRoundRecord> sessionRecords = new List<ThreeCardPokerRoundRecord>();
    int nextRoundIndex;

    // Tall column on the right, same as Sic Bo and Three Pictures
    static readonly Vector2 HistoryPos  = new Vector2(820, -28);
    static readonly Vector2 HistorySize = new Vector2(270, 1016);

    void Start()
    {
        Application.runInBackground = true;
        SoundManager.ApplyPersistedMuteState();

        SetupCamera();
        SetupLight();

        bankroll = new Bankroll(1000);
        shoe = new Shoe(1, new SystemRandomSource()); // single deck, reshuffled every hand

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

        // No CLOSE APP in a browser — a web page can't close its own tab
        if (Application.platform != RuntimePlatform.WebGLPlayer) UIFactory.MakeButton(canvasGO.transform, "CloseAppBtn", new Vector2(880, 515), new Vector2(140, 32),
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
        switcherPanel.Build(canvasGO.transform, "ThreeCardPoker");
        UIFactory.MakeButton(canvasGO.transform, "MenuNavBtn", new Vector2(-880, 515), new Vector2(180, 32),
            "MENU", UIFactory.PanelDarker, () => switcherPanel.Toggle(), 13, pixelFont: true);

        rulesPanel = gameObject.AddComponent<RulesPopupUI>();
        rulesPanel.Build(canvasGO.transform, "THREE CARD POKER", new RulesContent()
            .Heading("HOW TO PLAY")
            .Text("Bet the ANTE (PAIR PLUS optional), then DEAL. Look at your 3 cards:")
            .Pay("Play", "add a bet equal to the Ante")
            .Pay("Fold", "lose the Ante")
            .Text("Dealer needs QUEEN HIGH to qualify. If not, Ante pays 1 to 1 and Play pushes. If so, the higher hand wins.")
            .Heading("HAND RANKING (high to low)")
            .Pay("Straight flush", "Q-K-A same suit")
            .Pay("Three of a kind", "7-7-7")
            .Pay("Straight", "4-5-6")
            .Pay("Flush", "3 of one suit")
            .Pay("Pair", "9-9-2")
            .Pay("High card", "")
            .Note("A-K-Q is the highest straight, A-2-3 the lowest. One deck, shuffled every hand. RIGHT-CLICK a bet to take it down.")
            .Payouts()
            .Heading("ANTE & PLAY")
            .Pay("Beat the dealer", "1 to 1 each")
            .Pay("Dealer doesn't qualify", "Ante 1 to 1, Play push")
            .Pay("Tie", "push")
            .Heading("ANTE BONUS (when you play)")
            .Pay("Straight flush", "5 to 1")
            .Pay("Three of a kind", "4 to 1")
            .Pay("Straight", "1 to 1")
            .Heading("PAIR PLUS (even if you fold)")
            .Pay("Straight flush", "40 to 1")
            .Pay("Three of a kind", "25 to 1")
            .Pay("Straight", "5 to 1")
            .Pay("Flush", "4 to 1")
            .Pay("Pair", "1 to 1"));
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
                ThreeCardPokerSaveSystem.Save(bankroll, nextRoundIndex, sessionRecords, bettingController.OnTableTotal());
            },
            resetAmount =>
            {
                if (sessionRecords.Count > 0)
                {
                    int wins = sessionRecords.Count(r => r.NetChange > 0);
                    long biggest = sessionRecords.Max(r => r.NetChange);
                    string bestPart = biggest > 0 ? $", best +{UIFactory.FormatMoney(biggest)}" : "";
                    milestoneToast.Show($"Session: {sessionRecords.Count} hands, {wins} won{bestPart}", UIFactory.Accent, fontSize: 26);
                }
                bankroll.Reset(resetAmount);
                hud.Refresh();
                historyPanel.Clear();
                bettingController.ResetRound();
                soundManager.PlayReset();
                sessionRecords.Clear();
                nextRoundIndex = 0;
                bettingController.SetRoundIndex(0);
                ThreeCardPokerSaveSystem.Save(bankroll, nextRoundIndex, sessionRecords, bettingController.OnTableTotal());
            });

        chipSelector = gameObject.AddComponent<ChipSelectorUI>();
        chipSelector.Build(canvasGO.transform, soundManager);

        historyPanel = gameObject.AddComponent<ThreeCardPokerHistoryPanelUI>();
        historyPanel.Build(canvasGO.transform, HistoryPos, HistorySize);

        bettingController = gameObject.AddComponent<ThreeCardPokerBettingUIController>();
        bettingController.Build(canvasGO.transform, bankroll, chipSelector, shoe, soundManager, juiceManager,
            floatingText, milestoneToast,
            record =>
            {
                hud.Refresh();
                sessionRecords.Add(record);
                historyPanel.AddRecord(record);
                nextRoundIndex = record.RoundIndex + 1;
                ThreeCardPokerSaveSystem.Save(bankroll, nextRoundIndex, sessionRecords, bettingController.OnTableTotal());
            },
            () => hud.Refresh());

        if (ThreeCardPokerSaveSystem.TryLoad(out long balance, out long startingBalance, out long totalFunded,
                out int loadedNextRoundIndex, out List<ThreeCardPokerRoundRecord> loadedRecords))
        {
            bankroll.LoadState(balance, startingBalance, totalFunded);
            hud.Refresh();
            bettingController.SetRoundIndex(loadedNextRoundIndex);
            nextRoundIndex = loadedNextRoundIndex;
            sessionRecords.AddRange(loadedRecords);
            foreach (var r in loadedRecords) historyPanel.AddRecord(r);
        }

        soundManager.PlayMusic();
        SceneTransition.Reveal();
    }

    // Leaving the table (quit or switching games): chips before the deal go back to the wallet;
    // a hand in progress counts as a fold (Pair Plus still settled) and is paid before saving.
    void OnApplicationQuit() => LeaveTable();
    void OnDestroy() => LeaveTable();

    void LeaveTable()
    {
        if (bankroll == null || bettingController == null) return;
        bettingController.RefundTableBets();
        ThreeCardPokerSaveSystem.Save(bankroll, nextRoundIndex, sessionRecords, bettingController.OnTableTotal());
    }
}
