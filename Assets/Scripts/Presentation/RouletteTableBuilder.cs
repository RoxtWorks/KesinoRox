using System.Collections.Generic;
using UnityEngine;

// Builds the table, wheel and ball purely as visuals out of primitives at runtime —
// no Rigidbody, no colliders, no physics anywhere. The spin outcome comes from
// SpinResultGenerator (Core); this only has to look right. Swap primitives for
// real art later without touching any game logic.
public class RouletteTableBuilder : MonoBehaviour
{
    public float wheelRadius = 4.5f;
    public float bowlRadius = 5.2f;

    // WheelDisc is a default Unity cylinder (local bounds y: -1..+1) scaled 0.3 on Y,
    // so its actual top surface sits at local y=0.3, not 0.15 as a naive half-of-scale
    // guess would suggest. Anything meant to be visible on top of the disc (pocket
    // floors, hub, number labels) must sit above this value.
    const float DiscTopY = 0.3f;

    [HideInInspector] public Transform wheelPivot;
    [HideInInspector] public Transform ballTransform;
    [HideInInspector] public Renderer markerRenderer;

    // Lets the betting UI show, right on the wheel, which numbers currently have a
    // bet on them — otherwise the only place that's visible is the felt, which gets
    // hidden for the spin reveal.
    readonly Dictionary<int, Renderer> pocketFloorRenderers = new Dictionary<int, Renderer>();
    readonly Dictionary<int, Color> pocketBaseColors = new Dictionary<int, Color>();
    static readonly Color HighlightBlue = new Color(0.25f, 0.55f, 1f);

    // World angle (degrees) the wheel always rotates the winning pocket to — a static
    // post here marks where to look for the result, and WheelSpinAnimator's target
    // rotation math must agree with this exact value. 90 puts it at the top of the
    // wheel (furthest from camera) instead of the bottom, so it's visible in the same
    // glance as the belt sitting above the wheel, instead of needing to look down.
    public const float ResultMarkerAngleDeg = 90f;

    static readonly Color TableGreen = new Color(0.035f, 0.22f, 0.11f);
    static readonly Color WoodBrown = new Color(0.24f, 0.13f, 0.055f);
    static readonly Color WheelBlack = new Color(0.05f, 0.05f, 0.055f);
    static readonly Color Gold = new Color(0.85f, 0.68f, 0.24f);
    static readonly Color PocketRed = new Color(0.62f, 0.04f, 0.04f);
    static readonly Color PocketBlack = new Color(0.04f, 0.04f, 0.045f);
    static readonly Color PocketGreen = new Color(0.04f, 0.42f, 0.13f);
    static readonly Color BallSilver = new Color(0.92f, 0.92f, 0.95f);

