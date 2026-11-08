using UnityEngine;

// Piston run (Samuel, 2026-11-08; DESIGN.md "Obstacle kit", mockup
// mockups/piston-run/): a low corridor whose floor is a row of flush rams, one
// tile each with no gaps, under a ceiling slab. A player stepping on the first
// plate starts them: armTime lane seconds later the first ram slams up into the
// ceiling, then one every stepTime, so a wall chases the runner down the
// corridor. The rams stay up for sealTime after the last one, ratchet back down
// from the exit end, and the run arms again after coolTime.
//
// Everything runs on lane time, so the wave moves at lane speed / stepTime
// tiles per second (7 at 0.35x, 60 at 3x) against a run of 8: only a lane at
// its slowest (partner standing still) can be outrun. Break-even about 0.44x.
//
// Each ram is a kinematic body (layer Ground) whose box reaches from its top
// down into the ground, so a risen ram is a solid column. One rising under a
// player lifts them into the ceiling (CrushDetector); one whose top passes a
// player's feet by more than killDepth while overlapping them kills outright,
// so a fast ram that skips past a player in one step still crushes.
// A death (GameFlow.LevelReset) drops every ram and rearms the run.
// Art from tools/make_floor_ram.py; LevelMap.Lane.FloorRams builds it.
public class FloorRams : LaneObstacle
{
    [Header("Timing (lane seconds)")]
    public float armTime = 0.08f;
    public float stepTime = 0.05f;
    public float riseTime = 0.06f;
    public float sealTime = 0.6f;
    public float retractStep = 0.03f;
    public float retractTime = 0.45f;
    public float coolTime = 0.4f;
    [Tooltip("How long before its ram fires a housing lamp turns amber (the chase light).")]
    public float leadTime = 0.1f;
    [Tooltip("Lane speed up to which the gauge's lamp shows green (the run can be outrun).")]
    public float goSpeed = 0.4f;
    [Tooltip("Kill a player once a ram's top is this far above their feet while overlapping them.")]
    public float killDepth = 0.3f;

    [Header("Layout (world units)")]
    public float left;
    [Tooltip("Rams' top when flush: just under the ground collider's top.")]
    public float floorY;
    [Tooltip("The ceiling's underside: the rams' top when up.")]
    public float ceilingY;

    [Header("Parts (one per ram)")]
    public Rigidbody2D[] rams;
    public Transform[] heads;
    public SpriteRenderer[] shafts;
    public SpriteRenderer[] lamps;
    public SpriteRenderer[] flashes;
    public SpriteRenderer[] sparks;
    public Sprite[] lampFrames;   // off, green, dim red, amber, red

    [Header("Gauge")]
    public SpriteRenderer needle;
    public Sprite[] needleFrames;
    public SpriteRenderer goLamp;
    public Sprite[] goLampFrames;   // red, green

    // Head art: while flush its top sits on the ground's visual surface (3 px
    // under the housing tile's top, which is 1 px over the ground collider's
    // top, which is 0.02 over floorY); when up it meets the ceiling's pad.
    const float HeadSink = 2f / 32f - 0.02f;
    // Shaft starts in the housing's bore, 6 px under the housing tile's top
    // (which is 1 px over floorY + 0.02).
    const float ShaftBottom = 0.02f + 1f / 32f - 6f / 32f;
    const float FlashTime = 0.09f;   // real seconds

    bool running;
    float tau;
    float[] lift, lastLift, flashLeft;

    int Count => rams != null ? rams.Length : 0;
    float Travel => ceilingY - floorY;
    float FireAt(int k) => armTime + k * stepTime;
    float LastUp => FireAt(Count - 1) + riseTime;
    float RetractAt(int k) => LastUp + sealTime + (Count - 1 - k) * retractStep;
    float CycleEnd => RetractAt(0) + retractTime + coolTime;

    public bool Running => running;

    void Awake()
    {
        lift = new float[Count];
        lastLift = new float[Count];
        flashLeft = new float[Count];
    }

    void OnEnable() => GameFlow.LevelReset += Rearm;
    void OnDisable() => GameFlow.LevelReset -= Rearm;

    void Rearm()
    {
        running = false;
        tau = 0f;
        for (int k = 0; k < Count; k++)
        {
            lift[k] = lastLift[k] = 0f;
            if (rams[k] != null) rams[k].position = new Vector2(left + k, floorY);
        }
    }

