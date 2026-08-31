using System;
using System.Collections;
using System.Collections.Generic;
using DG.Tweening;
using UnityEngine;

// Trajectory + natural face detected by a pre-simulation run on shadow dice.
// RollPair consumes this to skip Phase 1 entirely — zero lag on ROLL press.
public class PreSimResult
{
    public List<Vector3> Pos1, Pos2;
    public List<Quaternion> Rot1, Rot2;
    public int Nat1, Nat2;
}

// A real 3D die. Visually it's built the same way RouletteTableBuilder/
// WheelSpinAnimator build the wheel, but the toss itself uses a real Rigidbody +
// BoxCollider bouncing off real (invisible) wall/floor colliders in the scene —
// actual PhysX simulation, not a scripted position curve. The roll outcome still
// always comes from CrapsRound.Roll() first, same as the wheel spinning to a
// target angle guaranteed to land the winning pocket: physics drives the flight
// and bounce purely for visual realism, then once it settles the rotation is
// snapped to whatever face the already-decided value needs, overriding wherever
// physics happened to leave it.
public class Dice3D : MonoBehaviour
{
    // Standard die: opposite faces sum to 7. Each face's quad sits at this local
    // offset direction (with the die at identity rotation) — rotating the whole die
    // so that direction points world-up is what puts that face "up" toward camera.
    static readonly (int face, Vector3 axis)[] FaceAxis =
    {
        (1, Vector3.up), (6, Vector3.down),
        (2, Vector3.right), (5, Vector3.left),
        (3, Vector3.forward), (4, Vector3.back)
    };

    static Texture2D[] pipTextures;
    static Shader fallbackShader;
    static PhysicsMaterial diceMaterial;
    Coroutine tumbleRoutine;
    Rigidbody rb;
    Vector3 anchorLocalPosition;
    Vector3 landingLocalPosition;
    bool domeMode;
    float domeRollRadius;
    float dieSize;

    // True once a toss has physically come to rest and the final face-up rotation
    // has finished snapping into place — CrapsBettingUIController waits on this
    // instead of a fixed timer, since real physics settling time isn't fixed.
    public bool Settled { get; private set; } = true;

    // launchLocalPosition is the fixed throw point every toss starts from;
    // landingLocalPosition just sets the general aim direction for the initial
    // throw velocity — real physics (bouncing off the pit of colliders, and off
    // each other, that CrapsGameManager builds around the play area) decides
    // where it actually ends up, not an exact scripted target.
    public static Dice3D Create(Transform parent, Vector3 launchLocalPosition, Vector3 landingLocalPosition, float size = 0.6f)
    {
        var go = new GameObject("Die3D");
        go.transform.SetParent(parent, false);
        go.transform.localPosition = launchLocalPosition;
        var die = go.AddComponent<Dice3D>();
        die.anchorLocalPosition = launchLocalPosition;
        die.landingLocalPosition = landingLocalPosition;
        die.BuildSelf(size);
        return die;
    }

    // Dome variant — dice bounce inside the cylindrical glass dome. No aim
    // direction needed; each toss launches in a random horizontal direction and
    // the dome's own colliders keep them contained.
    public static Dice3D CreateInDome(Transform parent, Vector3 localPosition, float rollRadius, float size = 0.95f)
    {
        var go = new GameObject("Die3D");
        go.transform.SetParent(parent, false);
        go.transform.localPosition = localPosition;
        var die = go.AddComponent<Dice3D>();
        die.anchorLocalPosition = localPosition;
        die.domeMode = true;
        die.domeRollRadius = rollRadius;
        die.BuildSelf(size);
        return die;
    }

