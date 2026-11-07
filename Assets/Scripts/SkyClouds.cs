using System.Collections.Generic;
using UnityEngine;

// Drifting pixel clouds for one lane, in the clear sky above the far skyline
// (between bandBottom and bandTop, y relative to this object). The shapes are
// generated from a seed when the component starts: puffy heads with wispy
// tails, or thin streak banks. They are coloured with the painted clouds' own
// colours for the look blend LaneSky passes in, move on lane time (a stopped
// lane stops them) and wrap around the lane camera, each layer scrolling at
// its parallax depth. Sprites and textures are runtime-only, never saved.
[ExecuteAlways]
[DefaultExecutionOrder(-30)]
public class SkyClouds : MonoBehaviour
{
    [System.Serializable]
    public class Palette
    {
        public Color hi, body, sh;
        public Palette(Color hi, Color body, Color sh) { this.hi = hi; this.body = body; this.sh = sh; }
    }

    [System.Serializable]
    public class CloudLayer
    {
        public int count = 5;
        [Tooltip("Size of the generated shapes (1 = near).")]
        public float scale = 1f;
        [Tooltip("Drift speed range in units per lane second.")]
        public Vector2 speed = new Vector2(0.08f, 0.13f);
        [Tooltip("Parallax depth: 0 moves with the ground, 1 with the camera.")]
        [Range(0f, 1f)] public float depth = 0.9f;
        [Tooltip("Height of the clouds' bottom edge, from the band's bottom (0) to its top (1).")]
        public Vector2 height = new Vector2(0f, 1f);
        public int sortingOrder = -13;
        public Palette day, dusk, night;
    }

    [Tooltip("Lane whose clock moves the clouds in play mode (0 Tick, 1 Tock).")]
    public int lane = -1;
    [Tooltip("The lane camera the clouds wrap around.")]
    public Transform cameraTransform;
    public float bandTop = 9.3f, bandBottom = 5.15f;
    public int seed = 1;
    public float pixelsPerUnit = 32f;
    [Tooltip("Snap positions to whole art pixels. Off: the clouds glide, since they move only a few pixels a second.")]
    public bool snapToPixels = false;
    [Tooltip("Lane seconds shown outside play mode.")]
    public float previewTime = 0f;

    // Colours picked from the painted clouds of bg_far_360h (day, dusk, night).
    public CloudLayer far = new CloudLayer
    {
        count = 6, scale = 0.65f, speed = new Vector2(0.035f, 0.06f), depth = 0.95f,
        height = new Vector2(0.55f, 1f), sortingOrder = -14,
        day = new Palette(Rgb(141, 176, 214), Rgb(135, 170, 208), Rgb(128, 163, 203)),
        dusk = new Palette(Rgb(196, 94, 155), Rgb(182, 96, 159), Rgb(150, 88, 162)),
        night = new Palette(Rgb(94, 69, 125), Rgb(85, 66, 123), Rgb(75, 60, 120)),
    };
    public CloudLayer near = new CloudLayer
    {
        count = 7, scale = 1f, speed = new Vector2(0.09f, 0.14f), depth = 0.9f,
        height = new Vector2(0f, 0.85f), sortingOrder = -13,
        day = new Palette(Rgb(162, 197, 229), Rgb(150, 185, 221), Rgb(139, 174, 212)),
        dusk = new Palette(Rgb(236, 125, 143), Rgb(211, 108, 157), Rgb(182, 96, 159)),
        night = new Palette(Rgb(117, 89, 133), Rgb(103, 78, 132), Rgb(89, 68, 126)),
    };

    class Cloud
    {
        public CloudLayer layer;
        public SpriteRenderer sr;
        public Texture2D tex;
        public byte[] shade;      // 0 empty, 1 lit rim, 2 body, 3 underside
        public float x0, y, speed, width;
    }

    readonly List<Cloud> clouds = new List<Cloud>();
    LaneSky.Look from = LaneSky.Look.Day, to = LaneSky.Look.Day;
    float blend;
    bool recolour = true;

