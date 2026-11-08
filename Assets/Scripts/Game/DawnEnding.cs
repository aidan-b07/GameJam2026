using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

// The ending, "Dawn of a New Day" (mockups/ending/README.md; scene C2 of
// mockups/ending/ending.html). Waits until both players have unlocked their
// clock door, then runs the shot on t, the seconds since then:
//   0-1.4    doors open, both players walk to their marks and idle;
//   1.5-6.5  both lane clocks spin (ease in-out) to the next 06:00, keys at 4x,
//            speed streaks (DawnSky);
//   6.5-     dawn: both clocks run on at 0.2 h per second; DawnSky draws the
//            sun, sky tint, rays and effects, then the birds at 9.6;
//   7-9      the rifts close into a gold seam, which dims to half over 11-13;
//   11.6-    letterbox, title "TICK & TOCK" (12.2), "THE END" (13.2);
//   16.5     the shot is done: "PRESS ANY KEY" blinks in the bottom bar.
// Any key from 13.2 s on (Escape at any time) fades to black and loads the
// main menu; with no key it goes back by itself at autoReturn.
// It drives the existing systems instead of copying them: GameManager's lane
// clocks (LaneSky, TowerClocks, the doors' hands and the lane HUD follow),
// LaneRift (suspended), LaneDivider (closed), CoopCamera (held still) and
// GameFlow (nothing kills any more); the players' input is switched off.
public class DawnEnding : MonoBehaviour
{
    public ClockDoor[] doors;
    public DawnSky sky;
    public CoopCamera cameraRig;
    public LaneSky[] skies;
    public LaneDivider divider;

    [Tooltip("Scene loaded when the ending is over.")]
    public string menuScene = "MainMenu";
    [Tooltip("Seconds of the shot (the mockup's length); then the prompt shows.")]
    public float shotLength = 16.5f;
    [Tooltip("Any key skips from this many seconds on (once the title is up); Escape always does.")]
    public float skipFrom = 13.2f;
    [Tooltip("Seconds after which the ending returns to the menu without a key.")]
    public float autoReturn = 30f;
    [Tooltip("Seconds of the fade to black before the menu loads.")]
    public float fadeTime = 1f;
    [Tooltip("Longest the walk to the marks may take.")]
    public float walkTime = 1.4f;

    public bool Running { get; private set; }
    public float T { get; private set; }

    struct Actor
    {
        public Transform t;
        public Rigidbody2D rb;
        public PlayerMovement movement;
        public float speed;
        // Set once the actor reaches its mark; from then on it stands still.
        public bool arrived;
    }

    Actor[] actors;
    float[] fromHours, toHours;
    float musicVolume = -1f;
    float leaving = -1f;
    int lastTick = -1;
    bool chimed;
    bool wasAnyDown = true, wasEscapeDown = true;

    Canvas canvas;
    RawImage seam, barTop, barBottom, title, theEnd, prompt, fade;
    // Textures made here (the seam band and the pixel-font text), destroyed in OnDestroy.
    readonly System.Collections.Generic.List<Texture2D> owned = new System.Collections.Generic.List<Texture2D>();

    void Update()
    {
        if (!Running)
        {
            if (doors != null && doors.Length > 0 && System.Array.TrueForAll(doors, d => d != null && d.Reached)
                && GameFlow.Instance != null && !GameFlow.Instance.Respawning && !PauseMenu.IsPaused)
                Begin();
            return;
        }

        T += Time.deltaTime;
        float t = T;
        Clocks(t);
        if (skies != null)
            foreach (var s in skies) if (s != null) s.hideBodies = t > 6.5f;
        if (sky != null) sky.Render(t);
        float rf = DawnSky.Seg(t, 7f, 9f);
        if (divider != null) divider.scale = 1f - rf;
        Overlay(t, rf);
        Audio(t);

        // Presses are edges of our own (down now, up last frame), so a key
        // still held from the level when the ending starts does not count.
        bool anyDown = AnyDown(out bool escapeDown);
        bool press = anyDown && !wasAnyDown, escape = escapeDown && !wasEscapeDown;
        wasAnyDown = anyDown;
        wasEscapeDown = escapeDown;
        if (leaving < 0f)
        {
            if ((escape && t > 0.5f) || (t >= skipFrom && press) || t >= autoReturn) leaving = 0f;
        }
        else
        {
            leaving += Time.unscaledDeltaTime;
            fade.color = new Color(0f, 0f, 0f, Mathf.Clamp01(leaving / fadeTime));
            if (Music.Instance != null) Music.Instance.volume = 0f;
            if (leaving >= fadeTime + 0.15f)
            {
                if (Music.Instance != null && musicVolume >= 0f) Music.Instance.volume = musicVolume;
                SceneManager.LoadScene(menuScene);
                enabled = false;
            }
        }
    }

