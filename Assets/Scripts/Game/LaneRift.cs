using UnityEngine;

// The Unraveling rift as a hazard: one rift hangs from the top edge of each
// lane's view (Tick's from the top of the screen, Tock's from the seam between
// the lanes) and grows down towards that lane's player while they dawdle.
//
// Each lane's rift follows its own player's horizontal speed (0..1 of their
// run speed). Below slowThreshold it grows exponentially: the growth rate is
// proportional to how deep it already is (plus growthSeed), so a short pause
// barely shows but a long one runs away. The growth comes in lurches (a slow
// creep, then a surge every surgePeriod). At or above the threshold it
// shrinks linearly, faster the faster the player runs (shrinkRate at full
// speed).
//
// Depth is the mean growth; the edge does not move down as one line. Each
// 4 px column of it (fixed in the lane's world) has its own lead: broad
// swells, narrow fingers that run ahead, and columns that crack down in
// LurchStep steps at their own moments (Uneven). ReachAt gives the edge at a
// world x, and the kill uses the deepest column over the player's width.
//
// A player whose head reaches the rift's edge dies (GameFlow.KillAll); that
// includes being flung up off the top of the view, since the camera no longer
// follows players upwards. Both rifts close again on every respawn.
//
// LaneDivider draws the rifts from Reach(lane).
[DefaultExecutionOrder(-40)]   // after CoopCamera (-50), before LaneDivider (-30)
public class LaneRift : MonoBehaviour
{
    public static LaneRift Instance { get; private set; }

    [Tooltip("Reach (units) of each rift below its lane's view top while it is closed.")]
    public float restReach = 0.28125f;   // 9 art pixels, the old rift's half height

    // Height (units) of the teeth along a rift's edge, centred on its reach
    // (a closed rift's teeth are as tall as its reach). LaneDivider draws them.
    public const float TeethAmplitude = 0.5f;   // 16 art pixels

    [Header("Growth (player slower than the threshold)")]
    [Tooltip("Normalised horizontal speed (0..1) below which the rift grows.")]
    [Range(0f, 1f)] public float slowThreshold = 0.35f;
    [Tooltip("Seconds a player may be slow before their rift starts to grow, so a short wait (or a jump straight up) is safe.")]
    public float slowGrace = 0.5f;
    [Tooltip("Exponential growth rate per second: depth grows by (depth + growthSeed) * this each second (on average, see surges).")]
    public float growthRate = 0.4f;
    [Tooltip("Depth (units) the exponential grows from: higher starts faster. " +
             "From closed (after slowGrace) it reaches a standing player in about 6.3-7.5 s, depending on the edge above them.")]
    public float growthSeed = 0.6f;
    [Tooltip("Seconds between growth surges; the rift creeps in between.")]
    public float surgePeriod = 1.3f;
    [Tooltip("Growth speed between surges, as a fraction of the average.")]
    [Range(0f, 1f)] public float surgeFloor = 0.35f;

    // Units an edge column drops at once when it cracks further down.
    public const float LurchStep = 0.25f;   // 8 art pixels

    [Header("Shrink (player at or above the threshold)")]
    [Tooltip("Units per second the rift closes at full run speed; scales with the player's speed.")]
    public float shrinkRate = 2.5f;

    [Header("Kill")]
    [Tooltip("Units above the top of the player's collider (their head) at which the rift kills; negative lets the teeth graze. " +
             "-0.25 lets a full jump from a 4-tile ledge pass under a closed rift with room to spare.")]
    public float headMargin = -0.25f;
    [Tooltip("Depth (units) at which the rift stops growing, about the full lane height.")]
    public float maxDepth = 12f;

    float[] depth = new float[2];
    float[] slowFor = new float[2];
    float[] growFor = new float[2];
    bool[] safe = new bool[2];

    // A safe lane's rift closes and cannot kill (a player waiting at the
    // unlocked clock door). Suspended does that for both lanes (the ending).
    public bool Suspended { get; set; }
    public void SetSafe(int lane, bool value)
    {
        if (safe != null && lane >= 0 && lane < safe.Length) safe[lane] = value;
    }

    // Rift reach (units) below the top of the lane's view: rest plus growth.
    public float Reach(int lane) => restReach + Depth(lane);

    // Rift reach (units) of the edge column above world x.
    public float ReachAt(int lane, float worldX) => restReach + Uneven(Depth(lane), worldX, lane);

    // Reach (units) of the lowest tooth tips over the lane's player: where
    // the rift kills. Mean reach when there is no player.
    public float TipReach(int lane)
    {
        var flow = GameFlow.Instance;
        if (flow == null || lane >= flow.players.Length) return Tip(Reach(lane));
        var span = flow.Span(lane);
        float deepest = 0f;
        for (float x = span.x; ; x += ColumnWidth)
        {
            deepest = Mathf.Max(deepest, Tip(ReachAt(lane, Mathf.Min(x, span.y))));
            if (x >= span.y) break;
        }
        return deepest;
    }

