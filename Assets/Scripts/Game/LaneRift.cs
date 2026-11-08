using UnityEngine;

// The Unraveling rift as a hazard: one rift hangs from the top edge of each
// lane's view (Tick's from the top of the screen, Tock's from the seam between
// the lanes) and grows down towards that lane's player while they dawdle.
//
// Each lane's rift follows its own player's horizontal speed (0..1 of their
// run speed). Below slowThreshold it grows exponentially: the growth rate is
// proportional to how deep it already is (plus growthSeed), so a short pause
// barely shows but a long one runs away. At or above the threshold it shrinks
// linearly, faster the faster the player runs (shrinkRate at full speed).
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
    [Tooltip("Exponential growth rate per second: depth grows by (depth + growthSeed) * this each second.")]
    public float growthRate = 0.55f;
    [Tooltip("Depth (units) the exponential grows from: higher starts faster. " +
             "From closed (after slowGrace) it reaches 1 unit in about 2 s and a standing player in about 5 s.")]
    public float growthSeed = 0.5f;

    [Header("Shrink (player at or above the threshold)")]
    [Tooltip("Units per second the rift closes at full run speed; scales with the player's speed.")]
    public float shrinkRate = 2.5f;

    [Header("Kill")]
    [Tooltip("Units above the top of the player's collider (their head) at which the rift kills; negative lets the teeth graze.")]
    public float headMargin = 0f;
    [Tooltip("Depth (units) at which the rift stops growing, about the full lane height.")]
    public float maxDepth = 12f;

    float[] depth = new float[2];
    float[] slowFor = new float[2];
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

    // Reach (units) of the lowest tooth tips: where the rift kills.
    public float TipReach(int lane)
    {
        float reach = Reach(lane);
        return reach + Mathf.Min(reach, TeethAmplitude) / 2f;
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
        for (int i = 0; i < depth.Length; i++) depth[i] = slowFor[i] = 0f;
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
                slowFor[lane] = 0f;
                depth[lane] = Mathf.Max(0f, depth[lane] - shrinkRate * dt);
                continue;
            }
            if (speed < slowThreshold)
            {
                slowFor[lane] += dt;
                if (slowFor[lane] > slowGrace)
                    depth[lane] += (depth[lane] + growthSeed) * growthRate * dt;
            }
            else
            {
                slowFor[lane] = 0f;
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
