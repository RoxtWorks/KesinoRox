using System;
using System.Collections;
using DG.Tweening;
using UnityEngine;

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

    void BuildSelf(float size)
    {
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
            Destroy(quad.GetComponent<Collider>());
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
        // Bumped up alongside the friction changes below — extra general drag so
        // it settles promptly instead of gliding.
        rb.linearDamping = 0.5f;
        rb.angularDamping = 0.3f;
        rb.interpolation = RigidbodyInterpolation.Interpolate;
        rb.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
        rb.isKinematic = true; // released into real physics only for the duration of a toss
    }

    static PhysicsMaterial DiceMaterial()
    {
        if (diceMaterial != null) return diceMaterial;
        diceMaterial = new PhysicsMaterial("Dice")
        {
            bounciness = 0.45f,
            // High friction, and Maximum instead of Average combine (with the
            // floor's own high-friction material — see CrapsGameManager) — with
            // rotation frozen (see PhysicsToss) the die can never bleed speed off
            // by actually tumbling the way a real die does, so friction is the
            // only thing left to stop a slide. The original 0.3/Average let it
            // glide a long way, reading as sliding on ice.
            dynamicFriction = 0.8f,
            staticFriction = 0.9f,
            frictionCombine = PhysicsMaterialCombine.Maximum,
            bounceCombine = PhysicsMaterialCombine.Average
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

    // "Custom strength" tuning for the throw — how hard it launches, how much
    // spread each toss gets, how long real physics is allowed to keep bouncing
    // before being forced to stop.
    // The high friction added to stop the "sliding on ice" problem also eats
    // momentum fast — a throw at the old speed now lands and stops short of the
    // far wall instead of actually reaching it. Thrown harder to reliably carry
    // it all the way there for a real visible bounce, while the friction still
    // takes over and stops it quickly right after.
    const float LaunchSpeed = 20f;
    const float LaunchUpSpeed = 6f;
    const float LaunchAngleJitterDeg = 12f;
    const float MaxTossDuration = 2.2f;
    const float SettleLinearThresholdSq = 0.35f;
    const float MinSettleCheckTime = 0.3f;

    // Rotation is entirely scripted, never physics-driven (see rb.constraints in
    // PhysicsToss). A first pass let real physics rotate the die too, then hard-
    // snapped it to the correct face once it stopped — a visible "correction"
    // whenever physics landed it on the wrong face (constantly, since PhysX has
    // no idea what CrapsRound.Roll() decided). Scripting the spin fixed that, but
    // a second pass still did it as two separate phases — spin fast around a
    // random axis, THEN ease into the target with a second Slerp — and those two
    // phases almost always rotate around different axes, which reads as a
    // visible "flip" at the handoff between them (e.g. looking like it landed on
    // a 1, then flipped to a 5). The actual fix is to never have two phases: pick
    // ONE axis (computed from start straight to the target face, via
    // ToAngleAxis) and one continuous, decelerating rotation around only that
    // axis for the whole toss — extra full 360s tacked onto the angle give it a
    // wild multi-spin tumble, but a whole-number of extra turns doesn't change
    // where it mathematically ends up, so it still lands exactly on the correct
    // face with nothing to correct and no axis change to notice.
    const float RotateDuration = 0.85f;
    const int MinExtraSpins = 3;
    const int MaxExtraSpins = 6;

    public void Roll(int finalValue)
    {
        if (tumbleRoutine != null) StopCoroutine(tumbleRoutine);
        tumbleRoutine = StartCoroutine(PhysicsToss(finalValue));
    }

    IEnumerator PhysicsToss(int finalValue)
    {
        Settled = false;
        transform.DOKill();

        // Every toss launches from the same fixed anchor rather than wherever the
        // last roll happened to land — a real toss always starts from the
        // shooter's hand, not from the previous roll's resting spot. Toggling
        // isKinematic around the teleport keeps physics from fighting the manual
        // position set. Physics.SyncTransforms() forces PhysX to actually pick up
        // that new position immediately — without it, setting transform.position
        // and re-enabling physics in the same frame occasionally left PhysX still
        // using its last cached (pre-teleport) position for the very next physics
        // step, so the launch velocity would get applied from the old landed spot
        // instead of the anchor, which is exactly what an occasional "thrown from
        // where it last landed" roll was.
        rb.isKinematic = true;
        transform.localPosition = anchorLocalPosition;
        transform.localRotation = Quaternion.identity;
        Physics.SyncTransforms();
        rb.isKinematic = false;
        rb.linearVelocity = Vector3.zero;
        rb.angularVelocity = Vector3.zero;
        // Physics only ever drives position (the bounce/travel path) — rotation
        // is fully scripted below, so freeze it here or collisions would still
        // apply real torque on top of (and fighting) the scripted spin.
        rb.constraints = RigidbodyConstraints.FreezeRotation;

        Vector3 aimDir = landingLocalPosition - anchorLocalPosition;
        aimDir.y = 0f;
        aimDir = aimDir.sqrMagnitude > 0.0001f ? aimDir.normalized : Vector3.forward;
        // A little random spread on the throw angle each roll so tosses aren't
        // all identical, same intent as the old position-jitter had.
        aimDir = Quaternion.Euler(0f, UnityEngine.Random.Range(-LaunchAngleJitterDeg, LaunchAngleJitterDeg), 0f) * aimDir;

        Vector3 worldDir = transform.parent != null ? transform.parent.TransformDirection(aimDir) : aimDir;
        rb.linearVelocity = worldDir * LaunchSpeed + Vector3.up * LaunchUpSpeed;

        var entry = Array.Find(FaceAxis, f => f.face == finalValue);
        Quaternion target = Quaternion.FromToRotation(entry.axis, Vector3.up);

        // One fixed axis for the whole toss, derived from exactly the rotation
        // needed to go from the current (identity) orientation to the target —
        // extra full spins added on top purely for a wild tumbling look, since a
        // whole number of extra 360s around the same axis doesn't change the
        // final orientation at all.
        Quaternion startRot = transform.rotation;
        (target * Quaternion.Inverse(startRot)).ToAngleAxis(out float deltaAngle, out Vector3 spinAxis);
        if (spinAxis.sqrMagnitude < 0.0001f) spinAxis = Vector3.up; // degenerate only when target == startRot
        float totalAngle = deltaAngle + 360f * UnityEngine.Random.Range(MinExtraSpins, MaxExtraSpins + 1);

        float rt = 0f;
        while (rt < RotateDuration)
        {
            rt += Time.deltaTime;
            float p = Mathf.Clamp01(rt / RotateDuration);
            float eased = 1f - Mathf.Pow(1f - p, 3f); // fast start, smooth decelerating stop
            transform.rotation = Quaternion.AngleAxis(totalAngle * eased, spinAxis) * startRot;
            yield return null;
        }
        transform.rotation = target;

        // Rotation is done; keep waiting only if the die is still physically
        // bouncing/sliding around, so it doesn't look like it stopped tumbling
        // while still visibly skidding across the felt.
        float t = 0f;
        while (t < MaxTossDuration)
        {
            t += Time.deltaTime;
            if (t > MinSettleCheckTime && rb.linearVelocity.sqrMagnitude < SettleLinearThresholdSq)
                break;
            yield return null;
        }

        rb.isKinematic = true;
        rb.linearVelocity = Vector3.zero;

        Settled = true;
        tumbleRoutine = null;
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
