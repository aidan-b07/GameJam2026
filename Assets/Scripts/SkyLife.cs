using System.Collections.Generic;
using UnityEngine;

// Life in one lane's sky, running on lane time (a frozen lane holds it still
// in mid-flap): flocks of birds and a passing airship by day, bats, shooting
// stars and twinkling stars by night, each fading with the look LaneSky passes
// in. Everything sits in the clear band above the far skyline, like the
// clouds, placed around the lane camera and rising with it. Sprites are built
// at runtime from pixel patterns (PixelArt) and never saved.
[DefaultExecutionOrder(-30)]
public class SkyLife : MonoBehaviour
{
    [Tooltip("Lane whose clock runs the sky (0 Tick, 1 Tock).")]
    public int lane = -1;
    [Tooltip("The lane camera everything is placed around.")]
    public Transform cameraTransform;
    public float bandTop = 9.3f, bandBottom = 5.15f;
    public int seed = 1;

    [Header("Birds (day)")]
    [Tooltip("Lane seconds between flocks.")]
    public Vector2 flockInterval = new Vector2(7f, 12f);
    [Tooltip("Units per lane second.")]
    public Vector2 birdSpeed = new Vector2(0.9f, 1.2f);
    [Range(0f, 1f)] public float birdDepth = 0.85f;
    public Color birdColour = Rgb(58, 42, 54);
    public int birdOrder = -12;

    [Header("Airship")]
    public Vector2 airshipInterval = new Vector2(20f, 45f);
    public float airshipSpeed = 0.13f;
    [Range(0f, 1f)] public float airshipDepth = 0.95f;
    public Color airshipDay = Rgb(170, 172, 198), airshipDusk = Rgb(196, 140, 172), airshipNight = Rgb(80, 68, 124);
    public int airshipOrder = -14;

    [Header("Night")]
    public int batCount = 3;
    public Color batColour = Rgb(26, 18, 41);
    public Vector2 shootingInterval = new Vector2(4f, 9f);
    public int twinkleCount = 14;
    [Tooltip("Behind the sun and moon (-15).")]
    public int starOrder = -16;

    static Color Rgb(int r, int g, int b) => new Color32((byte)r, (byte)g, (byte)b, 255);

    class Bird { public SpriteRenderer sr; public float p, y, speed, flap, bob; }
    class Bat { public SpriteRenderer sr; public float u, y, radius, phase; }
    class Star { public SpriteRenderer sr; public float u, y, phase, rate; }

    readonly List<Bird> birds = new List<Bird>();
    readonly List<Bat> bats = new List<Bat>();
    readonly List<Star> stars = new List<Star>();
    Sprite[] birdFrames, batFrames, airshipFrames;
    Sprite starDot, starCross, streak, lampPixel;
    SpriteRenderer airship, airshipLamp, shooting;
    float airshipP, airshipTimer, flockTimer, shootTimer, shootAge = -1f, shootX, shootY;
    bool airshipOn;
    System.Random rng;
    float t, lastLaneTime = -1f, cameraRestY, day = 1f, night;
    Color airshipTint = Color.white;

    float R(float a, float b) => a + (float)rng.NextDouble() * (b - a);
    float R(Vector2 range) => R(range.x, range.y);

    public void Show(LaneSky.Look from, LaneSky.Look to, float blend)
    {
        (day, night) = PixelArt.DayNight(from, to, blend);
        var a = Tint(from);
        var b = Tint(to);
        airshipTint = Color.Lerp(a, b, blend);
    }

    Color Tint(LaneSky.Look look) => look == LaneSky.Look.Day ? airshipDay : look == LaneSky.Look.Dusk ? airshipDusk : airshipNight;

