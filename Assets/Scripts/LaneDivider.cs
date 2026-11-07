using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

// The Unraveling rift drawn across the screen between Tick's lane (top) and
// Tock's (bottom). Lives on a screen-space overlay canvas, so it stays on the
// seam between the two camera viewports. One canvas unit is one art pixel.
//
// The rift is pixel art drawn into a texture at a low frame rate: stepped
// 4 px edge columns whose heights drift slowly, a dark outline and lit rim,
// a checker-dither glow, twinkling sparkles inside and 2x2 chunks that break
// off the rim. Its height follows the difference between the two lanes' world
// speeds (GameManager): wide when one player races and the other stands still,
// thin when they move alike. Chunks fly outwards while it grows and get pulled
// back in while it shrinks.
[ExecuteAlways]
public class LaneDivider : MonoBehaviour
{
    public RawImage rift;

    [Tooltip("Half height of the rift in art pixels with no speed difference.")]
    public float minHalfHeight = 4f;
    [Tooltip("Half height the rift approaches at a large speed difference.")]
    public float maxHalfHeight = 56f;
    [Tooltip("Speed difference (world speed units) that reaches about 63% of the way to the max.")]
    public float gapScale = 1.5f;
    [Tooltip("Animation frames per second (real time), stepped like the sprites.")]
    public float fps = 8f;
    [Tooltip("Fraction of the way to the target height covered each animation frame.")]
    [Range(0.01f, 1f)] public float ease = 0.06f;
    [Tooltip("Speed difference used outside play mode (and when there is no GameManager), for previewing.")]
    [Range(0f, 5f)] public float previewGap = 2f;

    public const int TextureHeight = 256;   // room for the max height plus drifting chunks
    const int Col = 4;                       // edge column width in pixels
    const int Sparkles = 90;

    static readonly Color32 Clear = new Color32(0, 0, 0, 0);
    static readonly Color32 Outline = Hex(0x22101e);
    static readonly Color32 Deep = Hex(0x100818);
    static readonly Color32 Void = Hex(0x28143a);
    static readonly Color32 Mid = Hex(0x482668);
    static readonly Color32 Edge = Hex(0x8040a8);
    static readonly Color32 Highlight = Hex(0xc478e0);
    static readonly Color32 Spark = Hex(0xf4e0ff);
    static readonly Color32 Gold = Hex(0xf2c46b);

    struct Chunk { public float x, y, vx, vy; public int age, life; }

    Texture2D tex;
    Color32[] pixels;
    int[] top, bottom;
    readonly List<Chunk> chunks = new List<Chunk>();
    readonly System.Random rng = new System.Random(7);
    float half, clock;
    int frame;
    bool started;

    void OnDisable()
    {
        if (tex != null) DestroyImmediate(tex);
        tex = null;
    }

