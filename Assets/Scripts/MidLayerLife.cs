using System.Collections.Generic;
using UnityEngine;

// Life on one tiled background layer (the mid buildings), running on lane
// time: pigeons on the arcade railing by day (now and then one flutters off
// and comes back), leaves falling from the ivy to the street, a pennant waving
// on the clock tower's spire, candle-lit windows flickering at night and
// fireflies drifting along the street at night. Positions are pixels of the
// background picture, from its top-left; each fixed item is drawn on the two
// repeats of the picture nearest the lane camera (a tiled sprite repeats the
// picture from its rect's bottom-left corner). A child of the layer's
// background, so it scrolls and rises with its parallax. Sprites are built at
// runtime (PixelArt) and never saved.
[DefaultExecutionOrder(-30)]
public class MidLayerLife : MonoBehaviour
{
    [Tooltip("The background whose picture the positions refer to.")]
    public SpriteRenderer background;
    [Tooltip("Off for a row of background sections: positions are pixels of the whole row from its top-left, this object sits at the row's bottom-left, and nothing repeats.")]
    public bool tiled = true;
    [Tooltip("Size in pixels of the row of sections (untiled only).")]
    public Vector2 pictureSize;
    public Transform cameraTransform;
    [Tooltip("Lane whose clock runs the layer (0 Tick, 1 Tock).")]
    public int lane = -1;
    public int seed = 1;
    [Tooltip("World y of the lane's ground surface, where the leaves settle.")]
    public float groundY = 0f;
    [Tooltip("Pigeons' feet on the railing, picture pixels.")]
    public Vector2[] pigeons;
    [Tooltip("Ivy clusters the leaves fall from, picture pixels.")]
    public RectInt[] ivy;
    [Tooltip("Top of the spire the pennant flies from, picture pixels.")]
    public Vector2 pennant;
    [Tooltip("More spire tips with pennants, picture pixels.")]
    public Vector2[] pennants;
    [Tooltip("Lit windows that flicker at night, picture pixels.")]
    public RectInt[] windows;
    [Tooltip("Tint of the pigeons, leaves and pennant at night.")]
    public Color nightTint = new Color(0.62f, 0.6f, 0.8f);
    [Tooltip("Window overlay when a candle flares and when it gutters.")]
    public Color candleFlare = new Color32(255, 240, 190, 255);
    public Color candleDim = new Color32(110, 52, 30, 255);
    public int fireflyCount = 16;
    public int sortingOrder = -6;

    class Pigeon { public Vector2 home, offset, velocity; public int state; public float timer, phase; public SpriteRenderer[] sr; }
    class Leaf { public SpriteRenderer sr; public Vector2 pos; public float phase, fall, rest, settle; public int colour; }
    class Fly { public SpriteRenderer sr; public float x, y, phase, speed, blink; }

    readonly List<Pigeon> birds = new List<Pigeon>();
    readonly List<Leaf> leaves = new List<Leaf>();
    readonly List<Fly> flies = new List<Fly>();
    SpriteRenderer[][] pennantSr;
    Vector2[] pennantTips;
    int copies = 2;
    const float Reach = 26f;   // local units from the camera beyond which fixed items are not updated
    SpriteRenderer[][] windowSr;
    float[] windowPhase;
    Sprite[] sit, fly, pennantFrames, leafSprites;
    Sprite candleSprite, fireflySprite;
    System.Random rng;
    float t, lastLaneTime = -1f, leafTimer, day = 1f, night, ppu = 32f, tileWidth, groundLocal;
    Vector2 picture, origin;

    float R(float a, float b) => a + (float)rng.NextDouble() * (b - a);

    public void Show(LaneSky.Look from, LaneSky.Look to, float blend) => (day, night) = PixelArt.DayNight(from, to, blend);

    // Picture pixel (from the top-left) to this layer's local units, on the
    // first repeat of the picture (at the rect's left edge).
    Vector2 Local(float px, float py) => new Vector2(origin.x + px / ppu, origin.y + (picture.y - py) / ppu);

