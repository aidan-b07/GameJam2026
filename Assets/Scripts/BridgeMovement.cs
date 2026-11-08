using System;
using System.Collections.Generic;
using UnityEngine;

// Heavy drawbridge that swings about a hinge on a loop:
// pause hanging, winch it over the top, it drops open and bounces on the
// ground, pause, winch it back over the top, it drops into the pit and
// rattles against the pit wall, repeat.
//
// Each swing moves like a real drawbridge on a winch chain: the winch eases
// in and then lifts at a steady speed (liftTimePer90) up to standing straight
// up (topAngle), and carries the leaf over the top without slowing. There the
// chain runs free and gravity takes over: the leaf is a rod on a hinge
// (angular acceleration -K sin(angle from hanging)), so it speeds up all the
// way down. It bounces off the ground when it lands open (groundRestitution)
// and off the pit wall when it drops back (wallRestitution) until a bounce is
// slower than settleSpeed. K is worked out so the drop from standing up to
// flat open takes fallTime. Everything runs on lane time
// (GameManager.GetWorldSpeed(lane)) in small fixed substeps, so the motion
// is the same at any lane speed.
//
// The leaf is a kinematic Rigidbody2D moved with MoveRotation and MovePosition
// in FixedUpdate, so it rotates exactly about the hinge point. Players cross by
// stopping lane time while it spans the pit (solidAngle). By default it is
// solid all the way round: it shoves players out of its way, and one caught
// between it and the ground is crushed (CrushDetector on the player). A
// smaller solidWindow makes it solid only near solidAngle and scenery
// elsewhere. Players standing on the deck are carried along it while it moves
// (PlayerMovement overwrites its horizontal velocity every step, so friction
// alone would not carry them).
//
// Angles are the body's world Z rotation in degrees. With the leaf art (long
// axis = local Y, hinge at the top end) a rotation of 0 hangs straight down;
// turning counterclockwise by 270 degrees passes through pointing right and
// pointing up and lands flat pointing left. Gravity assumes restAngle hangs
// straight down. DrawbridgeWinch reads Angle, Winching and FreeFalling to
// drive the winch tower, and listens to Impact.
[RequireComponent(typeof(Rigidbody2D))]
public class BridgeMovement : MonoBehaviour
{
    public enum SwingDirection { Counterclockwise, Clockwise }

    [Header("Hinge")]
    [Tooltip("Hinge point in the bridge's local space (like a HingeJoint2D anchor). " +
             "(0, 0.5) is the middle of the top end. The world hinge point is taken " +
             "from where the bridge stands in the scene when play starts.")]
    [SerializeField] private Vector2 hingeAnchor = new Vector2(0f, 0.5f);

    [Header("Angles (world Z rotation, degrees)")]
    [Tooltip("Rotation at rest. 0 = the leaf hangs straight down from the hinge.")]
    [SerializeField] private float restAngle = 0f;
    [Tooltip("Rotation when fully open. 270 = flat, pointing left from the hinge.")]
    [SerializeField] private float openAngle = 270f;
    [Tooltip("Which way the bridge turns when opening. It closes the same way back.")]
    [SerializeField] private SwingDirection direction = SwingDirection.Counterclockwise;

    [Tooltip("Rotation where the leaf stands straight up: the winch lifts it to here, " +
             "then it falls. 180 for a leaf hanging down at 0.")]
    [SerializeField] private float topAngle = 180f;

    [Header("Timing (seconds of lane time)")]
    [Tooltip("Winch time per 90 degrees of lifting, at full winch speed.")]
    [SerializeField, Min(0.05f)] private float liftTimePer90 = 1.4f;
    [Tooltip("Time the winch takes to get up to full speed.")]
    [SerializeField, Min(0f)] private float winchRamp = 0.5f;
    [Tooltip("Time the leaf takes to drop from standing up to flat open. Sets gravity.")]
    [SerializeField, Min(0.1f)] private float fallTime = 0.75f;
    [Tooltip("Wait at rest (hanging) before opening, also used once at start.")]
    [SerializeField, Min(0f)] private float restPause = 2f;
    [Tooltip("Wait while open (flat) before closing.")]
    [SerializeField, Min(0f)] private float openPause = 2f;
    [Tooltip("While opening, the winch holds the leaf across the pit (solidAngle) this long " +
             "before lifting on. 0 lifts straight through.")]
    [SerializeField, Min(0f)] private float spanPause = 0f;