    public GameObject Build()
    {
        var root = new GameObject("RouletteRig");

        var table = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        table.name = "TablePlate";
        table.transform.SetParent(root.transform);
        table.transform.localPosition = new Vector3(0, -0.75f, 0);
        table.transform.localScale = new Vector3(bowlRadius * 2.6f, 0.05f, bowlRadius * 2.6f);
        Object.Destroy(table.GetComponent<Collider>());
        Paint(table, TableGreen, metallic: 0f, smoothness: 0.15f);

        var bowl = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        bowl.name = "Bowl";
        bowl.transform.SetParent(root.transform);
        bowl.transform.localPosition = new Vector3(0, -0.5f, 0);
        bowl.transform.localScale = new Vector3(bowlRadius * 2f, 0.2f, bowlRadius * 2f);
        Object.Destroy(bowl.GetComponent<Collider>());
        Paint(bowl, WoodBrown, metallic: 0.1f, smoothness: 0.45f);

        // Thin gold trim ring where the bowl meets the felt — cheap detail that reads
        // as "finished edge" instead of a bare cylinder seam.
        var trim = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        trim.name = "BowlTrim";
        trim.transform.SetParent(root.transform);
        trim.transform.localPosition = new Vector3(0, -0.41f, 0);
        trim.transform.localScale = new Vector3(bowlRadius * 2.02f, 0.015f, bowlRadius * 2.02f);
        Object.Destroy(trim.GetComponent<Collider>());
        Paint(trim, Gold, metallic: 0.85f, smoothness: 0.75f);

        var wheelGO = new GameObject("Wheel");
        wheelGO.transform.SetParent(root.transform);
        wheelGO.transform.localPosition = Vector3.zero;
        var wheelDisc = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        wheelDisc.name = "WheelDisc";
        wheelDisc.transform.SetParent(wheelGO.transform);
        wheelDisc.transform.localPosition = Vector3.zero;
        wheelDisc.transform.localScale = new Vector3(wheelRadius * 2f, 0.3f, wheelRadius * 2f);
        Object.Destroy(wheelDisc.GetComponent<Collider>());
        Paint(wheelDisc, WheelBlack, metallic: 0.4f, smoothness: 0.6f);
        wheelPivot = wheelGO.transform;

        // Center hub cap — small decorative detail so the wheel doesn't look like a
        // flat blank disc at its axle point.
        var hub = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        hub.name = "Hub";
        hub.transform.SetParent(wheelGO.transform);
        hub.transform.localPosition = Vector3.up * DiscTopY;
        hub.transform.localScale = new Vector3(wheelRadius * 0.18f, 0.05f, wheelRadius * 0.18f);
        Object.Destroy(hub.GetComponent<Collider>());
        Paint(hub, Gold, metallic: 0.85f, smoothness: 0.8f);

        int pockets = WheelLayout.PocketCount;
        float pocketAngle = Mathf.PI * 2f / pockets;

        // Dividers sit at the BOUNDARY between two pockets, not through the center of
        // one — previously both used the same angle, so every divider bisected its own
        // pocket and visually cut through the number sitting there. Offset by half a
        // pocket so each divider sits between two numbers, like a real wheel's frets.
        for (int i = 0; i < pockets; i++)
        {
            float boundaryAngle = (i + 0.5f) * pocketAngle;
            var divider = GameObject.CreatePrimitive(PrimitiveType.Cube);
            divider.name = $"PocketDivider_{i}";
            divider.transform.SetParent(wheelGO.transform);
            Vector3 boundaryPos = new Vector3(Mathf.Cos(boundaryAngle), 0f, Mathf.Sin(boundaryAngle)) * (wheelRadius * 0.75f);
            divider.transform.localPosition = boundaryPos + Vector3.up * 0.25f;
            divider.transform.localScale = new Vector3(0.08f, 0.3f, wheelRadius * 0.28f);
            divider.transform.LookAt(wheelGO.transform.position + Vector3.up * 0.25f);
            Object.Destroy(divider.GetComponent<Collider>());
            Paint(divider, Gold, metallic: 0.85f, smoothness: 0.75f);
        }

        for (int i = 0; i < pockets; i++)
        {
            float a = i * pocketAngle;
            Vector3 pos = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * (wheelRadius * 0.75f);

            // Colour the pocket floor segment matching the real wheel's colour for that number.
            int number = WheelLayout.PocketOrder[i];
            var pocketFloor = GameObject.CreatePrimitive(PrimitiveType.Cube);
            pocketFloor.name = $"PocketFloor_{i}";
            pocketFloor.transform.SetParent(wheelGO.transform);
            // Must sit ABOVE the disc's own top surface (DiscTopY) or it's buried
            // inside the solid disc mesh and never visible at all — was at y=0.16
            // against a disc top of y=0.3, so every pocket floor was completely
            // hidden regardless of its color, red/black/green alike.
            pocketFloor.transform.localPosition = pos + Vector3.up * (DiscTopY + 0.01f);
            // LookAt below turns local X into the TANGENTIAL (along-the-circle) axis
            // and local Z into the RADIAL (toward-center) axis. The old code used the
            // same wheelRadius*0.28 for both — tangentially that's more than double
            // the actual arc spacing between pockets (radius*pocketAngle), so every
            // tile massively overlapped its neighbours on both sides. Invisible while
            // it was buried under the disc; exposed once that was fixed. Tangential
            // width now derived from the real arc spacing, radial depth left as-is.
            float tangentialWidth = wheelRadius * 0.75f * pocketAngle * 0.82f;
            pocketFloor.transform.localScale = new Vector3(tangentialWidth, 0.02f, wheelRadius * 0.28f);
            pocketFloor.transform.LookAt(wheelGO.transform.position + Vector3.up * (DiscTopY + 0.01f));
            Object.Destroy(pocketFloor.GetComponent<Collider>());
            Color pocketColor = number == 0 ? PocketGreen : (WheelLayout.IsRed(number) ? PocketRed : PocketBlack);
            // Black pockets are meant to read as black (matches a real wheel) so they
            // get no emissive boost; red and green need it to not crush to the same
            // near-black as their neighbours under this scene's dim lighting.
            Color? emission = number == 0 ? PocketGreen * 0.6f : WheelLayout.IsRed(number) ? PocketRed * 0.9f : (Color?)null;
            Paint(pocketFloor, pocketColor, metallic: 0.1f, smoothness: 0.35f, emission: emission);
            pocketFloorRenderers[number] = pocketFloor.GetComponent<Renderer>();
            pocketBaseColors[number] = pocketColor;

            // Number label, so the wheel's actual pocket order is visibly verifiable
            // instead of just trusted — sits just above the pocket floor, rotated flat
            // so it reads from the overhead camera, and spins with the wheel like real
            // wheel numbering does.
            var labelGO = new GameObject($"NumberLabel_{i}");
            labelGO.transform.SetParent(wheelGO.transform);
            labelGO.transform.localPosition = pos + Vector3.up * (DiscTopY + 0.03f);
            labelGO.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            var tm = labelGO.AddComponent<TextMesh>();
            tm.text = number.ToString();
            tm.characterSize = 0.045f;
            tm.fontSize = 64;
            tm.anchor = TextAnchor.MiddleCenter;
            tm.alignment = TextAlignment.Center;
            tm.color = Color.white;
            tm.fontStyle = FontStyle.Bold;
            // Sits right next to the tall gold dividers — without disabling shadows
            // it reads as dim/grey on whichever side is away from the key light.
            var labelRenderer = labelGO.GetComponent<MeshRenderer>();
            labelRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            labelRenderer.receiveShadows = false;
        }

        var ballGO = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        ballGO.name = "Ball";
        ballGO.transform.SetParent(root.transform);
        ballGO.transform.localScale = Vector3.one * 0.3f;
        Object.Destroy(ballGO.GetComponent<Collider>());
        ballTransform = ballGO.transform;
        Paint(ballGO, BallSilver, metallic: 0.9f, smoothness: 0.9f);

        // Static result marker — a needle from the hub pointing outward at a fixed
        // angle (parented to root, not wheelGO, so it never rotates with the wheel).
        // The wheel always rotates the winning pocket to line up under the tip, so
        // this is always "where to look." Used to be a post sitting past the rim
        // pointing inward; putting it at the center instead fills the wheel's empty
        // middle and reads more like a compass needle than a goalpost. Its color
        // flips to match the winning pocket (red/black/green) once a spin lands,
        // tying the wheel, marker and belt together.
        float markerAngleRad = ResultMarkerAngleDeg * Mathf.Deg2Rad;
        Vector3 markerDir = new Vector3(Mathf.Cos(markerAngleRad), 0f, Mathf.Sin(markerAngleRad));
        float needleLength = wheelRadius * 0.65f;
        var markerGO = GameObject.CreatePrimitive(PrimitiveType.Cube);
        markerGO.name = "ResultMarker";
        markerGO.transform.SetParent(root.transform);
        markerGO.transform.localPosition = markerDir * (needleLength / 2f) + Vector3.up * 0.45f;
        markerGO.transform.localScale = new Vector3(0.16f, 0.3f, needleLength);
        markerGO.transform.LookAt(root.transform.position + markerDir * (needleLength + 1f) + Vector3.up * 0.45f);
        Object.Destroy(markerGO.GetComponent<Collider>());
        Paint(markerGO, Gold, metallic: 0.85f, smoothness: 0.8f);
        markerRenderer = markerGO.GetComponent<Renderer>();

        return root;
    }

