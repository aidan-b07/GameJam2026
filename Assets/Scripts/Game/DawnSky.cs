using System.Diagnostics;
using UnityEngine;

// The light show of the ending (mockups/ending/README.md, "Dawn of a New Day";
// numbers from ending.html, scene C2). DawnEnding calls Render(t) every frame
// with the seconds since the ending started; everything here is a pure
// function of t, so stepping or skipping is safe.
//
// Works in art pixels of one lane view (y down from the lane's top edge, x
// from its left edge; the mockup's 1280-wide view is centred in a wider one).
// Both lanes show the same picture (same camera X, same city layer), so every
// texture is computed once and drawn in both lanes:
// - sky mask (quarter resolution) from the city layer's alpha as it is on
//   screen when the ending starts, and the roofline (first non-sky cell from
//   the top of each column);
// - sun glow (quarter res, dithered) and sun disc (2 px rows, heat shimmer),
//   behind the city and clipped to the sky above the roofline;
// - sky tint round the sun, cut to the sky: a 2x-multiply pass (soft-light
//   stand-in) and a normal pass;
// - god rays (quarter res, 4 dithered light levels, screen blend): 14 beams on
//   their own cycles, shadowed by marching towards the sun through the mask,
//   plus the sun's core and the first-light sweep over the rooftops;
// - additive effects at 2 px resolution: glint, corona, lens flare, streak
//   and motes;
// - a warm gradient over each lane, the V of birds (same frame in both lanes)
//   and the white speed streaks of the clock spin.
// Textures are point filtered and never saved.
[DefaultExecutionOrder(60)]
public class DawnSky : MonoBehaviour
{
    [Tooltip("Lane cameras: Tick, Tock.")]
    public Camera[] cameras;
    [Tooltip("The city layer (mid background, day copy) of Tick's lane: its transparent sky is the sky mask.")]
    public SpriteRenderer midLayer;
    [Tooltip("View row (art px from the lane's top) of the ground surface's top; rows below are never sky.")]
    public int groundRow = 313;
    [Tooltip("Where the sun ends its climb (view px from the lane's top). The mockup's 64 hides Tick's sun under the letterbox.")]
    public float sunTop = 104f;
    [Tooltip("Rays and effects are redrawn at this rate (stepped like the sprites); the rest every frame.")]
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

    // Milliseconds the last Render took (CPU, both lanes).
    public float LastMs { get; private set; }
    // Seconds from the ending's start at which the sun first shows over the roofline.
    public float PeekTime { get; private set; } = 99f;
    public bool Ready { get; private set; }

    const int LH = 360, MockW = 1280;
    const float Ppu = 32f, Tau = Mathf.PI * 2f;

    int viewW, rw, rh, fw, fh;
    float centreShift;          // mockup x to view x
    float sunShift;             // chosen so the sun rises in open sky (px)
    int lastRaysFrame = -1;
    Vector2Int glowKey = new Vector2Int(int.MinValue, 0);
    float[] mask;               // city sky, rw * rh, row 0 at the top: tint, glow and disc
    int[] roof;                 // per quarter column, of mask
    float[] light;              // mask without what the level puts in front: rays and visibility
    int[] lightRoof;
    Texture2D glowTex, discTex, tintMulTex, tintTex, raysTex, fxTex, warmTex;
    Color32[] glowPx, discPx, tintMulPx, tintPx, raysPx, fxPx;
    float[] fxAcc;
    Sprite[] birdFrames;
    Sprite white;
    Material screenMat, addMat, mulMat;
    LaneFx[] lanes;
    readonly Stopwatch watch = new Stopwatch();

    const int DiscCells = 48;   // disc texture, 2 px cells

    class LaneFx
    {
        public Transform root;
        public SpriteRenderer glow, disc, tintMul, tint, rays, fx, warm;
        public SpriteRenderer[] birds, streaks;
    }

    struct Beam { public float per, sway, a, w, len; }
    Beam[] beams;

