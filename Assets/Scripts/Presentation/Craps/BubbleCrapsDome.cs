using UnityEngine;

// Replicates the real bubble-craps machine's glass dome: a clear cylindrical
// tube with a wavy-cut top rim, sitting on a dark leather base, with a green
// felt floor lit from underneath by a green LED point light. Two dice bounce
// inside via real physics off a ring of box colliders approximating the
// cylinder wall. The dome and dice live on the CrapsDice layer so they draw
// on top of the canvas UI through the overlay camera.
public class BubbleCrapsDome : MonoBehaviour
{
    public Dice3D Die1 { get; private set; }
    public Dice3D Die2 { get; private set; }
    // Shadow dice run Phase 1 physics during idle time so ROLL triggers instant playback.
    // Renderers are disabled permanently — they never appear on screen.
    public Dice3D ShadowDie1 { get; private set; }
    public Dice3D ShadowDie2 { get; private set; }

    const float DomeRadius = 1.5f;
    const float DomeHeight = 2.5f;
    const int WallSegments = 20;

    public static BubbleCrapsDome Create(Transform parent, Vector3 worldPosition)
    {
        var go = new GameObject("BubbleCrapsDome");
        go.transform.SetParent(parent, false);
        go.transform.position = worldPosition;
        var dome = go.AddComponent<BubbleCrapsDome>();
        dome.Build();
        return dome;
    }

    void Build()
    {
        BuildVisuals();
        BuildPhysicsColliders();
        BuildLighting();
        BuildDice();
    }

    void BuildVisuals()
    {
        // Transparent glass cylinder — Unity's Cylinder primitive is height-1 in
        // local space, so localScale.y = DomeHeight/2 gives the full height.
        var cylinder = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        cylinder.name = "DomeGlass";
        cylinder.transform.SetParent(transform, false);
        cylinder.transform.localPosition = new Vector3(0, DomeHeight / 2f, 0);
        cylinder.transform.localScale = new Vector3(DomeRadius * 2f, DomeHeight / 2f, DomeRadius * 2f);
        Destroy(cylinder.GetComponent<Collider>());
        cylinder.GetComponent<Renderer>().material = GlassMaterial();

        // Green felt floor disc
        var floor = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        floor.name = "DomeFloor";
        floor.transform.SetParent(transform, false);
        floor.transform.localPosition = new Vector3(0, 0.02f, 0);
        floor.transform.localScale = new Vector3(DomeRadius * 1.85f, 0.02f, DomeRadius * 1.85f);
        Destroy(floor.GetComponent<Collider>());
        var feltMat = new Material(FallbackShader()) { color = new Color(0.04f, 0.42f, 0.10f) };
        feltMat.SetFloat("_Glossiness", 0.05f);
        floor.GetComponent<Renderer>().material = feltMat;

        // Dark leather base platform (wider than dome)
        var basePlatform = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        basePlatform.name = "DomeBase";
        basePlatform.transform.SetParent(transform, false);
        basePlatform.transform.localPosition = new Vector3(0, -0.15f, 0);
        basePlatform.transform.localScale = new Vector3(DomeRadius * 2.6f, 0.12f, DomeRadius * 2.6f);
        Destroy(basePlatform.GetComponent<Collider>());
        var baseMat = new Material(FallbackShader()) { color = new Color(0.12f, 0.08f, 0.07f) };
        baseMat.SetFloat("_Glossiness", 0.25f);
        basePlatform.GetComponent<Renderer>().material = baseMat;

    }

