using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.UI;

// The lane HUD (DESIGN.md "Readability"): a brass gauge in the top-left corner
// of each lane, plus a light touch of "fast" and "slow" on the lane itself.
//
// Gauge (art from tools/make_hud.py): the partner's colour and face, because
// the partner's movement sets this lane's speed; a speed dial whose needle
// springs to GameManager.GetWorldSpeed(lane) (idle 0.35x at the left end, 3x
// at the right); a small clock with the lane's hour. The needle and hands are
// drawn here into a small texture over the plate. While the lane's LaneRift
// grows towards its player, a violet and red glow pulses round the plate and a
// dithered shade hangs from the top of the lane, faster and stronger the
// closer the rift is to the player's head.
//
// Feel: above fastFrom the lane gets faint speed streaks drifting left and a
// warm, slightly more saturated grade; near idle a cool, slightly paler one.
// The grade is a local URP Volume (ColorAdjustments) on each lane camera,
// whose profile is made here at runtime.
//
// Public API for other scripts (e.g. the ending): SetVisible, SetHourOverride,
// ClearHourOverride, EffectsEnabled.
[DefaultExecutionOrder(-20)]   // LateUpdate after CoopCamera (-50) and LaneRift (-40)
public class LaneHud : MonoBehaviour
{
    public static LaneHud Instance { get; private set; }

    [System.Serializable]
    public class LaneView
    {
        [Tooltip("Gauge root: plate, warning glow and the runtime dial overlay.")]
        public RectTransform gauge;
        public RawImage warn;
        public RawImage dial;
        [Tooltip("Shade hanging from the top of the lane while its rift grows.")]
        public RawImage vignette;
        [Tooltip("Speed streaks over the lane (texture repeats both ways).")]
        public RawImage streaks;
        [Tooltip("Local volume on the lane camera for the fast and slow grade.")]
        public Volume grade;
    }

    [Tooltip("Tick's lane (0), Tock's lane (1).")]
    public LaneView[] lanes = new LaneView[2];
    [Tooltip("Fades all gauges together (SetVisible).")]
    public CanvasGroup gauges;

    [Header("Needle")]
    public float idleSpeed = 0.35f;
    public float maxSpeed = 3f;
    [Tooltip("Spring stiffness of the needle (1/s^2).")]
    public float needleStiffness = 140f;
    [Tooltip("Spring damping of the needle (1/s); below 2 * sqrt(stiffness) it overshoots a little.")]
    public float needleDamping = 15f;
    [Tooltip("Needle quiver (degrees) at full speed.")]
    public float quiver = 0.8f;

    [Header("Rift warning")]
    [Tooltip("Rift growth (units) before the warning shows.")]
    public float warnFromDepth = 0.05f;
    [Tooltip("Strongest vignette alpha.")]
    [Range(0f, 1f)] public float vignetteAlpha = 0.7f;
    [Tooltip("Vignette height in art pixels below the rift's edge (the texture is 96 tall).")]
    public float vignetteHeight = 96f;

    [Header("Feel")]
    [Tooltip("Lane speed where the streaks and the warm grade begin.")]
    public float fastFrom = 1.5f;
    [Tooltip("Streak opacity at full speed.")]
    [Range(0f, 1f)] public float streakAlpha = 0.5f;
    [Tooltip("Streak drift in art pixels per real second, per 1x of lane speed.")]
    public float streakDrift = 120f;
    [Tooltip("Saturation added at full speed (URP ColorAdjustments, -100..100).")]
    public float warmSaturation = 14f;
    [Tooltip("Saturation removed at idle speed.")]
    public float coolDesaturation = 12f;
    public Color warmFilter = new Color(1f, 0.96f, 0.88f);
    public Color coolFilter = new Color(0.95f, 0.97f, 1.03f);

    // Art layout from tools/make_hud.py (pixels, y down from the top).
    const int W = 136, H = 56;
    const int GaugeX = 72, GaugeY = 41, GaugeR = 27;
    const int ClockX = 113, ClockY = 27, ClockR = 13;

    static readonly Color32 Outline = new Color32(28, 12, 6, 255);
    static readonly Color32 Needle = new Color32(250, 244, 222, 255);
    static readonly Color32 NeedleTip = new Color32(236, 66, 44, 255);
    static readonly Color32 Cap = new Color32(240, 180, 70, 255);
    static readonly Color32 CapLight = new Color32(252, 236, 140, 255);
    static readonly Color32 Ink = new Color32(52, 30, 22, 255);
    static readonly Color32 HandRed = new Color32(176, 40, 26, 255);

