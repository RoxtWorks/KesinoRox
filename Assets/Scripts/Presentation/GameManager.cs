using System.Collections.Generic;
using System.Linq;
using UnityEngine;

// Thin orchestrator: builds the scene/UI at runtime and wires Presentation controllers
// to the Core session objects (Bankroll, SpinResultGenerator). Holds no betting/payout
// logic itself — see Assets/Scripts/Core for that.
public class GameManager : MonoBehaviour
{
    Bankroll bankroll;
    SpinResultGenerator generator;
    ConveyorBeltUI belt;
    WheelSpinAnimator wheelAnimator;
    RouletteTableBuilder builder;

    BankrollHudUI hud;
    ChipSelectorUI chipSelector;
    BettingUIController bettingController;
    HistoryPanelUI historyPanel;
    PastSpinsStripUI pastSpinsStrip;
    ProfitLossTrackerUI plTracker;
    HotColdTrackerUI hotColdTracker;
    SoundManager soundManager;
    JuiceManager juiceManager;
    FloatingTextUI floatingText;
    FloatingTextUI milestoneToast;
    Transform cameraTransform;
    Light keyLight;

    // Every spin this session, kept around purely so SaveSystem always has the full
    // list to write out — trackers each keep their own capped/derived view of this
    // same data.
    readonly List<SpinRecord> sessionRecords = new List<SpinRecord>();
    int nextSpinIndex;

    // Right-hand sidebar column: History (top, biggest), Your Bets (middle),
    // P/L (bottom) — stacked and sized to fill the column instead of leaving most
    // of it as dead space (Batch Sim used to sit here; removed, its slot handed
    // to Your Bets).
    static readonly Vector2 SidebarPos = new Vector2(800, 0);
    static readonly Vector2 HistoryPos = new Vector2(800, 290);
    static readonly Vector2 HistorySize = new Vector2(300, 380);
    static readonly Vector2 BetTrayPos = new Vector2(800, -50);
    static readonly Vector2 BetTraySize = new Vector2(300, 260);
    static readonly Vector2 PLPos = new Vector2(800, -360);
    static readonly Vector2 PLSize = new Vector2(300, 320);

    void Start()
    {
        // Without this, losing OS focus mid-spin (alt-tab, a screenshot tool grabbing
        // focus, clicking another window) stalls every Time.deltaTime-driven coroutine
        // — the wheel/belt animations freeze on whatever frame they were mid-lerp on
        // instead of reaching their final snapped rotation, which reads as the wheel
        // landing on the wrong pocket even though the bet already resolved correctly
        // against the real result.
        Application.runInBackground = true;

        builder = gameObject.AddComponent<RouletteTableBuilder>();
        builder.Build();

        SetupCamera();
        SetupLight();

        bankroll = new Bankroll(1000);
        generator = new SpinResultGenerator();

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
        // automatically — that only happens through the Editor's "Create > Camera"
        // menu. Without one, nothing in the scene can output audio no matter how many
        // AudioSources play; this was the entire reason sound was silent.
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

        // Cool fill from the opposite side so the wheel doesn't have a completely
        // black shadow face — cheap two-light setup instead of flat single directional.
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
        // Without this, the canvas renders at a fixed pixel size — on any screen
        // bigger than 1920x1080 (the resolution this layout was designed at) every
        // button and number shrinks relative to the screen instead of scaling up
        // with it, which is what made everything read as "too small" fullscreen.
        scaler.uiScaleMode = UnityEngine.UI.CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920, 1080);
        scaler.matchWidthOrHeight = 0.5f;
        canvasGO.AddComponent<UnityEngine.UI.GraphicRaycaster>();

        // Top-right corner, clear of the sidebar (its topmost panel starts at y=480)
        // and the felt below. #if UNITY_EDITOR branch is needed because
        // Application.Quit() is a no-op in the Editor — without it this button would
        // do nothing when testing in Play mode.
        UIFactory.MakeButton(canvasGO.transform, "CloseAppBtn", new Vector2(880, 515), new Vector2(140, 32),
            "CLOSE APP", new Color(0.4f, 0.16f, 0.16f), () =>
            {
#if UNITY_EDITOR
                UnityEditor.EditorApplication.isPlaying = false;
#else
                Application.Quit();
#endif
            }, 13, pixelFont: true);

        // Mirrors Blackjack.unity's own nav button back here — same top-corner
        // placement, opposite side from CLOSE APP.
        UIFactory.MakeButton(canvasGO.transform, "BlackjackNavBtn", new Vector2(-880, 515), new Vector2(180, 32),
            "BLACKJACK >", UIFactory.AccentDim, () => SceneTransition.Load("Blackjack"), 13, pixelFont: true);
        UIFactory.MakeButton(canvasGO.transform, "MenuNavBtn", new Vector2(-880, 470), new Vector2(180, 32),
            "MENU", UIFactory.PanelDarker, () => SceneTransition.Load("MainMenu"), 13, pixelFont: true);

        soundManager = gameObject.AddComponent<SoundManager>();
        soundManager.Build();

        juiceManager = gameObject.AddComponent<JuiceManager>();
        juiceManager.Build(canvasGO.transform, cameraTransform, builder.wheelPivot.position + Vector3.up * 1f, keyLight);

        floatingText = gameObject.AddComponent<FloatingTextUI>();
        // Starts near the HUD's own text line (inside the panel, not above it) — the
        // old spot (505, just under the 540 canvas ceiling) left almost no room to
        // rise before hitting the top of the screen, so the animation barely read as
        // movement. Starting lower means it rises up through/past the HUD, which
        // looks like money flying up out of the balance instead of a static blip.
        floatingText.Build(canvasGO.transform, new Vector2(0, 460));

