using UnityEngine;
using UnityEngine.UI;

// Shared backdrop for every scene: pixel-art grey stone floor tiles, each with a die on it,
// drifting slowly on a diagonal. Drawn by its own camera behind everything (3D tables, dice and
// all the overlay UI stay on top) — the scene's main camera just stops clearing to a flat colour.
public class AnimatedBackground : MonoBehaviour
{
    const int Layer = 31;            // only the background camera sees this layer
    const float TileSize = 150f;     // canvas units per tile at the 1920x1080 reference size
    const float Speed = 14f;         // canvas units per second, along both axes
    const int TilePixels = 30;       // art pixels per tile (5 screen pixels each at 1080p); texture holds 2x2 tiles

    static Texture2D tileTexture;

    RawImage image;
    RectTransform imageRt;
    Vector2 offset;

    public static void Attach(Camera mainCamera)
    {
        mainCamera.clearFlags = CameraClearFlags.Depth;
        mainCamera.cullingMask &= ~(1 << Layer);
        new GameObject("AnimatedBackground").AddComponent<AnimatedBackground>().Build();
    }

    void Build()
    {
        var camGO = new GameObject("BackgroundCamera");
        camGO.transform.SetParent(transform, false);
        var cam = camGO.AddComponent<Camera>();
        cam.depth = -100;
        cam.clearFlags = CameraClearFlags.SolidColor;
        cam.backgroundColor = new Color32(18, 18, 17, 255);
        cam.cullingMask = 1 << Layer;
        cam.orthographic = true;

        var canvasGO = new GameObject("BackgroundCanvas") { layer = Layer };
        canvasGO.transform.SetParent(transform, false);
        var canvas = canvasGO.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceCamera;
        canvas.worldCamera = cam;
        canvas.planeDistance = 1f;
        var scaler = canvasGO.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920, 1080);
        scaler.matchWidthOrHeight = 0.5f;

        var imageGO = new GameObject("Tiles") { layer = Layer };
        imageGO.transform.SetParent(canvasGO.transform, false);
        image = imageGO.AddComponent<RawImage>();
        image.texture = TileTexture();
        image.raycastTarget = false;
        imageRt = image.rectTransform;
        imageRt.anchorMin = Vector2.zero;
        imageRt.anchorMax = Vector2.one;
        imageRt.offsetMin = imageRt.offsetMax = Vector2.zero;
    }

    void Update()
    {
        if (image == null) return;
        float texUnits = TileSize * 2f; // one texture = 2x2 tiles
        offset += new Vector2(Speed, Speed) * Time.unscaledDeltaTime / texUnits;
        offset.x = Mathf.Repeat(offset.x, 1f);
        offset.y = Mathf.Repeat(offset.y, 1f);
        Vector2 size = imageRt.rect.size;
        image.uvRect = new Rect(offset.x, offset.y, size.x / texUnits, size.y / texUnits);
    }

    // Pixel art, 2x2 tiles of 30x30 pixels, point-filtered so every art pixel stays a crisp block.
    // Each tile: a deep groove, bevelled stone panel, and a faint die emblem pressed into it.
    static Texture2D TileTexture()
    {
        if (tileTexture != null) return tileTexture;
        int size = TilePixels * 2;
        tileTexture = new Texture2D(size, size, TextureFormat.RGB24, false)
        {
            wrapMode = TextureWrapMode.Repeat,
            filterMode = FilterMode.Point,
        };
        int[] faces = { 5, 2, 3, 6 };
        var px = new Color32[size * size];
        for (int y = 0; y < size; y++)
        for (int x = 0; x < size; x++)
        {
            int tx = x / TilePixels, ty = y / TilePixels;
            // Art is authored top-down; texture rows run bottom-up
            px[y * size + x] = TilePixel(x % TilePixels, TilePixels - 1 - y % TilePixels, faces[ty * 2 + tx], tx * 31 + ty * 17);
        }
        tileTexture.SetPixels32(px);
        tileTexture.Apply(false);
        return tileTexture;
    }

    static readonly Color32 GroutPx = new Color32(18, 18, 17, 255);
    static readonly Color32 StonePx = new Color32(36, 36, 33, 255);
    static readonly Color32 StoneLightPx = new Color32(50, 50, 46, 255);
    static readonly Color32 StoneDarkPx = new Color32(27, 27, 25, 255);
    static readonly Color32 DiePx = new Color32(41, 41, 38, 255);
    static readonly Color32 DieLightPx = new Color32(46, 46, 42, 255);
    static readonly Color32 DieDarkPx = new Color32(33, 33, 30, 255);
    static readonly Color32 PipPx = new Color32(31, 31, 29, 255);

    // x, y: 0..29 within the tile, y = 0 at the top
    static Color32 TilePixel(int x, int y, int face, int seed)
    {
        const int n = TilePixels;
        // Deep 2px groove between tiles, then a bevel: lit edge top-left, shaded edge bottom-right
        if (x <= 1 || y <= 1) return GroutPx;

        // Die: 14x14 with clipped corners, lit from the top-left
        const int d0 = 8, d1 = 21;
        bool inDie = x >= d0 && x <= d1 && y >= d0 && y <= d1
                     && !((x == d0 || x == d1) && (y == d0 || y == d1));
        if (inDie)
        {
            foreach (var (px, py) in Pips(face))
                if ((x == px || x == px + 1) && (y == py || y == py + 1)) return PipPx;
            if (y == d0 || x == d0) return DieLightPx;
            if (y == d1 || x == d1) return DieDarkPx;
            return DiePx;
        }
        // Drop shadow under the die, one pixel down-right
        if (x >= d0 + 1 && x <= d1 + 1 && y >= d0 + 1 && y <= d1 + 1) return new Color32(32, 32, 29, 255);

        // Stone bevel and a sprinkle of lighter / darker specks
        if (y == 2 || x == 2) return StoneLightPx;
        if (y >= n - 2 || x >= n - 2) return StoneDarkPx;
        int h = (x * 73856093) ^ (y * 19349663) ^ (seed * 83492791);
        h = (h >> 5) & 31;
        if (h == 0) return new Color32(40, 40, 37, 255);
        if (h == 1) return new Color32(32, 32, 29, 255);
        return StonePx;
    }

    // Top-left pixel of each 2x2 pip on the 14x14 die (die spans 8..21)
    static (int, int)[] Pips(int face)
    {
        const int a = 10, m = 14, b = 18;
        switch (face)
        {
            case 1: return new[] { (m, m) };
            case 2: return new[] { (a, a), (b, b) };
            case 3: return new[] { (a, a), (m, m), (b, b) };
            case 4: return new[] { (a, a), (b, a), (a, b), (b, b) };
            case 5: return new[] { (a, a), (b, a), (m, m), (a, b), (b, b) };
            default: return new[] { (a, a), (b, a), (a, m), (b, m), (a, b), (b, b) };
        }
    }
}