    class State
    {
        public float shown, velocity;
        public float lastDepth, warn;
        public float streakX;
        public Texture2D tex;
        public Color32[] px;
        public int drawnAngle = int.MinValue, drawnMinute = int.MinValue;
        public bool hasHour;
        public float hourOverride;
        public VolumeProfile profile;
        public ColorAdjustments adjust;
    }

    State[] state;
    float fadeTarget = 1f, fadeSpeed = 2.5f;
    bool effects = true;

    // Shows or hides every gauge, fading over `fade` seconds (0: at once).
    public void SetVisible(bool visible, float fade = 0.4f)
    {
        fadeTarget = visible ? 1f : 0f;
        fadeSpeed = fade <= 0f ? float.PositiveInfinity : 1f / fade;
        if (fade <= 0f && gauges != null) gauges.alpha = fadeTarget;
    }

    public bool Visible => fadeTarget > 0.5f;

    // Makes the lane's clock show this hour (0-24) instead of GameManager's.
    public void SetHourOverride(int lane, float hour)
    {
        if (state == null || lane < 0 || lane >= state.Length) return;
        state[lane].hasHour = true;
        state[lane].hourOverride = Mathf.Repeat(hour, 24f);
    }

    // Back to GameManager's lane hour; lane -1 clears both.
    public void ClearHourOverride(int lane = -1)
    {
        if (state == null) return;
        for (int i = 0; i < state.Length; i++)
            if (lane < 0 || lane == i) state[i].hasHour = false;
    }

    // Speed streaks, the grade and the rift warning; off leaves only the gauges.
    public bool EffectsEnabled
    {
        get => effects;
        set => effects = value;
    }

    void OnEnable()
    {
        Instance = this;
        state = new State[lanes.Length];
        for (int i = 0; i < lanes.Length; i++)
        {
            var s = state[i] = new State { shown = idleSpeed };
            s.tex = new Texture2D(W, H, TextureFormat.RGBA32, false)
            {
                filterMode = FilterMode.Point,
                wrapMode = TextureWrapMode.Clamp,
                hideFlags = HideFlags.HideAndDontSave,
            };
            s.px = new Color32[W * H];
            if (lanes[i]?.dial != null) lanes[i].dial.texture = s.tex;
            var v = lanes[i]?.grade;
            if (v != null)
            {
                s.profile = ScriptableObject.CreateInstance<VolumeProfile>();
                s.profile.hideFlags = HideFlags.HideAndDontSave;
                s.adjust = s.profile.Add<ColorAdjustments>(false);
                s.adjust.saturation.overrideState = true;
                s.adjust.colorFilter.overrideState = true;
                v.sharedProfile = s.profile;
            }
        }
    }

    void OnDisable()
    {
        if (Instance == this) Instance = null;
        if (state == null) return;
        for (int i = 0; i < state.Length; i++)
        {
            if (lanes[i]?.grade != null) lanes[i].grade.sharedProfile = null;
            if (state[i].tex != null) Destroy(state[i].tex);
            if (state[i].profile != null) Destroy(state[i].profile);
        }
        state = null;
    }

    void LateUpdate()
    {
        float dt = Time.unscaledDeltaTime;
        if (gauges != null && !Mathf.Approximately(gauges.alpha, fadeTarget))
            gauges.alpha = Mathf.MoveTowards(gauges.alpha, fadeTarget, fadeSpeed * dt);

        var gm = GameManager.INSTANCE;
        for (int lane = 0; lane < lanes.Length; lane++)
        {
            var view = lanes[lane];
            var s = state[lane];
            if (view == null) continue;
            float speed = gm != null ? gm.GetWorldSpeed(lane) : 1f;
            float hour = s.hasHour ? s.hourOverride
                : gm != null ? gm.GetLaneHour(lane)
                : lane == 0 ? 10f : 2f;

            UpdateNeedle(s, speed, Time.deltaTime > 0f ? Mathf.Min(dt, 0.05f) : 0f);
            DrawDial(s, hour);
            UpdateWarning(lane, view, s, dt);
            UpdateFeel(view, s, speed, dt);
        }
    }

