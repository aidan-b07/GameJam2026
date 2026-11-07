using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Serialization;

// Heavy drawbridge that swings about a hinge on a loop:
// pause at rest, swing open, bounce on landing, pause, swing back, sway, repeat.
//
// Each swing moves like a real drawbridge: a slow winch lifts the leaf up to
// standing straight up (topAngle), it teeters there, then gravity takes it
// and it falls the rest of the way, faster and faster, onto the ground
// (bounce) or back to hanging (sway).
//
// The leaf is a kinematic Rigidbody2D moved with MoveRotation and MovePosition
// in FixedUpdate, so it rotates exactly about the hinge point. It is only solid
// near solidAngle, where it spans the pit; players cross by stopping lane time
// (SpeedMultiplier = 0) at that moment. Elsewhere it is scenery and does not
// shove or carry players. Players standing on the deck are carried along it
// while it moves (PlayerMovement overwrites its horizontal velocity every step,
// so friction alone would not carry them).
//
// Angles are the body's world Z rotation in degrees. With the placeholder
// (long axis = local Y, hinge at the top end) a rotation of 0 hangs straight
// down; turning counterclockwise by 270 degrees passes through pointing right
// and pointing up and lands flat pointing left.
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
    [Tooltip("Winch time per 90 degrees of lifting (slow and even).")]
    [SerializeField, Min(0.05f)] private float liftTimePer90 = 1.4f;
    [Tooltip("Time to fall 90 degrees from standing up. Longer falls take " +
             "longer by the square root of the angle, like a falling weight.")]
    [SerializeField, Min(0.05f)] private float fallTimePer90 = 0.7f;
    [Tooltip("Wait at rest (hanging) before opening, also used once at start.")]
    [SerializeField, Min(0f)] private float restPause = 2f;
    [Tooltip("Wait while open (flat) before closing.")]
    [SerializeField, Min(0f)] private float openPause = 2f;

    [Header("Feel")]
    [Tooltip("How long the landing bounce and the hanging sway last.")]
    [SerializeField, Min(0f)] private float settleDuration = 0.8f;
    [Tooltip("Number of bounces (landing) or half swings (hanging) while settling.")]
    [SerializeField, Min(0.5f)] private float settleBounces = 2f;
    [Tooltip("Height of the first rebound off the ground when it lands open, in degrees.")]
    [SerializeField, Min(0f)] private float landBounceAngle = 3f;
    [Tooltip("Overshoot of the first sway when it drops back to hanging, in degrees.")]
    [SerializeField, Min(0f)] private float hangSwayAngle = 5f;

    [Header("Lane time")]
    [Tooltip("Lane time scale: 1 = normal, 0 = stopped, 2 = fast. Set from the " +
             "time system through SpeedMultiplier.")]
    [FormerlySerializedAs("speedMut")]
    [SerializeField, Range(0f, MaxSpeedMultiplier)] private float speedMultiplier = 1f;

    [Header("Collision and riders")]
    [Tooltip("Rotation where the leaf spans the pit, flat. 90 for a leaf hanging " +
             "from the pit's left edge.")]
    [SerializeField] private float solidAngle = 90f;
    [Tooltip("The leaf is solid only within this many degrees of solidAngle; " +
             "elsewhere players pass through it.")]
    [SerializeField, Range(0f, 90f)] private float solidWindow = 25f;
    [Tooltip("Carry bodies standing on the leaf while it moves.")]
    [SerializeField] private bool carryRiders = true;
    [Tooltip("Steepest slope (degrees) a rider is still carried on; steeper and they fall off.")]
    [SerializeField, Range(0f, 89f)] private float maxCarrySlope = 60f;

    private enum Phase { RestPause, Opening, OpenPause, Closing }

    // Faster than this the leaf moves too far per physics step to push or carry
    // players reliably.
    private const float MaxSpeedMultiplier = 4f;

    private Rigidbody2D rb;
    private Collider2D[] colliders;
    private Vector2 pivot;          // world hinge point, fixed for the whole run
    private Vector2 hingeOffset;    // hinge relative to the body origin at rotation 0, world units
    private Phase phase = Phase.RestPause;
    private float phaseTime;
    private float currentAngle;     // unwrapped target angle of the last step

    private readonly List<ContactPoint2D> contacts = new List<ContactPoint2D>();
    private readonly HashSet<Rigidbody2D> carried = new HashSet<Rigidbody2D>();

    // Lane time scale hook for the time system (clamped to >= 0).
    public float SpeedMultiplier
    {
        get => speedMultiplier;
        set => speedMultiplier = Mathf.Clamp(value, 0f, MaxSpeedMultiplier);
    }

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

    private float LiftTime(float degrees) => liftTimePer90 * degrees / 90f;
    private float FallTime(float degrees) => fallTimePer90 * Mathf.Sqrt(degrees / 90f);

    // Opening: winch rest -> top, fall top -> open. Closing: winch open -> top,
    // fall top -> rest.
    private float OpeningSwingTime => LiftTime(LiftAngle) + FallTime(Mathf.Abs(SwingDelta) - LiftAngle);
    private float ClosingSwingTime => LiftTime(Mathf.Abs(SwingDelta) - LiftAngle) + FallTime(LiftAngle);

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
    }

    private void FixedUpdate()
    {
        phaseTime += Time.fixedDeltaTime * speedMultiplier;
        AdvancePhase();

        float target = AngleForPhase();
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

    private void AdvancePhase()
    {
        // Loop in case a large time step skips a whole short phase.
        for (int guard = 0; guard < 4; guard++)
        {
            float length = PhaseLength(phase);
            if (phaseTime < length) return;
            phaseTime -= length;
            phase = (Phase)(((int)phase + 1) % 4);
        }
    }

    private float PhaseLength(Phase p)
    {
        switch (p)
        {
            case Phase.RestPause: return restPause;
            case Phase.OpenPause: return openPause;
            case Phase.Opening: return OpeningSwingTime + settleDuration;
            default: return ClosingSwingTime + settleDuration;
        }
    }

    private float AngleForPhase()
    {
        float delta = SwingDelta;
        float sign = Mathf.Sign(delta);
        float lift = LiftAngle;                 // rest -> top, unsigned
        float drop = Mathf.Abs(delta) - lift;   // top -> open, unsigned
        float top = restAngle + sign * lift;
        switch (phase)
        {
            case Phase.Opening:
            {
                float winch = LiftTime(lift), fall = FallTime(drop);
                if (phaseTime < winch)
                    return restAngle + sign * lift * Winch(phaseTime / winch);
                if (phaseTime < winch + fall)
                    return top + sign * drop * Fall((phaseTime - winch) / fall);
                // Landed on the ground: rebound back against the opening direction.
                return restAngle + delta - sign * landBounceAngle * Settle(true, winch + fall);
            }

            case Phase.Closing:
            {
                float winch = LiftTime(drop), fall = FallTime(lift);
                if (phaseTime < winch)
                    return restAngle + delta - sign * drop * Winch(phaseTime / winch);
                if (phaseTime < winch + fall)
                    return top - sign * lift * Fall((phaseTime - winch) / fall);
                // Dropped back to hanging: overshoot in the closing direction, then sway.
                return restAngle - sign * hangSwayAngle * Settle(false, winch + fall);
            }

            case Phase.OpenPause:
                return restAngle + delta;

            default:
                return restAngle;
        }
    }

    // Winch progress 0..1: eases out of the pose and slows to a stop at the top,
    // where the leaf teeters before it falls.
    private static float Winch(float u)
    {
        u = Mathf.Clamp01(u);
        return u * u * (3f - 2f * u);
    }

    // Fall progress 0..1: starts at rest and keeps accelerating (constant
    // acceleration), so the leaf arrives at full speed.
    private static float Fall(float u)
    {
        u = Mathf.Clamp01(u);
        return u * u;
    }

    // Damped settle in units of the first peak, 0 at both ends.
    // bounce = true: rebounds that stay on one side (hitting the ground).
    // bounce = false: a sway to both sides (hanging pendulum).
    private float Settle(bool bounce, float swingTime)
    {
        if (settleDuration <= 0f) return 0f;
        float tau = Mathf.Clamp01((phaseTime - swingTime) / settleDuration);
        float envelope = (1f - tau) * (1f - tau);
        float wave = Mathf.Sin(Mathf.PI * settleBounces * tau);
        return envelope * (bounce ? Mathf.Abs(wave) : wave);
    }

    // Solid only while it spans the pit; elsewhere the leaf is scenery.
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