    static Color Rgb(int r, int g, int b) => new Color32((byte)r, (byte)g, (byte)b, 255);

    void OnEnable() => Build();
    void OnDisable() => Clear();
    // Objects cannot be destroyed in OnValidate; rebuild on the next frame.
    void OnValidate() => rebuild = true;
    bool rebuild;

    public void Show(LaneSky.Look from, LaneSky.Look to, float t)
    {
        if (from == this.from && to == this.to && Mathf.Abs(t - blend) < 0.002f) return;
        this.from = from;
        this.to = to;
        blend = t;
        recolour = true;
    }

    void Build()
    {
        if (clouds.Count > 0) return;
        var rng = new System.Random(seed);
        foreach (var layer in new[] { far, near })
            for (int i = 0; i < layer.count; i++)
            {
                var shade = Generate(rng, layer.scale, out int w, out int h);
                var tex = new Texture2D(w, h, TextureFormat.RGBA32, false)
                {
                    filterMode = FilterMode.Point,
                    wrapMode = TextureWrapMode.Clamp,
                    hideFlags = HideFlags.HideAndDontSave,
                };
                var sprite = Sprite.Create(tex, new Rect(0, 0, w, h), Vector2.zero, pixelsPerUnit, 0, SpriteMeshType.FullRect);
                sprite.hideFlags = HideFlags.HideAndDontSave;
                var go = new GameObject("Cloud") { hideFlags = HideFlags.HideAndDontSave };
                go.transform.SetParent(transform, false);
                var sr = go.AddComponent<SpriteRenderer>();
                sr.sprite = sprite;
                sr.sortingOrder = layer.sortingOrder;
                float hu = h / pixelsPerUnit;
                float lo = bandBottom, hiY = Mathf.Max(lo, bandTop - hu);
                float u = Mathf.Lerp(layer.height.x, layer.height.y, (float)rng.NextDouble());
                clouds.Add(new Cloud
                {
                    layer = layer, sr = sr, tex = tex, shade = shade,
                    x0 = (float)rng.NextDouble() * 1000f,
                    y = Mathf.Round(Mathf.Lerp(lo, hiY, u) * pixelsPerUnit) / pixelsPerUnit,
                    speed = Mathf.Lerp(layer.speed.x, layer.speed.y, (float)rng.NextDouble()),
                    width = w / pixelsPerUnit,
                });
            }
        recolour = true;
        LateUpdate();
    }

    void Clear()
    {
        foreach (var c in clouds)
        {
            if (c.sr != null)
            {
                if (c.sr.sprite != null) DestroyImmediate(c.sr.sprite);
                DestroyImmediate(c.sr.gameObject);
            }
            if (c.tex != null) DestroyImmediate(c.tex);
        }
        clouds.Clear();
    }

    void LateUpdate()
    {
        if (rebuild)
        {
            rebuild = false;
            Clear();
            Build();
        }
        if (clouds.Count == 0) return;
        if (recolour) Recolour();

        float t = previewTime;
        if (Application.isPlaying && lane >= 0 && GameManager.INSTANCE != null)
            t = GameManager.INSTANCE.GetLaneTime(lane);

        float camX = cameraTransform != null ? cameraTransform.position.x : transform.position.x;
        float halfView = 20f;
        var cam = cameraTransform != null ? cameraTransform.GetComponent<Camera>() : null;
        if (cam != null && cam.orthographic) halfView = cam.orthographicSize * cam.aspect;

        foreach (var c in clouds)
        {
            // Wraps across a strip as wide as the view plus a cloud on each side,
            // centred on the camera, so a cloud leaves on one side out of sight and
            // comes back on the other. The camera term gives the parallax: a camera
            // move of d moves the cloud by d * depth.
            float span = 2f * (halfView + c.width) + 2f;
            float rel = Mathf.Repeat(c.x0 + c.speed * t - camX * (1f - c.layer.depth), span) - span / 2f - c.width / 2f;
            if (snapToPixels && pixelsPerUnit > 0f) rel = Mathf.Round(rel * pixelsPerUnit) / pixelsPerUnit;
            c.sr.transform.position = new Vector3(camX + rel, transform.position.y + c.y, 0f);
        }
    }

