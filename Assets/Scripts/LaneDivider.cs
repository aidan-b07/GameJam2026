using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

// The Unraveling rifts, drawn on a screen-space overlay canvas (one canvas
// unit is one art pixel). Two bands, one per lane, each hanging from the top
// edge of its lane's view and reaching down by LaneRift.ReachAt(lane, x)
// (uneven along the edge: swells, fingers and columns that crack down):
// - rift, on the seam between Tick's lane (top) and Tock's (bottom), grows
//   down into Tock's view; its top edge stays a thin crack in Tick's ground;
// - topRift, along the top of the screen, grows down into Tick's view.
//
// Each rift is pixel art drawn into a texture at a low frame rate: stepped
// 4 px edge columns, a dark outline and lit rim, a checker-dither glow,
// twinkling sparkles inside and 2x2 chunks that break off the rim. Chunks fly
// outwards while a rift grows and get pulled back in while it shrinks.
//
// The edges belong to the level: each edge's shape is fixed to its lane's
// world (topCamera for the edges in Tick's view, bottomCamera for the one in
// Tock's), so they scroll with the tiles as the cameras move. Between
// animation frames the texture is slid by the camera's pixel offset (it is
// drawn with a margin on both sides), so the edges keep up with the level at
// full frame rate. The teeth only morph slowly in place.
[ExecuteAlways]
[DefaultExecutionOrder(-30)]   // LateUpdate after CoopCamera (-50) and LaneRift (-40)
public class LaneDivider : MonoBehaviour
{
    [Tooltip("Rift on the seam; grows down into Tock's lane (lane 1).")]
    public RawImage rift;
    [Tooltip("Rift along the top of the screen; grows down into Tick's lane (lane 0). Optional.")]
    public RawImage topRift;
    [Tooltip("Camera of the top lane (Tick).")]
    public Transform topCamera;
    [Tooltip("Camera of the bottom lane (Tock).")]
    public Transform bottomCamera;

    [Tooltip("Reach in art pixels of a closed rift, and of the seam rift's crack up into Tick's ground.")]
    public float minHalfHeight = 9f;
    [Tooltip("Animation frames per second (real time), stepped like the sprites.")]
    public float fps = 12f;
    [Tooltip("Seconds for the edge teeth to morph into a new shape.")]
    public float morphTime = 5f;
    [Tooltip("Rift growth (units) shown outside play mode (and when there is no LaneRift), for previewing.")]
    [Range(0f, 12f)] public float previewDepth = 0f;

    // 1 normal, 0 closed: the ending shrinks both rifts to nothing.
    [System.NonSerialized] public float scale = 1f;

    const float PixelsPerUnit = 32f;         // art pixels per world unit (32 PPU, 1 canvas unit = 1 px)
    const int Margin = 64;                   // extra pixels drawn each side, for sliding between frames
    const int Col = 4;                       // edge column width in pixels
    const int SparklePeriod = 1024;          // sparkles repeat every this many world pixels
    const int Sparkles = 70;                 // per period
    const int Below = 416;                   // texture rows below the anchor: a whole lane (360) plus teeth and chunks
    const int SeamAbove = 48;                // rows above the seam: the crack into Tick's ground
    const int TopAbove = 4;                  // rows above the screen top (never seen)

    static readonly Color32 Clear = new Color32(0, 0, 0, 0);
    static readonly Color32 Outline = Hex(0x22101e);
    static readonly Color32 Deep = Hex(0x100818);
    static readonly Color32 Void = Hex(0x28143a);
    static readonly Color32 Mid = Hex(0x482668);
    static readonly Color32 Edge = Hex(0x8040a8);
    static readonly Color32 Highlight = Hex(0xc478e0);
    static readonly Color32 Spark = Hex(0xf4e0ff);
    static readonly Color32 Gold = Hex(0xf2c46b);

    // x is in world pixels of the band's lower edge.
    struct Chunk { public float x, y, vx, vy; public int age, life; }

    class Band
    {
        public RawImage image;
        public int lane, above, seed;
        public bool upperEdge;
        public Texture2D tex;
        public Color32[] pixels;
        public int[] top, bottom, lastBottom;
        public int lastOrigin;
        public bool hasLast;
        public readonly List<Chunk> chunks = new List<Chunk>();
        public float drawnReach = -1f;
        public int drawnCameraPx;
        public int Height => above + Below;
    }

    Band seam, screenTop;
    readonly System.Random rng = new System.Random(7);
    float clock;
    int frame;

