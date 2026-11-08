using UnityEngine;

// Water-wheel bucket lift (Samuel's design, 2026-11-07): it needs fast time.
// A flume pours into the buckets on the wheel's left side. While the lane is
// slow the flume only trickles: the bucket under it fills and spills over its
// rim and the wheel stands still. When the partner runs, the flume gushes,
// the full left buckets turn the wheel counterclockwise, and a player standing
// in the bottom bucket rides up the right side to a ledge (as long as the
// partner keeps running). When the flow drops the wheel coasts to rest with a
// bucket under the stream (a detent every 45 degrees).
//
// Unlike the other lab obstacles the wheel has momentum, so it is simulated
// (in real time) rather than a function of lane time: the water's push grows
// with the lane's speed (`flowLow` to `flowHigh`), damping slows it, and a
// rider's weight turns it back (a rider on the right side sinks), so climbing
// the buckets of a standing wheel does not work. The water art runs on lane
// time. Buckets hang upright from the 8 rim pins (one-way kinematic floors,
// layer Ground) and carry their riders by position, like Conveyor.
// Art from tools/make_waterwheel.py; LevelMap.Lane.WaterWheel builds it.
[DefaultExecutionOrder(50)]   // after PlayerMovement: carries riders by position
public class WaterWheel : LaneObstacle
{
    [Header("Wheel")]
    [Tooltip("World position of the axle.")]
    public Vector2 centre;
    [Tooltip("Radius of the bucket pins (units).")]
    public float pinRadius = 92f / 32f;
    [Tooltip("Bucket floor below its pin (units).")]
    public float floorDrop = 30f / 32f;
    [Tooltip("Lane speeds at which the flume starts to gush and runs full.")]
    public float flowLow = 1.2f, flowHigh = 2.6f;
    [Tooltip("Angular acceleration from the water at full flow (degrees/s^2, counterclockwise).")]
    public float waterAccel = 240f;
    [Tooltip("Angular acceleration a rider's weight gives at the side of the wheel (degrees/s^2).")]
    public float riderAccel = 120f;
    [Tooltip("Damping (per second): with full flow the empty wheel settles at waterAccel / damping.")]
    public float damping = 3f;
    [Tooltip("Pull towards a bucket under the stream when nothing drives the wheel (per s^2).")]
    public float detent = 20f;
    [Tooltip("Pin angle (degrees, counterclockwise from +x) under the flume's mouth.")]
    public float pourAngle = 135f;

    [Header("Parts")]
    public SpriteRenderer wheel;
    public Sprite[] wheelFrames;          // 24, each 1.875 degrees counterclockwise
    public Rigidbody2D[] buckets;         // 8, pin j at wheel angle + 45 j
    public SpriteRenderer[] bucketWater;  // water surface over each bucket
    public Sprite[] bucketFrames;         // 19, tilt -90..90 in 10 degree steps
    public Sprite[] waterFrames;          // 7, tilt -30..30
    public SpriteRenderer[] flume;
    public Sprite[] flumeFrames;
    public SpriteRenderer[] tailrace;
    public Sprite[] tailraceFrames;
    public SpriteRenderer pour, trickle, spill;
    public Sprite[] pourFrames, trickleFrames;
    public SpriteRenderer splashTop, splashBottom;
    public Sprite[] splashFrames;
    [Tooltip("World position of the flume's mouth (top of the stream).")]
    public Vector2 mouth;
    [Tooltip("World y of the tailrace's water surface.")]
    public float tailraceY;

    const float WaterFps = 12f;
    const float TipTime = 0.6f;

    public float Angle { get; private set; }     // degrees
    public float Omega { get; private set; }     // degrees per second
    public float Flow { get; private set; }      // 0..1

    bool[] full, rider;
    Rigidbody2D[] riderOf;
    float[] tip, swing;
    SpriteRenderer[] bucketArt;
    SfxLoop sound;
    int lastNotch;

    void Awake()
    {
        int n = buckets.Length;
        full = new bool[n];
        rider = new bool[n];
        riderOf = new Rigidbody2D[n];
        tip = new float[n];
        swing = new float[n];
        bucketArt = new SpriteRenderer[n];
        for (int j = 0; j < n; j++) bucketArt[j] = buckets[j].GetComponent<SpriteRenderer>();
    }

    void Start()
    {
        sound = Sfx.Loop("water_loop");
        lastNotch = Mathf.FloorToInt((Angle + 22.5f) / 45f);
    }

    void OnDestroy() => Sfx.Release(sound);

    float PinAngle(int j) => Angle + 45f * j;

    Vector2 Pin(float deg) => centre + new Vector2(Mathf.Cos(deg * Mathf.Deg2Rad), Mathf.Sin(deg * Mathf.Deg2Rad)) * pinRadius;

    protected override void Step(float dt)
    {
        float s = LaneSpeed;
        Flow = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(flowLow, flowHigh, s));

        // Who stands in which bucket (before the wheel moves).
        bool anyRider = false;
        for (int j = 0; j < buckets.Length; j++)
        {
            rider[j] = false;
            riderOf[j] = null;
            Vector2 pin = Pin(PinAngle(j));
            float floor = pin.y - floorDrop;
            foreach (var p in Players)
            {
                if (!Live(p, out var rb, out var box)) continue;
                if (box.min.y > floor - 0.15f && box.min.y < floor + 0.25f
                    && Mathf.Abs(box.center.x - pin.x) < 0.75f && rb.linearVelocity.y <= 4f)
                {
                    rider[j] = anyRider = true;
                    riderOf[j] = rb;
                }
            }
        }

