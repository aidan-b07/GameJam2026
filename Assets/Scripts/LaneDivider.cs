using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

// The Unraveling rift drawn across the screen between Tick's lane (top) and
// Tock's (bottom). Lives on a screen-space overlay canvas, so it stays on the
// seam between the two camera viewports. One canvas unit is one art pixel.
//
// The rift is pixel art drawn into a texture at a low frame rate: stepped
// 4 px edge columns, a dark outline and lit rim, a checker-dither glow,
// twinkling sparkles inside and 2x2 chunks that break off the rim. Its height
// follows the difference between the two lanes' world speeds (GameManager):
// wide when one player races and the other stands still, thin when they move
// alike. Chunks fly outwards while it grows and get pulled back in while it
// shrinks.
//
// The edges belong to the level: the top edge's shape is fixed to Tick's lane
// in world space and the bottom edge's to Tock's, so they scroll with the
// tiles as the cameras move. Between animation frames the texture is slid by
// the camera's pixel offset (it is drawn with a margin on both sides), so the
// edges keep up with the level at full frame rate. The shape only morphs
// slowly in place.
[ExecuteAlways]
[DefaultExecutionOrder(-30)]   // LateUpdate after CoopCamera (-50) moves the cameras
public class LaneDivider : MonoBehaviour
{
    public RawImage rift;
    [Tooltip("Camera of the top lane (Tick); the top edge scrolls with it.")]
    public Transform topCamera;
    [Tooltip("Camera of the bottom lane (Tock); the bottom edge scrolls with it.")]
    public Transform bottomCamera;

    [Tooltip("Half height of the rift in art pixels with no speed difference (the old static crack was 7, 3-11 px each side).")]
    public float minHalfHeight = 9f;
    [Tooltip("Half height the rift approaches at a large speed difference.")]
    public float maxHalfHeight = 28f;
    [Tooltip("Speed difference (world speed units) that reaches about 63% of the way to the max.")]
    public float gapScale = 1.5f;
    [Tooltip("Animation frames per second (real time), stepped like the sprites.")]
    public float fps = 8f;
    [Tooltip("Fraction of the way to the target height covered each animation frame.")]
    [Range(0.01f, 1f)] public float ease = 0.06f;
    [Tooltip("Seconds for the edge teeth to morph into a new shape.")]
    public float morphTime = 5f;
    [Tooltip("Speed difference used outside play mode (and when there is no GameManager), for previewing.")]
    [Range(0f, 5f)] public float previewGap = 0f;

    public const int TextureHeight = 256;   // room for the max height plus drifting chunks
    const float PixelsPerUnit = 32f;         // art pixels per world unit (32 PPU, 1 canvas unit = 1 px)
    const int Margin = 64;                   // extra pixels drawn each side, for sliding between frames
    const int Col = 4;                       // edge column width in pixels
    const int SparklePeriod = 1024;          // sparkles repeat every this many world pixels
    const int Sparkles = 70;                 // per period

    static readonly Color32 Clear = new Color32(0, 0, 0, 0);
    static readonly Color32 Outline = Hex(0x22101e);
    static readonly Color32 Deep = Hex(0x100818);
    static readonly Color32 Void = Hex(0x28143a);
    static readonly Color32 Mid = Hex(0x482668);
    static readonly Color32 Edge = Hex(0x8040a8);
    static readonly Color32 Highlight = Hex(0xc478e0);
    static readonly Color32 Spark = Hex(0xf4e0ff);
    static readonly Color32 Gold = Hex(0xf2c46b);

    // x is in top-lane world pixels.
    struct Chunk { public float x, y, vx, vy; public int age, life; }

    Texture2D tex;
    Color32[] pixels;
    int[] top, bottom;
    readonly List<Chunk> chunks = new List<Chunk>();
    readonly System.Random rng = new System.Random(7);
    float half, clock;
    int frame;
    bool started;
    int drawnCameraPx;   // top camera position (world px) the texture was drawn for

    void OnDisable()
    {
        if (tex != null) DestroyImmediate(tex);
        tex = null;
    }

    void LateUpdate()
    {
        if (rift == null) return;
        float viewWidth = rift.rectTransform.rect.width;
        int width = Mathf.Max(Col, Mathf.CeilToInt(viewWidth)) + 2 * Margin;
        bool redraw = EnsureTexture(width);

        float target = TargetHalfHeight();
        if (!started) { half = target; started = true; }

        int cameraPx = CameraPx(topCamera);
        if (Mathf.Abs(cameraPx - drawnCameraPx) > Margin - 2) redraw = true;

        clock += Application.isPlaying ? Time.unscaledDeltaTime : 0f;
        float step = 1f / Mathf.Max(1f, fps);
        if (clock >= step || redraw || !Application.isPlaying)
        {
            bool tick = Application.isPlaying && clock >= step;
            clock = Application.isPlaying ? clock % step : 0f;
            float growth = 0f;
            if (tick)
            {
                frame++;
                float before = half;
                half += (target - half) * ease;
                growth = half - before;
            }
            else if (!Application.isPlaying) half = target;
            Draw(cameraPx, viewWidth, growth, tick);
        }

        float slide = cameraPx - drawnCameraPx;
        rift.uvRect = new Rect((Margin + slide) / tex.width, 0f, viewWidth / tex.width, 1f);
    }