    float Lift(float t, int k)
    {
        float fire = FireAt(k);
        if (t < fire) return 0f;
        if (t < fire + riseTime)
        {
            float u = (t - fire) / riseTime;
            return Travel * u * u;   // accelerating slam
        }
        float back = RetractAt(k);
        if (t < back) return Travel;
        if (t < back + retractTime)
        {
            float u = (t - back) / retractTime;
            return Travel * (1f - Mathf.Min(6f, Mathf.Floor(u * 6f + 0.5f)) / 6f);   // 6 ratchet clicks
        }
        return 0f;
    }

    // A live player in this lane, standing near the corridor's floor.
    bool InLane(Bounds box) => Mathf.Abs(box.min.y - floorY) < 6f;

    protected override void Step(float dt)
    {
        if (!running)
        {
            foreach (var p in Players)
            {
                if (!Live(p, out _, out var box) || !InLane(box)) continue;
                if (box.center.x >= left && box.center.x < left + 1f && box.min.y < floorY + 0.15f)
                {
                    running = true;
                    tau = 0f;
                    Sfx.Play("ratchet_click", new Vector3(left + 0.5f, floorY, 0f), 0.6f, lane: lane);
                    break;
                }
            }
        }
        else
        {
            tau += dt * LaneSpeed;
            if (tau > CycleEnd) Rearm();
        }

        for (int k = 0; k < Count; k++)
        {
            lastLift[k] = lift[k];
            lift[k] = running ? Lift(tau, k) : 0f;
            if (rams[k] != null) rams[k].MovePosition(new Vector2(left + k, floorY + lift[k]));
            var at = new Vector3(left + k + 0.5f, floorY + lift[k], 0f);
            if (lift[k] >= Travel && lastLift[k] < Travel)
            {
                flashLeft[k] = FlashTime;
                Sfx.Play("plunger_slam", at, 0.3f, lane: lane);
            }
            else if (lift[k] < lastLift[k] && lastLift[k] >= Travel)
                Sfx.Play("ratchet_click", at, 0.25f, lane: lane);
        }

        if (!running) return;
        foreach (var p in Players)
        {
            if (!Live(p, out _, out var box) || !InLane(box)) continue;
            for (int k = 0; k < Count; k++)
            {
                if (lift[k] <= 0f) continue;
                float overlap = Mathf.Min(box.max.x, left + k + 1f) - Mathf.Max(box.min.x, left + k);
                if (overlap > 0.05f && floorY + lift[k] > box.min.y + killDepth)
                {
                    KillAll();
                    return;
                }
            }
        }
    }

    void LateUpdate()
    {
        float speed = LaneSpeed;
        for (int k = 0; k < Count; k++)
        {
            if (rams[k] == null) continue;
            float top = rams[k].transform.position.y;   // interpolated
            float up = Mathf.Clamp01((top - floorY) / Travel);
            if (heads != null && heads[k] != null)
                heads[k].localPosition = new Vector3(0f, -HeadSink * (1f - up), 0f);
            if (shafts != null && shafts[k] != null)
            {
                float height = top - HeadSink * (1f - up) - 0.5f - (floorY + ShaftBottom) + 1f / 32f;
                shafts[k].enabled = height > 0.01f;
                if (shafts[k].enabled) shafts[k].size = new Vector2(1f, height);
            }
            if (lamps != null && lamps[k] != null) Show(lamps[k], lampFrames, LampFrame(k, speed));
            if (flashLeft != null && k < flashLeft.Length)
            {
                if (flashLeft[k] > 0f) flashLeft[k] -= Time.deltaTime;
                bool lit = flashLeft[k] > 0f;
                if (flashes != null && flashes[k] != null) flashes[k].enabled = lit;
                if (sparks != null && sparks[k] != null) sparks[k].enabled = lit;
            }
        }
        float frac = Mathf.Sqrt(Mathf.Clamp01((speed - 0.35f) / 2.65f));
        Show(needle, needleFrames, Mathf.RoundToInt(frac * (needleFrames != null ? needleFrames.Length - 1 : 0)));
        Show(goLamp, goLampFrames, !running && speed <= goSpeed ? 1 : 0);
    }

    // 0 off, 1 green, 2 dim red, 3 amber, 4 red.
    int LampFrame(int k, float speed)
    {
        if (!running) return speed <= goSpeed ? 1 : 2;
        float t = tau + Mathf.Max(0f, Time.time - Time.fixedTime) * speed;
        float back = RetractAt(k);
        if (t >= back) return Mathf.Repeat(t * 12f, 2f) < 1f ? 3 : 0;
        if (t >= FireAt(k) + riseTime) return 4;
        if (t >= FireAt(k) - leadTime) return 3;
        return 0;
    }
}