    void OnDisable()
    {
        Release(seam);
        Release(screenTop);
        seam = screenTop = null;
    }

    static void Release(Band b)
    {
        if (b?.tex != null) DestroyImmediate(b.tex);
    }

    void LateUpdate()
    {
        if (seam == null || seam.image != rift)
        {
            Release(seam);
            seam = rift == null ? null : new Band { image = rift, lane = 1, above = SeamAbove, seed = 3, upperEdge = true };
        }
        if (screenTop == null || screenTop.image != topRift)
        {
            Release(screenTop);
            screenTop = topRift == null ? null : new Band { image = topRift, lane = 0, above = TopAbove, seed = 5 };
        }

        clock += Application.isPlaying ? Time.unscaledDeltaTime : 0f;
        float step = 1f / Mathf.Max(1f, fps);
        bool tick = Application.isPlaying && clock >= step;
        if (tick)
        {
            clock %= step;
            frame++;
        }

        bool shown = scale > 0.08f;
        if (rift != null) rift.enabled = shown;
        if (topRift != null) topRift.enabled = shown;
        if (!shown) return;
        if (seam != null) UpdateBand(seam, bottomCamera, topCamera, tick);
        if (screenTop != null) UpdateBand(screenTop, topCamera, null, tick);
    }

    // lowerCamera: the lane whose world the growing edge belongs to;
    // upperCamera: the lane above the anchor (the seam's crack), or null.
    void UpdateBand(Band b, Transform lowerCamera, Transform upperCamera, bool tick)
    {
        var rt = b.image.rectTransform;
        rt.sizeDelta = new Vector2(0f, b.Height);
        rt.pivot = new Vector2(0.5f, Below / (float)b.Height);

        float viewWidth = rt.rect.width;
        int width = Mathf.Max(Col, Mathf.CeilToInt(viewWidth)) + 2 * Margin;
        bool redraw = EnsureTexture(b, width);

        int cameraPx = CameraPx(lowerCamera);
        if (Mathf.Abs(cameraPx - b.drawnCameraPx) > Margin - 2) redraw = true;

        if (tick || redraw || !Application.isPlaying)
        {
            float reach = Reach(b.lane) * scale;
            float growth = tick && b.drawnReach >= 0f ? reach - b.drawnReach : 0f;
            b.drawnReach = reach;
            Draw(b, cameraPx, CameraPx(upperCamera) - cameraPx, viewWidth, growth, tick);
        }

        float slide = cameraPx - b.drawnCameraPx;
        b.image.uvRect = new Rect((Margin + slide) / b.tex.width, 0f, viewWidth / b.tex.width, 1f);
    }

    static int CameraPx(Transform cam) => cam == null ? 0 : Mathf.RoundToInt(cam.position.x * PixelsPerUnit);

    // Mean reach of the lane's rift below its anchor, in art pixels.
    float Reach(int lane)
    {
        if (Application.isPlaying && LaneRift.Instance != null)
            return LaneRift.Instance.Reach(lane) * PixelsPerUnit;
        return minHalfHeight + previewDepth * PixelsPerUnit;
    }

    // Reach in art pixels of the edge column at world pixel wx.
    float ReachAt(int lane, int wx)
    {
        float x = wx / PixelsPerUnit;
        if (Application.isPlaying && LaneRift.Instance != null)
            return LaneRift.Instance.ReachAt(lane, x) * PixelsPerUnit * scale;
        return (minHalfHeight + LaneRift.Uneven(previewDepth, x, lane) * PixelsPerUnit) * scale;
    }

    bool EnsureTexture(Band b, int width)
    {
        if (b.tex != null && b.tex.width == width) return false;
        if (b.tex != null) DestroyImmediate(b.tex);
        b.tex = new Texture2D(width, b.Height, TextureFormat.RGBA32, false)
        {
            filterMode = FilterMode.Point,
            wrapMode = TextureWrapMode.Clamp,
            hideFlags = HideFlags.DontSave,
            name = "Rift",
        };
        b.pixels = new Color32[width * b.Height];
        b.top = new int[width];
        b.bottom = new int[width];
        b.lastBottom = new int[width];
        b.hasLast = false;
        b.image.texture = b.tex;
        return true;
    }

