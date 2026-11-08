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

    [Header("Growth (player slower than the threshold)")]
    [Tooltip("Normalised horizontal speed (0..1) below which the rift grows.")]
    [Range(0f, 1f)] public float slowThreshold = 0.35f;
    [Tooltip("Exponential growth rate per second: depth grows by (depth + growthSeed) * this each second.")]
    public float growthRate = 0.55f;
    [Tooltip("Depth (units) the exponential grows from: higher starts faster. " +
             "From closed it reaches 1 unit in about 2 s and the player in about 5 s.")]
    public float growthSeed = 0.5f;

    [Header("Shrink (player at or above the threshold)")]
    [Tooltip("Units per second the rift closes at full run speed; scales with the player's speed.")]
    public float shrinkRate = 2.5f;

    [Header("Kill")]
    [Tooltip("Units above the top of the player's collider (their head) at which the rift kills.")]
    public float headMargin = 0.1f;
    [Tooltip("Depth (units) at which the rift stops growing, about the full lane height.")]
    public float maxDepth = 12f;

    float[] depth = new float[2];

    // Rift reach (units) below the top of the lane's view: rest plus growth.
    public float Reach(int lane) => restReach + Depth(lane);

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
        for (int i = 0; i < depth.Length; i++) depth[i] = 0f;
    }

    void Update()
    {
        var flow = GameFlow.Instance;
        if (flow == null || flow.Respawning) return;
        var cams = flow.cameraRig != null ? flow.cameraRig.cameras : null;
        float dt = Time.deltaTime;

        for (int lane = 0; lane < depth.Length && lane < flow.players.Length; lane++)
        {
            var player = flow.players[lane];
            if (player == null || !player.gameObject.activeInHierarchy) continue;

            float speed = flow.HorizontalSpeed(lane);
            if (speed < slowThreshold)
                depth[lane] += (depth[lane] + growthSeed) * growthRate * dt;
            else
                depth[lane] -= shrinkRate * speed * dt;
            depth[lane] = Mathf.Clamp(depth[lane], 0f, maxDepth);

            var cam = cams != null && lane < cams.Length ? cams[lane] : null;
            if (cam == null) continue;
            float edge = cam.transform.position.y + cam.orthographicSize - Reach(lane);
            if (flow.Top(lane) + headMargin >= edge)
            {
                flow.KillAll();
                return;
            }
        }
    }
}
