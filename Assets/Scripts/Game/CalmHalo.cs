using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

// The scenic zone's cue (concept C "Calm halo", Samuel's pick 2026-11-08;
// mockup mockups/pretty-zone/glow.html): while the ScenicZone is active a
// soft golden glow breathes round the inside of each lane's frame, motes
// drift up through it and LaneHud warms the grade a little. Screen space,
// not tied to the world: it shows the state, not the place. Drawn in art
// pixels with the 4x4 Bayer dither the rift and HUD use, one runtime texture
// per lane on its own overlay canvas (under the HUD gauges). Added at runtime
// by ScenicZone; fades with the same 1.5 s smoothstep as the lane speed.
public class CalmHalo : MonoBehaviour
{
    public ScenicZone zone;
    public Color32 glow = new Color32(255, 204, 132, 255);
    public Color32 moteColor = new Color32(255, 236, 190, 255);
    [Tooltip("Seconds to fade in or out (smoothstep), like GameManager's calm blend.")]
    public float fadeTime = 1.5f;
    [Tooltip("Glow alpha at the frame edge at full calm.")]
    public float strength = 0.4f;
    [Tooltip("Art pixels over which the glow falls to 1/e from the frame edge.")]
    public float falloff = 34f;
    public float breathePeriod = 4f;
    [Range(0f, 1f)] public float breatheDepth = 0.15f;
    public float motesPerSecond = 10f;
    public Vector2 moteLife = new Vector2(3f, 6f);
    [Tooltip("Art pixels per second a mote rises.")]
    public Vector2 moteRise = new Vector2(5f, 14f);
    public float fps = 20f;
    public int sortingOrder = 10;

    public float Amount { get; private set; }

    const int Levels = 6;
    const int NoiseRes = 64;   // noise table samples per unit
    static readonly int[] Bayer = { 0, 8, 2, 10, 12, 4, 14, 6, 3, 11, 1, 9, 15, 7, 13, 5 };

    class Mote { public float x, y, age, life, vy, phase; public bool big; }

    class Lane
    {
        public RawImage image;
        public Texture2D tex;
        public Color32[] px;
        public float[] edge;    // exp(-d / falloff), d = art pixels to the frame edge
        public float[] diag;    // noise coordinate, x / 90 + y / 70
        public int w, h;
        public float spawn;
        public readonly List<Mote> motes = new List<Mote>();
    }

    Lane[] lanes;
    float blend, clock, sinceDraw;
    float[] noise = new float[0];
    readonly System.Random rng = new System.Random(5);

    void Start()
    {
        var go = new GameObject("CalmHalo", typeof(Canvas), typeof(CanvasScaler));
        go.transform.SetParent(transform, false);
        go.layer = LayerMask.NameToLayer("UI");
        var canvas = go.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = sortingOrder;
        var scaler = go.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1280f, 720f);
        scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
        scaler.matchWidthOrHeight = 1f;

