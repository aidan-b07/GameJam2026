using System.Collections.Generic;
using UnityEngine;

// The Unraveling rift as a hazard: one rift hangs from the top edge of each
// lane's view (Tick's from the top of the screen, Tock's from the seam between
// the lanes) and grows down towards that lane's player while they dawdle.
//
// Each lane's rift follows its own player's measured speed (0..1 of their run
// speed, PlayerMovement.MeasuredSpeed): the distance the body really moved,
// the same value that drives the partner's lane speed, so pushing into a wall
// or walking against a belt counts as standing, and riding a belt, jumping
// and falling count as moving. The rift's rate blends over that speed:
//   - at or below growBelow it grows exponentially: the growth rate is
//     proportional to how deep it already is (plus growthSeed), so a short
//     pause barely shows but a long one runs away;
//   - from growBelow to holdFrom the growth fades out (smoothstep);
//   - from holdFrom to shrinkFrom it holds still: the front keeps morphing
//     and tendrils keep creeping out and back (holdTendrils), so it lives in
//     place without coming down;
//   - above shrinkFrom it retreats, faster the faster the player moves,
//     from 0 up to shrinkRate at full speed.
//
// Those give the rift's target rate; its actual rate eases towards it
// (critically damped, rateSmoothTime), so the rift has momentum: when the
// player starts moving it slows to a stop, then retreats, and when they stop
// it slows its retreat, then creeps back down.
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
// follows players upwards. Both rifts close again on every respawn.
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

    [Header("Speed bands (measured speed, 0..1 of run speed)")]
    [Tooltip("At or below this the rift grows at its full rate.")]
    [Range(0f, 1f)] public float growBelow = 0.1f;
    [Tooltip("Growth fades out between growBelow and this; from here to shrinkFrom the rift holds still.")]
    [Range(0f, 1f)] public float holdFrom = 0.2f;
    [Tooltip("Above this the rift retreats, reaching shrinkRate at full speed.")]
    [Range(0f, 1f)] public float shrinkFrom = 0.35f;
    [Tooltip("Tendril sprouting while the rift holds, as a fraction of the growing rate (keeps a held rift alive).")]
    [Range(0f, 1f)] public float holdTendrils = 0.5f;

    [Header("Growth (player slower than holdFrom)")]
    [Tooltip("Seconds a player may be slow before their rift starts to grow, so a short wait (or a jump straight up) is safe.")]
    public float slowGrace = 0.5f;
    [Tooltip("Exponential growth rate per second: depth grows by (depth + growthSeed) * this each second.")]
    public float growthRate = 0.22f;
    [Tooltip("Depth (units) the exponential grows from: higher starts faster. " +
             "From closed (after slowGrace) it reaches a standing player in about 12 s, sooner under a tendril.")]
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
    [Tooltip("Units per second the rift closes at full run speed; scales from 0 at shrinkFrom.")]
    public float shrinkRate = 3f;

    [Header("Momentum")]
    [Tooltip("Seconds the rift's rate takes to ease to a new target (critically damped): higher turns around more slowly, " +
             "so a rift keeps growing a little after its player starts moving.")]
    public float rateSmoothTime = 0.7f;

    [Header("Kill")]
    [Tooltip("Units above the top of the player's collider (their head) at which the rift kills; negative lets the teeth graze. " +
             "-0.25 lets a full jump from a 4-tile ledge pass under a closed rift with room to spare.")]
    public float headMargin = -0.25f;
    [Tooltip("Depth (units) at which the rift stops growing, about the full lane height.")]
    public float maxDepth = 12f;

    float[] depth = new float[2];
    float[] slowFor = new float[2];
    float[] rate = new float[2];        // units per second, positive grows
    float[] rateAccel = new float[2];   // SmoothDamp state of rate
    float[] frontClock = new float[2];

    class Tendril
    {
        public float x, halfWidth, length, maxLength, speed, hold, lean;
        public bool receding;
    }

    readonly List<Tendril>[] tendrils = { new List<Tendril>(), new List<Tendril>() };
    readonly System.Random rng = new System.Random(23);
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

    void UpdateTendrils(int lane, float sprout, bool retreating, float viewX, float halfView, float dt)
    {
        var list = tendrils[lane];
        float d = depth[lane];
        if (sprout > 0f && list.Count < maxTendrils && rng.NextDouble() < sprout * (tendrilRate + tendrilsPerDepth * d) * dt)
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
            if (retreating) t.receding = true;
            if (t.receding)
            {
                t.length -= (retreating ? tendrilRecedeRunning : tendrilRecede) * dt;
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
        GameFlow.LevelReset += Close;
    }

    void OnDisable()
    {
        GameFlow.LevelReset -= Close;
        if (Instance == this) Instance = null;
    }

    public void Close()
    {
        for (int i = 0; i < depth.Length; i++) depth[i] = slowFor[i] = frontClock[i] = rate[i] = rateAccel[i] = 0f;
        foreach (var list in tendrils) list.Clear();
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

            var cam = cams != null && lane < cams.Length ? cams[lane] : null;
            float viewX = cam != null ? cam.transform.position.x : player.transform.position.x;
            float halfView = cam != null ? cam.orthographicSize * cam.aspect + 1f : 21f;
            frontClock[lane] += dt;

            float speed = flow.MeasuredSpeed(lane);
            float target;
            bool open = !(Suspended || safe[lane]);
            if (!open)
            {
                slowFor[lane] = 0f;
                target = -shrinkRate;
            }
            else if (speed < holdFrom)
            {
                slowFor[lane] += dt;
                float grow = 1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(growBelow, holdFrom, speed));
                target = slowFor[lane] > slowGrace ? grow * (depth[lane] + growthSeed) * growthRate : 0f;
            }
            else
            {
                slowFor[lane] = 0f;
                target = -shrinkRate * Mathf.InverseLerp(shrinkFrom, 1f, speed);
            }
            rate[lane] = Mathf.SmoothDamp(rate[lane], target, ref rateAccel[lane], rateSmoothTime, Mathf.Infinity, dt);
            depth[lane] += rate[lane] * dt;
            if (depth[lane] <= 0f || depth[lane] >= maxDepth)
            {
                // A closed or full rift stops dead instead of carrying its rate on.
                depth[lane] = Mathf.Clamp(depth[lane], 0f, maxDepth);
                if (depth[lane] <= 0f ? rate[lane] < 0f : rate[lane] > 0f) rate[lane] = rateAccel[lane] = 0f;
            }
            // Tendrils follow what the rift is doing: sprout fully while it
            // comes down, at holdTendrils while it holds, and pull back while
            // it retreats (a dead band so a rate easing through 0 holds).
            const float still = 0.05f;
            bool retreating = !open || rate[lane] < -still;
            float sprout = retreating ? 0f : rate[lane] > still ? 1f : holdTendrils;
            UpdateTendrils(lane, sprout, retreating, viewX, halfView, dt);
            if (!open) continue;

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