    void Start()
    {
        if (tiled && (background == null || background.sprite == null)) { enabled = false; return; }
        rng = new System.Random(seed);
        if (tiled)
        {
            var src = background.sprite;
            ppu = src.pixelsPerUnit;
            picture = src.rect.size;
            // Bottom-left corner of the tiled rect, in local units.
            origin = -src.pivot / src.rect.size * background.size;
            tileWidth = picture.x / ppu;
        }
        else
        {
            ppu = 32f;
            picture = pictureSize;
            origin = Vector2.zero;
            tileWidth = picture.x / ppu;
            copies = 1;
        }
        groundLocal = transform.InverseTransformPoint(new Vector3(0f, groundY, 0f)).y;

        var feet = new Vector2(0f, 0f);
        Color32 body = new Color32(91, 84, 102, 255), head = new Color32(139, 132, 150, 255);
        System.Func<char, Color32> pigeon = c => c == 'H' ? head : body;
        sit = new[]
        {
            PixelArt.Make(new[] { ".HB.", "BBBB", "BBB." }, pigeon, feet),
            PixelArt.Make(new[] { ".HB..", "BBBBB" }, pigeon, feet),   // pecking
        };
        fly = new[]
        {
            PixelArt.Make(new[] { "X...X", ".X.X.", "..X.." }, body, feet),
            PixelArt.Make(new[] { "XX.XX", "..X.." }, body, feet),
            PixelArt.Make(new[] { "..X..", ".X.X.", "X...X" }, body, feet),
        };
        foreach (var p in pigeons ?? new Vector2[0])
        {
            var b = new Pigeon { home = Local(p.x, p.y), timer = R(2f, 8f), phase = R(0f, 6f), sr = new SpriteRenderer[copies] };
            for (int i = 0; i < copies; i++) b.sr[i] = PixelArt.Renderer(transform, "Pigeon", sortingOrder);
            birds.Add(b);
        }

        pennantFrames = new Sprite[8];
        for (int f = 0; f < 8; f++) pennantFrames[f] = PennantFrame(f / 8f * 2f * Mathf.PI);
        var tips = new List<Vector2>();
        if (pennant != Vector2.zero) tips.Add(pennant);
        if (pennants != null) tips.AddRange(pennants);
        pennantTips = tips.ToArray();
        pennantSr = new SpriteRenderer[pennantTips.Length][];
        for (int k = 0; k < pennantTips.Length; k++)
        {
            pennantSr[k] = new SpriteRenderer[copies];
            for (int i = 0; i < copies; i++) pennantSr[k][i] = PixelArt.Renderer(transform, "Pennant", sortingOrder);
        }

        Color32[] leafColours = { new Color32(217, 130, 43, 255), new Color32(181, 83, 31, 255), new Color32(232, 163, 58, 255), new Color32(194, 98, 42, 255) };
        leafSprites = new Sprite[8];
        for (int c = 0; c < 4; c++)
        {
            leafSprites[c * 2] = PixelArt.Make(new[] { "XX" }, leafColours[c], new Vector2(0.5f, 0.5f));
            leafSprites[c * 2 + 1] = PixelArt.Make(new[] { "X", "X" }, leafColours[c], new Vector2(0.5f, 0.5f));
        }

        candleSprite = PixelArt.Make(new[] { "X" }, new Color32(255, 255, 255, 255), Vector2.zero);
        int n = windows != null ? windows.Length : 0;
        windowSr = new SpriteRenderer[n][];
        windowPhase = new float[n];
        for (int w = 0; w < n; w++)
        {
            windowPhase[w] = R(0f, 50f);
            windowSr[w] = new SpriteRenderer[copies];
            for (int i = 0; i < copies; i++)
            {
                var sr = PixelArt.Renderer(transform, "Candle", sortingOrder - 2);
                sr.sprite = candleSprite;
                sr.transform.localScale = new Vector3(windows[w].width, windows[w].height, 1f);
                windowSr[w][i] = sr;
            }
        }

        fireflySprite = PixelArt.Make(new[] { "..o..", ".oio.", "oiXio", ".oio.", "..o.." },
            c => c == 'X' ? new Color32(255, 250, 170, 255) : c == 'i' ? new Color32(255, 216, 74, 120) : new Color32(255, 216, 74, 46),
            new Vector2(0.5f, 0.5f));
        for (int i = 0; i < fireflyCount; i++)
        {
            var sr = PixelArt.Renderer(transform, "Firefly", sortingOrder + 1);
            sr.sprite = fireflySprite;
            flies.Add(new Fly { sr = sr, x = R(0f, 1f), y = R(1.2f, 3.6f), phase = R(0f, 100f), speed = R(0.4f, 0.9f), blink = R(0f, 10f) });
        }
    }