        lanes = new Lane[2];
        for (int i = 0; i < 2; i++)
        {
            var img = new GameObject(i == 0 ? "Tick" : "Tock", typeof(RectTransform), typeof(RawImage)).GetComponent<RawImage>();
            img.transform.SetParent(go.transform, false);
            var rt = img.rectTransform;
            rt.anchorMin = new Vector2(0f, i == 0 ? 0.5f : 0f);
            rt.anchorMax = new Vector2(1f, i == 0 ? 1f : 0.5f);
            rt.offsetMin = rt.offsetMax = Vector2.zero;
            img.raycastTarget = false;
            img.enabled = false;
            lanes[i] = new Lane { image = img };
        }
    }

    void OnDisable()
    {
        Amount = 0f;
        if (LaneHud.Instance != null) LaneHud.Instance.Calm = 0f;
    }

    void OnDestroy()
    {
        if (lanes == null) return;
        foreach (var l in lanes) if (l.tex != null) Destroy(l.tex);
    }

    void Update()
    {
        if (lanes == null) return;
        float dt = Time.deltaTime;
        bool on = zone != null && zone.Active;
        blend = Mathf.MoveTowards(blend, on ? 1f : 0f, dt / Mathf.Max(0.0001f, fadeTime));
        Amount = blend * blend * (3f - 2f * blend);
        if (LaneHud.Instance != null) LaneHud.Instance.Calm = Amount;

        bool visible = Amount > 0.001f;
        foreach (var l in lanes) if (l.motes.Count > 0) visible = true;
        if (!visible)
        {
            foreach (var l in lanes) l.image.enabled = false;
            return;
        }

        clock += dt;
        sinceDraw += dt;
        if (sinceDraw < 1f / Mathf.Max(1f, fps)) return;
        float step = sinceDraw;
        sinceDraw = 0f;
        for (int i = 0; i < lanes.Length; i++)
        {
            var l = lanes[i];
            if (!Ensure(l)) continue;
            StepMotes(l, step);
            Draw(l);
            l.image.enabled = true;
        }
    }

    // (Re)makes the lane's texture at its rect size in art pixels.
    bool Ensure(Lane l)
    {
        var rect = l.image.rectTransform.rect;
        int w = Mathf.RoundToInt(rect.width), h = Mathf.RoundToInt(rect.height);
        if (w < 8 || h < 8) return false;
        if (l.tex != null && l.w == w && l.h == h) return true;
        if (l.tex != null) Destroy(l.tex);
        l.w = w; l.h = h;
        l.tex = new Texture2D(w, h, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp };
        l.px = new Color32[w * h];
        l.edge = new float[w * h];
        l.diag = new float[w * h];
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                int d = Mathf.Min(Mathf.Min(x, w - 1 - x), Mathf.Min(y, h - 1 - y));
                l.edge[y * w + x] = Mathf.Exp(-d / falloff);
                l.diag[y * w + x] = x / 90f + (h - 1 - y) / 70f;
            }
        l.image.texture = l.tex;
        return true;
    }

    void StepMotes(Lane l, float dt)
    {
        l.spawn += dt * motesPerSecond * Amount;
        while (l.spawn >= 1f)
        {
            l.spawn -= 1f;
            l.motes.Add(new Mote
            {
                x = (float)rng.NextDouble() * l.w,
                y = 20f + (float)rng.NextDouble() * (l.h - 40f),
                life = Mathf.Lerp(moteLife.x, moteLife.y, (float)rng.NextDouble()),
                vy = Mathf.Lerp(moteRise.x, moteRise.y, (float)rng.NextDouble()),
                phase = (float)rng.NextDouble() * 6.28f,
                big = rng.NextDouble() < 0.3,
            });
        }
        for (int i = l.motes.Count - 1; i >= 0; i--)
        {
            var m = l.motes[i];
            m.age += dt;
            m.y += m.vy * dt;
            m.x += Mathf.Sin(clock * 0.9f + m.phase) * 6f * dt;
            if (m.age >= m.life || m.y > l.h + 4f) l.motes.RemoveAt(i);
        }
    }

    void Draw(Lane l)
    {
        int w = l.w, h = l.h;
        float amp = strength * Amount * (1f - breatheDepth + breatheDepth * Mathf.Sin(clock * Mathf.PI * 2f / Mathf.Max(0.1f, breathePeriod)));
        // Slow light pools along the diagonal: 1D value noise, tabled per redraw.
        float shift = clock * 0.2f;
        int n = (int)((w / 90f + h / 70f + 2f) * NoiseRes);
        if (noise.Length < n) noise = new float[n];
        int baseCell = Mathf.FloorToInt(shift);
        for (int k = 0; k < n; k++) noise[k] = ValueNoise(baseCell + (float)k / NoiseRes);
        float frac = shift - baseCell;

        var px = l.px;
        Color32 clear = default;
        for (int y = 0; y < h; y++)
        {
            int row = y * w, by = (y & 3) * 4;
            for (int x = 0; x < w; x++)
            {
                int i = row + x;
                float e = l.edge[i];
                if (amp * e < 0.004f) { px[i] = clear; continue; }
                int k = (int)((l.diag[i] + frac) * NoiseRes);
                float a = amp * e * (0.8f + 0.2f * noise[k]);
                int q = (int)(a * Levels + (Bayer[by + (x & 3)] + 0.5f) / 16f);
                if (q <= 0) { px[i] = clear; continue; }
                px[i] = new Color32(glow.r, glow.g, glow.b, (byte)Mathf.Min(255, q * 255 / Levels));
            }
        }

        foreach (var m in l.motes)
        {
            float life = Mathf.Sin(Mathf.PI * m.age / m.life) * Amount * (0.7f + 0.3f * Mathf.Sin(clock * 5f + m.phase));
            int mx = Mathf.RoundToInt(m.x), my = Mathf.RoundToInt(m.y);
            Put(l, mx, my, life * 0.95f);
            if (m.big)
            {
                Put(l, mx + 1, my, life * 0.45f); Put(l, mx - 1, my, life * 0.45f);
                Put(l, mx, my + 1, life * 0.45f); Put(l, mx, my - 1, life * 0.45f);
            }
        }
        l.tex.SetPixels32(px);
        l.tex.Apply(false);
    }

    void Put(Lane l, int x, int y, float a)
    {
        if (x < 0 || y < 0 || x >= l.w || y >= l.h || a <= 0f) return;
        int i = y * l.w + x;
        byte alpha = (byte)Mathf.Clamp(Mathf.RoundToInt(a * 255f), 0, 255);
        if (l.px[i].a >= alpha) return;
        l.px[i] = new Color32(moteColor.r, moteColor.g, moteColor.b, alpha);
    }

    static float Hash(int i)
    {
        float s = Mathf.Sin(i * 127.1f + 311.7f) * 43758.5453f;
        return s - Mathf.Floor(s);
    }

    static float ValueNoise(float x)
    {
        int i = Mathf.FloorToInt(x);
        float f = x - i, u = f * f * (3f - 2f * f);
        return Mathf.Lerp(Hash(i), Hash(i + 1), u);
    }
}