    // Edge extent (px from the anchor) of the world column containing world
    // pixel wx: the column's reach plus teeth. The broad shape and the teeth each
    // cross-fade between fixed noise fields, so the edge reshapes in place
    // without sliding along the level.
    float Extent(int wx, float reach, float seconds, int seed)
    {
        int col = Mathf.FloorToInt(wx / (float)Col);
        float broad = Morph(col * Col / 120f, seconds / (morphTime * 2.5f), seed);
        float teeth = Morph(col * Col / 24f, seconds / morphTime, seed + 50);
        float shape = 0.65f * broad + 0.35f * teeth;
        float amp = Mathf.Min(reach, LaneRift.TeethAmplitude * PixelsPerUnit);
        return Mathf.Round((reach + amp * (shape - 0.5f)) / 2f) * 2f;
    }

    static float Morph(float x, float phase, int seed)
    {
        int k = Mathf.FloorToInt(phase);
        float f = phase - k, s = f * f * (3f - 2f * f);
        return Mathf.Lerp(Noise(x, seed + k * 977), Noise(x, seed + (k + 1) * 977), s);
    }

    void Draw(Band b, int cameraPx, int upperShift, float viewWidth, float growth, bool tick)
    {
        b.drawnCameraPx = cameraPx;
        int w = b.tex.width, mid = b.above;
        // World pixel (lower edge's lane) of texture column 0.
        int origin = cameraPx - Mathf.RoundToInt(viewWidth / 2f) - Margin;
        float seconds = frame / Mathf.Max(1f, fps);
        float reach = b.drawnReach;
        float heat = Mathf.Clamp01((reach - minHalfHeight) / 64f);
        System.Array.Fill(b.pixels, Clear);

        for (int x = 0; x < w; x++)
        {
            // Without an upper edge the void runs off the top of the texture.
            b.top[x] = b.upperEdge
                ? Mathf.Clamp((int)Extent(origin + x + upperShift, minHalfHeight * scale, seconds, 1), 2, mid - 8)
                : mid + 3;
            b.bottom[x] = Mathf.Clamp((int)Extent(origin + x, ReachAt(b.lane, origin + x), seconds, b.seed), 2, Below - 8);
        }

        int band = heat > 0.5f ? 4 : 2;
        for (int x = 0; x < w; x++)
        {
            int y0 = mid - b.top[x], y1 = mid + b.bottom[x];   // y grows downwards (screen order)
            int wxBottom = origin + x, wxTop = wxBottom + upperShift;

            // Glow: checker dither in world pixels, wider when the rift is wide.
            for (int k = 1; k <= band; k++)
            {
                Color32 c = k <= 2 ? Mid : Void;
                if (((wxTop + k) & 1) == 0) Put(b, x, y0 - 1 - k, c);
                if (((wxBottom + k) & 1) == 0) Put(b, x, y1 + k, c);
            }

            Put(b, x, y0 - 1, Outline);
            Put(b, x, y1, Outline);
            Put(b, x, y0, Highlight);                       // top light, like the tiles
            Put(b, x, y0 + 1, Edge);
            Fill(b, x, y1 - 2, 1, 2, Edge);
            int inner = Mathf.Max(0, y1 - 2 - (y0 + 2));
            Fill(b, x, y0 + 2, 1, inner, Mid);              // lit lip, then void, then deep core
            if (inner > 4) Fill(b, x, y0 + 4, 1, inner - 4, Void);
            if (inner > 10) Fill(b, x, y0 + 7, 1, inner - 10, Deep);

            // Outline down the side of each step, so the edge reads as blocks.
            if (x > 0)
            {
                int pt = b.top[x - 1], pb = b.bottom[x - 1];
                if (pt != b.top[x]) Fill(b, x, mid - Mathf.Max(pt, b.top[x]) - 1, 1, Mathf.Abs(pt - b.top[x]) + 1, Outline);
                if (pb != b.bottom[x]) Fill(b, x, mid + Mathf.Min(pb, b.bottom[x]), 1, Mathf.Abs(pb - b.bottom[x]) + 1, Outline);
            }
        }

        // Sparkles: fixed in the lower lane's world, slow drift, 4-step twinkle
        // (dot, plus, dot, off).
        int firstPeriod = Mathf.FloorToInt(origin / (float)SparklePeriod);
        for (int p = firstPeriod; p * SparklePeriod < origin + w; p++)
            for (int n = 0; n < Sparkles; n++)
            {
                int wx = p * SparklePeriod + (int)Mathf.Repeat(Hash(n * 3 + b.seed) * SparklePeriod + seconds * (2f + Hash(n) * 4f), SparklePeriod);
                int x = wx - origin;
                if (x < 1 || x >= w - 1) continue;
                int span = Mathf.Min(b.top[x], mid) + b.bottom[x] - 8;
                if (span <= 0) continue;
                int y = Mathf.RoundToInt(mid - Mathf.Min(b.top[x], mid) + 4 + Hash(n * 5) * span);
                int phase = (int)(seconds * 2f + Hash(n * 9) * 4f) % 4;
                if (phase == 3) continue;
                Put(b, x, y, n % 7 == 0 ? Gold : Spark);
                if (phase == 1 && span > 6)
                {
                    Put(b, x - 1, y, Highlight); Put(b, x + 1, y, Highlight);
                    Put(b, x, y - 1, Highlight); Put(b, x, y + 1, Highlight);
                }
            }

        // Chunks: peel off the rim; outwards while growing, inwards while shrinking.
        if (tick)
        {
            float rate = 0.25f + Mathf.Min(2f, Mathf.Abs(growth) * 0.5f) * w / 1280f;
            int count = (int)rate + (rng.NextDouble() < rate % 1f ? 1 : 0);
            bool inward = growth < -0.5f;
            for (int n = 0; n < count; n++)
            {
                int x = Margin + rng.Next(Mathf.Max(1, w - 2 * Margin));
                bool up = b.upperEdge && rng.NextDouble() < 0.3;
                float y = up ? mid - b.top[x] - 3 : mid + b.bottom[x] + 1;
                if (inward) y += up ? -10 : 10;
                float dir = (up ? -1f : 1f) * (inward ? -1f : 1f);
                b.chunks.Add(new Chunk
                {
                    x = origin + x, y = y,
                    vx = (float)(rng.NextDouble() - 0.5) * 0.4f,
                    vy = dir * (0.5f + (float)rng.NextDouble() * 0.5f) + (up ? 0f : growth),
                    life = 10 + rng.Next(10),
                });
            }
            // Crumbs drop from columns that just cracked further down.
            if (b.hasLast && growth > 0f)
            {
                int shift = origin - b.lastOrigin, crumbs = 0;
                int cols = (w - 2 * Margin) / Col, start = rng.Next(Mathf.Max(1, cols));
                for (int i = 0; i < cols && crumbs < 6; i++)
                {
                    int x = Margin + (start + i) % cols * Col, last = x + shift;
                    if (last < 0 || last >= w || b.bottom[x] - b.lastBottom[last] < 6) continue;
                    if (rng.NextDouble() > 0.2) continue;
                    crumbs++;
                    b.chunks.Add(new Chunk
                    {
                        x = origin + x + rng.Next(Col), y = mid + b.bottom[x] + 1,
                        vx = (float)(rng.NextDouble() - 0.5) * 0.3f,
                        vy = 1f + (float)rng.NextDouble(),
                        life = 6 + rng.Next(6),
                    });
                }
            }
            System.Array.Copy(b.bottom, b.lastBottom, w);
            b.lastOrigin = origin;
            b.hasLast = true;
            for (int n = b.chunks.Count - 1; n >= 0; n--)
            {
                var c = b.chunks[n];
                c.x += c.vx; c.y += c.vy; c.age++;
                if (c.age >= c.life) { b.chunks.RemoveAt(n); continue; }
                b.chunks[n] = c;
            }
        }
        foreach (var c in b.chunks)
        {
            int cx = Mathf.RoundToInt(c.x) - origin, cy = Mathf.RoundToInt(c.y);
            Color32 col = c.age < c.life * 0.5f ? Edge : c.age < c.life * 0.8f ? Mid : Outline;
            Fill(b, cx - 1, cy - 1, 4, 4, Outline);
            Fill(b, cx, cy, 2, 2, col);
        }

        b.tex.SetPixels32(b.pixels);
        b.tex.Apply(false);
    }

    // Screen-order y (0 = top row) into the bottom-up texture.
    static void Put(Band b, int x, int y, Color32 c)
    {
        int h = b.Height;
        if (x < 0 || y < 0 || x >= b.tex.width || y >= h) return;
        b.pixels[(h - 1 - y) * b.tex.width + x] = c;
    }

    static void Fill(Band b, int x, int y, int w, int h, Color32 c)
    {
        for (int yy = y; yy < y + h; yy++)
            for (int xx = x; xx < x + w; xx++)
                Put(b, xx, yy, c);
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