    static readonly float[] Bayer = { 0, 8, 2, 10, 12, 4, 14, 6, 3, 11, 1, 9, 15, 7, 13, 5 };
    static readonly Color32[] RayPal =
    {
        new Color32(255, 190, 110, 0), new Color32(255, 206, 130, 64), new Color32(255, 224, 160, 104),
        new Color32(255, 240, 200, 150), new Color32(255, 252, 235, 195),
    };

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

    // Sun centre in view pixels at time t.
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

        BuildMask(cam0);
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

        glowTex = NewTex(rw, rh, out glowPx, "DawnGlow");
        discTex = NewTex(DiscCells, DiscCells, out discPx, "DawnDisc");
        tintMulTex = NewTex(rw, rh, out tintMulPx, "DawnTintMul");
        tintTex = NewTex(rw, rh, out tintPx, "DawnTint");
        raysTex = NewTex(rw, rh, out raysPx, "DawnRays");
        fxTex = NewTex(fw, fh, out fxPx, "DawnFx");
        fxAcc = new float[fw * fh * 3];
        warmTex = NewTex(1, rh, out var warmPx, "DawnWarm");
        for (int y = 0; y < rh; y++)
        {
            float p = (y + 0.5f) / rh;   // 0 at the top
            warmPx[(rh - 1 - y) * 1] = new Color32(255, (byte)Mathf.Lerp(170, 150, p), (byte)Mathf.Lerp(90, 80, p), (byte)(p * 255));
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

        var quarter = new Vector2(0f, 1f);
        Sprite S(Texture2D tex, float ppu, Vector2 pivot)
        {
            var s = Sprite.Create(tex, new Rect(0, 0, tex.width, tex.height), pivot, ppu, 0, SpriteMeshType.FullRect);
            s.hideFlags = HideFlags.HideAndDontSave;
            return s;
        }
        var glowS = S(glowTex, Ppu / 4f, quarter);
        var discS = S(discTex, Ppu / 2f, quarter);
        var tintMulS = S(tintMulTex, Ppu / 4f, quarter);
        var tintS = S(tintTex, Ppu / 4f, quarter);
        var raysS = S(raysTex, Ppu / 4f, quarter);
        var fxS = S(fxTex, Ppu / 2f, quarter);
        var warmS = S(warmTex, Ppu / 4f, quarter);

        lanes = new LaneFx[cameras.Length];
        for (int i = 0; i < cameras.Length; i++)
        {
            var cam = cameras[i];
            var root = new GameObject("DawnSky_" + (i == 0 ? "Tick" : "Tock")).transform;
            root.SetParent(cam.transform, false);
            // Top-left corner of the view; children are placed in view pixels from there.
            root.localPosition = new Vector3(-viewW / 2f / Ppu, cam.orthographicSize, -cam.transform.position.z);
            var l = new LaneFx { root = root };
            l.glow = Layer(root, "Glow", glowS, glowOrder, null);
            l.disc = Layer(root, "Disc", discS, discOrder, null);
            l.tintMul = Layer(root, "TintSoft", tintMulS, tintOrder, mulMat);
            l.tint = Layer(root, "Tint", tintS, tintOrder + 1, null);
            l.rays = Layer(root, "Rays", raysS, raysOrder, screenMat);
            l.fx = Layer(root, "Effects", fxS, fxOrder, addMat);
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
            lanes[i] = l;
        }
        Ready = true;
        Render(0f);
    }

    static Texture2D NewTex(int w, int h, out Color32[] px, string name)
    {
        px = new Color32[w * h];
        return new Texture2D(w, h, TextureFormat.RGBA32, false)
        {
            filterMode = FilterMode.Point,
            wrapMode = TextureWrapMode.Clamp,
            hideFlags = HideFlags.HideAndDontSave,
            name = name,
        };
    }

    static Material NewMat(Shader shader, UnityEngine.Rendering.BlendMode src, UnityEngine.Rendering.BlendMode dst, float mode)
    {
        var m = new Material(shader) { hideFlags = HideFlags.HideAndDontSave };
        m.SetFloat("_SrcBlend", (float)src);
        m.SetFloat("_DstBlend", (float)dst);
        m.SetFloat("_Mode", mode);
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
    static Sprite BirdSprite(int f)
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
        return s;
    }