    [Header("Bounces")]
    [Tooltip("Share of its speed the leaf keeps when it bounces off the ground (open).")]
    [SerializeField, Range(0f, 0.9f)] private float groundRestitution = 0.3f;
    [Tooltip("Share of its speed the leaf keeps when it bounces off the pit wall (closed).")]
    [SerializeField, Range(0f, 0.9f)] private float wallRestitution = 0.35f;
    [Tooltip("Air and hinge friction, per second.")]
    [SerializeField, Min(0f)] private float damping = 0.12f;
    [Tooltip("A bounce slower than this (degrees per second) ends the swing.")]
    [SerializeField, Min(0f)] private float settleSpeed = 20f;

    [Header("Collision and riders")]
    [Tooltip("Rotation where the leaf spans the pit, flat. 90 for a leaf hanging " +
             "from the pit's left edge.")]
    [SerializeField] private float solidAngle = 90f;
    [Tooltip("The leaf is solid only within this many degrees of solidAngle; " +
             "elsewhere players pass through it. 180 = always solid.")]
    [SerializeField, Range(0f, 180f)] private float solidWindow = 180f;
    [Tooltip("Carry bodies standing on the leaf while it moves.")]
    [SerializeField] private bool carryRiders = true;
    [Tooltip("Steepest slope (degrees) a rider is still carried on; steeper and they fall off.")]
    [SerializeField, Range(0f, 89f)] private float maxCarrySlope = 60f;

    [SerializeField] 
    [Tooltip("Which lane is the object in? 0 = top, 1 = bottom")]
    [Range(0, 1)]
    private int lane;
    
    private enum Phase { RestPause, Opening, OpenPause, Closing }

    // Physics substep in lane seconds.
    private const float Substep = 1f / 240f;

    private Rigidbody2D rb;
    private Collider2D[] colliders;
    private Vector2 pivot;          // world hinge point, fixed for the whole run
    private Vector2 hingeOffset;    // hinge relative to the body origin at rotation 0, world units
    private Phase phase = Phase.RestPause;
    private float phaseTime;
    private float currentAngle;     // unwrapped target angle of the last step

    // Swing state, in degrees along the opening direction from rest (0 = hanging,
    // |SwingDelta| = open) and degrees per second.
    private float progress, speed, winchTime;
    private bool winching, freeFalling;
    private float spanHeld = -1f;   // lane seconds held across the pit this opening; -1 before the hold
    private float gravity;          // K in degrees per second squared

    // Sounds (Sfx): winch loop while the winch lifts, a clank when the pawl
    // lets go at the top, a slam for every bounce.
    private SfxLoop winchSound;
    private bool clankDue;
    private readonly List<(float speed, bool open)> impacts = new List<(float, bool)>();

    private readonly List<ContactPoint2D> contacts = new List<ContactPoint2D>();
    private readonly HashSet<Rigidbody2D> carried = new HashSet<Rigidbody2D>();

    // Current rotation (degrees), the world hinge point and the lane it runs in.
    public float Angle => currentAngle;
    public Vector2 Pivot => pivot;
    public int Lane => lane;
    // The winch is lifting the leaf / the leaf is dropping freely before its first bounce.
    public bool Winching => winching && !HoldingSpan;
    // The winch is holding the leaf across the pit (spanPause).
    public bool HoldingSpan => spanHeld >= 0f && spanHeld < spanPause;
    public bool FreeFalling => freeFalling;
    // The leaf hit the ground (open = true) or the pit wall, at this speed in degrees per second.
    public event Action<float, bool> Impact;