    void Start()
    {
        rng = new System.Random(seed);
        if (cameraTransform != null) cameraRestY = cameraTransform.position.y;
        var mid = new Vector2(0.5f, 0.5f);
        birdFrames = new[]
        {
            PixelArt.Make(new[] { "X...X", ".X.X.", "..X.." }, birdColour, mid),
            PixelArt.Make(new[] { "XX.XX", "..X.." }, birdColour, mid),
            PixelArt.Make(new[] { "..X..", ".X.X.", "X...X" }, birdColour, mid),
        };
        batFrames = new[]
        {
            PixelArt.Make(new[] { "X.....X", "XX.X.XX", ".XXXXX.", "...X..." }, batColour, mid),
            PixelArt.Make(new[] { "..XXX..", ".XXXXX.", "XX.X.XX", "X.....X" }, batColour, mid),
        };
        // Shades of white, tinted per look; P is the propeller (bright or dark).
        string[] ship =
        {
            "....BBBBBBBBBBBBBB....",
            "DDBBHHHHHHHHHHHHBBBB..",
            "..BBBBBBBBBBBBBBBBBBP.",
            "..BBBBBBBBBBBBBBBBBBP.",
            "DDBBBBBBBBBBBBBBBBBB..",
            "....BBBBBBBBBBBBBB....",
            "........DDDDDD........",
            "........DDDDDD........",
        };
        System.Func<char, Color32> shade = c =>
            c == 'H' ? new Color32(255, 255, 255, 255) : c == 'B' ? new Color32(214, 214, 214, 255) : new Color32(160, 160, 160, 255);
        airshipFrames = new[]
        {
            PixelArt.Make(ship, c => c == 'P' ? new Color32(255, 250, 220, 255) : shade(c), mid),
            PixelArt.Make(ship, c => c == 'P' ? new Color32(110, 106, 130, 255) : shade(c), mid),
        };
        lampPixel = PixelArt.Make(new[] { "X" }, new Color32(255, 106, 74, 255), mid);
        starDot = PixelArt.Make(new[] { "X" }, new Color32(255, 251, 232, 255), mid);
        starCross = PixelArt.Make(new[] { ".A.", "AXA", ".A." },
            c => c == 'X' ? new Color32(255, 251, 232, 255) : new Color32(255, 251, 232, 140), mid);
        // A falling star: bright head at the bottom left, tail fading up and to the right.
        var rows = new string[14];
        for (int i = 0; i < 14; i++)
        {
            var line = new char[36];
            for (int x = 0; x < 36; x++) line[x] = '.';
            line[Mathf.RoundToInt(i * 2.5f)] = (char)('a' + i);
            rows[13 - i] = new string(line);
        }
        streak = PixelArt.Make(rows, c => new Color32(255, 248, 220, (byte)(255 * (1f - (c - 'a') / 14f))), Vector2.zero);

        airship = PixelArt.Renderer(transform, "Airship", airshipOrder);
        airship.enabled = false;
        airshipLamp = PixelArt.Renderer(airship.transform, "Lamp", airshipOrder);
        airshipLamp.sprite = lampPixel;
        airshipLamp.transform.localPosition = new Vector3(0f, -5f / 32f, 0f);
        shooting = PixelArt.Renderer(transform, "ShootingStar", starOrder);
        shooting.sprite = streak;
        shooting.enabled = false;

        for (int i = 0; i < batCount; i++)
        {
            var sr = PixelArt.Renderer(transform, "Bat", birdOrder);
            bats.Add(new Bat { sr = sr, u = (i + 0.5f) / batCount * 2f - 1f + R(-0.15f, 0.15f), y = R(bandBottom + 0.8f, bandTop - 1f), radius = R(0.8f, 1.9f), phase = R(0f, 20f) });
        }
        for (int i = 0; i < twinkleCount; i++)
        {
            var sr = PixelArt.Renderer(transform, "Star", starOrder);
            stars.Add(new Star { sr = sr, u = R(-1f, 1f), y = Mathf.Round(R(bandBottom + 1.2f, bandTop - 0.1f) * 32f) / 32f, phase = R(0f, 10f), rate = R(0.5f, 1.3f) });
        }
        flockTimer = 1f;
        airshipTimer = R(4f, 12f);
        shootTimer = 2f;
    }

    void LateUpdate()
    {
        if (rng == null) return;
        float dt = PixelArt.LaneDelta(lane, ref lastLaneTime);
        t += dt;

        float camX = cameraTransform != null ? cameraTransform.position.x : transform.position.x;
        float baseY = transform.position.y + (cameraTransform != null ? cameraTransform.position.y - cameraRestY : 0f);
        float halfView = 20f;
        var cam = cameraTransform != null ? cameraTransform.GetComponent<Camera>() : null;
        if (cam != null && cam.orthographic) halfView = cam.orthographicSize * cam.aspect;

        UpdateBirds(dt, camX, baseY, halfView);
        UpdateAirship(dt, camX, baseY, halfView);
        UpdateNight(dt, camX, baseY, halfView);
    }