    // The pennant at one phase of its wave: pole in column 0 from the spire
    // tip up, flag to the right, drooping and narrowing towards the tip.
    Sprite PennantFrame(float phase)
    {
        var rows = new char[14][];
        for (int y = 0; y < 14; y++) { rows[y] = new char[11]; for (int x = 0; x < 11; x++) rows[y][x] = '.'; }
        for (int y = 5; y < 14; y++) rows[y][0] = 'P';   // pole, 9 px
        for (int i = 0; i < 9; i++)
        {
            int h = Mathf.Max(1, 4 - Mathf.FloorToInt(i / 2.5f));
            int wy = Mathf.RoundToInt(Mathf.Sin(phase - i * 0.7f) * 1.2f) + (i > 4 ? 1 : 0);
            for (int k = 0; k < h; k++)
            {
                int y = 5 + 1 + wy + k;
                if (y >= 0 && y < 14) rows[y][1 + i] = k == h - 1 && h > 1 ? 'D' : 'F';
            }
        }
        var lines = new string[14];
        for (int y = 0; y < 14; y++) lines[y] = new string(rows[y]);
        return PixelArt.Make(lines, c => c == 'P' ? new Color32(45, 32, 48, 255) : c == 'D' ? new Color32(150, 48, 44, 255) : new Color32(200, 70, 60, 255), Vector2.zero);
    }

    // Local x of the two repeats of a picture position nearest the camera.
    (float a, float b) Repeats(float x, float camLocal)
    {
        if (!tiled) return (x, x);
        float k = Mathf.Round((camLocal - x) / tileWidth);
        float a = x + k * tileWidth;
        return (a, a + (camLocal > a ? tileWidth : -tileWidth));
    }

    void LateUpdate()
    {
        if (rng == null) return;
        float dt = PixelArt.LaneDelta(lane, ref lastLaneTime);
        t += dt;
        float camLocal = cameraTransform != null ? transform.InverseTransformPoint(cameraTransform.position).x : 0f;
        var tint = Color.Lerp(Color.white, nightTint, night);

        UpdatePigeons(dt, camLocal, tint);
        UpdatePennant(camLocal, tint);
        UpdateLeaves(dt, camLocal, tint);
        UpdateWindows(camLocal);
        UpdateFireflies(camLocal);
    }