    static int CameraPx(Transform cam) => cam == null ? 0 : Mathf.RoundToInt(cam.position.x * PixelsPerUnit);

    float TargetHalfHeight()
    {
        float gap = previewGap;
        if (Application.isPlaying && GameManager.INSTANCE != null)
            gap = Mathf.Abs(GameManager.INSTANCE.GetWorldSpeed(0) - GameManager.INSTANCE.GetWorldSpeed(1));
        float t = 1f - Mathf.Exp(-gap / Mathf.Max(0.01f, gapScale));
        return minHalfHeight + (maxHalfHeight - minHalfHeight) * t;
    }

    bool EnsureTexture(int width)
    {
        if (tex != null && tex.width == width) return false;
        if (tex != null) DestroyImmediate(tex);
        tex = new Texture2D(width, TextureHeight, TextureFormat.RGBA32, false)
        {
            filterMode = FilterMode.Point,
            wrapMode = TextureWrapMode.Clamp,
            hideFlags = HideFlags.DontSave,
            name = "Rift",
        };
        pixels = new Color32[width * TextureHeight];
        top = new int[width];
        bottom = new int[width];
        rift.texture = tex;
        return true;
    }

    // Edge extent (px from the seam) of the world column containing world pixel wx.
    // The broad shape and the teeth each cross-fade between fixed noise fields,
    // so the edge reshapes in place without sliding along the level.
    float Extent(int wx, float seconds, int seed)
    {
        int col = Mathf.FloorToInt(wx / (float)Col);
        float broad = Morph(col * Col / 120f, seconds / (morphTime * 2.5f), seed);
        float teeth = Morph(col * Col / 24f, seconds / morphTime, seed + 50);
        float shape = 0.65f * broad + 0.35f * teeth;
        return Mathf.Round(half * (0.5f + shape) / 2f) * 2f;
    }

    static float Morph(float x, float phase, int seed)
    {
        int k = Mathf.FloorToInt(phase);
        float f = phase - k, s = f * f * (3f - 2f * f);
        return Mathf.Lerp(Noise(x, seed + k * 977), Noise(x, seed + (k + 1) * 977), s);
    }

