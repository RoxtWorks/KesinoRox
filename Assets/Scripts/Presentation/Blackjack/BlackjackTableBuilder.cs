using UnityEngine;

// 3D set-dressing behind the 2D card UI — same role RouletteTableBuilder plays for
// the wheel scene (a real centerpiece the camera looks down on), just simpler since
// the actual game elements (cards, totals, buttons) are 2D canvas UI, not 3D meshes.
public class BlackjackTableBuilder : MonoBehaviour
{
    public float tableRadius = 6f;

    static readonly Color TableGreen = new Color(0.035f, 0.22f, 0.11f);
    static readonly Color WoodBrown = new Color(0.24f, 0.13f, 0.055f);
    static readonly Color Silver = new Color(0.72f, 0.78f, 0.86f);
    static readonly Color ShoeBlack = new Color(0.05f, 0.05f, 0.055f);

    public GameObject Build()
    {
        var root = new GameObject("BlackjackTableRig");

        var felt = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        felt.name = "FeltTable";
        felt.transform.SetParent(root.transform);
        felt.transform.localPosition = new Vector3(0, -0.75f, 0);
        felt.transform.localScale = new Vector3(tableRadius * 2.6f, 0.05f, tableRadius * 2.6f);
        Object.Destroy(felt.GetComponent<Collider>());
        Paint(felt, TableGreen, metallic: 0f, smoothness: 0.15f);

        var rail = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        rail.name = "WoodRail";
        rail.transform.SetParent(root.transform);
        rail.transform.localPosition = new Vector3(0, -0.5f, 0);
        rail.transform.localScale = new Vector3(tableRadius * 2.15f, 0.2f, tableRadius * 2.15f);
        Object.Destroy(rail.GetComponent<Collider>());
        Paint(rail, WoodBrown, metallic: 0.1f, smoothness: 0.45f);

        var trim = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        trim.name = "RailTrim";
        trim.transform.SetParent(root.transform);
        trim.transform.localPosition = new Vector3(0, -0.41f, 0);
        trim.transform.localScale = new Vector3(tableRadius * 2.02f, 0.015f, tableRadius * 2.02f);
        Object.Destroy(trim.GetComponent<Collider>());
        Paint(trim, Silver, metallic: 0.85f, smoothness: 0.75f);

        // Small decorative shoe box off to one side — purely cosmetic, the real
        // Shoe/card dealing is all Core logic + 2D CardUI, this just visually
        // acknowledges "cards come from somewhere."
        var shoeBox = GameObject.CreatePrimitive(PrimitiveType.Cube);
        shoeBox.name = "DealingShoe";
        shoeBox.transform.SetParent(root.transform);
        shoeBox.transform.localPosition = new Vector3(tableRadius * 0.55f, -0.35f, tableRadius * 0.55f);
        shoeBox.transform.localScale = new Vector3(1.4f, 0.9f, 0.8f);
        shoeBox.transform.localRotation = Quaternion.Euler(0f, -30f, 0f);
        Object.Destroy(shoeBox.GetComponent<Collider>());
        Paint(shoeBox, ShoeBlack, metallic: 0.2f, smoothness: 0.5f);

        return root;
    }

    static Shader fallbackShader;

    // Same build-stripping guard RouletteTableBuilder uses — Shader.Find("Standard")
    // can silently return null in a standalone build unless something serialized
    // references it, which would crash Material's constructor before Start() even
    // reaches camera/UI setup.
    static Shader FallbackShader()
    {
        if (fallbackShader != null) return fallbackShader;
        fallbackShader = Shader.Find("Standard")
            ?? Shader.Find("Universal Render Pipeline/Lit")
            ?? Shader.Find("Sprites/Default");
        return fallbackShader;
    }

    static void Paint(GameObject go, Color color, float metallic = 0f, float smoothness = 0.3f)
    {
        var renderer = go.GetComponent<Renderer>();
        if (renderer == null) return;
        var mat = new Material(FallbackShader());
        mat.color = color;
        mat.SetFloat("_Metallic", metallic);
        mat.SetFloat("_Glossiness", smoothness);
        renderer.material = mat;
    }
}