    // Signed opening rotation, for example +270 for counterclockwise from 0 to 270.
    private float SwingDelta
    {
        get
        {
            float delta = direction == SwingDirection.Counterclockwise
                ? Mathf.Repeat(openAngle - restAngle, 360f)
                : -Mathf.Repeat(restAngle - openAngle, 360f);
            return Mathf.Approximately(delta, 0f) ? 0f : delta;
        }
    }

    // Part of the opening swing (degrees, unsigned) that is lifted by the winch,
    // from rest up to topAngle; the rest of it is the drop onto the ground.
    // Without a top inside the swing the whole swing is winched.
    private float LiftAngle
    {
        get
        {
            float delta = Mathf.Abs(SwingDelta);
            float lift = direction == SwingDirection.Counterclockwise
                ? Mathf.Repeat(topAngle - restAngle, 360f)
                : Mathf.Repeat(restAngle - topAngle, 360f);
            return lift > 0f && lift < delta ? lift : delta;
        }
    }

    private float WinchSpeed => 90f / liftTimePer90;

    private void Awake()
    {
        rb = GetComponent<Rigidbody2D>();
        rb.bodyType = RigidbodyType2D.Kinematic;
        rb.interpolation = RigidbodyInterpolation2D.Interpolate;
        rb.linearVelocity = Vector2.zero;
        rb.angularVelocity = 0f;

        // The hinge stays where it is in the scene; the leaf is posed around it.
        pivot = transform.TransformPoint(hingeAnchor);
        hingeOffset = Vector2.Scale(hingeAnchor, transform.lossyScale);

        currentAngle = restAngle;
        Vector2 pos = BodyPosition(restAngle);
        transform.SetPositionAndRotation(
            new Vector3(pos.x, pos.y, transform.position.z),
            Quaternion.Euler(0f, 0f, restAngle));
        rb.position = pos;
        rb.rotation = restAngle;

        colliders = GetComponents<Collider2D>();
        SetSolid(restAngle);
        gravity = TuneGravity();
    }

    private void Start()
    {
        winchSound = Sfx.Loop("winch_loop");
    }

    private void OnDestroy()
    {
        Sfx.Release(winchSound);
    }

    private void Update()
    {
        winchSound?.Set(Winching, pivot, 0.7f);
        if (clankDue) Sfx.Play("ratchet_click", pivot, 1f, lane: lane);
        clankDue = false;
        foreach (var (hitSpeed, open) in impacts)
        {
            float volume = Mathf.Clamp01(hitSpeed / 300f);
            if (volume > 0.05f)
                Sfx.Play("bridge_slam", open ? pivot + Vector2.left * 3f : pivot, volume, lane: lane);
        }
        impacts.Clear();
    }

    private void FixedUpdate()
    {
        var manager = GameManager.INSTANCE;
        float laneDelta = Time.fixedDeltaTime * (manager != null ? manager.GetWorldSpeed(lane) : 1f);
        int steps = Mathf.CeilToInt(laneDelta / Substep - 0.001f);
        for (int i = 0; i < steps; i++) Step(laneDelta / steps);

        float target = restAngle + Mathf.Sign(SwingDelta) * progress;
        float step = target - currentAngle;
        if (Mathf.Approximately(step, 0f)) return;

        if (carryRiders) CarryRiders(step);
        SetSolid(target);

        currentAngle = target;
        // Keep the requested rotation continuous with the body's own value, in
        // case the engine reports it wrapped, so MoveRotation never spins the long way.
        float bodyTarget = rb.rotation + Mathf.DeltaAngle(rb.rotation, target);
        rb.MovePosition(BodyPosition(target));
        rb.MoveRotation(bodyTarget);
    }

