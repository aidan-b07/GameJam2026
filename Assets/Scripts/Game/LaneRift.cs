using System.Collections.Generic;
using UnityEngine;

// The Unraveling rift as a hazard: one rift hangs from the top edge of each
// lane's view (Tick's from the top of the screen, Tock's from the seam between
// the lanes) and grows down towards that lane's player while they dawdle.
//
// Each lane's rift follows its own player's horizontal speed (0..1 of their
// run speed). Below slowThreshold it grows exponentially: the growth rate is
// proportional to how deep it already is (plus growthSeed), so a short pause
// barely shows but a long one runs away; it creeps on smoothly, no lurches.
// At or above the threshold it shrinks linearly, faster the faster the
// player runs (shrinkRate at full speed).
//
// Depth is the mean growth; the edge does not just stretch down. Its front
// (Front) bulges and dips in shapes that slowly morph while the rift lives,
// and tendrils creep out of it at random places (more the deeper it is):
// each grows to its own length, hangs there, then shrinks slowly, so the
// front catches up and swallows it while new ones creep out elsewhere.
// Running pulls them all back. ReachAt gives
// the edge (front plus tendrils, in 4 px columns fixed in the lane's world)
// at a world x, and the kill uses the deepest column over the player's width.
//
// A player whose head reaches the rift's edge dies (GameFlow.KillAll); that
// includes being flung up off the top of the view, since the camera no longer
// follows players upwards. On a death both rifts draw back: they hold for
// retractDelay, then ease closed over retractTime (fast at first, settling
// gently), tendrils shrinking with them, so they are closed by the respawn.
//
// LaneDivider draws the rifts from ReachAt(lane, x).
[DefaultExecutionOrder(-40)]   // after CoopCamera (-50), before LaneDivider (-30)
public class LaneRift : MonoBehaviour
{
    public static LaneRift Instance { get; private set; }

    [Tooltip("Reach (units) of each rift below its lane's view top while it is closed.")]
    public float restReach = 0.28125f;   // 9 art pixels, the old rift's half height

    // Height (units) of the teeth along a rift's edge, centred on its reach
    // (a closed rift's teeth are as tall as its reach). LaneDivider draws them.
    public const float TeethAmplitude = 0.3125f;   // 10 art pixels

    [Header("Growth (player slower than the threshold)")]
    [Tooltip("Normalised horizontal speed (0..1) below which the rift grows.")]
    [Range(0f, 1f)] public float slowThreshold = 0.35f;
    [Tooltip("Seconds a player may be slow before their rift starts to grow, so a short wait (or a jump straight up) is safe.")]
    public float slowGrace = 0.5f;
    [Tooltip("Exponential growth rate per second: depth grows by (depth + growthSeed) * this each second.")]
    public float growthRate = 0.36f;
    [Tooltip("Depth (units) the exponential grows from: higher starts faster. " +
             "From closed (after slowGrace) it reaches a standing player in about 7 s, sooner under a tendril.")]
    public float growthSeed = 1f;


    [Header("Tendrils")]
    [Tooltip("Tendrils sprouting per second along a growing rift's view, plus tendrilsPerDepth per unit of depth.")]
    public float tendrilRate = 1.5f;
    public float tendrilsPerDepth = 0.4f;
    [Tooltip("Tendril length (units) is tendrilLength plus up to tendrilLengthPerDepth per unit of depth, times a random 0-1 on top of the minimum.")]
    public float tendrilLength = 0.5f;
    public float tendrilLengthPerDepth = 0.2f;
    [Tooltip("Units per second a tendril grows out at first (slowing as it nears its length).")]
    public Vector2 tendrilSpeed = new Vector2(0.25f, 0.6f);
    [Tooltip("Seconds a grown tendril hangs before it shrinks.")]
    public Vector2 tendrilHold = new Vector2(1f, 3f);
    [Tooltip("Units per second a tendril shrinks while the rift grows (the front swallows it), and while its player runs.")]
    public float tendrilRecede = 0.15f;
    public float tendrilRecedeRunning = 1.5f;
    public int maxTendrils = 40;

    [Header("Shrink (player at or above the threshold)")]
    [Tooltip("Units per second the rift closes at full run speed; scales with the player's speed.")]
    public float shrinkRate = 2.5f;

    [Header("Kill")]
    [Tooltip("Units above the top of the player's collider (their head) at which the rift kills; negative lets the teeth graze. " +
             "-0.25 lets a full jump from a 4-tile ledge pass under a closed rift with room to spare.")]
    public float headMargin = -0.25f;
    [Tooltip("Depth (units) at which the rift stops growing, about the full lane height.")]
    public float maxDepth = 12f;