    void BuildSelf(float size)
    {
        dieSize = size;
        EnsurePipTextures();

        // Solid core underneath the 6 pip quads — without it, the die is a hollow
        // shell with zero volume between opposite faces. At a steep top-down camera
        // angle mid-tumble, an edge-on view of that shell reads as flat overlapping
        // cards instead of a cube. A plain white cube filling the shell guarantees
        // real 3D volume (and shading) from any angle or spin speed.
        // 0.98 (a 1% gap) was too tight — at this camera distance the pip quads and
        // the core's own faces could depth-fight, randomly letting the untextured
        // white core win the pixel and making the die flicker blank. A real gap
        // removes the ambiguity entirely.
        var core = GameObject.CreatePrimitive(PrimitiveType.Cube);
        core.name = "Core";
        core.transform.SetParent(transform, false);
        core.transform.localScale = Vector3.one * size * 0.85f;
        Destroy(core.GetComponent<Collider>());
        core.GetComponent<Renderer>().material = MatteMaterial(Color.white);

        foreach (var (face, axis) in FaceAxis)
        {
            var quad = GameObject.CreatePrimitive(PrimitiveType.Quad);
            quad.name = $"Face_{face}";
            quad.transform.SetParent(transform, false);
            quad.transform.localPosition = axis * (size / 2f);
            // Quad's front (textured, normal-facing) side looks down -local Z by
            // default — orienting -Z to point along `axis` puts the pip texture
            // facing outward, away from the die's center.
            quad.transform.localRotation = Quaternion.LookRotation(-axis, axis == Vector3.up || axis == Vector3.down ? Vector3.forward : Vector3.up);
            quad.transform.localScale = Vector3.one * size;
            DestroyImmediate(quad.GetComponent<Collider>());
            var mat = MatteMaterial(Color.white);
            mat.mainTexture = pipTextures[face];
            quad.GetComponent<Renderer>().material = mat;
        }
        SetFaceUp(1);

        // One collider on the root representing the die's whole bounding box (the
        // per-face/core colliders were destroyed above — real collision only needs
        // this one, not six thin quad colliders plus the core's).
        var col = gameObject.AddComponent<BoxCollider>();
        col.size = Vector3.one * size;
        col.material = DiceMaterial();

        rb = gameObject.AddComponent<Rigidbody>();
        rb.mass = 1f;
        rb.linearDamping = 0.02f;
        rb.angularDamping = 0.05f;
        rb.interpolation = RigidbodyInterpolation.Interpolate;
        rb.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
        rb.isKinematic = true; // released into real physics only for the duration of a toss
    }

    static PhysicsMaterial DiceMaterial()
    {
        if (diceMaterial != null) return diceMaterial;
        diceMaterial = new PhysicsMaterial("Dice")
        {
            bounciness = 0.80f,
            dynamicFriction = 0.4f,
            staticFriction = 0.5f,
            frictionCombine = PhysicsMaterialCombine.Average,
            bounceCombine = PhysicsMaterialCombine.Maximum
        };
        return diceMaterial;
    }

    // Standard shader defaults to a glossy 0.5 smoothness — under this scene's
    // directional key light, a shiny white die face can bloom into a blown-out
    // specular highlight from a steep top-down camera, washing the pip contrast out
    // entirely (the actual cause behind dice reading as "just white cubes" even
    // though the pip texture itself was already correct). Matte finish fixes it.
    static Material MatteMaterial(Color color)
    {
        var mat = new Material(FallbackShader()) { color = color };
        mat.SetFloat("_Glossiness", 0.05f);
        mat.SetFloat("_Metallic", 0f);
        return mat;
    }

    // Rotates the whole die (world rotation, not local — safe regardless of what
    // it's parented under) so the given face's local axis now points world-up.
    public void SetFaceUp(int value)
    {
        var entry = Array.Find(FaceAxis, f => f.face == value);
        transform.rotation = Quaternion.FromToRotation(entry.axis, Vector3.up);
    }

    const float LaunchSpeed = 20f;
    const float LaunchUpSpeed = 6f;
    const float LaunchAngleJitterDeg = 12f;
    const float DomeLaunchUpSpeed = 4.5f;
    const float DomeLaunchHorizontalSpeed = 0.3f;
    const float DomeSimGravity = -5.0f; // reduced from -9.8 so arcs hang visibly longer
    const float MaxTossDuration = 8.0f;
    const float SettleLinearThresholdSq = 0.002f;   // ~0.045 m/s — visually motionless
    const float SettleAngularThresholdSq = 0.002f;  // ~0.045 rad/s — visually motionless
    const float MinSettleCheckTime = 0.8f;
    // bounciness=0.80 causes 20+ micro-bounces below 0.1 m/s before velocity reaches
    // the settle threshold. These play back as visible "jumps after landing." Applying
    // extra per-frame damping only in the low-energy regime kills the micro-bounce tail
    // without touching the big visible bounces (which are all >> 0.1 m/s).
    const float MicroDampLinThreshSq = 0.04f;  // 0.2 m/s — catches last low-energy bounces earlier
    const float MicroDampFactor = 0.80f;       // was 0.88 — more aggressive kill of micro-bounce tail
    // Random spin added at launch so the die tumbles through the air naturally —
    // physics then settles it on whatever face it lands; we remap textures at
    // the end instead of rotating, so there's no visible snap or correction.
    const float LaunchAngularSpeed = 3f;

