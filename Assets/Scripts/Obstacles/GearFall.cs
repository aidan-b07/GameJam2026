using UnityEngine;

// Gear fall: a ceiling chute drops a 2x2 brass cog every `interval` lane
// seconds. The cog falls with lane-time gravity, lands, rolls `rollTime`
// along the ground and shatters. Every cog is a pure function of lane time
// (cog k leaves the chute at k * interval), so a slow lane (partner standing)
// makes the cogs hang and roll slowly: solid stepping stones (kinematic,
// layer Ground) a player can stand on, ride and jump off to reach a ledge.
// A fast lane rains them down. A falling cog that lands on a player kills
// both; a rolling cog carries players standing on it.
// The lamp blinks amber before a drop and is red while the shutters are open.
// Art from tools/make_gearfall.py; LevelMap.Lane.GearFall builds it with a
// pool of cog bodies (enough for the cogs alive at once).
[DefaultExecutionOrder(50)]   // after PlayerMovement, like Conveyor: carries riders by position
public class GearFall : LaneObstacle
{
    [Tooltip("Lane seconds between drops.")]
    public float interval = 3f;
    [Tooltip("Fall acceleration in units per lane second squared (players have 29.4).")]
    public float gravity = 12f;
    [Tooltip("Roll speed in units per lane second; negative rolls left.")]
    public float rollSpeed = 2f;
    [Tooltip("Lane seconds a cog rolls before it shatters.")]
    public float rollTime = 2f;
    [Tooltip("World position of a cog's centre as it leaves the chute.")]
    public Vector2 spawn;
    [Tooltip("World y of a landed cog's centre.")]
    public float landY;
    [Tooltip("Cog collider radius (units).")]
    public float radius = 0.9375f;

    [Header("Art")]
    public SpriteRenderer chute;
    public Sprite[] chuteFrames;     // 0 closed, 1-4 opening
    public SpriteRenderer lamp;
    public Sprite[] lampFrames;      // off, amber, red
    public Sprite[] gearFrames;      // 8, one loop turns 60 degrees clockwise
    public Sprite[] shatterFrames;   // 8 at 15 fps
    [Tooltip("Cog bodies, reused in turn (cog k uses k mod count).")]
    public Rigidbody2D[] gears;

    const float ShatterFps = 15f;
    const int ShatterCount = 8;
    const float OpenTime = 0.35f;        // shutters open before and after a drop
    const float BlinkTime = 0.9f;        // amber blinking before a drop
    const float RollPerLoop = 31.4f / 32f;  // units rolled per 8-frame loop

    struct Slot { public int cog; public int phase; public SpriteRenderer sr; public Collider2D col; }
    Slot[] slots;

    float FallTime => Mathf.Sqrt(2f * Mathf.Max(0.01f, spawn.y - landY) / Mathf.Max(0.01f, gravity));
    float Life => FallTime + rollTime + ShatterCount / ShatterFps;

    void Awake()
    {
        slots = new Slot[gears.Length];
        for (int i = 0; i < gears.Length; i++)
            slots[i] = new Slot { cog = int.MinValue, sr = gears[i].GetComponent<SpriteRenderer>(), col = gears[i].GetComponent<Collider2D>() };
    }

    // Where cog k is at lane time t: phase 0 waiting, 1 falling, 2 rolling, 3 shattering, 4 gone.
    int Pose(int k, float t, out Vector2 pos, out int frame)
    {
        float age = t - k * interval;
        pos = spawn;
        frame = 0;
        if (age < 0f) return 0;
        float fall = FallTime;
        if (age < fall)
        {
            pos = spawn + Vector2.down * (0.5f * gravity * age * age);
            return 1;
        }
        float ra = Mathf.Min(age - fall, rollTime);
        float dist = rollSpeed * ra;
        pos = new Vector2(spawn.x + dist, landY);
        if (age - fall < rollTime)
        {
            // Rolling right turns the cog clockwise: frames forward (left: backward).
            frame = (int)Mathf.Repeat(Mathf.Floor(dist / RollPerLoop * 8f), 8f);
            return 2;
        }
        float sa = age - fall - rollTime;
        frame = (int)(sa * ShatterFps);
        return frame < ShatterCount ? 3 : 4;
    }

    protected override void Step(float dt)
    {
        float t = T;
        int newest = Mathf.FloorToInt(t / interval);
        int n = slots.Length;
        for (int k = newest - n + 1; k <= newest; k++)
        {
            int i = ((k % n) + n) % n;
            ref var s = ref slots[i];
            var rb = gears[i];
            int phase = Pose(k, t, out var pos, out _);
            bool fresh = s.cog != k;
            Vector2 before = rb.position;
            if (fresh)
            {
                s.cog = k;
                rb.position = pos;   // teleport back to the chute
                rb.transform.position = pos;
                before = pos;
            }
            else
            {
                rb.MovePosition(pos);
            }
            bool solid = phase == 1 || phase == 2;
            if (s.col != null) s.col.enabled = solid;

            // Sounds on entering a phase.
            if (!fresh && phase != s.phase)
            {
                if (phase == 2) Sfx.Play("gear_clank", pos, 0.8f, lane: lane);
                else if (phase == 3) Sfx.Play("gear_shatter", pos, 0.8f, lane: lane);
            }
            s.phase = phase;

            if (!solid) continue;
            Vector2 delta = pos - before;
            foreach (var p in Players)
            {
                if (!Live(p, out var prb, out var box)) continue;
                // Landing on a player: the falling cog overlaps them from above.
                if (phase == 1 && CircleHitsBox(pos, radius - 0.1f, box) && box.center.y < pos.y - 0.3f)
                {
                    KillAll();
                    return;
                }
                // Carry a rider standing on top of a rolling cog.
                if (phase == 2 && Mathf.Abs(box.min.y - (pos.y + radius)) < 0.2f
                    && Mathf.Abs(box.center.x - pos.x) < radius * 0.8f && prb.linearVelocity.y <= 0.5f)
                    prb.position += new Vector2(delta.x, 0f);
            }
        }
    }

    void LateUpdate()
    {
        float t = DrawT;
        int newest = Mathf.FloorToInt(t / interval);
        int n = slots.Length;
        for (int k = newest - n + 1; k <= newest; k++)
        {
            int i = ((k % n) + n) % n;
            var sr = slots[i].sr;
            if (sr == null) continue;
            int phase = Pose(k, t, out _, out int frame);
            sr.enabled = phase >= 1 && phase <= 3;
            if (phase == 3) Show(sr, shatterFrames, frame);
            else Show(sr, gearFrames, frame);   // rolling left counts the frames down
        }

        // Chute and lamp, from the time to the next drop and since the last one.
        float since = Mathf.Repeat(t, interval);
        float until = interval - since;
        int open = since < OpenTime ? 4 : until < OpenTime ? Mathf.Clamp(4 - (int)(until / OpenTime * 4f), 0, 4) : 0;
        Show(chute, chuteFrames, open);
        int light = since < OpenTime ? 2 : until < BlinkTime ? ((int)(until * 6f) % 2 == 0 ? 1 : 0) : 0;
        Show(lamp, lampFrames, light);
    }
}