    void UpdateNeedle(State s, float target, float dt)
    {
        // Spring, a little underdamped, so the needle swings past and settles.
        float acc = needleStiffness * (target - s.shown) - needleDamping * s.velocity;
        s.velocity += acc * dt;
        s.shown += s.velocity * dt;
        // Pin stops just past both ends.
        float lo = idleSpeed - 0.05f, hi = maxSpeed + 0.05f;
        if (s.shown < lo) { s.shown = lo; s.velocity = Mathf.Max(0f, s.velocity); }
        if (s.shown > hi) { s.shown = hi; s.velocity = Mathf.Min(0f, s.velocity); }
    }

    // Degrees: 180 (left) at idle, 0 (right) at max speed.
    float SpeedAngle(float speed) =>
        180f * (1f - (speed - idleSpeed) / Mathf.Max(0.01f, maxSpeed - idleSpeed));

    void DrawDial(State s, float hour)
    {
        float fast = Mathf.InverseLerp(fastFrom, maxSpeed, s.shown);
        float angle = SpeedAngle(s.shown);
        if (Application.isPlaying && fast > 0f)
            angle += Mathf.Sin(Time.unscaledTime * 53f) * quiver * fast;
        int angleKey = Mathf.RoundToInt(angle * 2f);
        int minute = Mathf.FloorToInt(hour * 60f);
        if (angleKey == s.drawnAngle && minute == s.drawnMinute) return;
        s.drawnAngle = angleKey;
        s.drawnMinute = minute;

        System.Array.Clear(s.px, 0, s.px.Length);

        // Clock hands (lane hour), hour hand short, minute hand long.
        float hourA = (hour % 12f) / 12f * 2f * Mathf.PI;
        float minA = (minute % 60) / 60f * 2f * Mathf.PI;
        Line(s.px, ClockX, ClockY, ClockX + Mathf.Sin(minA) * (ClockR - 5f), ClockY - Mathf.Cos(minA) * (ClockR - 5f), 0.55f, Ink);
        Line(s.px, ClockX, ClockY, ClockX + Mathf.Sin(hourA) * (ClockR - 8f), ClockY - Mathf.Cos(hourA) * (ClockR - 8f), 0.85f, HandRed);
        Disc(s.px, ClockX, ClockY, 1.1f, Ink);

        // Needle: dark outline, cream body, red tip, brass cap.
        float a = angle * Mathf.Deg2Rad;
        float len = GaugeR - 7f;
        float tx = GaugeX + Mathf.Cos(a) * len, ty = GaugeY - Mathf.Sin(a) * len;
        float mx = GaugeX + Mathf.Cos(a) * (len - 5f), my = GaugeY - Mathf.Sin(a) * (len - 5f);
        float bx = GaugeX - Mathf.Cos(a) * 4f, by = GaugeY + Mathf.Sin(a) * 4f;
        Line(s.px, bx, by, tx, ty, 1.5f, Outline);
        Line(s.px, bx, by, mx, my, 0.7f, Needle);
        Line(s.px, mx, my, tx, ty, 0.7f, NeedleTip);
        Disc(s.px, GaugeX, GaugeY, 3.2f, Outline);
        Disc(s.px, GaugeX, GaugeY, 2.2f, Cap);
        Set(s.px, GaugeX - 1, GaugeY - 1, CapLight);

        s.tex.SetPixels32(s.px);
        s.tex.Apply(false);
    }