        // Torque: water pushes counterclockwise, riders' weight turns their side down.
        float accel = waterAccel * Flow - damping * Omega;
        for (int j = 0; j < buckets.Length; j++)
            if (rider[j]) accel -= riderAccel * Mathf.Cos(PinAngle(j) * Mathf.Deg2Rad);
        if (Flow < 0.05f && !anyRider)
        {
            // Settle with a bucket under the stream (pins every 45 degrees).
            float off = Mathf.DeltaAngle(Mathf.Round((Angle - pourAngle) / 45f) * 45f + pourAngle, Angle);
            accel += -detent * off - damping * Omega;
        }
        Omega += accel * dt;
        Angle += Omega * dt;

        int notch = Mathf.FloorToInt((Angle + 22.5f) / 45f);   // between rest positions, so a settled wheel stays quiet
        if (notch != lastNotch) Sfx.Play("ratchet_click", centre, 0.35f, lane: lane);
        lastNotch = notch;

        // Move the buckets, carrying their riders.
        for (int j = 0; j < buckets.Length; j++)
        {
            float phi = PinAngle(j);
            Vector2 pin = Pin(phi);
            Vector2 delta = pin - buckets[j].position;
            buckets[j].MovePosition(pin);
            if (riderOf[j] != null) riderOf[j].position += delta;

            // Water: the bucket under the stream fills (it overflows while the
            // wheel stands); a full one tips its water out at the bottom.
            float a = Mathf.Repeat(phi, 360f);
            if (Mathf.Abs(Mathf.DeltaAngle(a, pourAngle)) < 15f) full[j] = true;
            if (full[j] && a > 250f && a < 300f && !rider[j])
            {
                full[j] = false;
                tip[j] = TipTime;
            }
            tip[j] = Mathf.Max(0f, tip[j] - dt);
            float target = rider[j] ? 0f : Mathf.Clamp(-Omega * 0.25f, -20f, 20f);
            swing[j] = Mathf.Lerp(swing[j], target, 1f - Mathf.Exp(-dt * 4f));
        }
    }

    void LateUpdate()
    {
        float t = DrawT;
        Show(wheel, wheelFrames, (int)(Mathf.Repeat(Angle, 45f) / 1.875f) % 24);

        int under = -1;
        for (int j = 0; j < buckets.Length; j++)
        {
            float tilt = swing[j];
            if (tip[j] > 0f) tilt -= 70f * Mathf.Sin(Mathf.PI * (1f - tip[j] / TipTime));
            Show(bucketArt[j], bucketFrames, Mathf.Clamp(Mathf.RoundToInt(tilt / 10f) + 9, 0, 18));
            var w = bucketWater != null && j < bucketWater.Length ? bucketWater[j] : null;
            if (w != null)
            {
                w.enabled = full[j] && Mathf.Abs(tilt) <= 35f;
                if (w.enabled) Show(w, waterFrames, Mathf.Clamp(Mathf.RoundToInt(tilt / 10f) + 3, 0, 6));
            }
            if (Mathf.Abs(Mathf.DeltaAngle(PinAngle(j), pourAngle)) < 12f) under = j;
        }

        int wf = Loop(t, WaterFps, 0, 8);
        foreach (var sr in flume) Show(sr, flumeFrames, wf);
        foreach (var sr in tailrace) Show(sr, tailraceFrames, wf);

        bool gush = Flow > 0.3f;
        // The stream ends in the bucket under the mouth, else in the tailrace.
        float streamBottom = under >= 0 ? Pin(PinAngle(under)).y - 6f / 32f : tailraceY;
        if (pour != null)
        {
            pour.enabled = gush;
            pour.size = new Vector2(pour.size.x, Mathf.Max(0.25f, mouth.y - streamBottom));
            Show(pour, pourFrames, wf);
        }
        if (trickle != null)
        {
            trickle.enabled = !gush;
            trickle.size = new Vector2(trickle.size.x, Mathf.Max(0.25f, mouth.y - streamBottom));
            Show(trickle, trickleFrames, wf);
        }
        // Overflow: the full bucket under a standing wheel spills over its left rim.
        bool spilling = under >= 0 && full[under] && Mathf.Abs(Omega) < 3f;
        if (spill != null)
        {
            spill.enabled = spilling;
            if (spilling)
            {
                Vector2 pin = Pin(PinAngle(under));
                spill.transform.position = new Vector3(pin.x - 0.62f, pin.y - 0.2f, 0f);
                spill.size = new Vector2(spill.size.x, Mathf.Max(0.25f, pin.y - 0.2f - tailraceY));
                Show(spill, trickleFrames, (wf + 3) % 8);
            }
        }
        if (splashTop != null)
        {
            splashTop.enabled = gush && under >= 0;
            if (splashTop.enabled)
            {
                splashTop.transform.position = new Vector3(mouth.x, streamBottom, 0f);
                Show(splashTop, splashFrames, Loop(t, WaterFps, 0, 6));
            }
        }
        if (splashBottom != null)
        {
            int tipping = -1;
            for (int j = 0; j < buckets.Length; j++) if (tip[j] > 0f) tipping = j;
            splashBottom.enabled = tipping >= 0 || spilling;
            if (splashBottom.enabled)
            {
                float x = tipping >= 0 ? Pin(PinAngle(tipping)).x - 0.4f : spill.transform.position.x;
                splashBottom.transform.position = new Vector3(x, tailraceY, 0f);
                Show(splashBottom, splashFrames, Loop(t, WaterFps, 0, 6));
            }
        }

        sound?.Set(true, centre, 0.25f + 0.6f * Flow);
    }
}