        milestoneToast = gameObject.AddComponent<FloatingTextUI>();
        milestoneToast.Build(canvasGO.transform, new Vector2(0, 250));

        hud = gameObject.AddComponent<BankrollHudUI>();
        hud.Build(canvasGO.transform, bankroll,
            addAmount =>
            {
                // Top-up: adds to whatever's already there, doesn't wipe the session.
                // AddFunds (not Deposit) so this counts as new money in, not winnings —
                // otherwise Net would read it as profit instead of your own funding.
                bankroll.AddFunds(addAmount);
                hud.Refresh();
                soundManager.PlayAddMoney();
                SaveSystem.Save(bankroll, nextSpinIndex, sessionRecords);
            },
            resetAmount =>
            {
                // Recap before wiping anything — a session disappearing into RESET
                // with zero acknowledgment feels like it never happened; a one-line
                // "here's what you did" gives it a proper close instead.
                if (sessionRecords.Count > 0)
                {
                    int wins = sessionRecords.Count(r => r.NetChange > 0);
                    long biggest = sessionRecords.Max(r => r.NetChange);
                    string bestPart = biggest > 0 ? $", best +{biggest}" : "";
                    milestoneToast.Show($"Session: {sessionRecords.Count} spins, {wins} wins{bestPart}", UIFactory.Accent, fontSize: 26);
                }

                // Full reset: bankroll back to the stated amount, every tracker wiped —
                // the only button that actually starts a fresh session.
                bankroll.Reset(resetAmount);
                hud.Refresh();
                historyPanel.Clear();
                plTracker.Clear();
                pastSpinsStrip.Clear();
                hotColdTracker.Clear();
                bettingController.ResetBets();
                soundManager.PlayReset();
                sessionRecords.Clear();
                nextSpinIndex = 0;
                bettingController.SetSpinIndex(0);
                SaveSystem.Save(bankroll, nextSpinIndex, sessionRecords);
            });

        chipSelector = gameObject.AddComponent<ChipSelectorUI>();
        chipSelector.Build(canvasGO.transform, soundManager);

        hotColdTracker = gameObject.AddComponent<HotColdTrackerUI>();
        hotColdTracker.Build(canvasGO.transform, new Vector2(-810, 80));

        historyPanel = gameObject.AddComponent<HistoryPanelUI>();
        historyPanel.Build(canvasGO.transform, HistoryPos, HistorySize);

        // Conveyor-belt spin readout sits between the HUD and the betting felt's
        // status line — a straight scrolling strip of pockets in real wheel order,
        // rolling to a stop on the pre-decided result. Replaces animating the 3D
        // wheel/ball, which had camera-angle legibility problems and no real payoff
        // over a simpler, always-crisp 2D reel.
        belt = gameObject.AddComponent<ConveyorBeltUI>();
        belt.Build(canvasGO.transform, new Vector2(0, 380), 460, 64, soundManager, juiceManager);

        // Wheel rotation stays in sync with the belt (same duration) and lands the
        // winning pocket at the same fixed marker angle every time.
        wheelAnimator = gameObject.AddComponent<WheelSpinAnimator>();
        wheelAnimator.wheelPivot = builder.wheelPivot;
        wheelAnimator.markerRenderer = builder.markerRenderer;

        pastSpinsStrip = gameObject.AddComponent<PastSpinsStripUI>();
        pastSpinsStrip.Build(canvasGO.transform, new Vector2(0, -500));

        plTracker = gameObject.AddComponent<ProfitLossTrackerUI>();
        plTracker.Build(canvasGO.transform, PLPos, PLSize);

        bettingController = gameObject.AddComponent<BettingUIController>();
        bettingController.Build(canvasGO.transform, bankroll, chipSelector, generator, belt, wheelAnimator, builder, pastSpinsStrip,
            BetTrayPos, BetTraySize, soundManager, juiceManager, floatingText, milestoneToast, record =>
        {
            hud.Refresh();
            historyPanel.AddRecord(record);
            pastSpinsStrip.AddSpin(record.WinningNumber);
            plTracker.AddRecord(record);
            hotColdTracker.AddSpin(record.WinningNumber);

            sessionRecords.Add(record);
            nextSpinIndex = record.SpinIndex + 1;
            SaveSystem.Save(bankroll, nextSpinIndex, sessionRecords);
        });

        // Restore last session, if a save exists — bankroll first, then replay every
        // saved spin through the exact same AddRecord/AddSpin calls a live spin uses,
        // so History/P-L/Hot-Cold/Recent-Spins come back looking exactly as they were
        // instead of just the balance number being right.
        if (SaveSystem.TryLoad(out long balance, out long startingBalance, out long totalFunded,
                out int loadedNextSpinIndex, out List<SpinRecord> loadedRecords))
        {
            bankroll.LoadState(balance, startingBalance, totalFunded);
            hud.Refresh();
            bettingController.SetSpinIndex(loadedNextSpinIndex);
            nextSpinIndex = loadedNextSpinIndex;
            sessionRecords.AddRange(loadedRecords);
            foreach (var record in loadedRecords)
            {
                historyPanel.AddRecord(record);
                pastSpinsStrip.AddSpin(record.WinningNumber);
                plTracker.AddRecord(record);
                hotColdTracker.AddSpin(record.WinningNumber);
            }
        }

        soundManager.PlayMusic();
    }

    // Safety net alongside the per-action saves (spin/add-funds/reset already save
    // immediately) — catches anything else that might end the process.
    void OnApplicationQuit()
    {
        if (bankroll != null) SaveSystem.Save(bankroll, nextSpinIndex, sessionRecords);
    }
}
