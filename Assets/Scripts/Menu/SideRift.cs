using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

// The Unraveling rift down both sides of the main menu: LaneDivider's drawing
// turned on its side. Stepped 4 px edge rows, a dark outline and lit rim, a
// checker-dither glow, twinkling sparkles inside and 2x2 chunks that break off
// the rim and fly into the screen, redrawn at a low frame rate like the
// sprites. The left edge flows down and the right edge flows up.
//
// Flare() (on every change of selection) widens it for a moment; it eases back.
// While it is wide it glitches more: for one animation frame at a time, thin
// slices of the picture beside the rift jump sideways with a purple scanline.
// The slices are RawImages over the menu screen showing shifted strips of the
// same render texture, so they carry whatever is drawn there.
//
// The rift texture is the size of the menu's art screen (640x360) and is drawn
// on a sprite in front of the menu camera.
public class SideRift : MonoBehaviour
{
    [Tooltip("Shows the rift. Its sprite is made at runtime.")]
    public SpriteRenderer target;
    [Tooltip("The menu screen (render texture) the glitch slices copy from.")]
    public RawImage screen;
    [Tooltip("Stretched over the screen; holds the glitch slices.")]
    public RectTransform glitchLayer;

    [Header("Shape")]
    public int width = 640;
    public int height = 360;
    [Tooltip("Rift size at rest; the edge sits about 0.5-1.1 times this from the screen edge.")]
    public float minHalf = 22f;
    [Tooltip("Rift size at the strongest flare.")]
    public float maxHalf = 46f;
    [Tooltip("Animation frames per second (real time), stepped like the sprites.")]
    public float fps = 8f;
    [Tooltip("Fraction of the way to the target size covered each animation frame.")]
    [Range(0.01f, 1f)] public float ease = 0.18f;
    [Tooltip("Fraction of a flare left after each animation frame.")]
    [Range(0f, 1f)] public float flareDecay = 0.82f;
    [Tooltip("Size added by one Flare().")]
    public float flareSize = 10f;
    [Tooltip("Seconds for the edge teeth to morph into a new shape.")]
    public float morphTime = 5f;

    [Header("Glitch")]
    [Tooltip("Chance per animation frame of glitch slices at rest; a flare adds to it.")]
    [Range(0f, 1f)] public float glitchChance = 0.3f;
    public Color scanline = new Color(196 / 255f, 120 / 255f, 224 / 255f, 0.55f);
    public bool glitch = true;

    const int Col = 4;
    const int SlicePool = 8;

    static readonly Color32 Clear = new Color32(0, 0, 0, 0);
    static readonly Color32 Outline = Hex(0x22101e);
    static readonly Color32 Deep = Hex(0x100818);
    static readonly Color32 Void = Hex(0x28143a);
    static readonly Color32 Mid = Hex(0x482668);
    static readonly Color32 Edge = Hex(0x8040a8);
    static readonly Color32 Highlight = Hex(0xc478e0);
    static readonly Color32 Spark = Hex(0xf4e0ff);
    static readonly Color32 Gold = Hex(0xf2c46b);

    struct Chunk { public int side; public float x, y, vx, vy; public int age, life; }

    Texture2D tex;
    Color32[] pixels;
    int[] ext;
    readonly List<Chunk> chunks = new List<Chunk>();
    readonly System.Random rng = new System.Random(7);
    float half, surge, clock;
    int frame;
    RawImage[] slices;
    Image[] lines;

    public void Flare() => surge = Mathf.Min(maxHalf - minHalf, surge + flareSize);

    void Awake()
    {
        tex = new Texture2D(width, height, TextureFormat.RGBA32, false)
        {
            filterMode = FilterMode.Point,
            wrapMode = TextureWrapMode.Clamp,
            name = "SideRift",
        };
        pixels = new Color32[width * height];
        ext = new int[height];
        half = minHalf;
        if (target != null)
            target.sprite = Sprite.Create(tex, new Rect(0, 0, width, height), new Vector2(0.5f, 0.5f), 32f, 0, SpriteMeshType.FullRect);
        BuildSlices();
        Draw(false);
    }

    void OnDestroy()
    {
        if (tex != null) Destroy(tex);
    }

