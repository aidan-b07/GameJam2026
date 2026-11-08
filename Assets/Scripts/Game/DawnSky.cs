using System.Collections.Generic;
using System.Diagnostics;
using UnityEngine;
using UnityEngine.Rendering.Universal;

// The light show of the ending (mockups/ending/README.md, "Dawn of a New Day";
// numbers from ending.html, scene C2). DawnEnding calls Render(t) every frame
// with the seconds since the ending started; everything here is a pure
// function of t, so stepping or skipping is safe.
//
// Works in art pixels of one lane view (y down from the lane's top edge, x
// from its left edge; the mockup's 1280-wide view is centred in a wider one).
// The sun, birds and streaks are at the same place in both lanes, but each
// lane has its own city (the mid background is a row of sections in a
// per-lane order), so each lane has its own masks and light:
// - sky mask (quarter resolution) from a render of the lane's city stills
//   (day look, transparent sky) exactly as they are on screen when the
//   ending starts, and the roofline (first non-sky cell from the top of each
//   column); a light mask also removes what the level puts in front of the
//   sky (a render of the lane without its sky and players);
// - sun glow (quarter res, dithered) and sun disc (2 px rows, heat shimmer),
//   behind the city and clipped to the sky above the roofline;
// - sky tint round the sun, cut to the sky: a 2x-multiply pass (soft-light
//   stand-in) and a normal pass;
// - god rays (quarter res, 4 dithered light levels, screen blend): 14 beams on
//   their own cycles, shadowed by marching towards the sun through the light
//   mask, plus the sun's core and the first-light sweep over the rooftops;
// - additive effects at 2 px resolution: glint, corona, lens flare, streak
//   and motes;
// - a warm gradient over each lane, the V of birds (same frame in both lanes)
//   and the white speed streaks of the clock spin.
// Glow, tint, rays and effects are plain array maths: each lane computes them
// on a worker thread at raysFps and shows them the frame after (on WebGL,
// which has no threads, in place). Textures are point filtered, never saved.
[DefaultExecutionOrder(60)]
public class DawnSky : MonoBehaviour
{
    [Tooltip("Lane cameras: Tick, Tock.")]
    public Camera[] cameras;
    [Tooltip("Each lane's sky (Tick, Tock): its mid layer (one picture or a row of sections) is the city; " +
             "its whole object is hidden while the level's occluders are captured. Found by lane when empty.")]
    public LaneSky[] skies;
    [Tooltip("View row (art px from the lane's top) of the ground surface's top; rows below are never sky.")]
    public int groundRow = 313;
    [Tooltip("Where the sun ends its climb (view px from the lane's top). The mockup's 64 hides Tick's sun under the letterbox.")]
    public float sunTop = 104f;
    [Tooltip("Glow, tint, rays and effects are redrawn at this rate (stepped like the sprites); the disc every frame.")]
    public float raysFps = 24f;
    [Tooltip("Peek time the sun's horizontal placement aims for (the mockup's is about 7.8 s).")]
    public float targetPeek = 7.8f;

    [Header("Sorting orders")]
    public int glowOrder = -16;
    public int discOrder = -15;
    public int tintOrder = -6;
    public int warmOrder = 14;
    public int raysOrder = 12;
    public int fxOrder = 13;
    public int birdOrder = 15;

    // Milliseconds the last Render took on the main thread (both lanes).
    public float LastMs { get; private set; }
    // Milliseconds the slowest lane's last worker job took.
    public float RaysMs { get; private set; }
    // Earliest time (s from the ending's start) the sun shows over a lane's roofline.
    public float PeekTime { get; private set; } = 99f;
    public float LanePeek(int lane) => lanes != null && lane < lanes.Length ? lanes[lane].peek : 99f;
    public bool Ready { get; private set; }

    const int LH = 360, MockW = 1280;
    const float Ppu = 32f, Tau = Mathf.PI * 2f;
    const int DiscCells = 48;   // disc texture, 2 px cells

    int viewW, rw, rh, fw, fh;
    float centreShift;          // mockup x to view x
    float sunShift;             // chosen so the sun rises in open sky (px)
    int lastRaysFrame = -1;
    Sprite[] birdFrames;
    Sprite white;
    Texture2D warmTex;
    // Runtime objects this component made; destroyed in OnDestroy.
    readonly List<Object> owned = new List<Object>();
    Material screenMat, addMat, mulMat;
    Lane[] lanes;
    readonly Stopwatch watch = new Stopwatch();

    struct Beam { public float per, sway, a, w, len; }
    Beam[] beams;
    float[] weights;
    float weightSum;

    static readonly float[] Bayer = { 0, 8, 2, 10, 12, 4, 14, 6, 3, 11, 1, 9, 15, 7, 13, 5 };
    static readonly Color32[] RayPal =
    {
        new Color32(255, 190, 110, 0), new Color32(255, 206, 130, 64), new Color32(255, 224, 160, 104),
        new Color32(255, 240, 200, 150), new Color32(255, 252, 235, 195),
    };

    struct Job { public Vector2 sun; public float t, low, on, vis; public bool redrawGlow; }

    // One lane's masks, textures, renderers and worker job.
    class Lane
    {
        public int index;
        public Camera cam;
        public LaneSky sky;
        public float[] mask, light;          // city sky; city sky without the level's occluders
        public int[] roof, lightRoof;
        public float peek = 99f;
        public Texture2D glowTex, discTex, tintMulTex, tintTex, raysTex, fxTex;
        public Color32[] glowPx, discPx, tintMulPx, tintPx, raysPx, raysBack, fxPx;
        public float[] fxAcc;
        public Transform root;
        public SpriteRenderer glow, disc, tintMul, tint, rays, fx, warm;
        public SpriteRenderer[] birds, streaks;
        public System.Threading.Tasks.Task task;
        public Job job;
        public float jobMs;
        public Vector2Int glowKey = new Vector2Int(int.MinValue, 0);
    }