    void FixedUpdate()
    {
        if (!Running || actors == null) return;
        for (int lane = 0; lane < actors.Length; lane++)
        {
            ref var a = ref actors[lane];
            if (a.rb == null) continue;
            float mark = doors[lane].markX, dx = mark - a.rb.position.x;
            // Latch the arrival: the body settles a little after it stops, and
            // walking again whenever it drifts past the tolerance flickered the
            // sprite between run and idle.
            if (Mathf.Abs(dx) < 0.05f) a.arrived = true;
            float vx = a.arrived ? 0f : Mathf.Sign(dx) * Mathf.Min(a.speed, Mathf.Abs(dx) / Time.fixedDeltaTime);
            a.rb.linearVelocity = new Vector2(vx, a.rb.linearVelocity.y);
            // Face the way they walk, then the door (right).
            var s = a.t.localScale;
            float face = vx < -0.01f ? -1f : 1f;
            a.t.localScale = new Vector3(Mathf.Abs(s.x) * face, s.y, s.z);
        }
    }

    // Starts the ending now (also callable from tests).
    [ContextMenu("Begin")]
    public void Begin()
    {
        if (Running) return;
        Running = true;
        T = 0f;
        var flow = GameFlow.Instance;
        if (flow != null) flow.Finished = true;
        if (LaneRift.Instance != null) LaneRift.Instance.Suspended = true;
        if (cameraRig != null)
        {
            cameraRig.Hold = true;
            var leash = cameraRig.GetComponent<ScreenLeash>();
            if (leash != null) leash.enabled = false;
        }

        int n = doors.Length;
        actors = new Actor[n];
        for (int lane = 0; lane < n; lane++)
        {
            var p = flow != null && lane < flow.players.Length ? flow.players[lane] : null;
            if (p == null) continue;
            var a = new Actor { t = p, rb = p.GetComponent<Rigidbody2D>(), movement = p.GetComponent<PlayerMovement>() };
            if (a.movement != null) a.movement.enabled = false;
            if (a.rb != null)
            {
                a.rb.simulated = true;
                a.speed = Mathf.Clamp(Mathf.Abs(doors[lane].markX - a.rb.position.x) / walkTime, 2.5f, 8f);
            }
            actors[lane] = a;
            doors[lane].Open();
        }

        var gm = GameManager.INSTANCE;
        fromHours = new float[n];
        toHours = new float[n];
        if (gm != null)
        {
            gm.Scripted = true;
            for (int lane = 0; lane < n; lane++)
            {
                fromHours[lane] = gm.GetLaneHoursTotal(lane);
                // The next 06:00, at least a few hours away so the clock visibly races.
                float gap = Mathf.Repeat(6f - gm.GetLaneHour(lane), 24f);
                if (gap < 3f) gap += 24f;
                toHours[lane] = fromHours[lane] + gap;
                gm.SetLane(lane, fromHours[lane], 1f);
            }
        }
        if (sky != null) sky.Begin();
        if (Music.Instance != null) musicVolume = Music.Instance.volume;
        BuildOverlay();
    }

    void Clocks(float t)
    {
        var gm = GameManager.INSTANCE;
        if (gm == null) return;
        bool spin = t > 1.5f && t < 6.5f;
        for (int lane = 0; lane < fromHours.Length; lane++)
        {
            float h = t <= 6.5f
                ? Mathf.Lerp(fromHours[lane], toHours[lane], DawnSky.Eio(DawnSky.Seg(t, 1.5f, 6.5f)))
                : toHours[lane] + (t - 6.5f) * 0.2f;
            gm.SetLane(lane, h, spin ? 4f : 1f);
        }
    }

    // --- sound ---------------------------------------------------------------

    void Audio(float t)
    {
        // Clockwork racing: tick and tock alternate, fastest mid-spin.
        if (t > 1.5f && t < 6.5f)
        {
            float p = DawnSky.Seg(t, 1.5f, 6.5f);
            // Count ticks on the clocks' own progress, so they follow the ease.
            int tick = Mathf.FloorToInt(DawnSky.Eio(p) * 48f);
            if (tick != lastTick)
            {
                lastTick = tick;
                Sfx.PlayCentred(tick % 2 == 0 ? "tick" : "tock", 0.5f);
            }
        }
        if (!chimed && t >= 6.5f)
        {
            chimed = true;
            Sfx.PlayCentred("dawn", 1f);
        }
        // The level's drive music makes way: down while the clocks spin, gone by the peek.
        if (Music.Instance != null && musicVolume >= 0f && leaving < 0f)
            Music.Instance.volume = musicVolume * (1f - 0.6f * DawnSky.Seg(t, 0f, 1.5f)) * (1f - DawnSky.Seg(t, 6.5f, 8.5f));
    }

    // --- overlay: seam, letterbox, titles, prompt, fade -------------------------

    void BuildOverlay()
    {
        var go = new GameObject("DawnOverlay", typeof(Canvas), typeof(CanvasScaler));
        go.transform.SetParent(transform, false);
        canvas = go.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 50;
        var scaler = go.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1280f, 720f);
        scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
        scaler.matchWidthOrHeight = 1f;

