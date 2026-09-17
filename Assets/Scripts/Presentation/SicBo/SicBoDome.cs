using UnityEngine;

// Sic Bo shaker dome — same glass dome, felt floor and colliders as BubbleCrapsDome,
// with three physics dice. Shadow dice pre-simulate the toss (Dice3D.RunPreSimGroup)
// so ROLL plays back a real tumble that lands on the core's result.
public class SicBoDome : MonoBehaviour
{
    public Dice3D[] Dice { get; private set; }
    // Renderers disabled permanently — never appear on screen.
    public Dice3D[] ShadowDice { get; private set; }
    // The popper floor: a kinematic body the pre-sim kicks, and the visible felt disc that replays its motion
    public Rigidbody FloorBody { get; private set; }
    public Transform FloorVisual { get; private set; }

    const float DomeRadius = 1.5f;
    const float DomeHeight = 2.5f;
    const int WallSegments = 20;
    const float DieSize = 0.48f;

    static readonly Vector3[] SpawnPoints =
    {
        new Vector3(-0.5f, 0.28f, -0.3f),
        new Vector3( 0.5f, 0.28f, -0.3f),
        new Vector3( 0.0f, 0.28f,  0.5f)
    };

    public static SicBoDome Create(Transform parent, Vector3 worldPosition)
    {
        var go = new GameObject("SicBoDome");
        go.transform.SetParent(parent, false);
        go.transform.position = worldPosition;
        var dome = go.AddComponent<SicBoDome>();
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
        var cylinder = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        cylinder.name = "DomeGlass";
        cylinder.transform.SetParent(transform, false);
        cylinder.transform.localPosition = new Vector3(0, DomeHeight / 2f, 0);
        cylinder.transform.localScale = new Vector3(DomeRadius * 2f, DomeHeight / 2f, DomeRadius * 2f);
        Destroy(cylinder.GetComponent<Collider>());
        cylinder.GetComponent<Renderer>().material = GlassMaterial();

        FloorVisual = new GameObject("DomeFloorVisual").transform;
        FloorVisual.SetParent(transform, false);
        var floor = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        floor.name = "DomeFloor";
        floor.transform.SetParent(FloorVisual, false);
        floor.transform.localPosition = new Vector3(0, 0.02f, 0);
        floor.transform.localScale = new Vector3(DomeRadius * 1.85f, 0.02f, DomeRadius * 1.85f);
        Destroy(floor.GetComponent<Collider>());
        var feltMat = new Material(FallbackShader()) { color = new Color(0.04f, 0.42f, 0.10f) };
        feltMat.SetFloat("_Glossiness", 0.05f);
        floor.GetComponent<Renderer>().material = feltMat;

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
            // Soft felt: each floor kick is its own toss instead of stacking bounce energy
            bounciness = 0.3f,
            dynamicFriction = 0.5f,
            staticFriction = 0.6f,
            frictionCombine = PhysicsMaterialCombine.Average,
            bounceCombine = PhysicsMaterialCombine.Average
        };

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
                Mathf.Cos(rad) * (DomeRadius - wallT * 0.5f));
            wallGO.transform.localRotation = Quaternion.Euler(0f, -angleDeg, 0f);
            var col = wallGO.AddComponent<BoxCollider>();
            col.size = new Vector3(segWidth, DomeHeight, wallT);
            col.material = wallMat;
        }

        // Floor top at y=0.04 to match the felt disc top
        var floorGO = new GameObject("DomeFloorCollider");
        floorGO.transform.SetParent(transform, false);
        floorGO.transform.localPosition = new Vector3(0f, -0.01f, 0f);
        var fc = floorGO.AddComponent<BoxCollider>();
        fc.size = new Vector3(DomeRadius * 2f, 0.1f, DomeRadius * 2f);
        fc.material = floorMat;
        FloorBody = floorGO.AddComponent<Rigidbody>();
        FloorBody.isKinematic = true;
        FloorBody.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;

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
        lightGO.transform.SetParent(FloorVisual, false);
        lightGO.transform.localPosition = new Vector3(0f, 0.05f, 0f);
        var light = lightGO.AddComponent<Light>();
        light.type = LightType.Point;
        light.color = new Color(0.08f, 0.88f, 0.18f);
        light.intensity = 3f;
        light.range = DomeRadius * 3.5f;
    }

    void BuildDice()
    {
        Dice = new Dice3D[SpawnPoints.Length];
        ShadowDice = new Dice3D[SpawnPoints.Length];
        // Livelier than the felt but calmer than the craps dice (0.8, Maximum), so repeated floor kicks don't pile up energy
        var diceMat = new PhysicsMaterial("SicBoDice")
        {
            bounciness = 0.5f,
            dynamicFriction = 0.4f,
            staticFriction = 0.5f,
            frictionCombine = PhysicsMaterialCombine.Average,
            bounceCombine = PhysicsMaterialCombine.Average
        };
        for (int i = 0; i < SpawnPoints.Length; i++)
        {
            Dice[i] = Dice3D.CreateInDome(transform, SpawnPoints[i], DomeRadius * 0.7f, size: DieSize);
            ShadowDice[i] = Dice3D.CreateInDome(transform, SpawnPoints[i], DomeRadius * 0.7f, size: DieSize);
            ShadowDice[i].GetComponent<BoxCollider>().material = diceMat;
            foreach (var r in ShadowDice[i].GetComponentsInChildren<Renderer>()) r.enabled = false;
        }

        // Shadow dice collide with the dome and each other, never with the visible dice
        foreach (var s in ShadowDice)
            foreach (var d in Dice)
                Physics.IgnoreCollision(s.GetComponent<BoxCollider>(), d.GetComponent<BoxCollider>(), true);
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