    // --- maths (as in the mockup) ---------------------------------------------

    public static float Seg(float t, float a, float b) => Mathf.Clamp01((t - a) / (b - a));
    public static float Eio(float p) => p < 0.5f ? 2f * p * p : 1f - Mathf.Pow(-2f * p + 2f, 2f) / 2f;
    public static float EOut(float p) => 1f - Mathf.Pow(1f - p, 3f);

    static float Rnd(int i, int k = 0)
    {
        double x = System.Math.Sin(i * 127.1 + k * 311.7 + 13.13) * 43758.5453;
        return (float)(x - System.Math.Floor(x));
    }

    static Color Hex(int rgb) => new Color32((byte)(rgb >> 16), (byte)(rgb >> 8), (byte)rgb, 255);

    // Sun centre in view pixels at time t (the same in both lanes).
    public Vector2 SunPath(float t)
    {
        float p = Seg(t, 6.6f, 12.4f);
        return new Vector2(Mathf.Lerp(560f, 720f, p) + centreShift + sunShift, Mathf.Lerp(336f, sunTop, EOut(p)));
    }

    // 1 on the horizon, 0 high up.
    public static float Low(Vector2 sun) => Mathf.Clamp01((sun.y - 64f) / 230f);

    // --- setup ------------------------------------------------------------------

    // Builds the masks and renderers. Call once the camera holds still.
    public void Begin()
    {
        if (Ready || cameras == null || cameras.Length == 0 || cameras[0] == null) return;
        var cam0 = cameras[0];
        float halfW = cam0.orthographicSize * cam0.aspect;
        viewW = Mathf.CeilToInt(halfW * 2f * Ppu / 4f) * 4;
        centreShift = (viewW - MockW) / 2f;
        rw = viewW / 4; rh = LH / 4;
        fw = viewW / 2; fh = LH / 2;

        if (skies == null || skies.Length < cameras.Length)
        {
            skies = new LaneSky[cameras.Length];
            foreach (var s in FindObjectsByType<LaneSky>())
                if (s.lane >= 0 && s.lane < skies.Length) skies[s.lane] = s;
        }

        lanes = new Lane[cameras.Length];
        for (int i = 0; i < cameras.Length; i++)
        {
            var l = lanes[i] = new Lane { index = i, cam = cameras[i], sky = skies[i] };
            if (l.cam != null) BuildMasks(l);
        }
        PlaceSun();

        beams = new Beam[14];
        for (int k = 0; k < beams.Length; k++)
            beams[k] = new Beam
            {
                per = 3f + Rnd(k, 46) * 3.5f,
                sway = (Rnd(k, 47) - 0.5f) * 0.5f,
                a = k / 14f * Tau + (Rnd(k, 41) - 0.5f) * 0.3f,
                w = 0.06f + Rnd(k, 42) * 0.09f,
                len = 0.55f + Rnd(k, 43) * 0.6f,
            };
        weights = new float[22];
        weightSum = 0f;
        for (int k = 0; k < weights.Length; k++) weightSum += weights[k] = Mathf.Pow(0.92f, k);

        warmTex = NewTex(1, rh, out var warmPx, "DawnWarm");
        for (int y = 0; y < rh; y++)
        {
            float p = (y + 0.5f) / rh;   // 0 at the top
            warmPx[rh - 1 - y] = new Color32(255, (byte)Mathf.Lerp(170, 150, p), (byte)Mathf.Lerp(90, 80, p), (byte)(p * 255));
        }
        warmTex.SetPixels32(warmPx);
        warmTex.Apply(false);

        var shader = Resources.Load<Shader>("Shaders/EndingBlend");
        screenMat = NewMat(shader, UnityEngine.Rendering.BlendMode.OneMinusDstColor, UnityEngine.Rendering.BlendMode.One, 0f);
        addMat = NewMat(shader, UnityEngine.Rendering.BlendMode.One, UnityEngine.Rendering.BlendMode.One, 0f);
        mulMat = NewMat(shader, UnityEngine.Rendering.BlendMode.DstColor, UnityEngine.Rendering.BlendMode.SrcColor, 1f);

        birdFrames = new Sprite[4];
        for (int f = 0; f < 4; f++) birdFrames[f] = BirdSprite(f);
        var wt = NewTex(1, 1, out var wpx, "DawnWhite");
        wpx[0] = new Color32(255, 255, 255, 255);
        wt.SetPixels32(wpx); wt.Apply(false);
        white = Sprite.Create(wt, new Rect(0, 0, 1, 1), new Vector2(0f, 1f), 1f);
        white.hideFlags = HideFlags.HideAndDontSave;
        owned.Add(white);

        var quarter = new Vector2(0f, 1f);
        var warmS = MakeSprite(warmTex, Ppu / 4f, quarter);
        foreach (var l in lanes)
        {
            if (l.cam == null) continue;
            l.glowTex = NewTex(rw, rh, out l.glowPx, "DawnGlow");
            l.discTex = NewTex(DiscCells, DiscCells, out l.discPx, "DawnDisc");
            l.tintMulTex = NewTex(rw, rh, out l.tintMulPx, "DawnTintMul");
            l.tintTex = NewTex(rw, rh, out l.tintPx, "DawnTint");
            l.raysTex = NewTex(rw, rh, out l.raysPx, "DawnRays");
            l.raysBack = new Color32[l.raysPx.Length];
            l.fxTex = NewTex(fw, fh, out l.fxPx, "DawnFx");
            l.fxAcc = new float[fw * fh * 3];

            var root = new GameObject("DawnSky_" + (l.index == 0 ? "Tick" : "Tock")).transform;
            root.SetParent(l.cam.transform, false);
            // Top-left corner of the view; children are placed in view pixels from there.
            root.localPosition = new Vector3(-viewW / 2f / Ppu, l.cam.orthographicSize, -l.cam.transform.position.z);
            l.root = root;
            l.glow = Layer(root, "Glow", MakeSprite(l.glowTex, Ppu / 4f, quarter), glowOrder, null);
            l.disc = Layer(root, "Disc", MakeSprite(l.discTex, Ppu / 2f, quarter), discOrder, null);
            l.tintMul = Layer(root, "TintSoft", MakeSprite(l.tintMulTex, Ppu / 4f, quarter), tintOrder, mulMat);
            l.tint = Layer(root, "Tint", MakeSprite(l.tintTex, Ppu / 4f, quarter), tintOrder + 1, null);
            l.rays = Layer(root, "Rays", MakeSprite(l.raysTex, Ppu / 4f, quarter), raysOrder, screenMat);
            l.fx = Layer(root, "Effects", MakeSprite(l.fxTex, Ppu / 2f, quarter), fxOrder, addMat);
            l.warm = Layer(root, "Warm", warmS, warmOrder, null);
            l.warm.transform.localScale = new Vector3(rw, 1f, 1f);
            l.birds = new SpriteRenderer[11];
            for (int b = 0; b < l.birds.Length; b++) l.birds[b] = Layer(root, "Bird", birdFrames[0], birdOrder, null);
            l.streaks = new SpriteRenderer[12];
            for (int s = 0; s < l.streaks.Length; s++)
            {
                l.streaks[s] = Layer(root, "Streak", white, birdOrder, null);
                l.streaks[s].transform.localScale = new Vector3(80f / Ppu, 2f / Ppu, 1f);
            }
        }
        Ready = true;
        Render(0f);
    }