    // Sky mask from the city layer's alpha where it is on screen now: 1 where
    // the layer is see-through (4x4 pixel average), 0 on buildings and at or
    // below the ground row.
    void BuildMask(Camera cam)
    {
        mask = new float[rw * rh];
        Color32[] src = null;
        int pw = 0, ph = 0;
        var sprite = midLayer != null ? midLayer.sprite : null;
        if (sprite != null)
        {
            var tex = sprite.texture;
            var rect = sprite.textureRect;
            pw = (int)rect.width; ph = (int)rect.height;
            var rt = RenderTexture.GetTemporary(tex.width, tex.height, 0, RenderTextureFormat.ARGB32);
            Graphics.Blit(tex, rt);
            var prev = RenderTexture.active;
            RenderTexture.active = rt;
            var read = new Texture2D(pw, ph, TextureFormat.RGBA32, false);
            read.ReadPixels(new Rect(rect.x, rect.y, pw, ph), 0, 0);
            read.Apply(false);
            RenderTexture.active = prev;
            RenderTexture.ReleaseTemporary(rt);
            src = read.GetPixels32();
            Destroy(read);
        }

        float viewLeft = cam.transform.position.x - viewW / 2f / Ppu;
        float viewTop = cam.transform.position.y + cam.orthographicSize;
        float rectLeft = 0f, pictureTop = viewTop;
        if (midLayer != null)
        {
            var size = midLayer.drawMode == SpriteDrawMode.Simple ? (Vector2)midLayer.bounds.size : midLayer.size;
            // Lane-relative: Tick's layer, Tick's camera.
            rectLeft = midLayer.transform.position.x - size.x / 2f;
            pictureTop = midLayer.transform.position.y + size.y / 2f;
        }
        int rowShift = Mathf.RoundToInt((pictureTop - viewTop) * Ppu);
        int colShift = Mathf.RoundToInt((viewLeft - rectLeft) * Ppu);

        for (int qy = 0; qy < rh; qy++)
            for (int qx = 0; qx < rw; qx++)
            {
                float m = 0f;
                if (qy * 4 < groundRow)
                {
                    if (src == null) m = 1f;
                    else
                    {
                        float a = 0f;
                        for (int dy = 0; dy < 4; dy++)
                        {
                            int py = qy * 4 + dy + rowShift;   // from the picture's top
                            for (int dx = 0; dx < 4; dx++)
                            {
                                if (py < 0 || py >= ph) continue;   // above the picture: sky
                                int px = ((qx * 4 + dx + colShift) % pw + pw) % pw;
                                a += src[(ph - 1 - py) * pw + px].a / 255f;
                            }
                        }
                        m = 1f - a / 16f;
                    }
                }
                mask[qy * rw + qx] = m;
            }
        // The level in front of the sky (gate housings, walls, the door, lamps)
        // blocks light too: render the lane without its sky and players and
        // take what covers the sky out of the mask.
        // Kept apart from the city mask: obstacles move (crusher heads), and a
        // hole in the tint where one stood when the ending began would show.
        var cover = Occluders(cam);
        light = (float[])mask.Clone();
        if (cover != null)
            for (int i = 0; i < light.Length; i++) light[i] *= 1f - cover[i];
        roof = Roofline(mask);
        lightRoof = Roofline(light);
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

    // Coverage (0-1, quarter res, row 0 at the top) of the level in the lane
    // camera's view, without the sky (the city layer's parent) and the players.
    float[] Occluders(Camera cam)
    {
        var hidden = new System.Collections.Generic.List<Renderer>();
        void Hide(Transform root)
        {
            if (root == null) return;
            foreach (var r in root.GetComponentsInChildren<Renderer>())
                if (r.enabled) { r.enabled = false; hidden.Add(r); }
        }
        Hide(midLayer != null ? midLayer.transform.parent : null);
        if (GameFlow.Instance != null)
            foreach (var p in GameFlow.Instance.players) Hide(p);

        int pw = viewW, ph = LH;
        var rt = RenderTexture.GetTemporary(pw, ph, 24, RenderTextureFormat.ARGB32);
        var target = cam.targetTexture;
        var rect = cam.rect;
        var clear = cam.backgroundColor;
        cam.backgroundColor = new Color(0f, 0f, 0f, 0f);
        cam.rect = new Rect(0f, 0f, 1f, 1f);
        cam.targetTexture = rt;
        cam.Render();
        cam.targetTexture = target;
        cam.rect = rect;
        cam.backgroundColor = clear;
        foreach (var r in hidden) r.enabled = true;

        var prev = RenderTexture.active;
        RenderTexture.active = rt;
        var read = new Texture2D(pw, ph, TextureFormat.RGBA32, false);
        read.ReadPixels(new Rect(0, 0, pw, ph), 0, 0);
        read.Apply(false);
        RenderTexture.active = prev;
        RenderTexture.ReleaseTemporary(rt);
        var px = read.GetPixels32();
        Destroy(read);

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
    // the end wall, the door) also blocks the sky, so the path is shifted
    // sideways to where the sun peeks closest to targetPeek and ends in open
    // sky, preferring small shifts.
    void PlaceSun()
    {
        float best = float.MaxValue, bestShift = 0f, bestPeek = 99f;
        for (float shift = -480f; shift <= 320f; shift += 8f)
        {
            sunShift = shift;
            if (SunVisible(SunPath(12.4f)) < 0.95f) continue;
            float peek = Peek(0.05f);
            float score = Mathf.Abs(peek - targetPeek) + Mathf.Abs(shift) * 0.002f;
            if (score < best) { best = score; bestShift = shift; bestPeek = peek; }
        }
        sunShift = bestShift;
        PeekTime = bestPeek < 99f ? Peek(0.02f) : 99f;
    }

    float Peek(float step)
    {
        for (float tt = 6.6f; tt < 12.4f; tt += step)
            if (SunVisible(SunPath(tt)) > 0.03f) return tt;
        return 99f;
    }

    // Share (0-1) of a 7-cell disc round the sun above the roofline.
    public float SunVisible(Vector2 sun)
    {
        if (lightRoof == null) return 0f;
        float v = 0f; int n = 0;
        for (int dy = -7; dy <= 7; dy++)
            for (int dx = -7; dx <= 7; dx++)
            {
                if (dx * dx + dy * dy > 49) continue;
                int x = Mathf.FloorToInt(sun.x / 4f + dx + 0.5f), y = Mathf.FloorToInt(sun.y / 4f + dy + 0.5f);
                n++;
                if (x >= 0 && x < rw && y >= 0 && y < rh) v += y < lightRoof[x] ? 1f : 0f;
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
        float vis = SunVisible(sun);

        if (dawn)
        {
            // Glow and tint only change when the sun moves a quarter cell.
            var key = new Vector2Int(Mathf.FloorToInt(sun.x / 4f), Mathf.FloorToInt(sun.y / 4f));
            if (key != glowKey)
            {
                glowKey = key;
                DrawGlow(sun, low);
                DrawTint(sun, low);
            }
            DrawDisc(sun, t, low);
            int raysFrame = Mathf.FloorToInt(t * raysFps);
            if (raysFrame != lastRaysFrame)
            {
                lastRaysFrame = raysFrame;
                float tr = raysFrame / raysFps;
                var rs = SunPath(tr);
                DrawRays(rs, tr, Low(rs), Seg(tr, 6.6f, 7.4f), SunVisible(rs));
                DrawFx(rs, tr, Low(rs), Seg(tr, 6.6f, 7.4f), SunVisible(rs));
            }
        }

        float warm = 0.22f * Seg(t, 6.6f, 9f);
        float tint = Seg(t, 6.5f, 7.5f);
        bool spin = t > 1.5f && t < 6.5f;
        for (int li = 0; li < lanes.Length; li++)
        {
            var l = lanes[li];
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
                float x = Rnd(i, Mathf.FloorToInt(t * 20f) + li) * MockW + centreShift, y = Rnd(i, 4 + li) * LH;
                s.transform.localPosition = new Vector3(Snap(x) / Ppu, -Snap(y) / Ppu, 0f);
                s.color = new Color(1f, 1f, 1f, 0.35f);
            }
        }

        watch.Stop();
        LastMs = (float)watch.Elapsed.TotalMilliseconds;
    }

    static float Snap(float px) => Mathf.Round(px / 2f) * 2f;

    // A V of 11 birds from t 9.6, the same in both lanes.
    void Birds(LaneFx l, float t)
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

    float Dither(int x, int y) => (Bayer[(y & 3) * 4 + (x & 3)] + 0.5f) / 16f;

    // Soft radial glow behind the city, clipped to the sky above the roofline.
    void DrawGlow(Vector2 sun, float low)
    {
        float r = (170f + 90f * low) / 4f, cx = sun.x / 4f, cy = sun.y / 4f;
        Color col = low > 0.5f ? new Color(1f, 110f / 255f, 60f / 255f, 0.6f) : new Color(1f, 215f / 255f, 150f / 255f, 0.55f);
        for (int y = 0; y < rh; y++)
            for (int x = 0; x < rw; x++)
            {
                int i = (rh - 1 - y) * rw + x;
                float d = Mathf.Sqrt((x + 0.5f - cx) * (x + 0.5f - cx) + (y + 0.5f - cy) * (y + 0.5f - cy));
                float a = y < roof[x] && d < r ? 0.95f * col.a * (1f - d / r) : 0f;
                // 8 dithered levels: soft, but still pixels.
                float lv = Mathf.Floor(a * 8f + Dither(x, y)) / 8f;
                glowPx[i] = lv <= 0f ? default : new Color32(255, (byte)(col.g * 255), (byte)(col.b * 255), (byte)(Mathf.Clamp01(lv) * 255));
            }
        glowTex.SetPixels32(glowPx);
        glowTex.Apply(false);
    }

    // Three nested discs in 2 px rows, squashed and shimmering low down;
    // red on the horizon, gold, then white. Clipped to the sky above the roofline.
    void DrawDisc(Vector2 sun, float t, float low)
    {
        System.Array.Clear(discPx, 0, discPx.Length);
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
                    if (cy < 0 || qx < 0 || qx >= rw || qy >= rh || qy >= roof[qx]) continue;
                    int tx = cx - ax, ty = cy - ay;
                    if (tx < 0 || ty < 0 || tx >= DiscCells || ty >= DiscCells) continue;
                    discPx[(DiscCells - 1 - ty) * DiscCells + tx] = c;
                }
            }
        }
        discTex.SetPixels32(discPx);
        discTex.Apply(false);
    }

    // Gradient round the sun (radius 0.75 of the lane width), cut to the sky.
    void DrawTint(Vector2 sun, float low)
    {
        Color s0 = Color.Lerp(Hex(0xfff4c8), Hex(0xff6a38), low);
        Color s1 = Color.Lerp(Hex(0xffd08a), Hex(0xff8a6a), low);
        Color s2 = Color.Lerp(Hex(0x8ab0e8), Hex(0x5a3a8a), low);
        float cx = sun.x / 4f, cy = sun.y / 4f, r = rw * 0.75f;
        for (int y = 0; y < rh; y++)
            for (int x = 0; x < rw; x++)
            {
                int i = (rh - 1 - y) * rw + x;
                float m = mask[y * rw + x];
                if (m <= 0f) { tintPx[i] = tintMulPx[i] = default; continue; }
                float d = Mathf.Min(1f, Mathf.Sqrt((x + 0.5f - cx) * (x + 0.5f - cx) + (y + 0.5f - cy) * (y + 0.5f - cy)) / r);
                Color c = d < 0.35f ? Color.Lerp(s0, s1, d / 0.35f) : Color.Lerp(s1, s2, (d - 0.35f) / 0.65f);
                c.a = m;
                tintPx[i] = tintMulPx[i] = c;
            }
        tintTex.SetPixels32(tintPx);
        tintTex.Apply(false);
        tintMulTex.SetPixels32(tintMulPx);
        tintMulTex.Apply(false);
    }

    void DrawRays(Vector2 sun, float t, float low, float on, float vis)
    {
        float cx = sun.x / 4f, cy = sun.y / 4f, since = t - PeekTime;
        float grow = EOut(Seg(since, 0f, 2.2f));
        float reach = Mathf.Lerp(0.75f, 1.15f, 1f - low) * rw * grow;
        float front = since > 0f && grow < 1f ? reach : -99f;
        float sweep = since > 0f ? since * 240f : -1f;
        float sweepFade = 1f - Seg(since, 0.5f, 4f), behind = 0.16f * (1f - low);

        int n = beams.Length;
        var ang = new float[n]; var wid = new float[n]; var len = new float[n]; var env = new float[n];
        for (int k = 0; k < n; k++)
        {
            var b = beams[k];
            float l = Mathf.Repeat((t + b.a * 3f) / b.per, 1f);
            env[k] = Mathf.Sin(Mathf.PI * l);
            ang[k] = Mathf.Repeat(b.a + t * 0.045f + b.sway * Mathf.Sin(t * 0.4f + b.a * 5f) + Mathf.PI, Tau) - Mathf.PI;
            wid[k] = b.w * (0.7f + 0.5f * env[k]);
            len[k] = b.len * (0.45f + 0.55f * EOut(Mathf.Min(1f, l * 2f)));
        }
        const int N = 22;
        float wsum = 0f;
        for (int k = 0; k < N; k++) wsum += Mathf.Pow(0.92f, k);
        var weights = new float[N];
        for (int k = 0; k < N; k++) weights[k] = Mathf.Pow(0.92f, k);

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
                        if (Mathf.Abs(dist - front) < 3f) beam += 0.5f;
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
                        v = System.MathF.Pow(acc / wsum, 1.6f) * (0.3f + 0.7f * mh) * beam * 1.35f;
                    }
                }
                v += vis * (Mathf.Max(0f, 1f - dist / 22f) * 0.9f + Mathf.Max(0f, 1f - dist / 60f) * 0.25f);
                if (sweep > 0f && mask[y * rw + x] < 0.5f && y * 4 < groundRow)
                {
                    float ax = Mathf.Abs(x - cx), top = Mathf.Clamp01(1f - (y - roof[x]) / 12f);
                    float band = (ax - sweep) / 5f;
                    v += top * (System.MathF.Exp(-band * band) * 0.9f * sweepFade + (ax < sweep ? behind : 0f));
                }
                v *= on;
                int lvl = Mathf.FloorToInt(v * 4f + Dither(x, y) * 0.9f);
                int i = (rh - 1 - y) * rw + x;
                raysPx[i] = lvl <= 0 ? default : RayPal[Mathf.Min(4, lvl)];
            }
        raysTex.SetPixels32(raysPx);
        raysTex.Apply(false);
    }

    // --- additive effects at 2 px resolution ------------------------------------

    void Add(int cx, int cy, Color c, float a)
    {
        if (a <= 0f || cx < 0 || cy < 0 || cx >= fw || cy >= fh) return;
        int i = ((fh - 1 - cy) * fw + cx) * 3;
        fxAcc[i] += c.r * a; fxAcc[i + 1] += c.g * a; fxAcc[i + 2] += c.b * a;
    }

    // Mockup P(): a rectangle snapped to 2 px.
    void P(float x, float y, float w, float h, Color c, float a)
    {
        int x0 = Mathf.FloorToInt(x / 2f + 0.5f), y0 = Mathf.FloorToInt(y / 2f + 0.5f);
        int nw = Mathf.Max(1, Mathf.RoundToInt(w / 2f)), nh = Mathf.Max(1, Mathf.RoundToInt(h / 2f));
        int xa = Mathf.Max(0, x0), xb = Mathf.Min(fw, x0 + nw), ya = Mathf.Max(0, y0), yb = Mathf.Min(fh, y0 + nh);
        for (int yy = ya; yy < yb; yy++)
            for (int xx = xa; xx < xb; xx++) Add(xx, yy, c, a);
    }

    // Disc (filled) or ring (only cells further out than inner) in 2 px blocks.
    void Blob(float cx, float cy, float R, float inner, Color c, float a)
    {
        for (float y = -R; y <= R; y += 2f)
            for (float x = -R; x <= R; x += 2f)
            {
                float d = Mathf.Sqrt(x * x + y * y);
                if (d > R || d <= inner) continue;
                P(cx + x, cy + y, 2f, 2f, c, a);
            }
    }

    // Radial glow from c (alpha a) at the centre to nothing at r.
    void Glow(float x, float y, float r, Color c, float a)
    {
        if (a <= 0f || r <= 0f) return;
        int x0 = Mathf.Max(0, Mathf.FloorToInt((x - r) / 2f)), x1 = Mathf.Min(fw - 1, Mathf.CeilToInt((x + r) / 2f));
        int y0 = Mathf.Max(0, Mathf.FloorToInt((y - r) / 2f)), y1 = Mathf.Min(fh - 1, Mathf.CeilToInt((y + r) / 2f));
        for (int cy = y0; cy <= y1; cy++)
            for (int cx = x0; cx <= x1; cx++)
            {
                float dx = cx * 2f + 1f - x, dy = cy * 2f + 1f - y, d = Mathf.Sqrt(dx * dx + dy * dy);
                if (d < r) Add(cx, cy, c, a * (1f - d / r));
            }
    }

    void DrawFx(Vector2 sun, float t, float low, float on, float vis)
    {
        System.Array.Clear(fxAcc, 0, fxAcc.Length);
        float since = t - PeekTime;
        float sx = sun.x, sy = sun.y;

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
                    P(gx + Mathf.Cos(th) * r - wv / 2f, gy + Mathf.Sin(th) * r - wv / 2f, wv, wv, r < L * 0.3f ? hot : warm, a);
                }
            }
            Glow(gx, gy, 90f * a, new Color(1f, 250f / 255f, 220f / 255f), a);
        }

        // Corona.
        Color corona = Hex(0xffd890);
        for (int k = 0; k < 3; k++)
            Blob(sx, sy, 44f + k * 14f, 40f + k * 14f, corona, on * vis * (0.18f - k * 0.05f) * (0.8f + 0.2f * Mathf.Sin(t * 2f + k)));

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
                Blob(sx + vx * g.k, sy + vy * g.k, g.r, g.fill ? -1f : g.r - 4f, Hex(g.c), ga);
            float sl = 420f * vis;
            P(sx - sl, sy - 1f, sl * 2f, 2f, Hex(0xffe6b0), on * vis * 0.35f);
            P(sx - sl * 0.6f, sy - 3f, sl * 1.2f, 6f, Hex(0xffd27a), on * vis * 0.15f);
        }

        // Motes: bright only inside lit beams.
        Color bright = Hex(0xfff6d8), faint = Hex(0xffd27a);
        for (int i = 0; i < 70; i++)
        {
            float ph = Mathf.Repeat(t * 0.12f + Rnd(i, 52), 1f);
            float x = sx + (Rnd(i, 53) - 0.5f) * MockW * 1.1f + Mathf.Sin(t * 0.6f + i) * 14f, y = LH * (1f - ph) - 20f;
            int qx = Mathf.FloorToInt(x / 4f), qy = Mathf.FloorToInt(y / 4f);
            float lit = qx >= 0 && qx < rw && qy >= 0 && qy < rh ? raysPx[(rh - 1 - qy) * rw + qx].a / 195f : 0f;
            P(x, y, 2f, 2f, lit > 0.4f ? bright : faint, on * (0.15f + 0.85f * lit) * Mathf.Sin(ph * Mathf.PI));
        }

        for (int i = 0, j = 0; i < fxPx.Length; i++, j += 3)
            fxPx[i] = new Color32(To8(fxAcc[j]), To8(fxAcc[j + 1]), To8(fxAcc[j + 2]), 255);
        fxTex.SetPixels32(fxPx);
        fxTex.Apply(false);
    }

    static byte To8(float v) => (byte)(v >= 1f ? 255 : v <= 0f ? 0 : v * 255f);

    void OnDestroy()
    {
        foreach (var tex in new[] { glowTex, discTex, tintMulTex, tintTex, raysTex, fxTex, warmTex })
            if (tex != null) Destroy(tex);
        foreach (var m in new[] { screenMat, addMat, mulMat })
            if (m != null) Destroy(m);
    }
}
