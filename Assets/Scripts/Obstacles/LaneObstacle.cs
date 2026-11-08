using UnityEngine;

// Shared base of the obstacle lab pieces (Pendulum, GearFall, TeslaGate,
// SteamVent, WaterWheel). Each runs on its lane's time: laneTime adds up
// fixed steps scaled by GameManager.GetWorldSpeed(lane), like TrapDoor and
// LaneLift, so the obstacle is a function of lane time (T) and slows or
// speeds up with the partner. Also helpers to find the players and kill them
// through GameFlow.KillAll (both die and respawn, like every other hazard).
public abstract class LaneObstacle : MonoBehaviour
{
    [Tooltip("Lane whose world speed drives it (0 = Tick, 1 = Tock).")]
    public int lane;
    [Tooltip("Lane seconds to shift its cycle by.")]
    public float cycleOffset;

    protected float laneTime;

    protected float LaneSpeed => GameManager.INSTANCE != null ? GameManager.INSTANCE.GetWorldSpeed(lane) : 1f;

    // Lane time of the last physics step, shifted by the cycle offset.
    protected float T => laneTime + cycleOffset;

    // Lane time for drawing: the last physics step plus the time since, so the
    // art moves smoothly when frames come faster than physics steps.
    protected float DrawT => T + Mathf.Max(0f, Time.time - Time.fixedTime) * LaneSpeed;

    protected virtual void FixedUpdate()
    {
        laneTime += Time.fixedDeltaTime * LaneSpeed;
        Step(Time.fixedDeltaTime);
    }

    // One physics step, after laneTime has advanced.
    protected abstract void Step(float dt);

    static PlayerMovement[] players;

    // The players in the scene (cached; found again if one went away).
    protected static PlayerMovement[] Players
    {
        get
        {
            bool stale = players == null;
            if (!stale)
                foreach (var p in players)
                    if (p == null) { stale = true; break; }
            if (stale) players = FindObjectsByType<PlayerMovement>();
            return players;
        }
    }

    // A player that can be hit or carried right now (not dead or respawning).
    protected static bool Live(PlayerMovement p, out Rigidbody2D rb, out Bounds box)
    {
        rb = null;
        box = default;
        if (p == null || !p.enabled) return false;
        if (GameFlow.Instance != null && GameFlow.Instance.Respawning) return false;
        rb = p.GetComponent<Rigidbody2D>();
        var col = p.GetComponent<BoxCollider2D>();
        if (rb == null || col == null || !rb.simulated) return false;
        box = col.bounds;
        return true;
    }

    protected static void KillAll()
    {
        if (GameFlow.Instance != null) GameFlow.Instance.KillAll();
    }

    protected static bool CircleHitsBox(Vector2 c, float r, Bounds b)
    {
        var q = new Vector2(Mathf.Clamp(c.x, b.min.x, b.max.x), Mathf.Clamp(c.y, b.min.y, b.max.y));
        return (q - c).sqrMagnitude <= r * r;
    }

    // Frame of a looping animation at `fps` frames per lane second.
    protected static int Loop(float t, float fps, int first, int count) =>
        first + (int)Mathf.Repeat(Mathf.Floor(t * fps), count);

    protected static void Show(SpriteRenderer sr, Sprite[] frames, int i)
    {
        if (sr != null && frames != null && i >= 0 && i < frames.Length) sr.sprite = frames[i];
    }
}