    void Recolour()
    {
        recolour = false;
        foreach (var layer in new[] { far, near })
        {
            var a = Pick(layer, from);
            var b = Pick(layer, to);
            var cols = new Color32[]
            {
                new Color32(0, 0, 0, 0),
                Color.Lerp(a.hi, b.hi, blend),
                Color.Lerp(a.body, b.body, blend),
                Color.Lerp(a.sh, b.sh, blend),
            };
            foreach (var c in clouds)
            {
                if (c.layer != layer) continue;
                var px = new Color32[c.shade.Length];
                for (int i = 0; i < px.Length; i++) px[i] = cols[c.shade[i]];
                c.tex.SetPixels32(px);
                c.tex.Apply(false);
            }
        }
    }

    static Palette Pick(CloudLayer layer, LaneSky.Look look) =>
        look == LaneSky.Look.Day ? layer.day : look == LaneSky.Look.Dusk ? layer.dusk : layer.night;

    static float Hash(int a, int b, int c)
    {
        unchecked
        {
            uint h = (uint)a * 374761393u ^ (uint)b * 668265263u ^ (uint)c * 1274126177u;
            h = (h ^ (h >> 13)) * 1274126177u;
            return (h ^ (h >> 16)) / 4294967296f;
        }
    }

    struct Band { public float x0, y, len, thick; public int id; }