    [Header("Retract on death")]
    [Tooltip("Seconds after a death before the rifts start to draw back, so the kill reads.")]
    public float retractDelay = 0.3f;
    [Tooltip("Seconds the rifts take to close after a death (ease out).")]
    public float retractTime = 1.6f;

    float[] depth = new float[2];
    float[] slowFor = new float[2];
    float[] frontClock = new float[2];

    class Tendril
    {
        public float x, halfWidth, length, maxLength, speed, hold, lean, retractFrom;
        public bool receding;
    }

    readonly List<Tendril>[] tendrils = { new List<Tendril>(), new List<Tendril>() };
    readonly System.Random rng = new System.Random(23);
    bool[] safe = new bool[2];
    float[] retractFrom = new float[2];
    float retractClock = -1f;   // seconds since the death; -1 while alive

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
    public float ReachAt(int lane, float worldX)
    {
        float clock = lane >= 0 && lane < frontClock.Length ? frontClock[lane] : 0f;
        return restReach + Front(Depth(lane), worldX, clock, lane) + TendrilAt(lane, worldX);
    }

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

    // Reach (units) of the shallowest drawn edge between world x0 and x1,
    // teeth included: anything hung from here is covered by the rift.
    public float ShallowestReach(int lane, float x0, float x1)
    {
        float least = float.MaxValue;
        for (float x = x0; x <= x1; x += ColumnWidth) least = Mathf.Min(least, ReachAt(lane, x));
        if (least == float.MaxValue) least = Reach(lane);
        return Mathf.Max(0f, least - Mathf.Min(least, TeethAmplitude) / 2f);
    }

    static float Tip(float reach) => reach + Mathf.Min(reach, TeethAmplitude) / 2f;

    const float ColumnWidth = 4f / 32f;   // edge columns are 4 art pixels wide

    // Growth (units) of the front in the edge column above world x: depth
    // times a lead of 0.55-1.35 from bulges (about 2.5 units wide) and bumps
    // (0.7), slowly morphing into new shapes as clock runs (every 6 and 3 s),
    // so the front changes shape instead of stretching. Snapped to 4 px; 0 at depth 0.
    // Lane picks the pattern.
    public static float Front(float depth, float worldX, float clock, int lane)
    {
        if (depth <= 0f) return 0f;
        int col = Mathf.FloorToInt(worldX / ColumnWidth);
        float cx = col * ColumnWidth;
        int seed = 11 + lane * 37;
        float lead = 0.55f + 0.6f * Morph(cx / 2.5f, clock / 6f, seed) + 0.2f * Morph(cx / 0.7f, clock / 3f, seed + 3);
        return Mathf.Max(0f, Mathf.Floor(depth * lead / ColumnWidth + 0.5f) * ColumnWidth);
    }

    // How far (units) tendrils hang below the front in the column above world x.
    float TendrilAt(int lane, float worldX)
    {
        if (lane < 0 || lane >= tendrils.Length) return 0f;
        float x = (Mathf.Floor(worldX / ColumnWidth) + 0.5f) * ColumnWidth, extra = 0f;
        foreach (var t in tendrils[lane])
        {
            // Tapered spike; its tip leans off centre as it grows.
            float u = (x - t.x) / t.halfWidth - t.lean * t.length / Mathf.Max(0.01f, t.maxLength);
            if (u <= -1f || u >= 1f) continue;
            extra = Mathf.Max(extra, t.length * Mathf.Pow(1f - Mathf.Abs(u), 0.8f));
        }
        return Mathf.Floor(extra / ColumnWidth) * ColumnWidth;
    }

    void UpdateTendrils(int lane, bool growing, bool running, float viewX, float halfView, float dt)
    {
        var list = tendrils[lane];
        float d = depth[lane];
        if (growing && list.Count < maxTendrils && rng.NextDouble() < (tendrilRate + tendrilsPerDepth * d) * dt)
            list.Add(new Tendril
            {
                x = viewX + (float)(rng.NextDouble() * 2 - 1) * halfView,
                halfWidth = 0.15f + (float)rng.NextDouble() * 0.4f,
                maxLength = tendrilLength + (float)rng.NextDouble() * (0.9f + tendrilLengthPerDepth * d),
                speed = Mathf.Lerp(tendrilSpeed.x, tendrilSpeed.y, (float)rng.NextDouble()),
                hold = Mathf.Lerp(tendrilHold.x, tendrilHold.y, (float)rng.NextDouble()),
                lean = (float)rng.NextDouble() * 0.6f - 0.3f,
            });
        for (int i = list.Count - 1; i >= 0; i--)
        {
            var t = list[i];
            if (!growing) t.receding = true;
            if (t.receding)
            {
                t.length -= (running || !growing ? tendrilRecedeRunning : tendrilRecede) * dt;
                if (t.length <= 0f) list.RemoveAt(i);
            }
            else if (t.length < t.maxLength * 0.97f)
                t.length += t.speed * Mathf.Max(0.3f, 1f - t.length / t.maxLength) * dt;
            else if ((t.hold -= dt) <= 0f)
                t.receding = true;
        }
    }