    // Runs Phase 1 (silent sim) on invisible shadow dice and stores the result.
    // Called immediately after each roll completes so the trajectory is ready
    // before the player presses ROLL again. Shadow dice never appear on screen —
    // their renderers are disabled permanently in BubbleCrapsDome.
    public static IEnumerator RunPreSim(Dice3D shadow1, Dice3D shadow2, Action<PreSimResult> onDone)
    {
        var prevMode = Physics.simulationMode;
        var prevGravity = Physics.gravity;
        Physics.simulationMode = SimulationMode.Script;
        Physics.gravity = new Vector3(0f, DomeSimGravity, 0f);

        List<Vector3> pos1 = null, pos2 = null;
        List<Quaternion> rot1 = null, rot2 = null;
        int b1, b2;
        do
        {
            SimulateRoll(shadow1, shadow2, out pos1, out rot1, out pos2, out rot2, out b1, out b2);
            if (!IsValidSettle(shadow1, shadow2) || b1 < 5 || b2 < 5) yield return null;
        }
        while (!IsValidSettle(shadow1, shadow2) || b1 < 5 || b2 < 5);

        int nat1 = shadow1.DetectTopFace();
        int nat2 = shadow2.DetectTopFace();
        shadow1.rb.isKinematic = true;
        shadow2.rb.isKinematic = true;

        Physics.gravity = prevGravity;
        Physics.simulationMode = prevMode;

        onDone(new PreSimResult { Pos1 = pos1, Rot1 = rot1, Pos2 = pos2, Rot2 = rot2, Nat1 = nat1, Nat2 = nat2 });
    }

    // Phase 2 only — trajectory already computed by RunPreSim on shadow dice.
    // Applies the face-rotation offset for the RNG-decided values and plays back
    // the recorded positions. No Phase 1, no lag, no retry loop visible to player.
    public static IEnumerator RollPair(Dice3D d1, int val1, Dice3D d2, int val2, PreSimResult presim)
    {
        d1.Settled = false;
        d2.Settled = false;
        if (d1.tumbleRoutine != null) { d1.StopCoroutine(d1.tumbleRoutine); d1.tumbleRoutine = null; }
        if (d2.tumbleRoutine != null) { d2.StopCoroutine(d2.tumbleRoutine); d2.tumbleRoutine = null; }
        d1.transform.DOKill();
        d2.transform.DOKill();

        Quaternion off1 = FaceRotationOffset(presim.Nat1, val1);
        Quaternion off2 = FaceRotationOffset(presim.Nat2, val2);

        d1.rb.isKinematic = true;
        d1.transform.localPosition = d1.anchorLocalPosition; d1.transform.localRotation = off1;
        d2.rb.isKinematic = true;
        d2.transform.localPosition = d2.anchorLocalPosition; d2.transform.localRotation = off2;
        Physics.SyncTransforms();

        // PlaybackSpeed < 1 = slower than sim time. 0.4 ≈ 2.5× slower than real
        // physics, matching the leisurely pace of a real bubble-craps machine.
        const float PlaybackSpeed = 3f;
        var pos1 = presim.Pos1; var rot1 = presim.Rot1;
        var pos2 = presim.Pos2; var rot2 = presim.Rot2;
        int count = pos1.Count;
        float stepAccum = 0f;
        int frame = 0;
        while (true)
        {
            d1.transform.position = pos1[frame];
            d1.transform.rotation = rot1[frame] * off1;
            d2.transform.position = pos2[frame];
            d2.transform.rotation = rot2[frame] * off2;
            if (frame >= count - 1) break;
            yield return null;
            stepAccum += PlaybackSpeed * (Time.deltaTime / Time.fixedDeltaTime);
            while (stepAccum >= 1f && frame < count - 1) { stepAccum -= 1f; frame++; }
        }

        d1.Settled = true;
        d2.Settled = true;
    }