    static float Tip(float reach) => reach + Mathf.Min(reach, TeethAmplitude) / 2f;

    const float ColumnWidth = 4f / 32f;   // edge columns are 4 art pixels wide

    // Growth (units) of the edge column above world x when the mean growth is
    // depth: depth times the column's lead (0.62-1.72: broad swells,
    // rough bumps and fingers that hang down), cracked down in LurchSteps at a phase of its own, so
    // neighbouring columns drop at different moments. Never above depth's
    // edge when depth is 0. Lane picks the pattern.
    public static float Uneven(float depth, float worldX, int lane)
    {
        if (depth <= 0f) return 0f;
        int col = Mathf.FloorToInt(worldX / ColumnWidth);
        float cx = col * ColumnWidth;
        int seed = 11 + lane * 37;
        float broad = Noise(cx / 3f, seed);
        float rough = Noise(cx / 0.5f, seed + 3);
        float finger = Mathf.Max(0f, Noise(cx / 0.9f, seed + 1) - 0.5f) / 0.5f;
        float lead = 0.62f + 0.4f * broad + 0.2f * rough + 0.5f * finger * Mathf.Sqrt(finger);
        float phase = 0.6f * Noise(cx / 1.1f, seed + 2) + 0.4f * Hash(col * 13 + seed);
        return Mathf.Max(0f, Mathf.Floor(depth * lead / LurchStep + phase) * LurchStep);
    }

    public static float Hash(int i)
    {
        uint h = (uint)i * 747796405u + 2891336453u;
        h = ((h >> (int)((h >> 28) + 4u)) ^ h) * 277803737u;
        h = (h >> 22) ^ h;
        return h / 4294967296f;
    }

    // 1D value noise, 0-1.
    public static float Noise(float x, int seed)
    {
        int i = Mathf.FloorToInt(x);
        float f = x - i, s = f * f * (3f - 2f * f);
        return Mathf.Lerp(Hash(i * 7919 + seed * 104729), Hash((i + 1) * 7919 + seed * 104729), s);
    }

    // Growth speed factor over a surge cycle: surgeFloor between surges, a
    // sharp peak once per surgePeriod, 1 on average.
    float Surge(float t)
    {
        float s = Mathf.Max(0f, Mathf.Sin(t / Mathf.Max(0.1f, surgePeriod) * 2f * Mathf.PI - 0.5f * Mathf.PI));
        // max(0, sin)^3 averages 2 / (3 pi) over a cycle.
        return surgeFloor + (1f - surgeFloor) * s * s * s * (1.5f * Mathf.PI);
    }

    // Growth (units) beyond the closed rift.
    public float Depth(int lane) => depth != null && lane >= 0 && lane < depth.Length ? depth[lane] : 0f;

    // 0 closed, 1 as deep as maxDepth (for the hum).
    public float Openness => Mathf.Clamp01(Mathf.Max(Depth(0), Depth(1)) / Mathf.Max(0.01f, maxDepth));

    void OnEnable()
    {
        Instance = this;
        GameFlow.LevelReset += Close;
    }

    void OnDisable()
    {
        GameFlow.LevelReset -= Close;
        if (Instance == this) Instance = null;
    }

    public void Close()
    {
        for (int i = 0; i < depth.Length; i++) depth[i] = slowFor[i] = growFor[i] = 0f;
        for (int i = 0; i < safe.Length; i++) safe[i] = false;
    }

    void Update()
    {
        var flow = GameFlow.Instance;
        if (flow == null || flow.Respawning || flow.Finished && !Suspended) return;
        var cams = flow.cameraRig != null ? flow.cameraRig.cameras : null;
        float dt = Time.deltaTime;

        for (int lane = 0; lane < depth.Length && lane < flow.players.Length; lane++)
        {
            var player = flow.players[lane];
            if (player == null || !player.gameObject.activeInHierarchy) continue;

            float speed = flow.HorizontalSpeed(lane);
            if (Suspended || safe[lane])
            {
                slowFor[lane] = growFor[lane] = 0f;
                depth[lane] = Mathf.Max(0f, depth[lane] - shrinkRate * dt);
                continue;
            }
            if (speed < slowThreshold)
            {
                slowFor[lane] += dt;
                if (slowFor[lane] > slowGrace)
                {
                    growFor[lane] += dt;
                    depth[lane] += (depth[lane] + growthSeed) * growthRate * Surge(growFor[lane]) * dt;
                }
            }
            else
            {
                slowFor[lane] = growFor[lane] = 0f;
                depth[lane] -= shrinkRate * speed * dt;
            }
            depth[lane] = Mathf.Clamp(depth[lane], 0f, maxDepth);

            var cam = cams != null && lane < cams.Length ? cams[lane] : null;
            if (cam == null) continue;
            float edge = cam.transform.position.y + cam.orthographicSize - TipReach(lane);
            if (flow.Top(lane) + headMargin >= edge)
            {
                flow.KillAll();
                return;
            }
        }
    }
}