    // One cloud as shade indices, bottom row first (texture order). Shapes are
    // built with y growing downwards and flipped when written out.
    static byte[] Generate(System.Random rng, float scale, out int w, out int height)
    {
        float R(float a, float b) => a + (float)rng.NextDouble() * (b - a);
        const int pad = 10;
        bool streak = rng.NextDouble() < 0.35;
        int iw = Mathf.RoundToInt(R(80f, 140f) * scale);
        int h = Mathf.RoundToInt((streak ? R(10f, 14f) : R(22f, 34f)) * scale);
        w = iw + 2 * pad;
        height = h + 4;
        int bottom = h - 1;
        int sd = rng.Next(1000000);
        var bumps = new List<Vector4>();   // x, y, radius, 1 for the flat core
        var bands = new List<Band>();

        if (streak)
        {
            // Long thin bands, stacked and offset; lower rows in front.
            int rows = 3 + rng.Next(2);
            for (int i = 0; i < rows; i++)
            {
                float len = R(iw * 0.4f, iw * 0.9f) - i * 8;
                bands.Add(new Band { x0 = pad + R(0f, iw - len), y = bottom - i * 2, len = Mathf.Max(12f, len), thick = i == 0 ? 3 : 2, id = i });
            }
        }
        else
        {
            // Cumulus: a flat core, puffs on its upper rim, smaller puffs on the
            // highest of those, thin tails off both sides.
            float cx = pad + R(0.35f, 0.65f) * iw, cw = R(0.2f, 0.3f) * iw, ch = h * 0.42f;
            for (int i = 0; i < 6; i++) bumps.Add(new Vector4(cx - cw + i * cw * 0.4f, bottom - ch * 0.45f, ch * 0.6f, 1f));
            int m = 6 + rng.Next(4);
            for (int i = 0; i < m; i++)
            {
                float a = Mathf.PI * (0.08f + 0.84f * (i + R(-0.2f, 0.2f)) / (m - 1));
                float r = h * R(0.22f, 0.33f) * (0.7f + 0.5f * Mathf.Sin(a));
                bumps.Add(new Vector4(cx - Mathf.Cos(a) * cw, bottom - ch * 0.5f - Mathf.Sin(a) * ch * 0.9f, r, 0f));
            }
            var top = bumps.FindAll(b => b.w == 0f && b.y < bottom - ch * 0.9f);
            for (int i = 0; i < Mathf.Min(3, top.Count); i++)
            {
                var t = top[i];
                bumps.Add(new Vector4(t.x + R(-t.z * 0.5f, t.z * 0.5f), t.y - t.z * 0.7f, t.z * R(0.5f, 0.7f), 0f));
            }
            int tails = 2 + rng.Next(2);
            for (int j = 0; j < tails; j++)
            {
                int dir = j % 2 == 1 ? 1 : -1;
                float len = R(iw * 0.2f, iw * 0.4f);
                float x0 = Mathf.Max(1f, dir < 0 ? cx - cw - len + 4 : cx + cw - 4);
                bands.Add(new Band { x0 = x0, y = bottom - j, len = Mathf.Min(len, w - 2 - x0), thick = j > 0 ? 1 : 2, id = 9 + j });
            }
        }

        // Owner of each pixel: -1 empty, -2 band, -3 core, else the front puff
        // (the one with the lowest centre).
        int hh = height, ww = w;
        var g = new int[ww * hh];
        for (int i = 0; i < g.Length; i++) g[i] = -1;
        for (int y = 0; y <= Mathf.Min(bottom + 1, hh - 1); y++)
            for (int x = 0; x < ww; x++)
            {
                int own = -1;
                float oy = float.MinValue;
                for (int i = 0; i < bumps.Count; i++)
                {
                    var b = bumps[i];
                    float jag = (Hash(x >> 1, y >> 1, sd + i) - 0.5f) * 0.9f;
                    float dx = x + 0.5f - b.x, dy = y + 0.5f - b.y;
                    if (Mathf.Sqrt(dx * dx + dy * dy) >= b.z + jag) continue;
                    if (b.w > 0f) { if (own == -1) own = -3; }
                    else if (b.y > oy) { own = i; oy = b.y; }
                }
                if (own == -1)
                    foreach (var band in bands)
                    {
                        float u = (x - band.x0) / band.len;
                        if (u < 0f || u > 1f) continue;
                        float t = Mathf.Min(u, 1f - u) * band.len;
                        float n = Hash(Mathf.FloorToInt((x + band.id * 5) / 5f), band.id, sd);
                        int cap = t < 3f ? 1 : t < 7f ? 2 : 9;
                        int thick = Mathf.Max(1, Mathf.Min(cap, Mathf.RoundToInt(band.thick - 0.6f + n * 2.2f)));
                        int sag = Hash(Mathf.FloorToInt(x / 9f), band.id + 20, sd) < 0.3f ? 1 : 0;
                        if (y >= band.y - thick + 1 + sag && y <= band.y + sag) { own = -2; break; }
                    }
                g[y * ww + x] = own;
            }

        int At(int x, int y) => x < 0 || y < 0 || x >= ww || y >= hh ? -1 : g[y * ww + x];
        var shade = new byte[ww * hh];
        for (int y = 0; y < hh; y++)
            for (int x = 0; x < ww; x++)
            {
                int o = g[y * ww + x];
                if (o == -1) continue;
                byte s = 2;
                if (At(x, y + 1) == -1 || (o != -2 && At(x + 1, y + 2) == -1) || y >= bottom) s = 3;   // underside
                else if (At(x - 1, y - 1) == -1 || At(x, y - 2) == -1) s = 1;                         // outer rim, lit from the upper left
                else if (o >= 0)
                {
                    // Rim of each front puff.
                    var b = bumps[o];
                    float dx = x + 0.5f - b.x, dy = y + 0.5f - b.y;
                    if (dy < -b.z * 0.25f && dx < b.z * 0.4f && Mathf.Sqrt(dx * dx + dy * dy) > b.z - 1.6f) s = 1;
                }
                shade[(hh - 1 - y) * ww + x] = s;
            }
        return shade;
    }
}