    // A V of 4-6 birds crosses left to right; they finish crossing even if
    // night falls meanwhile, but no new flock starts after dark.
    void UpdateBirds(float dt, float camX, float baseY, float halfView)
    {
        float frame = camX * (1f - birdDepth);
        flockTimer -= dt;
        if (flockTimer <= 0f)
        {
            flockTimer = R(flockInterval);
            if (day > 0.4f)
            {
                int n = 4 + rng.Next(3);
                float y = R(bandBottom + 0.8f, bandTop - 0.8f), v = R(birdSpeed);
                for (int i = 0; i < n; i++)
                {
                    int k = (i + 1) / 2;
                    var sr = PixelArt.Renderer(transform, "Bird", birdOrder);
                    birds.Add(new Bird { sr = sr, p = frame - halfView - 1f - k * 0.25f, y = y - k * 5f / 32f - (i % 2) / 32f, speed = v, flap = R(0f, 3f), bob = R(0f, 6f) });
                }
            }
        }
        for (int i = birds.Count - 1; i >= 0; i--)
        {
            var b = birds[i];
            b.p += b.speed * dt;
            b.flap += dt * 8f;
            b.bob += dt * 2f;
            if (b.p - frame > halfView + 1.5f)
            {
                Destroy(b.sr.gameObject);
                birds.RemoveAt(i);
                continue;
            }
            int f = Mathf.FloorToInt(b.flap) % 4;
            b.sr.sprite = birdFrames[f == 3 ? 1 : f];
            b.sr.transform.position = new Vector3(camX * birdDepth + b.p, baseY + b.y + Mathf.Sin(b.bob) * 1.2f / 32f, 0f);
        }
    }

    // Now and then an airship drifts across, high up; a red lamp blinks under
    // the gondola after dark.
    void UpdateAirship(float dt, float camX, float baseY, float halfView)
    {
        float frame = camX * (1f - airshipDepth);
        if (!airshipOn)
        {
            airshipTimer -= dt;
            if (airshipTimer > 0f) return;
            airshipOn = true;
            airshipP = frame - halfView - 1f;
        }
        airshipP += airshipSpeed * dt;
        if (airshipP - frame > halfView + 1f)
        {
            airshipOn = false;
            airship.enabled = false;
            airshipTimer = R(airshipInterval);
            return;
        }
        airship.enabled = true;
        airship.sprite = airshipFrames[Mathf.FloorToInt(t * 6f) % 2];
        airship.color = airshipTint;
        airship.transform.position = new Vector3(camX * airshipDepth + airshipP, baseY + bandTop - 1.3f, 0f);
        airshipLamp.enabled = night > 0.5f && Mathf.FloorToInt(t * 1.5f) % 2 == 1;
    }

    void UpdateNight(float dt, float camX, float baseY, float halfView)
    {
        float batAlpha = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.35f, 0.75f, night));
        foreach (var b in bats)
        {
            b.sr.enabled = batAlpha > 0.01f;
            if (!b.sr.enabled) continue;
            float a = t * 0.9f + b.phase;
            float x = b.u * (halfView - 3f) + Mathf.Cos(a) * b.radius + Mathf.Sin(a * 3.1f) * 0.25f;
            float y = b.y + Mathf.Sin(a * 1.7f) * b.radius * 0.4f + Mathf.Sin(a * 7f) * 0.09f;
            b.sr.sprite = batFrames[Mathf.FloorToInt(t * 10f + b.phase) % 2];
            b.sr.color = PixelArt.Alpha(Color.white, batAlpha);
            b.sr.transform.position = new Vector3(camX + x, baseY + y, 0f);
        }

        // Stars are at infinity: fixed to the camera.
        foreach (var s in stars)
        {
            float v = Mathf.Sin(t * s.rate * 3f + s.phase);
            s.sr.enabled = night > 0.05f && v > 0.55f;
            if (!s.sr.enabled) continue;
            s.sr.sprite = v > 0.85f ? starCross : starDot;
            s.sr.color = PixelArt.Alpha(Color.white, night);
            s.sr.transform.position = new Vector3(Mathf.Round((camX + s.u * halfView) * 32f) / 32f, baseY + s.y, 0f);
        }

        shootTimer -= dt;
        if (shootTimer <= 0f)
        {
            shootTimer = R(shootingInterval);
            if (night > 0.6f)
            {
                shootAge = 0f;
                shootX = R(-halfView * 0.2f, halfView);
                shootY = R(bandTop - 1.3f, bandTop - 0.1f);
            }
        }
        if (shootAge >= 0f)
        {
            shootAge += dt;
            shootX -= 150f / 32f * dt;
            shootY -= 60f / 32f * dt;
            if (shootAge > 0.7f) shootAge = -1f;
        }
        shooting.enabled = shootAge >= 0f;
        if (shooting.enabled)
        {
            shooting.color = PixelArt.Alpha(Color.white, (1f - shootAge / 0.7f) * night);
            shooting.transform.position = new Vector3(camX + shootX, baseY + shootY, 0f);
        }
    }

    void OnDestroy()
    {
        foreach (var b in birds) if (b.sr != null) Destroy(b.sr.gameObject);
    }
}