    // Tints matching pocket floors blue (blended, not replaced — red/black/green
    // identity still has to read) so the player can spot their number(s) live on the
    // wheel during the spin instead of only in the bet tray. Pass null/empty to
    // clear back to normal.
    public void SetHighlightedNumbers(HashSet<int> numbers)
    {
        foreach (var kv in pocketFloorRenderers)
        {
            Color baseColor = pocketBaseColors[kv.Key];
            bool hi = numbers != null && numbers.Contains(kv.Key);
            kv.Value.material.color = hi ? Color.Lerp(baseColor, HighlightBlue, 0.55f) : baseColor;
        }
    }

    static Shader fallbackShader;

    // Shader.Find("Standard") relies on that shader surviving build-time stripping —
    // fine in the Editor (every built-in shader is loaded there) but in a standalone
    // build, a shader only ships if something serialized references it or it's in
    // Graphics Settings' "Always Included Shaders" list. Nothing here is a serialized
    // asset (everything's built at runtime), so without that list entry Shader.Find
    // silently returns null, the Material constructor throws, and GameManager.Start()
    // dies before it creates the camera or UI — the entire game was a black screen in
    // any build. Falling back to a guaranteed-present shader closes that hole even if
    // the Graphics Settings entry (the real fix, already applied) ever gets removed.
    // Must be lazy, not a field initializer — Unity forbids calling Shader.Find from
    // a MonoBehaviour's static constructor, only from Awake/Start onward.
    static Shader FallbackShader()
    {
        if (fallbackShader != null) return fallbackShader;
        fallbackShader = Shader.Find("Standard")
            ?? Shader.Find("Universal Render Pipeline/Lit")
            ?? Shader.Find("Sprites/Default");
        return fallbackShader;
    }

    static void Paint(GameObject go, Color color, float metallic = 0f, float smoothness = 0.3f, Color? emission = null)
    {
        var renderer = go.GetComponent<Renderer>();
        if (renderer == null) return;
        var mat = new Material(FallbackShader());
        mat.color = color;
        mat.SetFloat("_Metallic", metallic);
        mat.SetFloat("_Glossiness", smoothness);
        // Purely-lit dark colors (the pocket floors especially — a "dark red" at
        // (0.62,0.04,0.04) diffuse) get crushed to indistinguishable near-black by
        // this scene's deliberately dim casino lighting. A bit of self-emission keeps
        // the true hue visible regardless of light angle/intensity, the way real
        // wheel paint reads as red/black even in a dim room.
        if (emission.HasValue)
        {
            mat.EnableKeyword("_EMISSION");
            mat.SetColor("_EmissionColor", emission.Value);
        }
        renderer.material = mat;
    }
}