    Texture2D NewTex(int w, int h, out Color32[] px, string name)
    {
        px = new Color32[w * h];
        var tex = new Texture2D(w, h, TextureFormat.RGBA32, false)
        {
            filterMode = FilterMode.Point,
            wrapMode = TextureWrapMode.Clamp,
            hideFlags = HideFlags.HideAndDontSave,
            name = name,
        };
        owned.Add(tex);
        return tex;
    }

    Sprite MakeSprite(Texture2D tex, float ppu, Vector2 pivot)
    {
        var s = Sprite.Create(tex, new Rect(0, 0, tex.width, tex.height), pivot, ppu, 0, SpriteMeshType.FullRect);
        s.hideFlags = HideFlags.HideAndDontSave;
        owned.Add(s);
        return s;
    }

    Material NewMat(Shader shader, UnityEngine.Rendering.BlendMode src, UnityEngine.Rendering.BlendMode dst, float mode)
    {
        var m = new Material(shader) { hideFlags = HideFlags.HideAndDontSave };
        m.SetFloat("_SrcBlend", (float)src);
        m.SetFloat("_DstBlend", (float)dst);
        m.SetFloat("_Mode", mode);
        owned.Add(m);
        return m;
    }

    static SpriteRenderer Layer(Transform parent, string name, Sprite sprite, int order, Material mat)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        var sr = go.AddComponent<SpriteRenderer>();
        sr.sprite = sprite;
        sr.sortingOrder = order;
        if (mat != null) sr.sharedMaterial = mat;
        sr.enabled = false;
        return sr;
    }

    // Bird of the mockup (bird()), 2 px blocks, frame f of the 4-frame flap;
    // pivot at the body's top-left pixel.
    Sprite BirdSprite(int f)
    {
        int[] wys = { -4, -2, 2, -2 };
        int wy = wys[f];
        const int ox = 8, oy = 6, w = 24, h = 14;
        var tex = NewTex(w, h, out var px, "DawnBird");
        var c = new Color32(0x3a, 0x2a, 0x3a, 255);
        void P(int x, int y, int pw, int ph)
        {
            x = Mathf.FloorToInt(x / 2f + 0.5f) * 2; y = Mathf.FloorToInt(y / 2f + 0.5f) * 2;
            for (int yy = y; yy < y + ph; yy++)
                for (int xx = x; xx < x + pw; xx++)
                {
                    int tx = xx + ox, ty = yy + oy;
                    if (tx >= 0 && tx < w && ty >= 0 && ty < h) px[(h - 1 - ty) * w + tx] = c;
                }
        }
        P(0, 0, 4, 4); P(4, 0, 2, 2);
        P(-4, wy / 2, 4, 2); P(-8, wy, 4, 2);
        P(4, wy / 2 + (wy > 0 ? 2 : 0), 2, 2); P(6, wy / 2, 4, 2); P(10, wy, 4, 2);
        tex.SetPixels32(px);
        tex.Apply(false);
        var s = Sprite.Create(tex, new Rect(0, 0, w, h), new Vector2(ox / (float)w, 1f - oy / (float)h), Ppu, 0, SpriteMeshType.FullRect);
        s.hideFlags = HideFlags.HideAndDontSave;
        owned.Add(s);
        return s;
    }

    // --- masks --------------------------------------------------------------------

    // The lane's city stills: the mid layer (the sky layer with the highest
    // sorting order) as one picture or as a row of sections; the day copies,
    // whose sky is transparent (all looks share the shapes).
    static List<SpriteRenderer> CityStills(LaneSky sky)
    {
        var list = new List<SpriteRenderer>();
        if (sky == null || sky.layers == null || sky.layers.Length == 0) return list;
        LaneSky.Layer mid = null;
        foreach (var layer in sky.layers)
            if (layer != null && (mid == null || layer.sortingOrder > mid.sortingOrder)) mid = layer;
        if (mid == null) return list;
        if (mid.sections != null && mid.sections.sections != null)
        {
            foreach (var s in mid.sections.sections)
                if (s != null && s.day != null) list.Add(s.day);
        }
        else if (mid.day != null) list.Add(mid.day);
        return list;
    }

    // Sky mask: 1 where the lane's city is see-through (4x4 pixel average of a
    // render of its city stills alone, where they are on screen now), 0 on
    // buildings and at or below the ground row. Light mask: the same without
    // what the level puts in front of the sky (gate housings, walls, the door,
    // lamps), from a render of the lane without its sky and players. Kept
    // apart because obstacles move (crusher heads): a hole in the tint where
    // one stood when the ending began would show.
    void BuildMasks(Lane l)
    {
        var stills = CityStills(l.sky);
        float[] city = stills.Count > 0 ? RenderCity(l.cam, stills) : null;
        l.mask = new float[rw * rh];
        for (int qy = 0; qy < rh; qy++)
            for (int qx = 0; qx < rw; qx++)
                l.mask[qy * rw + qx] = qy * 4 >= groundRow ? 0f : city == null ? 1f : 1f - city[qy * rw + qx];

        var cover = RenderOccluders(l);
        l.light = (float[])l.mask.Clone();
        for (int i = 0; i < l.light.Length; i++) l.light[i] *= 1f - cover[i];
        l.roof = Roofline(l.mask);
        l.lightRoof = Roofline(l.light);
    }

    int[] Roofline(float[] m)
    {
        var r = new int[rw];
        for (int x = 0; x < rw; x++)
        {
            int y = 0;
            while (y < rh && m[y * rw + x] > 0.5f) y++;
            r[x] = y;
        }
        return r;
    }

    // Coverage of the city stills alone: moved to an unused layer for one
    // render with the camera culling everything else; forced on, opaque.
    float[] RenderCity(Camera cam, List<SpriteRenderer> stills)
    {
        int layer = 31;
        while (layer > 7 && !string.IsNullOrEmpty(LayerMask.LayerToName(layer))) layer--;
        var saved = new List<(SpriteRenderer sr, int layer, bool enabled, Color color)>();
        int culling = cam.cullingMask;
        try
        {
            foreach (var sr in stills)
            {
                saved.Add((sr, sr.gameObject.layer, sr.enabled, sr.color));
                sr.gameObject.layer = layer;
                sr.enabled = true;
                sr.color = Color.white;
            }
            cam.cullingMask = 1 << layer;
            return RenderAlpha(cam);
        }
        finally
        {
            cam.cullingMask = culling;
            foreach (var s in saved)
            {
                s.sr.gameObject.layer = s.layer;
                s.sr.enabled = s.enabled;
                s.sr.color = s.color;
            }
        }
    }

    // Coverage of the level in the lane camera's view, without the lane's
    // whole sky (far and mid backgrounds, sections, sun, clouds, life) and
    // the players.
    float[] RenderOccluders(Lane l)
    {
        var hidden = new List<Renderer>();
        void Hide(Component root)
        {
            if (root == null) return;
            foreach (var r in root.GetComponentsInChildren<Renderer>())
                if (r.enabled) { r.enabled = false; hidden.Add(r); }
        }
        try
        {
            Hide(l.sky);
            if (GameFlow.Instance != null)
                foreach (var p in GameFlow.Instance.players) Hide(p);
            return RenderAlpha(l.cam);
        }
        finally
        {
            foreach (var r in hidden) r.enabled = true;
        }
    }

    // One render of the lane camera into a transparent target; returns the
    // alpha as quarter-res coverage (0-1, row 0 at the top). Post processing
    // is off for it: the URP asset does not keep alpha through post
    // processing, which the lane HUD's grading volumes switch on.
    float[] RenderAlpha(Camera cam)
    {
        int pw = viewW, ph = LH;
        var target = cam.targetTexture;
        var rect = cam.rect;
        var clear = cam.backgroundColor;
        var clearFlags = cam.clearFlags;
        var data = cam.GetUniversalAdditionalCameraData();
        bool post = data != null && data.renderPostProcessing;
        var prev = RenderTexture.active;
        RenderTexture rt = null;
        Texture2D read = null;
        Color32[] px;
        try
        {
            rt = RenderTexture.GetTemporary(pw, ph, 24, RenderTextureFormat.ARGB32);
            if (data != null) data.renderPostProcessing = false;
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0f, 0f, 0f, 0f);
            cam.rect = new Rect(0f, 0f, 1f, 1f);
            cam.targetTexture = rt;
            cam.Render();

            RenderTexture.active = rt;
            read = new Texture2D(pw, ph, TextureFormat.RGBA32, false);
            read.ReadPixels(new Rect(0, 0, pw, ph), 0, 0);
            read.Apply(false);
            px = read.GetPixels32();
        }
        finally
        {
            cam.targetTexture = target;
            cam.rect = rect;
            cam.backgroundColor = clear;
            cam.clearFlags = clearFlags;
            if (data != null) data.renderPostProcessing = post;
            RenderTexture.active = prev;
            if (rt != null) RenderTexture.ReleaseTemporary(rt);
            if (read != null) Destroy(read);
        }

        var cover = new float[rw * rh];
        for (int qy = 0; qy < rh; qy++)
            for (int qx = 0; qx < rw; qx++)
            {
                float a = 0f;
                for (int dy = 0; dy < 4; dy++)
                {
                    int y = ph - 1 - (qy * 4 + dy);
                    for (int dx = 0; dx < 4; dx++) a += px[y * pw + qx * 4 + dx].a / 255f;
                }
                cover[qy * rw + qx] = Mathf.Clamp01(a / 16f);
            }
        return cover;
    }

    // The mockup's path is fixed on its screen; here the level (gate housings,
    // the end wall, the door) and each lane's own skyline block the sky, so
    // the path (shared by both lanes) is shifted sideways to where the sun
    // peeks closest to targetPeek in both lanes and ends in open sky in both,
    // preferring small shifts. Lanes with different skylines peek at
    // different times; each lane's rays start at its own peek.
    void PlaceSun()
    {
        float best = float.MaxValue, bestShift = 0f;
        for (float shift = -480f; shift <= 320f; shift += 8f)
        {
            sunShift = shift;
            var end = SunPath(12.4f);
            float score = Mathf.Abs(shift) * 0.002f;
            foreach (var l in lanes)
            {
                if (l.lightRoof == null) continue;
                if (SunVisible(l, end) < 0.95f) score += 100f;
                score += Mathf.Abs(Peek(l, 0.05f) - targetPeek);
            }
            if (score < best) { best = score; bestShift = shift; }
        }
        sunShift = bestShift;
        PeekTime = 99f;
        foreach (var l in lanes)
        {
            if (l.lightRoof == null) continue;
            l.peek = Peek(l, 0.02f);
            PeekTime = Mathf.Min(PeekTime, l.peek);
        }
    }

    float Peek(Lane l, float step)
    {
        for (float tt = 6.6f; tt < 12.4f; tt += step)
            if (SunVisible(l, SunPath(tt)) > 0.03f) return tt;
        return 99f;
    }

    // Share (0-1) of a 7-cell disc round the sun above the lane's light roofline.
    float SunVisible(Lane l, Vector2 sun)
    {
        if (l.lightRoof == null) return 0f;
        float v = 0f; int n = 0;
        for (int dy = -7; dy <= 7; dy++)
            for (int dx = -7; dx <= 7; dx++)
            {
                if (dx * dx + dy * dy > 49) continue;
                int x = Mathf.FloorToInt(sun.x / 4f + dx + 0.5f), y = Mathf.FloorToInt(sun.y / 4f + dy + 0.5f);
                n++;
                if (x >= 0 && x < rw && y >= 0 && y < rh) v += y < l.lightRoof[x] ? 1f : 0f;
                else if (y < 0) v++;
            }
        return v / n;
    }

    // --- per frame ----------------------------------------------------------------

    public void Render(float t)
    {
        if (!Ready) return;
        watch.Restart();

        bool dawn = t > 6.5f;
        var sun = SunPath(t);
        float low = Low(sun);
        int raysFrame = Mathf.FloorToInt(t * raysFps);
        bool newFrame = dawn && raysFrame != lastRaysFrame;
        bool busy = false;
        foreach (var l in lanes) busy |= l.task != null && !l.task.IsCompleted;

        float worst = 0f;
        foreach (var l in lanes)
        {
            if (l.cam == null) continue;
            // A finished job: show what it drew.
            if (l.task != null && l.task.IsCompleted)
            {
                l.task = null;
                Upload(l);
            }
            worst = Mathf.Max(worst, l.jobMs);
            if (dawn) DrawDisc(l, sun, t, low);
        }
        RaysMs = worst;

        // Both lanes start a job together, once neither is still working.
        if (newFrame && !busy)
        {
            lastRaysFrame = raysFrame;
            float tr = raysFrame / raysFps;
            var rs = SunPath(tr);
            foreach (var l in lanes)
            {
                if (l.cam == null) continue;
                var key = new Vector2Int(Mathf.FloorToInt(rs.x / 4f), Mathf.FloorToInt(rs.y / 4f));
                var job = new Job
                {
                    sun = rs, t = tr, low = Low(rs), on = Seg(tr, 6.6f, 7.4f), vis = SunVisible(l, rs),
                    redrawGlow = key != l.glowKey,
                };
                l.glowKey = key;
                l.job = job;
                var lane = l;
                if (Application.platform == RuntimePlatform.WebGLPlayer)
                {
                    Compute(lane);
                    Upload(lane);
                }
                else
                    l.task = System.Threading.Tasks.Task.Run(() => Compute(lane));
            }
        }

        float warm = 0.22f * Seg(t, 6.6f, 9f);
        float tint = Seg(t, 6.5f, 7.5f);
        bool spin = t > 1.5f && t < 6.5f;
        foreach (var l in lanes)
        {
            if (l.cam == null) continue;
            l.glow.enabled = l.disc.enabled = l.rays.enabled = l.fx.enabled = dawn;
            l.tintMul.enabled = l.tint.enabled = dawn && tint > 0f;
            l.tintMul.color = new Color(1f, 1f, 1f, tint * 0.6f);
            l.tint.color = new Color(1f, 1f, 1f, tint * 0.18f);
            l.warm.enabled = warm > 0f;
            l.warm.color = new Color(1f, 1f, 1f, warm);
            if (dawn)
            {
                int ax = Mathf.FloorToInt(sun.x / 2f + 0.5f) - DiscCells / 2, ay = Mathf.FloorToInt(sun.y / 2f + 0.5f) - DiscCells / 2;
                l.disc.transform.localPosition = new Vector3(ax * 2f / Ppu, -ay * 2f / Ppu, 0f);
            }
            Birds(l, t);
            for (int i = 0; i < l.streaks.Length; i++)
            {
                var s = l.streaks[i];
                s.enabled = spin;
                if (!spin) continue;
                float x = Rnd(i, Mathf.FloorToInt(t * 20f) + l.index) * MockW + centreShift, y = Rnd(i, 4 + l.index) * LH;
                s.transform.localPosition = new Vector3(Snap(x) / Ppu, -Snap(y) / Ppu, 0f);
                s.color = new Color(1f, 1f, 1f, 0.35f);
            }
        }

        watch.Stop();
        LastMs = (float)watch.Elapsed.TotalMilliseconds;
    }

    // Worker side: glow and tint (when the sun moved), rays, then the effects
    // (the motes read the rays). Touches no Unity objects.
    void Compute(Lane l)
    {
        var sw = Stopwatch.StartNew();
        var j = l.job;
        if (j.redrawGlow)
        {
            DrawGlow(l, j.sun, j.low);
            DrawTint(l, j.sun, j.low);
        }
        DrawRays(l, j, l.raysBack);
        (l.raysPx, l.raysBack) = (l.raysBack, l.raysPx);
        DrawFx(l, j);
        l.jobMs = (float)sw.Elapsed.TotalMilliseconds;
    }

    // Main thread: the job's pixels into the textures.
    void Upload(Lane l)
    {
        if (l.job.redrawGlow)
        {
            l.glowTex.SetPixels32(l.glowPx); l.glowTex.Apply(false);
            l.tintTex.SetPixels32(l.tintPx); l.tintTex.Apply(false);
            l.tintMulTex.SetPixels32(l.tintMulPx); l.tintMulTex.Apply(false);
        }
        l.raysTex.SetPixels32(l.raysPx); l.raysTex.Apply(false);
        l.fxTex.SetPixels32(l.fxPx); l.fxTex.Apply(false);
    }

    static float Snap(float px) => Mathf.Round(px / 2f) * 2f;

    // A V of 11 birds from t 9.6, the same in both lanes.
    void Birds(Lane l, float t)
    {
        float dt = t - 9.6f;
        for (int i = 0; i < l.birds.Length; i++)
        {
            var b = l.birds[i];
            int row = (i + 1) / 2, side = i % 2 == 1 ? 1 : -1;
            float x = -80f + dt * 130f - row * 26f + Mathf.Sin(dt * 1.3f + i) * 3f + centreShift;
            float y = 120f - dt * 6f + row * 12f * side + Mathf.Sin(dt * 2.2f + row) * 3f;
            b.enabled = dt >= 0f && x > -40f && x < viewW + 40f;
            if (!b.enabled) continue;
            int f = ((int)Mathf.Floor(t * 9f + i * 1.3f)) % 4;
            b.sprite = birdFrames[f];
            b.transform.localPosition = new Vector3(Snap(x) / Ppu, -Snap(y) / Ppu, 0f);
        }
    }

    static float Dither(int x, int y) => (Bayer[(y & 3) * 4 + (x & 3)] + 0.5f) / 16f;

    // Soft radial glow behind the city, clipped to the sky above the roofline.
    void DrawGlow(Lane l, Vector2 sun, float low)
    {
        float r = (170f + 90f * low) / 4f, cx = sun.x / 4f, cy = sun.y / 4f;
        Color col = low > 0.5f ? new Color(1f, 110f / 255f, 60f / 255f, 0.6f) : new Color(1f, 215f / 255f, 150f / 255f, 0.55f);
        byte g = (byte)(col.g * 255), b = (byte)(col.b * 255);
        for (int y = 0; y < rh; y++)
            for (int x = 0; x < rw; x++)
            {
                int i = (rh - 1 - y) * rw + x;
                float dx = x + 0.5f - cx, dy = y + 0.5f - cy, d = System.MathF.Sqrt(dx * dx + dy * dy);
                float a = y < l.roof[x] && d < r ? 0.95f * col.a * (1f - d / r) : 0f;
                // 8 dithered levels: soft, but still pixels.
                float lv = System.MathF.Floor(a * 8f + Dither(x, y)) / 8f;
                l.glowPx[i] = lv <= 0f ? default : new Color32(255, g, b, (byte)(Mathf.Clamp01(lv) * 255));
            }
    }

    // Three nested discs in 2 px rows, squashed and shimmering low down;
    // red on the horizon, gold, then white. Clipped to the sky above the roofline.
    void DrawDisc(Lane l, Vector2 sun, float t, float low)
    {
        var px = l.discPx;
        System.Array.Clear(px, 0, px.Length);
        float R = 30f + low * 8f, sq = 1f - 0.25f * low;
        Color32 c0 = Hex(low > 0.55f ? 0xe8482a : low > 0.25f ? 0xff8a30 : 0xffc860);
        Color32 c1 = Hex(low > 0.55f ? 0xff7a3a : low > 0.25f ? 0xffb850 : 0xffe8a0);
        Color32 c2 = Hex(low > 0.55f ? 0xffa860 : low > 0.25f ? 0xffe08a : 0xfffbe8);
        int ax = Mathf.FloorToInt(sun.x / 2f + 0.5f) - DiscCells / 2, ay = Mathf.FloorToInt(sun.y / 2f + 0.5f) - DiscCells / 2;
        for (float dy = -R; dy <= R; dy += 2f)
        {
            float sh = Mathf.Round(Mathf.Sin(t * 7f + dy * 0.45f) * low * 3f) * 2f, yy = sun.y + dy * sq;
            int cy = Mathf.FloorToInt(yy / 2f + 0.5f);
            Row(R, c0); Row(R - 6f, c1); Row(R - 14f, c2);

            void Row(float r, Color32 c)
            {
                if (Mathf.Abs(dy) >= r) return;
                float hw = Mathf.Sqrt(r * r - dy * dy);
                int x0 = Mathf.FloorToInt((sun.x - hw + sh) / 2f + 0.5f), n = Mathf.Max(1, Mathf.RoundToInt(hw));
                for (int cx = x0; cx < x0 + n; cx++)
                {
                    int qx = cx / 2, qy = cy / 2;
                    if (cy < 0 || qx < 0 || qx >= rw || qy >= rh || qy >= l.roof[qx]) continue;
                    int tx = cx - ax, ty = cy - ay;
                    if (tx < 0 || ty < 0 || tx >= DiscCells || ty >= DiscCells) continue;
                    px[(DiscCells - 1 - ty) * DiscCells + tx] = c;
                }
            }
        }
        l.discTex.SetPixels32(px);
        l.discTex.Apply(false);
    }

    // Gradient round the sun (radius 0.75 of the lane width), cut to the sky.
    void DrawTint(Lane l, Vector2 sun, float low)
    {
        Color s0 = Color.Lerp(Hex(0xfff4c8), Hex(0xff6a38), low);
        Color s1 = Color.Lerp(Hex(0xffd08a), Hex(0xff8a6a), low);
        Color s2 = Color.Lerp(Hex(0x8ab0e8), Hex(0x5a3a8a), low);
        float cx = sun.x / 4f, cy = sun.y / 4f, r = rw * 0.75f;
        for (int y = 0; y < rh; y++)
            for (int x = 0; x < rw; x++)
            {
                int i = (rh - 1 - y) * rw + x;
                float m = l.mask[y * rw + x];
                if (m <= 0f) { l.tintPx[i] = l.tintMulPx[i] = default; continue; }
                float dx = x + 0.5f - cx, dy = y + 0.5f - cy;
                float d = System.MathF.Min(1f, System.MathF.Sqrt(dx * dx + dy * dy) / r);
                Color c = d < 0.35f ? Color.Lerp(s0, s1, d / 0.35f) : Color.Lerp(s1, s2, (d - 0.35f) / 0.65f);
                c.a = m;
                l.tintPx[i] = l.tintMulPx[i] = c;
            }
    }

    // Fills dst (rw x rh, bottom row first) with the lane's dithered rays.
    void DrawRays(Lane l, Job j, Color32[] dst)
    {
        float t = j.t, low = j.low, cx = j.sun.x / 4f, cy = j.sun.y / 4f, since = t - l.peek;
        float grow = EOut(Seg(since, 0f, 2.2f));
        float reach = Mathf.Lerp(0.75f, 1.15f, 1f - low) * rw * grow;
        float front = since > 0f && grow < 1f ? reach : -99f;
        float sweep = since > 0f ? since * 240f : -1f;
        float sweepFade = 1f - Seg(since, 0.5f, 4f), behind = 0.16f * (1f - low);
        var mask = l.mask; var light = l.light; var roof = l.roof;

        int n = beams.Length;
        var ang = new float[n]; var wid = new float[n]; var len = new float[n]; var env = new float[n];
        for (int k = 0; k < n; k++)
        {
            var b = beams[k];
            float ph = Mathf.Repeat((t + b.a * 3f) / b.per, 1f);
            env[k] = Mathf.Sin(Mathf.PI * ph);
            ang[k] = Mathf.Repeat(b.a + t * 0.045f + b.sway * Mathf.Sin(t * 0.4f + b.a * 5f) + Mathf.PI, Tau) - Mathf.PI;
            wid[k] = b.w * (0.7f + 0.5f * env[k]);
            len[k] = b.len * (0.45f + 0.55f * EOut(Mathf.Min(1f, ph * 2f)));
        }
        int N = weights.Length;

        for (int y = 0; y < rh; y++)
            for (int x = 0; x < rw; x++)
            {
                float dx = cx - x, dy = cy - y, dist = System.MathF.Sqrt(dx * dx + dy * dy), mh = light[y * rw + x];
                float v = 0f;
                if (dist < reach)
                {
                    float a = System.MathF.Atan2(-dy, -dx), beam = 0f;
                    for (int k = 0; k < n; k++)
                    {
                        float da = a - ang[k];
                        if (da > Mathf.PI) da -= Tau; else if (da < -Mathf.PI) da += Tau;
                        if (da < 0f) da = -da;
                        if (da > wid[k]) continue;
                        float core = 1f - da / wid[k], fall = 1f - dist / (reach * len[k]);
                        if (fall > 0f) beam = System.MathF.Max(beam, core * core * fall * env[k]);
                    }
                    if (beam > 0f) beam *= 0.8f + 0.2f * System.MathF.Sin(t * 1.7f + dist * 0.08f);
                    if (beam > 0.02f)
                    {
                        if (System.MathF.Abs(dist - front) < 3f) beam += 0.5f;
                        float acc = 0f, stepX = dx / N, stepY = dy / N, fx = x + 0.5f, fy = y + 0.5f;
                        for (int k = 0; k < N; k++, fx += stepX, fy += stepY)
                        {
                            float mv;
                            if (fy < 0f) mv = 1f;
                            else
                            {
                                int sx = fx < 0f ? -1 : (int)fx, sy = (int)fy;
                                mv = sx < 0 || sx >= rw || sy >= rh ? 0f : light[sy * rw + sx];
                            }
                            acc += mv * weights[k];
                        }
                        v = System.MathF.Pow(acc / weightSum, 1.6f) * (0.3f + 0.7f * mh) * beam * 1.35f;
                    }
                }
                v += j.vis * (System.MathF.Max(0f, 1f - dist / 22f) * 0.9f + System.MathF.Max(0f, 1f - dist / 60f) * 0.25f);
                if (sweep > 0f && mask[y * rw + x] < 0.5f && y * 4 < groundRow)
                {
                    float ax = System.MathF.Abs(x - cx), top = Mathf.Clamp01(1f - (y - roof[x]) / 12f);
                    float band = (ax - sweep) / 5f;
                    v += top * (System.MathF.Exp(-band * band) * 0.9f * sweepFade + (ax < sweep ? behind : 0f));
                }
                v *= j.on;
                int lvl = (int)System.MathF.Floor(v * 4f + Dither(x, y) * 0.9f);
                int i = (rh - 1 - y) * rw + x;
                dst[i] = lvl <= 0 ? default : RayPal[lvl > 4 ? 4 : lvl];
            }
    }

    // --- additive effects at 2 px resolution ------------------------------------

    void Add(float[] acc, int cx, int cy, Color c, float a)
    {
        if (a <= 0f || cx < 0 || cy < 0 || cx >= fw || cy >= fh) return;
        int i = ((fh - 1 - cy) * fw + cx) * 3;
        acc[i] += c.r * a; acc[i + 1] += c.g * a; acc[i + 2] += c.b * a;
    }

    // Mockup P(): a rectangle snapped to 2 px.
    void P(float[] acc, float x, float y, float w, float h, Color c, float a)
    {
        int x0 = Mathf.FloorToInt(x / 2f + 0.5f), y0 = Mathf.FloorToInt(y / 2f + 0.5f);
        int nw = Mathf.Max(1, Mathf.RoundToInt(w / 2f)), nh = Mathf.Max(1, Mathf.RoundToInt(h / 2f));
        int xa = Mathf.Max(0, x0), xb = Mathf.Min(fw, x0 + nw), ya = Mathf.Max(0, y0), yb = Mathf.Min(fh, y0 + nh);
        for (int yy = ya; yy < yb; yy++)
            for (int xx = xa; xx < xb; xx++) Add(acc, xx, yy, c, a);
    }

    // Disc (filled) or ring (only cells further out than inner) in 2 px blocks.
    void Blob(float[] acc, float cx, float cy, float R, float inner, Color c, float a)
    {
        for (float y = -R; y <= R; y += 2f)
            for (float x = -R; x <= R; x += 2f)
            {
                float d = Mathf.Sqrt(x * x + y * y);
                if (d > R || d <= inner) continue;
                P(acc, cx + x, cy + y, 2f, 2f, c, a);
            }
    }

    // Radial glow from c (alpha a) at the centre to nothing at r.
    void Glow(float[] acc, float x, float y, float r, Color c, float a)
    {
        if (a <= 0f || r <= 0f) return;
        int x0 = Mathf.Max(0, Mathf.FloorToInt((x - r) / 2f)), x1 = Mathf.Min(fw - 1, Mathf.CeilToInt((x + r) / 2f));
        int y0 = Mathf.Max(0, Mathf.FloorToInt((y - r) / 2f)), y1 = Mathf.Min(fh - 1, Mathf.CeilToInt((y + r) / 2f));
        for (int cy = y0; cy <= y1; cy++)
            for (int cx = x0; cx <= x1; cx++)
            {
                float dx = cx * 2f + 1f - x, dy = cy * 2f + 1f - y, d = Mathf.Sqrt(dx * dx + dy * dy);
                if (d < r) Add(acc, cx, cy, c, a * (1f - d / r));
            }
    }

    void DrawFx(Lane l, Job j)
    {
        var acc = l.fxAcc;
        System.Array.Clear(acc, 0, acc.Length);
        float t = j.t, low = j.low, on = j.on, vis = j.vis, since = t - l.peek;
        float sx = j.sun.x, sy = j.sun.y;

        // Glint: a four-point star where the sun breaks the skyline.
        if (since > 0f && since < 1.2f)
        {
            float a = Mathf.Sin(Mathf.PI * since / 1.2f), L = a * 110f, gx = sx, gy = sy - (30f + low * 8f) * 0.8f, rot = since * 0.4f;
            Color hot = Color.white, warm = Hex(0xffe6a0);
            for (int k = 0; k < 4; k++)
            {
                float th = rot + k * Mathf.PI / 2f;
                for (float r = 0f; r < L; r += 2f)
                {
                    float wv = Mathf.Max(2f, 6f * (1f - r / L));
                    P(acc, gx + Mathf.Cos(th) * r - wv / 2f, gy + Mathf.Sin(th) * r - wv / 2f, wv, wv, r < L * 0.3f ? hot : warm, a);
                }
            }
            Glow(acc, gx, gy, 90f * a, new Color(1f, 250f / 255f, 220f / 255f), a);
        }

        // Corona.
        Color corona = Hex(0xffd890);
        for (int k = 0; k < 3; k++)
            Blob(acc, sx, sy, 44f + k * 14f, 40f + k * 14f, corona, on * vis * (0.18f - k * 0.05f) * (0.8f + 0.2f * Mathf.Sin(t * 2f + k)));

        // Lens flare ghosts on the line from the sun through the lane centre, and a flat streak.
        if (vis > 0.05f)
        {
            float vx = viewW / 2f - sx, vy = LH / 2f - sy, ga = on * vis * 0.28f;
            (float k, float r, int c, bool fill)[] ghosts =
            {
                (0.35f, 6f, 0xffd27a, true), (0.62f, 10f, 0x9fe8ff, false), (0.9f, 18f, 0xff9ad0, false),
                (1.25f, 5f, 0xffe08a, true), (1.6f, 26f, 0xa8fff4, false), (1.95f, 12f, 0xffb070, true),
            };
            foreach (var g in ghosts)
                Blob(acc, sx + vx * g.k, sy + vy * g.k, g.r, g.fill ? -1f : g.r - 4f, Hex(g.c), ga);
            float sl = 420f * vis;
            P(acc, sx - sl, sy - 1f, sl * 2f, 2f, Hex(0xffe6b0), on * vis * 0.35f);
            P(acc, sx - sl * 0.6f, sy - 3f, sl * 1.2f, 6f, Hex(0xffd27a), on * vis * 0.15f);
        }

        // Motes: bright only inside lit beams.
        Color bright = Hex(0xfff6d8), faint = Hex(0xffd27a);
        var rays = l.raysPx;
        for (int i = 0; i < 70; i++)
        {
            float ph = Mathf.Repeat(t * 0.12f + Rnd(i, 52), 1f);
            float x = sx + (Rnd(i, 53) - 0.5f) * MockW * 1.1f + Mathf.Sin(t * 0.6f + i) * 14f, y = LH * (1f - ph) - 20f;
            int qx = Mathf.FloorToInt(x / 4f), qy = Mathf.FloorToInt(y / 4f);
            float lit = qx >= 0 && qx < rw && qy >= 0 && qy < rh ? rays[(rh - 1 - qy) * rw + qx].a / 195f : 0f;
            P(acc, x, y, 2f, 2f, lit > 0.4f ? bright : faint, on * (0.15f + 0.85f * lit) * Mathf.Sin(ph * Mathf.PI));
        }

        var px = l.fxPx;
        for (int i = 0, k = 0; i < px.Length; i++, k += 3)
            px[i] = new Color32(To8(acc[k]), To8(acc[k + 1]), To8(acc[k + 2]), 255);
    }

    static byte To8(float v) => (byte)(v >= 1f ? 255 : v <= 0f ? 0 : v * 255f);

    void OnDestroy()
    {
        if (lanes != null)
            foreach (var l in lanes) l?.task?.Wait();
        foreach (var o in owned)
            if (o != null) Destroy(o);
        owned.Clear();
    }
}