    // One substep of h lane seconds.
    private void Step(float h)
    {
        float delta = Mathf.Abs(SwingDelta), lift = LiftAngle;
        switch (phase)
        {
            case Phase.RestPause:
            case Phase.OpenPause:
                phaseTime += h;
                if (phaseTime < (phase == Phase.RestPause ? restPause : openPause)) return;
                phase = phase == Phase.RestPause ? Phase.Opening : Phase.Closing;
                winching = true;
                winchTime = 0f;
                spanHeld = -1f;
                return;

            case Phase.Opening:
                if (winching)
                {
                    // Hold across the pit for spanPause, then ease the winch up again.
                    float span = Mathf.Abs(Mathf.DeltaAngle(restAngle, solidAngle));
                    if (spanPause > 0f && spanHeld < spanPause && progress >= span)
                    {
                        progress = span;
                        speed = 0f;
                        spanHeld = Mathf.Max(spanHeld, 0f) + h;
                        if (spanHeld >= spanPause) winchTime = 0f;
                        return;
                    }
                    // Lift from rest up to the top, then let go at winch speed.
                    progress += WinchStep(h);
                    if (progress >= lift) LetGo(lift, speed);
                    else return;
                }
                if (Swing(h, delta, groundRestitution, true))
                {
                    phase = Phase.OpenPause;
                    phaseTime = 0f;
                }
                return;

            default:
                if (winching)
                {
                    progress -= WinchStep(h);
                    if (progress <= lift) LetGo(lift, -speed);
                    else return;
                }
                if (Swing(h, 0f, wallRestitution, false))
                {
                    phase = Phase.RestPause;
                    phaseTime = 0f;
                }
                return;
        }
    }

    // Winch travel (degrees) for one substep; eases up to full speed.
    private float WinchStep(float h)
    {
        winchTime += h;
        float u = winchRamp > 0f ? Mathf.Clamp01(winchTime / winchRamp) : 1f;
        speed = WinchSpeed * u * u * (3f - 2f * u);
        return speed * h;
    }

    private void LetGo(float at, float velocity)
    {
        progress = at;
        speed = velocity;
        winching = false;
        freeFalling = true;
        clankDue = true;
    }

    // Free swing under gravity against a stop at `limit` (the ground beyond the
    // swing, or the pit wall at rest). Returns true once it has come to rest.
    private bool Swing(float h, float limit, float restitution, bool open)
    {
        speed += (-gravity * Mathf.Sin(progress * Mathf.Deg2Rad) - damping * speed) * h;
        progress += speed * h;
        bool past = open ? progress >= limit : progress <= limit;
        if (!past) return false;

        progress = limit;
        float hit = Mathf.Abs(speed);
        freeFalling = false;
        if (hit < settleSpeed)
        {
            speed = 0f;
            return true;
        }
        impacts.Add((hit, open));
        Impact?.Invoke(hit, open);
        speed = -restitution * speed;
        return false;
    }

    // Gravity (degrees per second squared) that drops the leaf from the top to
    // open in fallTime, starting at winch speed.
    private float TuneGravity()
    {
        float lift = LiftAngle, delta = Mathf.Abs(SwingDelta);
        if (delta - lift < 1f) return 0f;
        float lo = 1f, hi = 1e6f;
        for (int i = 0; i < 40; i++)
        {
            float k = Mathf.Sqrt(lo * hi);
            float p = lift, v = WinchSpeed, t = 0f;
            while (p < delta && t < 10f)
            {
                v += -k * Mathf.Sin(p * Mathf.Deg2Rad) * Substep;
                p += v * Substep;
                t += Substep;
            }
            if (t > fallTime) lo = k; else hi = k;
        }
        return Mathf.Sqrt(lo * hi);
    }

    // Solid within solidWindow of spanning the pit; elsewhere the leaf is scenery.
    private void SetSolid(float angle)
    {
        bool solid = Mathf.Abs(Mathf.DeltaAngle(angle, solidAngle)) <= solidWindow;
        foreach (Collider2D c in colliders)
            if (c.enabled != solid) c.enabled = solid;
    }

    private Vector2 BodyPosition(float angle) => pivot - Rotate(hingeOffset, angle);

    private static Vector2 Rotate(Vector2 v, float degrees)
    {
        float r = degrees * Mathf.Deg2Rad;
        float c = Mathf.Cos(r), s = Mathf.Sin(r);
        return new Vector2(c * v.x - s * v.y, s * v.x + c * v.y);
    }