    // Runs one full silent simulation attempt. Returns floor-contact bounce counts
    // per die (Y-velocity sign flip from negative to positive = one floor bounce).
    // Physics.simulationMode must already be SimulationMode.Script and gravity set.
    static void SimulateRoll(Dice3D d1, Dice3D d2,
        out System.Collections.Generic.List<Vector3> pos1,
        out System.Collections.Generic.List<Quaternion> rot1,
        out System.Collections.Generic.List<Vector3> pos2,
        out System.Collections.Generic.List<Quaternion> rot2,
        out int bounces1, out int bounces2)
    {
        d1.rb.isKinematic = true; d2.rb.isKinematic = true;
        d1.transform.localPosition = d1.anchorLocalPosition; d1.transform.localRotation = Quaternion.identity;
        d2.transform.localPosition = d2.anchorLocalPosition; d2.transform.localRotation = Quaternion.identity;
        Physics.SyncTransforms();

        Vector3 lv1 = d1.LaunchVelocity(), av1 = UnityEngine.Random.insideUnitSphere * LaunchAngularSpeed;
        Vector3 lv2 = d2.LaunchVelocity(), av2 = UnityEngine.Random.insideUnitSphere * LaunchAngularSpeed;

        d1.rb.isKinematic = false; d1.rb.constraints = RigidbodyConstraints.None;
        d1.rb.linearVelocity = lv1; d1.rb.angularVelocity = av1;
        d2.rb.isKinematic = false; d2.rb.constraints = RigidbodyConstraints.None;
        d2.rb.linearVelocity = lv2; d2.rb.angularVelocity = av2;

        pos1 = new System.Collections.Generic.List<Vector3>();
        rot1 = new System.Collections.Generic.List<Quaternion>();
        pos2 = new System.Collections.Generic.List<Vector3>();
        rot2 = new System.Collections.Generic.List<Quaternion>();

        bounces1 = 0; bounces2 = 0;
        float prevVelY1 = lv1.y, prevVelY2 = lv2.y;
        float simTime = 0f, dt = Time.fixedDeltaTime;
        while (simTime < MaxTossDuration)
        {
            pos1.Add(d1.transform.position); rot1.Add(d1.transform.rotation);
            pos2.Add(d2.transform.position); rot2.Add(d2.transform.rotation);
            Physics.Simulate(dt);
            simTime += dt;

            float velY1 = d1.rb.linearVelocity.y;
            float velY2 = d2.rb.linearVelocity.y;
            if (prevVelY1 < -0.1f && velY1 >= 0f) bounces1++;
            if (prevVelY2 < -0.1f && velY2 >= 0f) bounces2++;
            prevVelY1 = velY1; prevVelY2 = velY2;

            if (d1.rb.linearVelocity.sqrMagnitude < MicroDampLinThreshSq)
            { d1.rb.linearVelocity *= MicroDampFactor; d1.rb.angularVelocity *= MicroDampFactor; }
            if (d2.rb.linearVelocity.sqrMagnitude < MicroDampLinThreshSq)
            { d2.rb.linearVelocity *= MicroDampFactor; d2.rb.angularVelocity *= MicroDampFactor; }
            if (simTime > MinSettleCheckTime
                && d1.rb.linearVelocity.sqrMagnitude < SettleLinearThresholdSq
                && d1.rb.angularVelocity.sqrMagnitude < SettleAngularThresholdSq
                && d2.rb.linearVelocity.sqrMagnitude < SettleLinearThresholdSq
                && d2.rb.angularVelocity.sqrMagnitude < SettleAngularThresholdSq)
            {
                d1.rb.linearVelocity = Vector3.zero; d1.rb.angularVelocity = Vector3.zero;
                d2.rb.linearVelocity = Vector3.zero; d2.rb.angularVelocity = Vector3.zero;
                pos1.Add(d1.transform.position); rot1.Add(d1.transform.rotation);
                pos2.Add(d2.transform.position); rot2.Add(d2.transform.rotation);
                break;
            }
        }
    }

    // False if either die is tilted >~18°, stacked, or intersecting.
    // A real bubble-craps machine re-shakes until dice separate cleanly and lie flat.
    static bool IsValidSettle(Dice3D d1, Dice3D d2)
    {
        if (d1.MaxFaceUpDot() < 0.95f || d2.MaxFaceUpDot() < 0.95f) return false; // tilted >~18°
        float dy = Mathf.Abs(d1.transform.position.y - d2.transform.position.y);
        if (dy > d1.dieSize * 0.6f) return false; // stacked
        Vector3 p1h = new Vector3(d1.transform.position.x, 0f, d1.transform.position.z);
        Vector3 p2h = new Vector3(d2.transform.position.x, 0f, d2.transform.position.z);
        if (Vector3.Distance(p1h, p2h) < d1.dieSize * 0.85f) return false; // intersecting
        return true;
    }

