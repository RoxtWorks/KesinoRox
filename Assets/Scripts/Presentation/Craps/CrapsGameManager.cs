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

        // Two real 3D dice, built the same way the roulette wheel is (RouletteTableBuilder
        // + WheelSpinAnimator: purely visual, no physics) — launched from the top-left
        // corner (below the chip panel) and tossed across to a landing zone near the
        // right edge each roll, like a real toss thrown down the table to the far
        // rail. Visible over the felt/UI everywhere via the dice overlay camera/
        // RenderTexture built in BuildDiceOverlay below, instead of only inside a
        // single gap. Both points computed via a camera ray through a viewport point
        // intersected with a ground plane, rather than guessed screen pixels, since
        // the dice are real 3D objects the 2D canvas has no positioning authority over.
        const float groundY = -0.4f;
        Vector3 launchCenter = ViewportToGround(cam, new Vector2(0.085f, 0.62f), groundY);
        Vector3 landingCenter = ViewportToGround(cam, new Vector2(0.88f, 0.55f), groundY);

        BuildDicePit(groundY, launchCenter, landingCenter);

        // Offset front/back (Z — the screen-vertical axis under this top-down
        // camera) rather than left/right (X — the same axis as the throw itself).
        // Both dice still collide with each other for real (no IgnoreCollision —
        // a die clipping the other after it's already bounced off a wall is a
        // real, wanted moment), but two side-by-side lanes both throwing rightward
        // were prone to crossing paths early via each die's own random launch-
        // angle jitter, and an early collision dumps most of one die's momentum
        // into the other, leaving it stalled near the launch corner. Separate
        // front/back lanes keep the initial throws roughly parallel instead.
        var die1 = Dice3D.Create(transform, launchCenter + Vector3.back * 0.55f, landingCenter + Vector3.back * 0.55f, size: 0.95f);
        var die2 = Dice3D.Create(transform, launchCenter + Vector3.forward * 0.55f, landingCenter + Vector3.forward * 0.55f, size: 0.95f);

        bettingController = gameObject.AddComponent<CrapsBettingUIController>();
        bettingController.Build(canvasGO.transform, bankroll, chipSelector, rng, soundManager, juiceManager,
            floatingText, milestoneToast, die1, die2,
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

        BuildDiceOverlay(canvasGO.transform, die1, die2);

        SceneTransition.Reveal();
    }

    // The main camera renders the dice normally, but the Overlay canvas (everything
    // else — felt, HUD, panels) always draws on top of it with no exceptions, which
    // is why a toss could only ever be seen in the one small gap of screen the dice
    // happened to occupy. Switching the whole canvas to Screen Space - Camera would
    // fix that, but it also changes how CanvasScaler sizes every single element
    // relative to this scene's narrow top-down FOV — tried it, and it blew the whole
    // UI's scale up inconsistently. Instead: render just the dice (on their own
    // layer) to a separate camera pointed at a transparent RenderTexture, and show
    // that texture through one full-screen RawImage placed as the very last object
    // in the canvas. Wherever there's no die pixel the texture is transparent and
    // the normal UI shows through untouched; wherever a die pixel exists it draws on
    // top of everything else, letting a toss travel visibly over the whole table.
    void BuildDiceOverlay(Transform canvasParent, Dice3D die1, Dice3D die2)
    {
        int diceLayer = LayerMask.NameToLayer("CrapsDice");
        SetLayerRecursive(die1.transform, diceLayer);
        SetLayerRecursive(die2.transform, diceLayer);
        cam.cullingMask &= ~(1 << diceLayer); // main camera no longer needs to draw them itself

        // Explicit sRGB descriptor — in a Linear-colorspace project (this one's
        // default), a RenderTexture created without this reads back darker than the
        // same object looks when the main camera renders it straight to the gamma-
        // corrected backbuffer, which is exactly the washed-out/dim look the overlay
        // dice had here.
        var rtDesc = new RenderTextureDescriptor(1920, 1080, RenderTextureFormat.ARGB32, 24) { sRGB = true };
        var rt = new RenderTexture(rtDesc) { name = "DiceOverlayRT" };
        rt.Create();

        var diceCamGO = new GameObject("DiceOverlayCamera");
        var diceCam = diceCamGO.AddComponent<Camera>();
        diceCam.CopyFrom(cam); // matches position/rotation/FOV so the dice line up with where the main camera would draw them
        diceCam.clearFlags = CameraClearFlags.SolidColor;
        diceCam.backgroundColor = new Color(0f, 0f, 0f, 0f);
        diceCam.cullingMask = 1 << diceLayer;
        diceCam.targetTexture = rt;
        diceCam.depth = cam.depth + 1;

        var overlayGO = new GameObject("DiceOverlayImage", typeof(RectTransform));
        overlayGO.transform.SetParent(canvasParent, false);
        var raw = overlayGO.AddComponent<RawImage>();
        raw.texture = rt;
        raw.raycastTarget = false;
        var overlayRt = overlayGO.GetComponent<RectTransform>();
        overlayRt.anchorMin = Vector2.zero;
        overlayRt.anchorMax = Vector2.one;
        overlayRt.offsetMin = Vector2.zero;
        overlayRt.offsetMax = Vector2.zero;
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

    const float DicePitWallHeight = 2.2f;
    const float DicePitWallThickness = 0.3f;

    // A real (invisible) rectangular pit of BoxColliders around the play area —
    // walls on all four sides plus a floor — for the dice's real Rigidbody
    // physics to actually bounce off of. Sized directly from the known launch and
    // landing points with a fixed margin, NOT from camera viewport corners — a
    // first pass did that (a viewport rect at groundY), which sounded like the
    // "correct" way to match what's actually visible, but this camera is tilted
    // steeply enough that a viewport point near the top of the screen casts a ray
    // close to the horizon; ViewportPointToRay/plane-intersect on a ray that
    // shallow returns a point extremely far away, which silently placed a wall
    // way out past anything actually on screen — the dice weren't escaping a
    // hole in the pit, the "far" wall just wasn't anywhere near where it looked
    // like it should be. Bounds built from launchCenter/landingCenter plus a
    // fixed margin stay proportional to the actual play area regardless of the
    // camera's projection quirks. If a toss ever needs to be blocked in some
    // other direction, another collider can be added the same way this whole pit
    // was. No Renderer/mesh on any of them — invisible by construction, not by
    // an invisible material.
    const float DicePitMargin = 1.8f;

    void BuildDicePit(float groundY, Vector3 launchCenter, Vector3 landingCenter)
    {
        float minX = Mathf.Min(launchCenter.x, landingCenter.x) - DicePitMargin;
        float maxX = Mathf.Max(launchCenter.x, landingCenter.x) + DicePitMargin;
        float minZ = Mathf.Min(launchCenter.z, landingCenter.z) - DicePitMargin;
        float maxZ = Mathf.Max(launchCenter.z, landingCenter.z) + DicePitMargin;
        float centerX = (minX + maxX) / 2f;
        float centerZ = (minZ + maxZ) / 2f;
        float width = maxX - minX;
        float depth = maxZ - minZ;

        var wallMat = new PhysicsMaterial("DiceWall")
        {
            bounciness = 0.55f, dynamicFriction = 0.25f, staticFriction = 0.25f,
            frictionCombine = PhysicsMaterialCombine.Average, bounceCombine = PhysicsMaterialCombine.Average
        };
        // High friction + Maximum combine (paired with the die's own material) —
        // the die's rotation is frozen during a toss (see Dice3D.PhysicsToss), so
        // it can't shed speed by actually tumbling like a real die; the floor
        // needs to grip hard on its own or a slide reads as sliding on ice.
        var floorMat = new PhysicsMaterial("DiceFloor")
        {
            bounciness = 0.1f, dynamicFriction = 0.9f, staticFriction = 1f,
            frictionCombine = PhysicsMaterialCombine.Maximum, bounceCombine = PhysicsMaterialCombine.Average
        };

        float t = DicePitWallThickness;
        float h = DicePitWallHeight;
        BuildPitCollider("DicePit_Right", new Vector3(maxX + t / 2f, groundY + h / 2f, centerZ), new Vector3(t, h, depth), wallMat);
        BuildPitCollider("DicePit_Left", new Vector3(minX - t / 2f, groundY + h / 2f, centerZ), new Vector3(t, h, depth), wallMat);
        BuildPitCollider("DicePit_Far", new Vector3(centerX, groundY + h / 2f, maxZ + t / 2f), new Vector3(width, h, t), wallMat);
        BuildPitCollider("DicePit_Near", new Vector3(centerX, groundY + h / 2f, minZ - t / 2f), new Vector3(width, h, t), wallMat);
        BuildPitCollider("DicePit_Floor", new Vector3(centerX, groundY - t / 2f, centerZ), new Vector3(width, t, depth), floorMat);
        // Ceiling — the side walls only cap escape up to their own height, not
        // above it, so a hard enough bounce could arc clean over them and keep
        // traveling outside the pit while airborne. Closing the top makes it an
        // actual sealed box, not four open-topped walls.
        BuildPitCollider("DicePit_Ceiling", new Vector3(centerX, groundY + h + t / 2f, centerZ), new Vector3(width, t, depth), wallMat);
    }

    void BuildPitCollider(string name, Vector3 localPos, Vector3 size, PhysicsMaterial mat)
    {
        var go = new GameObject(name);
        go.transform.SetParent(transform, false);
        go.transform.localPosition = localPos;
        var col = go.AddComponent<BoxCollider>();
        col.size = size;
        col.material = mat;
    }

    void OnApplicationQuit()
    {
        if (bankroll != null) CrapsSaveSystem.Save(bankroll, nextRoundIndex, sessionRecords);
    }
}