    void Update()
    {
        if (rift == null) return;
        int width = Mathf.Max(Col, Mathf.CeilToInt(rift.rectTransform.rect.width));
        bool resized = EnsureTexture(width);

        float target = TargetHalfHeight();
        if (!started) { half = target; started = true; }

        clock += Application.isPlaying ? Time.unscaledDeltaTime : 0f;
        float step = 1f / Mathf.Max(1f, fps);
        if (clock >= step || resized || !Application.isPlaying)
        {
            clock = Application.isPlaying ? clock % step : 0f;
            if (Application.isPlaying) frame++;
            float before = half;
            half += (target - half) * (Application.isPlaying ? ease : 1f);
            Draw(half - before);
        }
        rift.uvRect = new Rect(0f, 0f, rift.rectTransform.rect.width / tex.width, 1f);
    }

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
        top = new int[width / Col + 1];
        bottom = new int[width / Col + 1];
        rift.texture = tex;
        return true;
    }

    void Draw(float growth)
    {
        int w = tex.width, mid = TextureHeight / 2;
        float seconds = frame / Mathf.Max(1f, fps);
        float heat = Mathf.Clamp01((half - minHalfHeight) / Mathf.Max(0.01f, maxHalfHeight - minHalfHeight));
        System.Array.Fill(pixels, Clear);

        // Edge profile: a broad shape drifting 3-4 px/s and small teeth that
        // morph over about 5 s, stepped to 2 px.
        int cols = (w + Col - 1) / Col;
        for (int i = 0; i < cols; i++)
        {
            float x = i * Col;
            float a = 0.65f * Noise((x + seconds * 4f) / 120f, 1) + 0.35f * Noise(x / 24f + seconds * 0.2f, 2);
            float b = 0.65f * Noise((x - seconds * 3f) / 120f, 3) + 0.35f * Noise(x / 24f + seconds * 0.17f, 4);
            top[i] = Mathf.Clamp(Mathf.RoundToInt(half * (0.5f + a) / 2f) * 2, 2, mid - 8);
            bottom[i] = Mathf.Clamp(Mathf.RoundToInt(half * (0.5f + b) / 2f) * 2, 2, mid - 8);
        }

        int band = heat > 0.5f ? 4 : 2;
        for (int i = 0; i < cols; i++)
        {
            int x = i * Col, cw = Mathf.Min(Col, w - x);
            int y0 = mid - top[i], y1 = mid + bottom[i];   // y grows downwards (screen order)

            // Glow: checker dither, wider when the rift is wide.
            for (int xx = x; xx < x + cw; xx++)
                for (int k = 1; k <= band; k++)
                    if (((xx + k) & 1) == 0)
                    {
                        Color32 c = k <= 2 ? Mid : Void;
                        Put(xx, y0 - 1 - k, c);
                        Put(xx, y1 + k, c);
                    }

            Fill(x, y0 - 1, cw, 1, Outline);
            Fill(x, y1, cw, 1, Outline);
            Fill(x, y0, cw, 2, Edge);
            Fill(x, y1 - 2, cw, 2, Edge);
            Fill(x, y0, cw, 1, Highlight);                  // top light, like the tiles
            int inner = Mathf.Max(0, y1 - 2 - (y0 + 2));
            Fill(x, y0 + 2, cw, inner, Mid);                // lit lip, then void, then deep core
            if (inner > 4) Fill(x, y0 + 4, cw, inner - 4, Void);
            if (inner > 10) Fill(x, y0 + 7, cw, inner - 10, Deep);

            // Outline down the side of each step, so the edge reads as blocks.
            if (i > 0)
            {
                int pt = top[i - 1], pb = bottom[i - 1];
                if (pt != top[i]) Fill(x, mid - Mathf.Max(pt, top[i]) - 1, 1, Mathf.Abs(pt - top[i]) + 1, Outline);
                if (pb != bottom[i]) Fill(x, mid + Mathf.Min(pb, bottom[i]), 1, Mathf.Abs(pb - bottom[i]) + 1, Outline);
            }
        }

        // Sparkles: slow drift, 4-step twinkle (dot, plus, dot, off).
        for (int n = 0; n < Sparkles; n++)
        {
            int x = (int)Mathf.Repeat(Hash(n * 3) * w + seconds * (3f + Hash(n) * 6f), w);
            int i = x / Col;
            int span = top[i] + bottom[i] - 8;
            if (span <= 0) continue;
            int y = Mathf.RoundToInt(mid - top[i] + 4 + Hash(n * 5) * span);
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
        if (Application.isPlaying)
        {
            float rate = 0.25f + Mathf.Min(2f, Mathf.Abs(growth) * 3f) * w / 1280f;
            int count = (int)rate + (rng.NextDouble() < rate % 1f ? 1 : 0);
            bool inward = growth < -0.05f;
            for (int n = 0; n < count; n++)
            {
                int i = rng.Next(cols);
                bool up = rng.NextDouble() < 0.5;
                float y = up ? mid - top[i] - 3 : mid + bottom[i] + 1;
                if (inward) y += up ? -10 : 10;
                float dir = (up ? -1f : 1f) * (inward ? -1f : 1f);
                chunks.Add(new Chunk
                {
                    x = i * Col + rng.Next(Col), y = y,
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
            int cx = Mathf.RoundToInt(c.x), cy = Mathf.RoundToInt(c.y);
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
