using System.Collections.Generic;
using UnityEngine;

// Smoke from the chimneys painted into one tiled background layer. Puffs chug
// out of each mouth (every fourth one bigger), start sooty, are lit from the
// upper left, bend with the wind as they rise and thin away from the edge in.
// They run on lane time, so a stopped lane holds its smoke still. Drawn at
// fps into a transparent texture as wide as the background picture and tiled
// the same way (every repeat of the picture smokes), covering only the rows
// the smoke reaches. The texture has `resolution` texels per art pixel: each
// puff keeps its art-pixel cells, but they sit at the puff's sub-pixel
// position, so the smoke glides instead of stepping a whole pixel at a time. Sits on a child of the layer's background, like
// TowerClocks, so it scrolls with its parallax; LaneSky.Apply passes the look.
[ExecuteAlways]
[RequireComponent(typeof(SpriteRenderer))]
public class ChimneySmoke : MonoBehaviour
{
    [System.Serializable]
    public struct Chimney
    {
        [Tooltip("Middle of the chimney's mouth in pixels of the background picture, from its bottom-left pixel.")]
        public Vector2 mouth;
        [Tooltip("Lane seconds between puffs.")]
        public float interval;
        [Tooltip("Puff size, 1 = normal.")]
        public float size;
    }

    [System.Serializable]
    public class Palette
    {
        public Color soot, hi, body, sh;
        public Palette(Color soot, Color hi, Color body, Color sh) { this.soot = soot; this.hi = hi; this.body = body; this.sh = sh; }
    }

    [Tooltip("The background whose picture, size and tiling the smoke follows.")]
    public SpriteRenderer background;
    public Chimney[] chimneys;
    [Tooltip("Lane whose clock runs the smoke in play mode (0 Tick, 1 Tock).")]
    public int lane = -1;
    [Tooltip("How fast the smoke plays relative to lane time (rise, drift, puffs and fading alike).")]
    [Range(0.1f, 2f)] public float tempo = 0.55f;
    [Tooltip("Redraws per second (real time).")]
    public float fps = 30f;
    [Tooltip("Texels per art pixel; higher moves the smoke in finer steps.")]
    [Range(1, 4)] public int resolution = 2;
    [Tooltip("Rows of the picture above the highest mouth the smoke may reach.")]
    public int rise = 72;
    [Tooltip("Lane seconds simulated before the first frame, so the smoke is already up.")]
    public float warmUp = 8f;
    public int seed = 1;
    public Palette day = new Palette(Rgb(95, 87, 98), Rgb(236, 232, 230), Rgb(210, 204, 208), Rgb(170, 162, 174));
    public Palette dusk = new Palette(Rgb(72, 50, 72), Rgb(236, 184, 178), Rgb(196, 152, 168), Rgb(152, 114, 150));
    public Palette night = new Palette(Rgb(34, 30, 60), Rgb(124, 114, 164), Rgb(100, 92, 142), Rgb(80, 72, 122));

    class Puff { public float x, y, age, life, r0, grow, cap, r, wob; public int id; }
    class Stack { public float timer; public int count; }

    const float Step = 0.05f;   // simulation step, lane seconds
    readonly List<Puff> puffs = new List<Puff>();
    Stack[] stacks;
    System.Random rng;
    int nextId = 1;
    float laneTime = -1f, redraw;

    SpriteRenderer sr;
    Texture2D tex;
    Color32[] pixels;
    int width, rowLo, res;
    Palette pal;
    bool dirty = true;

    static Color Rgb(int r, int g, int b) => new Color32((byte)r, (byte)g, (byte)b, 255);

    void OnEnable() => Init();