    void Update()
    {
        clock += Time.unscaledDeltaTime;
        float step = 1f / Mathf.Max(1f, fps);
        if (clock < step) return;
        clock %= step;
        frame++;
        half += (minHalf + surge - half) * ease;
        surge *= flareDecay;
        for (int i = chunks.Count - 1; i >= 0; i--)
        {
            var c = chunks[i];
            c.x += c.vx; c.y += c.vy; c.age++;
            if (c.age >= c.life) { chunks.RemoveAt(i); continue; }
            chunks[i] = c;
        }
        Draw(true);
        Glitch();
    }

    float Heat => Mathf.Clamp01((half - minHalf) / Mathf.Max(0.01f, maxHalf - minHalf));

    // Edge depth (px from the screen edge) of the rift row containing y.
    float Extent(int y, float seconds, int seed)
    {
        int row = Mathf.FloorToInt(y / (float)Col);
        float broad = Morph(row * Col / 120f, seconds / (morphTime * 2.5f), seed);
        float teeth = Morph(row * Col / 24f, seconds / morphTime, seed + 50);
        return Mathf.Round(half * (0.5f + 0.65f * broad + 0.35f * teeth) / 2f) * 2f;
    }

    void Draw(bool stepped)
    {
        System.Array.Fill(pixels, Clear);
        float seconds = frame / Mathf.Max(1f, fps);
        float heat = Heat;
        int band = heat > 0.4f ? 4 : 2;

        for (int side = 0; side < 2; side++)
        {
            int drift = side == 0 ? frame : -frame;    // left flows down, right flows up
            for (int y = 0; y < height; y++)
                ext[y] = Mathf.Max(3, (int)Extent(y + drift, seconds, side == 0 ? 1 : 3));

            for (int y = 0; y < height; y++)
            {
                int e = ext[y], wy = y + drift;
                for (int k = 1; k <= band; k++)
                    if (((wy + k) & 1) == 0) Put(side, e + k, y, k <= 2 ? Mid : Void);
                Put(side, e, y, Outline);
                Put(side, e - 1, y, Highlight);
                Put(side, e - 2, y, Edge);
                for (int x = 0; x < e - 2; x++)
                    Put(side, x, y, x >= e - 5 ? Mid : x >= e - 10 ? Void : Deep);
                // Outline along each step, so the edge reads as blocks.
                if (y > 0 && ext[y - 1] != e)
                    for (int x = Mathf.Min(ext[y - 1], e); x <= Mathf.Max(ext[y - 1], e); x++)
                        Put(side, x, y, Outline);
            }

            // Sparkles in the rift's own space, drifting with it; 4-step twinkle.
            for (int n = 0; n < 34; n++)
            {
                int wy = (int)(Hash(n * 3 + side * 101) * 1024 + seconds * (3f + Hash(n) * 5f)) % 1024;
                int y = ((wy - drift) % 1024 + 1024) % 1024;
                if (y >= height) continue;
                int span = ext[y] - 5;
                if (span <= 1) continue;
                int x = 1 + (int)(Hash(n * 5 + side) * span);
                int phase = (int)(seconds * 2f + Hash(n * 9) * 4f) % 4;
                if (phase == 3) continue;
                Put(side, x, y, n % 7 == 0 ? Gold : Spark);
                if (phase == 1 && span > 5)
                {
                    Put(side, x - 1, y, Highlight); Put(side, x + 1, y, Highlight);
                    Put(side, x, y - 1, Highlight); Put(side, x, y + 1, Highlight);
                }
            }

            // Chunks peel off the rim and fly into the screen, more when it is wide.
            if (stepped)
            {
                float rate = 0.6f + heat * 2.5f;
                int count = (int)rate + (rng.NextDouble() < rate % 1f ? 1 : 0);
                for (int n = 0; n < count; n++)
                {
                    int y = rng.Next(height);
                    chunks.Add(new Chunk
                    {
                        side = side, x = ext[y] + 2, y = y,
                        vx = (0.6f + (float)rng.NextDouble() * 0.8f * (1f + heat)) * 3f,
                        vy = ((float)rng.NextDouble() - 0.5f) * 1.5f,
                        life = 8 + rng.Next(10),
                    });
                }
            }
        }

        foreach (var c in chunks)
        {
            Color32 col = c.age < c.life * 0.5f ? Edge : c.age < c.life * 0.8f ? Mid : Outline;
            int cx = Mathf.RoundToInt(c.x), cy = Mathf.RoundToInt(c.y);
            for (int yy = -1; yy < 3; yy++)
                for (int xx = -1; xx < 3; xx++)
                    Put(c.side, cx + xx, cy + yy, xx >= 0 && xx < 2 && yy >= 0 && yy < 2 ? col : Outline);
        }

        tex.SetPixels32(pixels);
        tex.Apply(false);
    }