    // Instantly settles a die that landed on an edge: snaps rotation to the nearest
    // face-up orientation and drops the center to floor-resting height so it doesn't
    // visually float. The floor top sits at +0.04 above the dome's origin (from
    // BubbleCrapsDome's floor collider: center=-0.01, half-height=0.05 → top=0.04).
    static void SnapDieToFloor(Dice3D die)
    {
        float bestDot = float.MinValue;
        Vector3 bestLocalAxis = Vector3.up;
        foreach (var (_, axis) in FaceAxis)
        {
            float d = Vector3.Dot(die.transform.rotation * axis, Vector3.up);
            if (d > bestDot) { bestDot = d; bestLocalAxis = axis; }
        }
        die.transform.rotation = Quaternion.FromToRotation(die.transform.rotation * bestLocalAxis, Vector3.up) * die.transform.rotation;
        float floorWorldY = die.transform.parent != null ? die.transform.parent.position.y + 0.04f : 0.04f;
        var p = die.transform.position;
        p.y = floorWorldY + die.dieSize * 0.5f;
        die.transform.position = p;
    }

    // Local-space pre-rotation applied as (physicsRot * offset) in playback.
    // We want the FINAL frame to satisfy: (rot_final * offset) * val_axis = world_up.
    // We know rot_final * nat_axis = world_up (nat was on top after sim).
    // So we need offset * val_axis = nat_axis  →  offset = FromToRotation(val, nat).
    // Note: arguments are (toFace=val, fromFace=nat) — opposite of the old left-multiply.
    static Quaternion FaceRotationOffset(int natFace, int valFace)
    {
        if (natFace == valFace) return Quaternion.identity;
        var nat = Array.Find(FaceAxis, f => f.face == natFace);
        var val = Array.Find(FaceAxis, f => f.face == valFace);
        return Quaternion.FromToRotation(val.axis, nat.axis);
    }

    // Computes a random launch velocity for this die based on its mode.
    Vector3 LaunchVelocity()
    {
        if (domeMode)
        {
            float angle = UnityEngine.Random.Range(0f, 360f) * Mathf.Deg2Rad;
            Vector3 hLocal = new Vector3(Mathf.Sin(angle), 0f, Mathf.Cos(angle));
            Vector3 hWorld = transform.parent != null ? transform.parent.TransformDirection(hLocal) : hLocal;
            return hWorld * DomeLaunchHorizontalSpeed + Vector3.up * DomeLaunchUpSpeed;
        }
        Vector3 aimDir = landingLocalPosition - anchorLocalPosition;
        aimDir.y = 0f;
        aimDir = aimDir.sqrMagnitude > 0.0001f ? aimDir.normalized : Vector3.forward;
        aimDir = Quaternion.Euler(0f, UnityEngine.Random.Range(-LaunchAngleJitterDeg, LaunchAngleJitterDeg), 0f) * aimDir;
        Vector3 worldDir = transform.parent != null ? transform.parent.TransformDirection(aimDir) : aimDir;
        return worldDir * LaunchSpeed + Vector3.up * LaunchUpSpeed;
    }

    // Single-die roll kept for backward compat — not used by the craps game
    // (which always rolls both dice together via RollPair).
    public void Roll(int finalValue)
    {
        if (tumbleRoutine != null) StopCoroutine(tumbleRoutine);
        tumbleRoutine = StartCoroutine(SingleToss());
    }

    IEnumerator SingleToss()
    {
        Settled = false;
        transform.DOKill();
        rb.isKinematic = true;
        transform.localPosition = anchorLocalPosition;
        transform.localRotation = Quaternion.identity;
        Physics.SyncTransforms();
        rb.isKinematic = false;
        rb.constraints = RigidbodyConstraints.None;
        rb.linearVelocity = LaunchVelocity();
        rb.angularVelocity = UnityEngine.Random.insideUnitSphere * LaunchAngularSpeed;
        float t = 0f;
        while (t < MaxTossDuration)
        {
            t += Time.deltaTime;
            if (t > MinSettleCheckTime
                && rb.linearVelocity.sqrMagnitude < SettleLinearThresholdSq
                && rb.angularVelocity.sqrMagnitude < SettleAngularThresholdSq)
                break;
            yield return null;
        }
        rb.isKinematic = true;
        Settled = true;
        tumbleRoutine = null;
    }

    int DetectTopFace()
    {
        int topFace = 1;
        float maxDot = float.MinValue;
        foreach (var (face, axis) in FaceAxis)
        {
            float dot = Vector3.Dot(transform.TransformDirection(axis), Vector3.up);
            if (dot > maxDot) { maxDot = dot; topFace = face; }
        }
        return topFace;
    }