    void UpdatePigeons(float dt, float camLocal, Color tint)
    {
        float alpha = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.25f, 0.6f, day));
        foreach (var b in birds)
        {
            b.timer -= dt;
            b.phase += dt;
            if (b.state == 0 && b.timer <= 0f)
            {
                b.state = 1;
                b.velocity = new Vector2(R(-30f, 30f), 35f) / ppu;
                b.timer = R(2.5f, 4f);
            }
            else if (b.state == 1)
            {
                b.offset += b.velocity * dt;
                if (b.timer <= 1.6f) b.state = 2;
            }
            else if (b.state == 2)
            {
                b.offset = Vector2.Lerp(b.offset, Vector2.zero, Mathf.Min(1f, dt * 2.2f));
                if (b.offset.magnitude < 0.6f / ppu) { b.offset = Vector2.zero; b.state = 0; b.timer = R(5f, 11f); }
            }
            Sprite s = b.state == 0 ? sit[Mathf.Sin(b.phase * 2.3f) > 0.8f ? 1 : 0] : fly[Mathf.FloorToInt(t * 12f) % 3];
            var (xa, xb) = Repeats(b.home.x, camLocal);
            bool near = tiled || Mathf.Abs(xa - camLocal) < Reach;
            for (int i = 0; i < copies; i++)
            {
                var sr = b.sr[i];
                sr.enabled = alpha > 0.01f && near;
                sr.sprite = s;
                sr.color = PixelArt.Alpha(tint, alpha);
                sr.transform.localPosition = new Vector3((i == 0 ? xa : xb) + b.offset.x, b.home.y + b.offset.y, 0f);
            }
        }
    }

    void UpdatePennant(float camLocal, Color tint)
    {
        for (int k = 0; k < pennantTips.Length; k++)
        {
            var tip = Local(pennantTips[k].x, pennantTips[k].y);
            var (xa, xb) = Repeats(tip.x, camLocal);
            // Each flag waves at its own phase.
            var frame = pennantFrames[(Mathf.FloorToInt(t * 6.4f) + k * 3) % 8];
            bool near = tiled || Mathf.Abs(xa - camLocal) < Reach;
            for (int i = 0; i < copies; i++)
            {
                var sr = pennantSr[k][i];
                sr.enabled = near;
                if (!near) continue;
                sr.sprite = frame;
                sr.color = tint;
                sr.transform.localPosition = new Vector3(i == 0 ? xa : xb, tip.y, 0f);
            }
        }
    }

    // A leaf every 0.4-1.1 lane seconds from a random ivy cluster on the
    // repeat nearest the camera; it sways down, settles on the street and
    // fades after a while.
    void UpdateLeaves(float dt, float camLocal, Color tint)
    {
        if (ivy != null && ivy.Length > 0)
        {
            leafTimer -= dt;
            if (leafTimer <= 0f)
            {
                leafTimer = R(0.4f, 1.1f);
                var r = ivy[rng.Next(ivy.Length)];
                // Untiled, only clusters near the camera drop leaves.
                for (int tries = 0; !tiled && tries < 8 && Mathf.Abs(Local(r.center.x, 0f).x - camLocal) > Reach; tries++)
                    r = ivy[rng.Next(ivy.Length)];
                var p = Local(R(r.xMin, r.xMax), R(r.yMin, r.yMax));
                p.x = Repeats(p.x, camLocal).a;
                var sr = PixelArt.Renderer(transform, "Leaf", sortingOrder);
                leaves.Add(new Leaf { sr = sr, pos = p, phase = R(0f, 6f), fall = R(6f, 10f) / ppu, colour = rng.Next(4), settle = groundLocal - R(1f, 3f) / ppu });
            }
        }
        for (int i = leaves.Count - 1; i >= 0; i--)
        {
            var l = leaves[i];
            if (l.pos.y > l.settle)
            {
                l.phase += dt * 3f;
                l.pos.y = Mathf.Max(l.settle, l.pos.y - l.fall * dt);
                l.pos.x += (Mathf.Sin(l.phase) * 10f + 3f) / ppu * dt;
            }
            else l.rest += dt;
            if (l.rest > 4f)
            {
                Destroy(l.sr.gameObject);
                leaves.RemoveAt(i);
                continue;
            }
            l.sr.sprite = leafSprites[l.colour * 2 + (Mathf.Sin(l.phase) > 0f ? 0 : 1)];
            l.sr.color = PixelArt.Alpha(tint, l.rest > 2f ? 1f - (l.rest - 2f) / 2f : 1f);
            l.sr.transform.localPosition = l.pos;
        }
    }

    void UpdateWindows(float camLocal)
    {
        for (int w = 0; w < windowSr.Length; w++)
        {
            var r = windows[w];
            var corner = Local(r.x, r.y + r.height);
            var (xa, xb) = Repeats(corner.x, camLocal);
            if (!tiled && Mathf.Abs(xa - camLocal) > Reach)
            {
                if (windowSr[w][0].enabled) windowSr[w][0].enabled = false;
                continue;
            }
            // -1 gutters (the window dims), +1 flares (it brightens).
            float flicker = Mathf.Sin(t * 9f + windowPhase[w]) * Mathf.Sin(t * 3.7f + windowPhase[w] * 2f);
            var colour = flicker > 0f ? candleFlare : candleDim;
            float a = night * Mathf.Abs(flicker) * (flicker > 0f ? 0.45f : 0.55f);
            for (int i = 0; i < copies; i++)
            {
                var sr = windowSr[w][i];
                sr.enabled = a > 0.01f;
                sr.color = PixelArt.Alpha(colour, a);
                sr.transform.localPosition = new Vector3(i == 0 ? xa : xb, corner.y, 0f);
            }
        }
    }

    // Fireflies wander along the street in front of the buildings, spread
    // over a strip around the camera that wraps as it scrolls.
    void UpdateFireflies(float camLocal)
    {
        float span = tiled ? tileWidth * 2f : 40f;
        foreach (var f in flies)
        {
            float a = t * f.speed + f.phase;
            float g = Mathf.Max(0f, Mathf.Sin(t * 2.2f + f.blink)) * night;
            f.sr.enabled = g > 0.15f;
            if (!f.sr.enabled) continue;
            float x = camLocal + Mathf.Repeat(f.x * span - camLocal, span) - span / 2f;
            x += (Mathf.Sin(a) * 14f + Mathf.Sin(a * 2.3f) * 5f) / ppu;
            float y = groundLocal + f.y + Mathf.Sin(a * 1.6f) * 6f / ppu;
            f.sr.color = PixelArt.Alpha(Color.white, g);
            f.sr.transform.localPosition = new Vector3(x, y, 0f);
        }
    }
}