    void Draw(int cameraPx, float viewWidth, float growth, bool tick)
    {
        drawnCameraPx = cameraPx;
        int w = tex.width, mid = TextureHeight / 2;
        int bottomShift = CameraPx(bottomCamera) - cameraPx;
        // World pixel (top lane) of texture column 0.
        int origin = cameraPx - Mathf.RoundToInt(viewWidth / 2f) - Margin;
        float seconds = frame / Mathf.Max(1f, fps);
        float heat = Mathf.Clamp01((half - minHalfHeight) / Mathf.Max(0.01f, maxHalfHeight - minHalfHeight));
        System.Array.Fill(pixels, Clear);

        for (int x = 0; x < w; x++)
        {
            top[x] = Mathf.Clamp((int)Extent(origin + x, seconds, 1), 2, mid - 8);
            bottom[x] = Mathf.Clamp((int)Extent(origin + x + bottomShift, seconds, 3), 2, mid - 8);
        }

        int band = heat > 0.5f ? 4 : 2;
        for (int x = 0; x < w; x++)
        {
            int y0 = mid - top[x], y1 = mid + bottom[x];   // y grows downwards (screen order)
            int wxTop = origin + x, wxBottom = wxTop + bottomShift;

            // Glow: checker dither in world pixels, wider when the rift is wide.
            for (int k = 1; k <= band; k++)
            {
                Color32 c = k <= 2 ? Mid : Void;
                if (((wxTop + k) & 1) == 0) Put(x, y0 - 1 - k, c);
                if (((wxBottom + k) & 1) == 0) Put(x, y1 + k, c);
            }

            Put(x, y0 - 1, Outline);
            Put(x, y1, Outline);
            Put(x, y0, Highlight);                          // top light, like the tiles
            Put(x, y0 + 1, Edge);
            Fill(x, y1 - 2, 1, 2, Edge);
            int inner = Mathf.Max(0, y1 - 2 - (y0 + 2));
            Fill(x, y0 + 2, 1, inner, Mid);                 // lit lip, then void, then deep core
            if (inner > 4) Fill(x, y0 + 4, 1, inner - 4, Void);
            if (inner > 10) Fill(x, y0 + 7, 1, inner - 10, Deep);

            // Outline down the side of each step, so the edge reads as blocks.
            if (x > 0)
            {
                int pt = top[x - 1], pb = bottom[x - 1];
                if (pt != top[x]) Fill(x, mid - Mathf.Max(pt, top[x]) - 1, 1, Mathf.Abs(pt - top[x]) + 1, Outline);
                if (pb != bottom[x]) Fill(x, mid + Mathf.Min(pb, bottom[x]), 1, Mathf.Abs(pb - bottom[x]) + 1, Outline);
            }
        }

        // Sparkles: fixed in the top lane's world, slow drift, 4-step twinkle
        // (dot, plus, dot, off).
        int firstPeriod = Mathf.FloorToInt(origin / (float)SparklePeriod);
        for (int p = firstPeriod; p * SparklePeriod < origin + w; p++)
            for (int n = 0; n < Sparkles; n++)
            {
                int wx = p * SparklePeriod + (int)Mathf.Repeat(Hash(n * 3) * SparklePeriod + seconds * (2f + Hash(n) * 4f), SparklePeriod);
                int x = wx - origin;
                if (x < 1 || x >= w - 1) continue;
                int span = top[x] + bottom[x] - 8;
                if (span <= 0) continue;
                int y = Mathf.RoundToInt(mid - top[x] + 4 + Hash(n * 5) * span);
                int phase = (int)(seconds * 2f + Hash(n * 9) * 4f) % 4;
                if (phase == 3) continue;
                Put(x, y, n % 7 == 0 ? Gold : Spark);
                if (phase == 1 && span > 6)
                {
                    Put(x - 1, y, Highlight); Put(x + 1, y, Highlight);
                    Put(x, y - 1, Highlight); Put(x, y + 1, Highlight);
                }
            }

        // Chunks: peel off the rim; outwards while growing, inwards while shrinking.
        if (tick)
        {
            float rate = 0.25f + Mathf.Min(2f, Mathf.Abs(growth) * 3f) * w / 1280f;
            int count = (int)rate + (rng.NextDouble() < rate % 1f ? 1 : 0);
            bool inward = growth < -0.05f;
            for (int n = 0; n < count; n++)
            {
                int x = Margin + rng.Next(Mathf.Max(1, w - 2 * Margin));
                bool up = rng.NextDouble() < 0.5;
                float y = up ? mid - top[x] - 3 : mid + bottom[x] + 1;
                if (inward) y += up ? -10 : 10;
                float dir = (up ? -1f : 1f) * (inward ? -1f : 1f);
                chunks.Add(new Chunk
                {
                    x = origin + x, y = y,
                    vx = (float)(rng.NextDouble() - 0.5) * 0.4f,
                    vy = dir * (0.5f + (float)rng.NextDouble() * 0.5f),
                    life = 10 + rng.Next(10),
                });
            }
            for (int n = chunks.Count - 1; n >= 0; n--)
            {
                var c = chunks[n];
                c.x += c.vx; c.y += c.vy; c.age++;
                if (c.age >= c.life) { chunks.RemoveAt(n); continue; }
                chunks[n] = c;
            }
        }
        foreach (var c in chunks)
        {
            int cx = Mathf.RoundToInt(c.x) - origin, cy = Mathf.RoundToInt(c.y);
            Color32 col = c.age < c.life * 0.5f ? Edge : c.age < c.life * 0.8f ? Mid : Outline;
            Fill(cx - 1, cy - 1, 4, 4, Outline);
            Fill(cx, cy, 2, 2, col);
        }

        tex.SetPixels32(pixels);
        tex.Apply(false);
    }

    // Screen-order y (0 = top row) into the bottom-up texture.
    void Put(int x, int y, Color32 c)
    {
        if (x < 0 || y < 0 || x >= tex.width || y >= TextureHeight) return;
        pixels[(TextureHeight - 1 - y) * tex.width + x] = c;
    }

    void Fill(int x, int y, int w, int h, Color32 c)
    {
        for (int yy = y; yy < y + h; yy++)
            for (int xx = x; xx < x + w; xx++)
                Put(xx, yy, c);
    }

    static float Hash(int i)
    {
        uint h = (uint)i * 747796405u + 2891336453u;
        h = ((h >> (int)((h >> 28) + 4u)) ^ h) * 277803737u;
        h = (h >> 22) ^ h;
        return h / 4294967296f;
    }

    // 1D value noise, 0-1.
    static float Noise(float x, int seed)
    {
        int i = Mathf.FloorToInt(x);
        float f = x - i, s = f * f * (3f - 2f * f);
        return Mathf.Lerp(Hash(i * 7919 + seed * 104729), Hash((i + 1) * 7919 + seed * 104729), s);
    }

    static Color32 Hex(int rgb) => new Color32((byte)(rgb >> 16), (byte)(rgb >> 8), (byte)rgb, 255);
}
