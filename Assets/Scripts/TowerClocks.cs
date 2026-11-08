using UnityEngine;

// Live hands for the clock faces painted into one tiled background layer (the
// painted hands were erased by tools/make_clockfaces.py). Draws the hands into
// a transparent texture as big as the background picture and tiles it the
// same way, so every repeat of the picture shows the time. Sits on a child of
// the layer's background, so it scrolls with its parallax. LaneSky.Apply
// passes the hour and the look blend; the hand colour follows the look.
[ExecuteAlways]
[RequireComponent(typeof(SpriteRenderer))]
public class TowerClocks : MonoBehaviour
{
    [System.Serializable]
    public struct Face
    {
        [Tooltip("Centre in pixels of the background picture, from its bottom-left pixel.")]
        public Vector2 centre;
        public float radius;
    }

    [Tooltip("The background whose picture, size and tiling the hands follow.")]
    public SpriteRenderer background;
    public Face[] faces;
    public Color dayHands = Color.black, duskHands = Color.black, nightHands = Color.black;
    [Tooltip("Only cover the faces with an untiled texture (a background section) instead of the whole tiled picture.")]
    public bool crop;

    SpriteRenderer sr;
    Texture2D tex;
    Color32[] pixels;
    float hour = -1f;
    int drawn = -1;
    int ox, oy;   // picture pixel of the texture's bottom-left

    void OnEnable() => Init();

    void Init()
    {
        sr = GetComponent<SpriteRenderer>();
        if (tex != null || background == null || background.sprite == null) return;
        var src = background.sprite;
        var rect = src.rect;
        int w = (int)rect.width, h = (int)rect.height;
        ox = oy = 0;
        if (crop && faces != null && faces.Length > 0)
        {
            float x0 = float.MaxValue, y0 = float.MaxValue, x1 = float.MinValue, y1 = float.MinValue;
            foreach (var f in faces)
            {
                x0 = Mathf.Min(x0, f.centre.x - f.radius); x1 = Mathf.Max(x1, f.centre.x + f.radius);
                y0 = Mathf.Min(y0, f.centre.y - f.radius); y1 = Mathf.Max(y1, f.centre.y + f.radius);
            }
            ox = Mathf.FloorToInt(x0) - 2;
            oy = Mathf.FloorToInt(y0) - 2;
            w = Mathf.CeilToInt(x1) + 3 - ox;
            h = Mathf.CeilToInt(y1) + 3 - oy;
        }
        tex = new Texture2D(w, h, TextureFormat.RGBA32, false)
        {
            filterMode = FilterMode.Point,
            wrapMode = TextureWrapMode.Clamp,
            hideFlags = HideFlags.HideAndDontSave,
        };
        pixels = new Color32[tex.width * tex.height];
        var sprite = Sprite.Create(tex, new Rect(0, 0, tex.width, tex.height), (src.pivot - new Vector2(ox, oy)) / new Vector2(w, h),
            src.pixelsPerUnit, 0, SpriteMeshType.FullRect);
        sprite.hideFlags = HideFlags.HideAndDontSave;
        sr.sprite = sprite;
        if (crop) sr.drawMode = SpriteDrawMode.Simple;
        else
        {
            sr.drawMode = SpriteDrawMode.Tiled;
            sr.size = background.size;
        }
        drawn = -1;
        if (hour >= 0f) Draw();
    }

    void OnDisable()
    {
        if (sr != null && sr.sprite != null && sr.sprite.texture == tex)
        {
            DestroyImmediate(sr.sprite);
            sr.sprite = null;
        }
        if (tex != null) DestroyImmediate(tex);
        tex = null;
    }

    public void Show(float hour, LaneSky.Look from, LaneSky.Look to, float t)
    {
        this.hour = Mathf.Repeat(hour, 24f);
        if (isActiveAndEnabled && tex == null) Init();
        if (sr != null) sr.color = Color.Lerp(Hands(from), Hands(to), t);
        Draw();
    }

    Color Hands(LaneSky.Look look) =>
        look == LaneSky.Look.Day ? dayHands : look == LaneSky.Look.Dusk ? duskHands : nightHands;

    // Redraws only when the clock shows a different minute.
    void Draw()
    {
        if (tex == null || faces == null) return;
        int minute = Mathf.FloorToInt(hour * 60f) % (24 * 60);
        if (minute == drawn) return;
        drawn = minute;

        System.Array.Clear(pixels, 0, pixels.Length);
        float minuteAngle = (minute % 60) / 60f * 2f * Mathf.PI;
        float hourAngle = (minute % 720) / 720f * 2f * Mathf.PI;
        foreach (var f in faces)
        {
            bool big = f.radius > 10f;
            // Hour hand short and (on big faces) thick, minute hand long and thin.
            Hand(f.centre, hourAngle, f.radius * (big ? 0.5f : 0.55f), big ? 1f : 0.5f);
            Hand(f.centre, minuteAngle, big ? f.radius * 0.78f : f.radius - 1.5f, 0.5f);
            Disc(f.centre, big ? 1.5f : 0.5f);
        }
        tex.SetPixels32(pixels);
        tex.Apply(false);
    }

    // Angle clockwise from 12 o'clock; fills the pixels whose centres lie within
    // halfWidth of the segment from the centre to the tip.
    void Hand(Vector2 c, float angle, float length, float halfWidth)
    {
        var tip = c + new Vector2(Mathf.Sin(angle), Mathf.Cos(angle)) * length;
        int x0 = Mathf.FloorToInt(Mathf.Min(c.x, tip.x) - halfWidth), x1 = Mathf.CeilToInt(Mathf.Max(c.x, tip.x) + halfWidth);
        int y0 = Mathf.FloorToInt(Mathf.Min(c.y, tip.y) - halfWidth), y1 = Mathf.CeilToInt(Mathf.Max(c.y, tip.y) + halfWidth);
        var d = tip - c;
        for (int y = y0; y <= y1; y++)
            for (int x = x0; x <= x1; x++)
            {
                var p = new Vector2(x, y);
                float s = Mathf.Clamp01(Vector2.Dot(p - c, d) / d.sqrMagnitude);
                if ((c + d * s - p).magnitude <= halfWidth) Set(x, y);
            }
    }

    void Disc(Vector2 c, float r)
    {
        for (int y = Mathf.FloorToInt(c.y - r); y <= Mathf.CeilToInt(c.y + r); y++)
            for (int x = Mathf.FloorToInt(c.x - r); x <= Mathf.CeilToInt(c.x + r); x++)
                if ((new Vector2(x, y) - c).magnitude <= r) Set(x, y);
    }

    void Set(int x, int y)
    {
        x -= ox;
        y -= oy;
        if (x < 0 || y < 0 || x >= tex.width || y >= tex.height) return;
        pixels[y * tex.width + x] = new Color32(255, 255, 255, 255);
    }
}