    void UpdateWarning(int lane, LaneView view, State s, float dt)
    {
        float target = 0f;
        var rift = LaneRift.Instance;
        var flow = GameFlow.Instance;
        if (effects && rift != null && flow != null && !flow.Respawning)
        {
            float depth = rift.Depth(lane);
            bool growing = depth > s.lastDepth + 1e-5f;
            s.lastDepth = depth;
            if (growing && depth > warnFromDepth)
            {
                // How far the rift has come towards the player's head (0..1).
                var cams = flow.cameraRig != null ? flow.cameraRig.cameras : null;
                var cam = cams != null && lane < cams.Length ? cams[lane] : null;
                float closeness = 0.5f;
                if (cam != null)
                {
                    float top = cam.transform.position.y + cam.orthographicSize;
                    float gap = top - rift.TipReach(lane) - flow.Top(lane);
                    closeness = Mathf.Clamp01(1f - gap / (cam.orthographicSize * 1.2f));
                }
                target = Mathf.Lerp(0.35f, 1f, closeness);
            }
        }
        else if (rift != null)
            s.lastDepth = rift.Depth(lane);

        // Quick to come, slower to go, so it does not flicker.
        s.warn = Mathf.MoveTowards(s.warn, target, (target > s.warn ? 3f : 1.5f) * dt);
        float freq = Mathf.Lerp(1.4f, 4f, s.warn);
        float pulse = 0.5f + 0.5f * Mathf.Sin(Time.unscaledTime * freq * 2f * Mathf.PI);
        if (view.warn != null)
        {
            view.warn.enabled = s.warn > 0.01f;
            view.warn.color = new Color(1f, 1f, 1f, s.warn * Mathf.Lerp(0.35f, 1f, pulse));
        }
        if (view.vignette != null)
        {
            view.vignette.enabled = s.warn > 0.01f;
            view.vignette.color = new Color(1f, 1f, 1f, s.warn * vignetteAlpha * Mathf.Lerp(0.75f, 1f, pulse));
            // Hangs from the rift's edge, so the shade leads it down towards the player.
            var rt = view.vignette.rectTransform;
            float reachPx = rift != null ? rift.Reach(lane) * 32f : 0f;
            rt.anchoredPosition = new Vector2(0f, -Mathf.Round(reachPx));
            rt.sizeDelta = new Vector2(rt.sizeDelta.x, vignetteHeight);
            // Tiled, not stretched, so the dither stays one art pixel wide.
            var tex = view.vignette.texture;
            if (tex != null)
                view.vignette.uvRect = new Rect(0f, 0f, rt.rect.width / tex.width, vignetteHeight / tex.height);
        }
    }

    void UpdateFeel(LaneView view, State s, float speed, float dt)
    {
        float fast = effects ? Smooth(fastFrom, maxSpeed, speed) : 0f;
        float slow = effects ? 1f - Smooth(idleSpeed, 1f, speed) : 0f;

        if (view.streaks != null)
        {
            view.streaks.enabled = fast > 0.01f;
            var tex = view.streaks.texture;
            if (fast > 0.01f && tex != null)
            {
                s.streakX += dt * speed * streakDrift;
                var rect = view.streaks.rectTransform.rect;
                float px = Mathf.Floor(s.streakX);
                view.streaks.uvRect = new Rect(px / tex.width, 0f, rect.width / tex.width, rect.height / tex.height);
                view.streaks.color = new Color(1f, 1f, 1f, fast * streakAlpha);
            }
        }

        if (s.adjust != null)
        {
            s.adjust.saturation.value = fast * warmSaturation - slow * coolDesaturation;
            var c = Color.Lerp(Color.white, coolFilter, slow);
            s.adjust.colorFilter.value = Color.Lerp(c, warmFilter, fast);
        }
    }

    static float Smooth(float a, float b, float v)
    {
        float t = Mathf.Clamp01((v - a) / Mathf.Max(0.0001f, b - a));
        return t * t * (3f - 2f * t);
    }

    // Pixel helpers, y down from the top like the art.
    static void Set(Color32[] px, int x, int y, Color32 c)
    {
        if (x < 0 || x >= W || y < 0 || y >= H) return;
        px[(H - 1 - y) * W + x] = c;
    }

    static void Line(Color32[] px, float x0, float y0, float x1, float y1, float half, Color32 c)
    {
        int minX = Mathf.FloorToInt(Mathf.Min(x0, x1) - half - 1), maxX = Mathf.CeilToInt(Mathf.Max(x0, x1) + half + 1);
        int minY = Mathf.FloorToInt(Mathf.Min(y0, y1) - half - 1), maxY = Mathf.CeilToInt(Mathf.Max(y0, y1) + half + 1);
        float dx = x1 - x0, dy = y1 - y0, len2 = Mathf.Max(1e-4f, dx * dx + dy * dy);
        for (int y = minY; y <= maxY; y++)
            for (int x = minX; x <= maxX; x++)
            {
                float t = Mathf.Clamp01(((x - x0) * dx + (y - y0) * dy) / len2);
                float ex = x0 + t * dx - x, ey = y0 + t * dy - y;
                if (ex * ex + ey * ey <= half * half + 0.1f) Set(px, x, y, c);
            }
    }

    static void Disc(Color32[] px, float cx, float cy, float r, Color32 c)
    {
        for (int y = Mathf.FloorToInt(cy - r); y <= Mathf.CeilToInt(cy + r); y++)
            for (int x = Mathf.FloorToInt(cx - r); x <= Mathf.CeilToInt(cx + r); x++)
                if ((x - cx) * (x - cx) + (y - cy) * (y - cy) <= r * r) Set(px, x, y, c);
    }
}