        // Gold seam: a 4 px line in a 60 px glow band.
        var band = new Texture2D(1, 60, TextureFormat.RGBA32, false)
        {
            filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp, hideFlags = HideFlags.HideAndDontSave,
        };
        var px = new Color32[60];
        for (int y = 0; y < 60; y++)
        {
            float d = Mathf.Abs(y + 0.5f - 30f);
            float a = d < 2f ? 1f : 0.35f * (1f - d / 30f);
            px[y] = new Color32(255, 214, 90, (byte)(Mathf.Round(a * 16f) / 16f * 255f));
        }
        band.SetPixels32(px);
        band.Apply(false);
        owned.Add(band);
        seam = Image(go.transform, "Seam", band, new Vector2(0f, 0.5f), new Vector2(1f, 0.5f), new Vector2(0f, 60f), Vector2.zero);

        barTop = Image(go.transform, "LetterboxTop", null, new Vector2(0f, 1f), new Vector2(1f, 1f), Vector2.zero, Vector2.zero);
        barTop.rectTransform.pivot = new Vector2(0.5f, 1f);
        barBottom = Image(go.transform, "LetterboxBottom", null, new Vector2(0f, 0f), new Vector2(1f, 0f), Vector2.zero, Vector2.zero);
        barBottom.rectTransform.pivot = new Vector2(0.5f, 0f);
        barTop.color = barBottom.color = Color.black;

        var outline = new Color32(0x2a, 0x10, 0x30, 255);
        title = Text(go.transform, "Title", "TICK & TOCK", new Color32(0xff, 0xe0, 0x8a, 255), outline, 10f, 30f);
        theEnd = Text(go.transform, "TheEnd", "THE END", new Color32(255, 255, 255, 255), outline, 4f, -50f);
        prompt = Text(go.transform, "Prompt", "PRESS ANY KEY", new Color32(255, 240, 200, 255), outline, 2f, -325f);
        fade = Image(go.transform, "Fade", null, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
        fade.color = new Color(0f, 0f, 0f, 0f);
        Overlay(0f, 0f);
    }

    void OnDestroy()
    {
        foreach (var tex in owned)
            if (tex != null) Destroy(tex);
        owned.Clear();
    }

    static RawImage Image(Transform parent, string name, Texture tex, Vector2 min, Vector2 max, Vector2 size, Vector2 pos)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(RawImage));
        go.transform.SetParent(parent, false);
        var rt = (RectTransform)go.transform;
        rt.anchorMin = min; rt.anchorMax = max; rt.sizeDelta = size; rt.anchoredPosition = pos;
        var img = go.GetComponent<RawImage>();
        img.texture = tex;
        img.raycastTarget = false;
        return img;
    }

    // Pixel-font text, scale x its pixels, centred y px above the seam.
    RawImage Text(Transform parent, string name, string text, Color32 fill, Color32 outline, float scale, float y)
    {
        var tex = PixelFont.Render(text, fill, outline);
        owned.Add(tex);
        var img = Image(parent, name, tex, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
            new Vector2(tex.width * scale, tex.height * scale), new Vector2(0f, y));
        img.color = new Color(1f, 1f, 1f, 0f);
        return img;
    }

    bool hudHidden;

    void Overlay(float t, float rf)
    {
        if (canvas == null) return;
        // The lane HUD keeps its gauges through the clock spin (they follow
        // the lane hour) but drops its speed effects, so they do not fight the
        // dawn grading, and fades out as the letterbox comes in.
        var hud = LaneHud.Instance;
        if (hud != null && !hudHidden)
        {
            hud.EffectsEnabled = false;
            if (t >= 11.6f) { hud.SetVisible(false, 1f); hudHidden = true; }
        }
        seam.color = new Color(1f, 1f, 1f, rf * (1f - DawnSky.Seg(t, 11f, 13f) * 0.5f));
        float h = Mathf.Round(70f * DawnSky.Eio(DawnSky.Seg(t, 11.6f, 12.6f)));
        barTop.rectTransform.sizeDelta = barBottom.rectTransform.sizeDelta = new Vector2(0f, h);
        barTop.enabled = barBottom.enabled = h > 0f;
        title.color = new Color(1f, 1f, 1f, DawnSky.Seg(t, 12.2f, 13.2f));
        theEnd.color = new Color(1f, 1f, 1f, DawnSky.Seg(t, 13.2f, 14f));
        // Blinks (stepped, like the sprites) once the shot is done.
        bool on = t >= shotLength && Mathf.Repeat(t - shotLength, 1.2f) < 0.8f;
        prompt.color = new Color(1f, 1f, 1f, on ? 1f : 0f);
    }

    // Any key or the gamepad's A, B or Start held; escape: Escape or Start.
    static bool AnyDown(out bool escape)
    {
        var kb = Keyboard.current;
        bool any = kb != null && kb.anyKey.isPressed;
        escape = kb != null && kb.escapeKey.isPressed;
        foreach (var pad in Gamepad.all)
        {
            any |= pad.buttonSouth.isPressed || pad.buttonEast.isPressed || pad.startButton.isPressed;
            escape |= pad.startButton.isPressed;
        }
        return any;
    }
}