    void BuildPhysicsColliders()
    {
        var wallMat = new PhysicsMaterial("DomeWall")
        {
            bounciness = 0.75f,
            dynamicFriction = 0.2f,
            staticFriction = 0.2f,
            frictionCombine = PhysicsMaterialCombine.Average,
            bounceCombine = PhysicsMaterialCombine.Maximum
        };
        var floorMat = new PhysicsMaterial("DomeFloor")
        {
            bounciness = 0.72f,
            dynamicFriction = 0.5f,
            staticFriction = 0.6f,
            frictionCombine = PhysicsMaterialCombine.Average,
            bounceCombine = PhysicsMaterialCombine.Maximum
        };

        // Ring of thin box colliders approximating the cylinder interior wall —
        // Unity has no built-in CylinderCollider; a ring of 20 segments gives a
        // smooth enough polygon that dice don't notice the flat edges.
        float segAngle = 360f / WallSegments;
        float segWidth = 2f * DomeRadius * Mathf.Sin(segAngle * 0.5f * Mathf.Deg2Rad) * 1.08f;
        float wallT = 0.18f;

        for (int i = 0; i < WallSegments; i++)
        {
            float angleDeg = i * segAngle;
            float rad = angleDeg * Mathf.Deg2Rad;
            var wallGO = new GameObject($"DomeWall_{i}");
            wallGO.transform.SetParent(transform, false);
            wallGO.transform.localPosition = new Vector3(
                Mathf.Sin(rad) * (DomeRadius - wallT * 0.5f),
                DomeHeight * 0.5f,
                Mathf.Cos(rad) * (DomeRadius - wallT * 0.5f)
            );
            wallGO.transform.localRotation = Quaternion.Euler(0f, -angleDeg, 0f);
            var col = wallGO.AddComponent<BoxCollider>();
            col.size = new Vector3(segWidth, DomeHeight, wallT);
            col.material = wallMat;
        }

        // Floor — top surface must sit at y=0.04, matching the visual felt disc top
        // (felt center=0.02, Unity cylinder half-height = scale.y = 0.02, top=0.04).
        // Old center was -0.05 → top at 0, dice rested below the visible felt.
        var floorGO = new GameObject("DomeFloorCollider");
        floorGO.transform.SetParent(transform, false);
        floorGO.transform.localPosition = new Vector3(0f, -0.01f, 0f);
        var fc = floorGO.AddComponent<BoxCollider>();
        fc.size = new Vector3(DomeRadius * 2f, 0.1f, DomeRadius * 2f);
        fc.material = floorMat;

        // Ceiling — keeps dice from launching out the open top
        var ceilGO = new GameObject("DomeCeiling");
        ceilGO.transform.SetParent(transform, false);
        ceilGO.transform.localPosition = new Vector3(0f, DomeHeight + 0.05f, 0f);
        var cc = ceilGO.AddComponent<BoxCollider>();
        cc.size = new Vector3(DomeRadius * 2f, 0.1f, DomeRadius * 2f);
        cc.material = wallMat;
    }

    void BuildLighting()
    {
        var lightGO = new GameObject("DomeUnderglow");
        lightGO.transform.SetParent(transform, false);
        lightGO.transform.localPosition = new Vector3(0f, 0.05f, 0f);
        var light = lightGO.AddComponent<Light>();
        light.type = LightType.Point;
        light.color = new Color(0.08f, 0.88f, 0.18f);
        light.intensity = 3f;
        light.range = DomeRadius * 3.5f;
    }

    void BuildDice()
    {
        float spawnY = 0.5f;
        Die1 = Dice3D.CreateInDome(transform,
            new Vector3(-0.35f, spawnY, -0.2f),
            DomeRadius * 0.7f, size: 0.45f);
        Die2 = Dice3D.CreateInDome(transform,
            new Vector3( 0.35f, spawnY,  0.2f),
            DomeRadius * 0.7f, size: 0.45f);

        // Shadow dice: same physics setup, renderers off. They occupy the same
        // dome space as the visual dice so sim trajectories are physically valid.
        ShadowDie1 = Dice3D.CreateInDome(transform,
            new Vector3(-0.35f, spawnY, -0.2f),
            DomeRadius * 0.7f, size: 0.45f);
        ShadowDie2 = Dice3D.CreateInDome(transform,
            new Vector3( 0.35f, spawnY,  0.2f),
            DomeRadius * 0.7f, size: 0.45f);
        foreach (var r in ShadowDie1.GetComponentsInChildren<Renderer>()) r.enabled = false;
        foreach (var r in ShadowDie2.GetComponentsInChildren<Renderer>()) r.enabled = false;

        // Prevent shadow dice from interacting with visual dice during sim.
        // Shadow dice still collide with dome walls/floor (same layer) for valid trajectories.
        var d1col = Die1.GetComponent<BoxCollider>();
        var d2col = Die2.GetComponent<BoxCollider>();
        var s1col = ShadowDie1.GetComponent<BoxCollider>();
        var s2col = ShadowDie2.GetComponent<BoxCollider>();
        Physics.IgnoreCollision(s1col, d1col, true);
        Physics.IgnoreCollision(s1col, d2col, true);
        Physics.IgnoreCollision(s2col, d1col, true);
        Physics.IgnoreCollision(s2col, d2col, true);
    }

    static Material GlassMaterial()
    {
        var mat = new Material(FallbackShader());
        mat.SetFloat("_Mode", 3);
        mat.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
        mat.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
        mat.SetInt("_ZWrite", 0);
        mat.DisableKeyword("_ALPHATEST_ON");
        mat.EnableKeyword("_ALPHABLEND_ON");
        mat.DisableKeyword("_ALPHAPREMULTIPLY_ON");
        mat.renderQueue = 3000;
        mat.color = new Color(0.82f, 0.93f, 1f, 0.35f);
        mat.SetFloat("_Glossiness", 0.92f);
        mat.SetFloat("_Metallic", 0.05f);
        return mat;
    }

    static Shader FallbackShader() =>
        Shader.Find("Standard")
        ?? Shader.Find("Universal Render Pipeline/Lit")
        ?? Shader.Find("Sprites/Default");
}