    // side 0 measures x from the left edge, side 1 from the right; y is screen order (0 = top).
    void Put(int side, int x, int y, Color32 c)
    {
        if (side == 1) x = width - 1 - x;
        if (x < 0 || y < 0 || x >= width || y >= height) return;
        pixels[(height - 1 - y) * width + x] = c;
    }

    void BuildSlices()
    {
        if (screen == null || glitchLayer == null) return;
        slices = new RawImage[SlicePool];
        lines = new Image[SlicePool];
        for (int i = 0; i < SlicePool; i++)
        {
            var go = new GameObject($"GlitchSlice_{i}", typeof(RectTransform), typeof(RawImage));
            go.transform.SetParent(glitchLayer, false);
            slices[i] = go.GetComponent<RawImage>();
            slices[i].raycastTarget = false;
            slices[i].enabled = false;
            var line = new GameObject($"GlitchLine_{i}", typeof(RectTransform), typeof(Image));
            line.transform.SetParent(glitchLayer, false);
            lines[i] = line.GetComponent<Image>();
            lines[i].color = scanline;
            lines[i].raycastTarget = false;
            lines[i].enabled = false;
        }
    }

    // For this animation frame: a few slices beside the rift, shifted sideways.
    void Glitch()
    {
        if (slices == null) return;
        foreach (var s in slices) s.enabled = false;
        foreach (var l in lines) l.enabled = false;
        float heat = Heat;
        if (!glitch || rng.NextDouble() >= glitchChance + heat) return;
        int count = Mathf.Min(SlicePool, 1 + rng.Next(2 + (int)(heat * 4f)));
        for (int i = 0; i < count; i++)
        {
            int side = rng.Next(2);
            int y = rng.Next(height - 8), h = 2 + rng.Next(7);
            int w = 30 + rng.Next(60 + (int)(heat * 120f));
            int dx = (rng.Next(2) == 0 ? -1 : 1) * (2 + rng.Next(6));
            int x0 = side == 0 ? 0 : width - w;
            var s = slices[i];
            s.texture = screen.texture;
            s.uvRect = new Rect(x0 / (float)width, 1f - (y + h) / (float)height, w / (float)width, h / (float)height);
            Place(s.rectTransform, x0 + dx, y, w, h);
            s.enabled = true;
            Place(lines[i].rectTransform, x0 + dx, y, w, 1);
            lines[i].enabled = true;
        }
    }

    // Art pixel rect (top-left origin) onto the glitch layer, which matches the screen.
    void Place(RectTransform rt, int x, int y, int w, int h)
    {
        rt.anchorMin = new Vector2(x / (float)width, 1f - (y + h) / (float)height);
        rt.anchorMax = new Vector2((x + w) / (float)width, 1f - y / (float)height);
        rt.offsetMin = rt.offsetMax = Vector2.zero;
    }

    static float Morph(float x, float phase, int seed)
    {
        int k = Mathf.FloorToInt(phase);
        float f = phase - k, s = f * f * (3f - 2f * f);
        return Mathf.Lerp(Noise(x, seed + k * 977), Noise(x, seed + (k + 1) * 977), s);
    }

    static float Hash(int i)
    {
        uint h = (uint)i * 747796405u + 2891336453u;
        h = ((h >> (int)((h >> 28) + 4u)) ^ h) * 277803737u;
        h = (h >> 22) ^ h;
        return h / 4294967296f;
    }

    static float Noise(float x, int seed)
    {
        int i = Mathf.FloorToInt(x);
        float f = x - i, s = f * f * (3f - 2f * f);
        return Mathf.Lerp(Hash(i * 7919 + seed * 104729), Hash((i + 1) * 7919 + seed * 104729), s);
    }

    static Color32 Hex(int rgb) => new Color32((byte)(rgb >> 16), (byte)(rgb >> 8), (byte)rgb, 255);
}