    static float Morph(float x, float phase, int seed)
    {
        int k = Mathf.FloorToInt(phase);
        float f = phase - k, s = f * f * (3f - 2f * f);
        return Mathf.Lerp(Noise(x, seed + k * 977), Noise(x, seed + (k + 1) * 977), s);
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

    // Growth (units) beyond the closed rift.
    public float Depth(int lane) => depth != null && lane >= 0 && lane < depth.Length ? depth[lane] : 0f;

    // 0 closed, 1 as deep as maxDepth (for the hum).
    public float Openness => Mathf.Clamp01(Mathf.Max(Depth(0), Depth(1)) / Mathf.Max(0.01f, maxDepth));

    void OnEnable()
    {
        Instance = this;
        GameFlow.LevelReset += ResetLanes;
    }

    void OnDisable()
    {
        GameFlow.LevelReset -= ResetLanes;
        if (Instance == this) Instance = null;
    }

    // Snaps both rifts closed.
    public void Close()
    {
        for (int i = 0; i < depth.Length; i++) depth[i] = slowFor[i] = frontClock[i] = 0f;
        foreach (var list in tendrils) list.Clear();
        ResetLanes();
    }

    // On a respawn the rifts keep closing on their own (Retract); only the
    // lanes' state goes back.
    void ResetLanes()
    {
        for (int i = 0; i < safe.Length; i++)
        {
            safe[i] = false;
            slowFor[i] = 0f;
        }
    }

    // During a death and respawn: hold, then ease both rifts closed.
    void Retract(float dt)
    {
        if (retractClock < 0f)
        {
            retractClock = 0f;
            for (int lane = 0; lane < depth.Length; lane++)
            {
                retractFrom[lane] = depth[lane];
                foreach (var t in tendrils[lane]) { t.retractFrom = t.length; t.receding = true; }
            }
        }
        retractClock += dt;
        float p = Mathf.Clamp01((retractClock - retractDelay) / Mathf.Max(0.01f, retractTime));
        float k = (1f - p) * (1f - p) * (1f - p);   // share still open: ease out cubic
        for (int lane = 0; lane < depth.Length; lane++)
        {
            frontClock[lane] += dt;
            depth[lane] = retractFrom[lane] * k;
            var list = tendrils[lane];
            for (int i = list.Count - 1; i >= 0; i--)
            {
                list[i].length = list[i].retractFrom * k;
                if (k <= 0f) list.RemoveAt(i);
            }
        }
    }

    void Update()
    {
        var flow = GameFlow.Instance;
        if (flow == null) return;
        if (flow.Respawning)
        {
            Retract(Time.deltaTime);
            return;
        }
        if (retractClock >= 0f)
        {
            // Respawned: make sure nothing is left over.
            retractClock = -1f;
            if (Depth(0) > 0f || Depth(1) > 0f || tendrils[0].Count > 0 || tendrils[1].Count > 0) Close();
        }
        if (flow.Finished && !Suspended) return;
        var cams = flow.cameraRig != null ? flow.cameraRig.cameras : null;
        float dt = Time.deltaTime;

        for (int lane = 0; lane < depth.Length && lane < flow.players.Length; lane++)
        {
            var player = flow.players[lane];
            if (player == null || !player.gameObject.activeInHierarchy) continue;

            var cam = cams != null && lane < cams.Length ? cams[lane] : null;
            float viewX = cam != null ? cam.transform.position.x : player.transform.position.x;
            float halfView = cam != null ? cam.orthographicSize * cam.aspect + 1f : 21f;
            frontClock[lane] += dt;

            float speed = flow.HorizontalSpeed(lane);
            if (Suspended || safe[lane])
            {
                slowFor[lane] = 0f;
                depth[lane] = Mathf.Max(0f, depth[lane] - shrinkRate * dt);
                UpdateTendrils(lane, false, false, viewX, halfView, dt);
                continue;
            }
            bool growing = false;
            if (speed < slowThreshold)
            {
                slowFor[lane] += dt;
                if (slowFor[lane] > slowGrace)
                {
                    growing = true;
                    depth[lane] += (depth[lane] + growthSeed) * growthRate * dt;
                }
            }
            else
            {
                slowFor[lane] = 0f;
                depth[lane] -= shrinkRate * speed * dt;
            }
            depth[lane] = Mathf.Clamp(depth[lane], 0f, maxDepth);
            UpdateTendrils(lane, growing, speed >= slowThreshold, viewX, halfView, dt);

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
