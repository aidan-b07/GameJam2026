using UnityEngine;

// Clock pendulum over a belt (main level, pendulum belts section): a long rod
// hangs from an iron beam and swings a blunt brass bob, a grandfather clock's
// pendulum, on lane time: amplitude * sin(2 pi T / period). It does not kill.
// The bob is solid and shoves a player it moves into, as hard as it moves:
// the player is thrown at knockScale times its velocity (capped at maxKnock)
// plus a lift, and loses control for knockTime (PlayerMovement.Knock). In the
// middle of its swing it is fast and throws them off the belt into a drop; at
// the outer part of its swing (nudgeFrom) a hit is only a nudge, at any lane
// speed, so a player near the belt's end is never thrown off it; at the very
// ends it lifts over head height, so the last tile or so of the belt is never
// touched. Moving away from a player it only blocks. Art from tools/make_pendulum.py (pendulum_clockbob_96px: frames by
// speed, mirrored when it swings left); LevelMap.Lane.ClockPendulum builds it.
// Runs after PlayerMovement (which sets the velocity each step) and the belts.
[DefaultExecutionOrder(60)]
public class ClockPendulum : LaneObstacle
{
    [Tooltip("Swing either side of straight down (degrees).")]
    public float amplitude = 36f;
    [Tooltip("Lane seconds for a full swing there and back.")]
    public float period = 3f;
    [Tooltip("Axis to the bob's centre (units).")]
    public float reach = 6.5f;
    [Tooltip("Radius of the bob's disc (units).")]
    public float bobRadius = 29.5f / 32f;

    [Header("Shove")]
    [Tooltip("The thrown player's horizontal speed, as a share of the bob's.")]
    public float knockScale = 1.15f;
    [Tooltip("Cap on the thrown player's horizontal speed (units/s).")]
    public float maxKnock = 16f;
    [Tooltip("Upward speed of a throw: liftBase plus liftPerSpeed times the bob's speed, at most maxLift.")]
    public float liftBase = 3f, liftPerSpeed = 0.2f, maxLift = 8f;
    [Tooltip("Seconds without control after a throw (it also ends on landing).")]
    public float knockTime = 0.35f;
    [Tooltip("A bob moving into a player slower than this (units/s) only pushes.")]
    public float minKnockSpeed = 1f;
    [Tooltip("Outer part of the swing, as a share of the amplitude, where a hit is only a nudge whatever the lane speed.")]
    [Range(0f, 1f)] public float nudgeFrom = 0.6f;
    [Tooltip("Horizontal speed (units/s) of a nudge; with knockTime it moves a player under a tile, so it never throws them off the belt.")]
    public float nudgeSpeed = 2.5f;
    [Tooltip("Upward speed of a nudge (units/s).")]
    public float nudgeLift = 2f;

    [Header("Art")]
    [Tooltip("Turns with the swing; the rod, hub and bob are its children.")]
    public Transform arm;
    public SpriteRenderer bob;
    [Tooltip("Bob frames: no smear, light, heavy (drawn moving right).")]
    public Sprite[] bobFrames;
    [Tooltip("Bob speeds (units per second) from which the light and heavy smear show.")]
    public float lightSmear = 9f, heavySmear = 20f;

    float lastAngle;

    // Angle (degrees, counterclockwise, 0 = straight down; positive swings
    // the bob right) at lane time t.
    float Angle(float t) => amplitude * Mathf.Sin(2f * Mathf.PI * t / period);

    // Angular speed in degrees per real second.
    float Omega(float t) => amplitude * 2f * Mathf.PI / period * Mathf.Cos(2f * Mathf.PI * t / period) * LaneSpeed;

    protected override void Step(float dt)
    {
        float a = Angle(T), omega = Omega(T) * Mathf.Deg2Rad;
        float ar = a * Mathf.Deg2Rad;
        Vector2 centre = (Vector2)transform.position + new Vector2(Mathf.Sin(ar), -Mathf.Cos(ar)) * reach;
        Vector2 velocity = new Vector2(Mathf.Cos(ar), Mathf.Sin(ar)) * (omega * reach);

        // Whoosh at each fast pass through the bottom of the swing.
        if (Mathf.Sign(a) != Mathf.Sign(lastAngle) && Mathf.Abs(velocity.x) > 2f)
            Sfx.Play("pendulum_whoosh", centre, 0.6f, lane: lane);
        lastAngle = a;

        foreach (var p in Players)
        {
            if (!Live(p, out var rb, out var box)) continue;
            var q = new Vector2(Mathf.Clamp(centre.x, box.min.x, box.max.x), Mathf.Clamp(centre.y, box.min.y, box.max.y));
            Vector2 d = q - centre;
            float dist = d.magnitude;
            if (dist >= bobRadius) continue;
            Vector2 n = dist > 1e-4f ? d / dist : new Vector2(velocity.x >= 0f ? 1f : -1f, 0f);

            // Solid: push the body out of the disc. Landing on top stops the fall.
            rb.position += n * (bobRadius - dist);
            if (n.y > 0.6f && rb.linearVelocity.y < 0f)
                rb.linearVelocity = new Vector2(rb.linearVelocity.x, 0f);

            // Moving into the player: throw them.
            float into = Vector2.Dot(velocity, n);
            if (into > minKnockSpeed && !p.Knocked)
            {
                float speed = velocity.magnitude;
                bool nudge = Mathf.Abs(a) >= nudgeFrom * amplitude;
                var knock = nudge
                    ? new Vector2(Mathf.Clamp(velocity.x * knockScale, -nudgeSpeed, nudgeSpeed), nudgeLift)
                    : new Vector2(Mathf.Clamp(velocity.x * knockScale, -maxKnock, maxKnock),
                                  Mathf.Min(maxLift, liftBase + liftPerSpeed * speed));
                if (nudge) speed = Mathf.Min(speed, nudgeSpeed);
                p.Knock(knock, knockTime);
                Sfx.Play("gear_clank", q, Mathf.Clamp01(0.35f + speed / 20f), lane: lane);
            }
        }
    }

    void LateUpdate()
    {
        float t = DrawT;
        float a = Angle(t);
        if (arm != null) arm.localRotation = Quaternion.Euler(0f, 0f, a);
        if (bob == null || bobFrames == null || bobFrames.Length < 3) return;
        float omega = Omega(t);
        float speed = Mathf.Abs(omega) * Mathf.Deg2Rad * reach;
        bob.sprite = bobFrames[speed > heavySmear ? 2 : speed > lightSmear ? 1 : 0];
        bob.flipX = omega < 0f;   // the smear trails behind: drawn for a bob moving right
    }
}