    // Highest dot product of any face axis with world-up — 1.0 = perfectly flat,
    // ~0.71 = balanced on an edge (45°), ~0.58 = balanced on a corner.
    float MaxFaceUpDot()
    {
        float max = 0f;
        foreach (var (_, axis) in FaceAxis)
            max = Mathf.Max(max, Vector3.Dot(transform.TransformDirection(axis), Vector3.up));
        return max;
    }

    static void EnsurePipTextures()
    {
        // pipTextures is a static C# array — it survives a Play-mode Stop with no
        // recompile in between (statics only reset on a script domain reload), but
        // Unity destroys every dynamically-created Texture2D from the previous Play
        // session when it stops. Checking only "pipTextures != null" tested the
        // array reference, which stays non-null forever — never checked whether its
        // elements were actually still alive, so a second Play session without a
        // recompile in between kept an array of destroyed textures and never
        // regenerated them (every die face silently rendered blank). Checking one
        // element with Unity's overridden null check (which is destroyed-object
        // aware, unlike a plain reference check) catches that case correctly.
        if (pipTextures != null && pipTextures[1] != null) return;
        pipTextures = new Texture2D[7];
        for (int face = 1; face <= 6; face++)
            pipTextures[face] = GeneratePipTexture(face);
    }

    // Same 7-slot pip layout (TL,TR,ML,MR,BL,BR,C) DiceUI.cs uses for the 2D dice —
    // duplicated in pixel form here rather than shared, since one draws UI Images
    // and the other rasterizes a Texture2D; keeping the slot arrangement identical
    // is what actually matters (both dice agree on what a "5" looks like).
    static readonly Vector2[] SlotPos =
    {
        new Vector2(0.25f, 0.75f), new Vector2(0.75f, 0.75f),
        new Vector2(0.25f, 0.5f), new Vector2(0.75f, 0.5f),
        new Vector2(0.25f, 0.25f), new Vector2(0.75f, 0.25f),
        new Vector2(0.5f, 0.5f)
    };
    static readonly int[][] FacePips =
    {
        new int[0],
        new[] { 6 },
        new[] { 0, 5 },
        new[] { 0, 6, 5 },
        new[] { 0, 1, 4, 5 },
        new[] { 0, 1, 6, 4, 5 },
        new[] { 0, 1, 2, 3, 4, 5 }
    };

    static Texture2D GeneratePipTexture(int face)
    {
        // No mip chain: a mipmapped texture's coarsest levels are a box-average of
        // the whole image, and since pips are a small fraction of a mostly-white
        // background, those coarse mips are themselves nearly solid white — if the
        // die ever renders smaller (different window/output resolution than an
        // in-editor test capture), the GPU can pick one of those levels and the
        // pips vanish even though the full-res texture is fine. The texture is only
        // 128x128 and always close to camera, so there's no real minification case
        // that needs mips here, only this failure mode from having them.
        const int size = 128;
        var tex = new Texture2D(size, size, TextureFormat.RGBA32, false) { filterMode = FilterMode.Bilinear };
        var pixels = new Color[size * size];
        for (int i = 0; i < pixels.Length; i++) pixels[i] = Color.white;

        // Pure black, generous radius — a dark-gray, thin dot was still too subtle
        // once minified at the die's actual on-screen size.
        float radius = size * 0.2f;
        foreach (int slot in FacePips[face])
        {
            Vector2 center = new Vector2(SlotPos[slot].x * size, SlotPos[slot].y * size);
            int minX = Mathf.Max(0, Mathf.FloorToInt(center.x - radius));
            int maxX = Mathf.Min(size - 1, Mathf.CeilToInt(center.x + radius));
            int minY = Mathf.Max(0, Mathf.FloorToInt(center.y - radius));
            int maxY = Mathf.Min(size - 1, Mathf.CeilToInt(center.y + radius));
            for (int y = minY; y <= maxY; y++)
                for (int x = minX; x <= maxX; x++)
                    if (Vector2.Distance(new Vector2(x, y), center) < radius)
                        pixels[y * size + x] = Color.black;
        }
        tex.SetPixels(pixels);
        tex.Apply(false);
        return tex;
    }

    static Shader FallbackShader()
    {
        if (fallbackShader != null) return fallbackShader;
        fallbackShader = Shader.Find("Standard")
            ?? Shader.Find("Universal Render Pipeline/Lit")
            ?? Shader.Find("Sprites/Default");
        return fallbackShader;
    }
}