    // Moves every dynamic body resting on top of the leaf along the deck with
    // the leaf's rotation about the hinge for this step. Only the part along the
    // deck: the contact solver already moves riders with the deck's normal
    // motion, so adding that too would lift them twice and make them hop.
    private void CarryRiders(float step)
    {
        carried.Clear();
        int count = rb.GetContacts(contacts);
        float minNormalY = Mathf.Cos(maxCarrySlope * Mathf.Deg2Rad);
        for (int i = 0; i < count; i++)
        {
            ContactPoint2D c = contacts[i];
            Rigidbody2D other = c.rigidbody == rb ? c.otherRigidbody : c.rigidbody;
            if (other == null || other == rb || other.bodyType != RigidbodyType2D.Dynamic) continue;
            if (carried.Contains(other)) continue;

            // Standing on top: surface not too steep and the rider above the contact.
            if (Mathf.Abs(c.normal.y) < minNormalY) continue;
            if (other.worldCenterOfMass.y <= c.point.y) continue;

            carried.Add(other);
            Vector2 p = other.position;
            Vector2 move = pivot + Rotate(p - pivot, step) - p;
            Vector2 n = c.normal;
            other.position = p + move - Vector2.Dot(move, n) * n;
        }
    }

    private void OnDrawGizmos()
    {
        bool playing = Application.isPlaying && rb != null;
        Vector2 hinge = playing ? pivot : (Vector2)transform.TransformPoint(hingeAnchor);
        Vector2 offset = playing ? hingeOffset : Vector2.Scale(hingeAnchor, transform.lossyScale);
        Vector2 size = transform.lossyScale;

        // Leaf tip: the far end of the long axis from the hinge.
        bool longY = Mathf.Abs(size.y) >= Mathf.Abs(size.x);
        Vector2 tipLocal = longY
            ? new Vector2(hingeAnchor.x, hingeAnchor.y >= 0f ? -0.5f : 0.5f)
            : new Vector2(hingeAnchor.x >= 0f ? -0.5f : 0.5f, hingeAnchor.y);
        Vector2 tipOffset = Vector2.Scale(tipLocal, size) - offset;   // tip relative to hinge at rotation 0

        // Arc of the tip from rest to open.
        Gizmos.color = new Color(1f, 0.8f, 0.2f, 0.9f);
        float delta = SwingDelta;
        const int segments = 48;
        Vector2 prev = hinge + Rotate(tipOffset, restAngle);
        for (int i = 1; i <= segments; i++)
        {
            Vector2 next = hinge + Rotate(tipOffset, restAngle + delta * i / segments);
            Gizmos.DrawLine(prev, next);
            prev = next;
        }

        // Leaf outline at rest (green) and open (cyan).
        DrawLeaf(hinge, offset, size, restAngle, new Color(0.3f, 1f, 0.3f, 0.9f));
        DrawLeaf(hinge, offset, size, restAngle + delta, new Color(0.3f, 0.9f, 1f, 0.9f));

        // Hinge point.
        Gizmos.color = Color.red;
        Gizmos.DrawWireSphere(hinge, 0.2f);
        Gizmos.DrawLine(hinge + new Vector2(-0.3f, 0f), hinge + new Vector2(0.3f, 0f));
        Gizmos.DrawLine(hinge + new Vector2(0f, -0.3f), hinge + new Vector2(0f, 0.3f));
    }

    private static void DrawLeaf(Vector2 hinge, Vector2 offset, Vector2 size, float angle, Color color)
    {
        Gizmos.color = color;
        Vector2 a = Corner(-0.5f, -0.5f), b = Corner(0.5f, -0.5f), c = Corner(0.5f, 0.5f), d = Corner(-0.5f, 0.5f);
        Gizmos.DrawLine(a, b);
        Gizmos.DrawLine(b, c);
        Gizmos.DrawLine(c, d);
        Gizmos.DrawLine(d, a);

        Vector2 Corner(float x, float y) => hinge + Rotate(Vector2.Scale(new Vector2(x, y), size) - offset, angle);
    }
}