    void Init()
    {
        sr = GetComponent<SpriteRenderer>();
        if (tex != null || background == null || background.sprite == null || chimneys == null || chimneys.Length == 0) return;
        var src = background.sprite;
        var rect = src.rect;
        res = Mathf.Max(1, resolution);
        width = (int)rect.width;
        float lo = float.MaxValue, hi = float.MinValue;
        foreach (var c in chimneys) { lo = Mathf.Min(lo, c.mouth.y); hi = Mathf.Max(hi, c.mouth.y); }
        rowLo = Mathf.Max(0, Mathf.FloorToInt(lo) - 2);
        int rowHi = Mathf.Min((int)rect.height, Mathf.CeilToInt(hi) + rise);
        tex = new Texture2D(width * res, (rowHi - rowLo) * res, TextureFormat.RGBA32, false)
        {
            filterMode = FilterMode.Point,
            wrapMode = TextureWrapMode.Clamp,
            hideFlags = HideFlags.HideAndDontSave,
        };
        pixels = new Color32[tex.width * tex.height];
        // Same pivot as the picture, so the band lines up with the rows it covers.
        var pivot = new Vector2(src.pivot.x / width, (src.pivot.y - rowLo) * res / tex.height);
        var sprite = Sprite.Create(tex, new Rect(0, 0, tex.width, tex.height), pivot, src.pixelsPerUnit * res, 0, SpriteMeshType.FullRect);
        sprite.hideFlags = HideFlags.HideAndDontSave;
        sr.sprite = sprite;
        sr.drawMode = SpriteDrawMode.Tiled;
        sr.size = new Vector2(background.size.x, tex.height / (src.pixelsPerUnit * res));

        rng = new System.Random(seed);
        puffs.Clear();
        stacks = new Stack[chimneys.Length];
        for (int i = 0; i < stacks.Length; i++) stacks[i] = new Stack { timer = i * 0.2f };
        for (float t = 0f; t < warmUp; t += Step) Simulate(Step);
        laneTime = -1f;
        dirty = true;
        Draw();
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

    public void Show(LaneSky.Look from, LaneSky.Look to, float t)
    {
        if (isActiveAndEnabled && tex == null) Init();
        var a = Pick(from);
        var b = Pick(to);
        var p = new Palette(Color.Lerp(a.soot, b.soot, t), Color.Lerp(a.hi, b.hi, t), Color.Lerp(a.body, b.body, t), Color.Lerp(a.sh, b.sh, t));
        if (pal != null && Same(pal.body, p.body) && Same(pal.soot, p.soot)) return;
        pal = p;
        dirty = true;
        if (!Application.isPlaying) Draw();
    }

    static bool Same(Color a, Color b)
    {
        Color32 x = a, y = b;
        return x.r == y.r && x.g == y.g && x.b == y.b;
    }

    Palette Pick(LaneSky.Look look) => look == LaneSky.Look.Day ? day : look == LaneSky.Look.Dusk ? dusk : night;

    void Update()
    {
        if (tex == null || !Application.isPlaying) return;
        if (lane >= 0 && GameManager.INSTANCE != null)
        {
            float now = GameManager.INSTANCE.GetLaneTime(lane);
            if (laneTime >= 0f)
            {
                float dt = Mathf.Clamp(now - laneTime, 0f, 1f) * tempo;
                while (dt > 0f)
                {
                    float s = Mathf.Min(Step, dt);
                    Simulate(s);
                    dt -= s;
                }
            }
            laneTime = now;
        }
        redraw -= Time.deltaTime;
        if (redraw > 0f) return;
        redraw = 1f / Mathf.Max(1f, fps);
        if (sr.isVisible) Draw();
    }

    float R(float a, float b) => a + (float)rng.NextDouble() * (b - a);

    void Simulate(float dt)
    {
        if (dt <= 0f) return;
        dirty = true;
        for (int i = 0; i < chimneys.Length; i++)
        {
            var c = chimneys[i];
            var s = stacks[i];
            s.timer -= dt;
            if (s.timer > 0f) continue;
            s.timer = Mathf.Max(0.05f, c.interval) * R(0.85f, 1.15f);
            s.count++;
            bool big = s.count % 4 == 0;
            float size = c.size > 0f ? c.size : 1f;
            puffs.Add(new Puff
            {
                x = c.mouth.x + R(-0.5f, 0.5f), y = c.mouth.y + 1f,
                life = big ? R(6f, 7.5f) : R(4.5f, 6f),
                r0 = (big ? 2.4f : 1.6f) * size, grow = (big ? 1.7f : 1.35f) * size, cap = 9f * size,
                wob = R(0f, 6f), id = nextId++,
            });
        }
        foreach (var p in puffs)
        {
            p.age += dt;
            float k = p.age / p.life;
            p.y += dt * 10f * (1f - k * 0.7f);
            // The wind bends the column more the higher it gets.
            p.x += dt * (1f + Mathf.Pow(p.age, 1.3f) * 2.2f + Mathf.Sin(p.age * 2f + p.wob) * 1.2f);
            p.r = Mathf.Min(p.cap, p.r0 + p.age * p.grow);
        }
        puffs.RemoveAll(p => p.age >= p.life);
    }

    void Draw()
    {
        if (tex == null || !dirty) return;
        dirty = false;
        System.Array.Clear(pixels, 0, pixels.Length);
        var p = pal ?? day;
        var soot = (Color32)p.soot; var hi = (Color32)p.hi; var body = (Color32)p.body; var sh = (Color32)p.sh;
        // Oldest first, so the fresh puffs at the mouth sit on top.
        foreach (var f in puffs)
        {
            float k = f.age / f.life, r = f.r;
            float fade = k < 0.45f ? 0f : Mathf.Pow((k - 0.45f) / 0.55f, 1.2f);
            // Texels covering the puff; each is coloured by the art-pixel cell of
            // the puff it falls in, measured from the puff's exact centre.
            int tw = width * res, th = tex.height;
            int X0 = Mathf.FloorToInt((f.x - r - 1f) * res), X1 = Mathf.CeilToInt((f.x + r + 1f) * res);
            int Y0 = Mathf.Max(0, Mathf.FloorToInt((f.y - r - 1f - rowLo) * res)), Y1 = Mathf.Min(th - 1, Mathf.CeilToInt((f.y + r + 1f - rowLo) * res));
            for (int Y = Y0; Y <= Y1; Y++)
            {
                int yy = Mathf.FloorToInt((Y + 0.5f) / res + rowLo - f.y + 0.5f);
                for (int X = X0; X <= X1; X++)
                {
                    int xx = Mathf.FloorToInt((X + 0.5f) / res - f.x + 0.5f);
                    float jag = (Hash(xx >> 1, yy >> 1, f.id) - 0.5f) * 0.8f;
                    if (xx * xx + yy * yy * 1.05f > (r + jag) * (r + jag)) continue;
                    // Thins from the edge in as it ages.
                    if (fade > 0f && Hash(xx + 64, yy + 64, f.id * 7) < fade * (0.35f + 0.9f * Mathf.Sqrt(xx * xx + yy * yy) / r)) continue;
                    Color32 col;
                    if (f.age < 0.35f) col = soot;
                    else if (f.age < 0.8f) col = Hash(xx, yy, f.id + 5) < 0.5f ? soot : sh;
                    else if (!Inside(xx - 1.5f, yy + 1.5f, r)) col = hi;    // upper-left rim (y up)
                    else if (!Inside(xx + 1.2f, yy - 1.8f, r)) col = sh;    // lower-right rim
                    else col = body;
                    pixels[Y * tw + ((X % tw) + tw) % tw] = col;
                }
            }
        }
        tex.SetPixels32(pixels);
        tex.Apply(false);
    }

    static bool Inside(float x, float y, float r) => x * x + y * y * 1.05f <= r * r;

    static float Hash(int a, int b, int c)
    {
        unchecked
        {
            uint h = (uint)a * 374761393u ^ (uint)b * 668265263u ^ (uint)c * 1274126177u;
            h = (h ^ (h >> 13)) * 1274126177u;
            return (h ^ (h >> 16)) / 4294967296f;
        }
    }
}
