using UnityEngine;

// Pendulum blade: a clock pendulum hanging from a ceiling bracket swings a
// brass bob with a steel crescent blade across the path. Its angle is a pure
// function of lane time, amplitude * sin(2 pi T / period), so it crawls while
// the partner stands still (0.35x: one swing about every 7 s) and whips past
// while they run (3x). Touching the blade (or the bob) kills both players.
// The bob shows a motion smear by its speed (frames 0-2) and is mirrored when
// it swings left. Art from tools/make_pendulum.py; LevelMap.Lane.Pendulum
// builds it (the object sits on the swing axis).
public class Pendulum : LaneObstacle
{
    [Tooltip("Swing either side of straight down (degrees).")]
    public float amplitude = 44f;
    [Tooltip("Lane seconds for a full swing there and back.")]
    public float period = 2.4f;
    [Tooltip("Axis to the bob's socket (units): the rod's length.")]
    public float reach = 6f;

    [Header("Art")]
    [Tooltip("Turns with the swing; the rod, hub and bob are its children.")]
    public Transform arm;
    public SpriteRenderer bob;
    [Tooltip("Bob frames: no smear, light, heavy (drawn moving right).")]
    public Sprite[] bobFrames;
    [Tooltip("Bob speeds (units per second) from which the light and heavy smear show.")]
    public float lightSmear = 9f, heavySmear = 20f;

    const float Px = 1f / 32f;
    // Blade: inside the circle of radius 34 px centred 20 px below the bob's
    // socket, outside the one centred 5 px below (make_pendulum.py draw_bob).
    // The kill shape is a row of circles along the middle of that crescent
    // plus the bob's disc (centre 23 px below the socket, radius 15.5 px).
    static readonly Vector2[] Blade = BladePoints();
    const float BladeRadius = 5f * Px;
    static readonly Vector2 BobCentre = new Vector2(0f, -23f * Px);
    const float BobRadius = 14f * Px;

    float lastAngle;

    static Vector2[] BladePoints()
    {
        var list = new System.Collections.Generic.List<Vector2>();
        Vector2 c1 = new Vector2(0f, -20f * Px), c2 = new Vector2(0f, -5f * Px);
        for (int deg = -84; deg <= 84; deg += 7)
        {
            float a = deg * Mathf.Deg2Rad;
            var p = c1 + new Vector2(Mathf.Sin(a), -Mathf.Cos(a)) * (34f - 5f) * Px;
            if ((p - c2).magnitude > 36f * Px) list.Add(p);
        }
        return list.ToArray();
    }

    // Angle (degrees, counterclockwise, 0 = straight down) at lane time t.
    float Angle(float t) => amplitude * Mathf.Sin(2f * Mathf.PI * t / period);

    // Angular speed in degrees per real second.
    float Omega(float t) => amplitude * 2f * Mathf.PI / period * Mathf.Cos(2f * Mathf.PI * t / period) * LaneSpeed;

    protected override void Step(float dt)
    {
        float a = Angle(T);
        float omega = Omega(T);
        // Whoosh at each pass through the bottom of the swing.
        if (Mathf.Sign(a) != Mathf.Sign(lastAngle) && Mathf.Abs(omega) * Mathf.Deg2Rad * reach > 2f)
            Sfx.Play("pendulum_whoosh", BobWorld(a), 0.7f, lane: lane);
        lastAngle = a;

        // Kill check against the blade where it is this step.
        var rot = Quaternion.Euler(0f, 0f, a);
        Vector2 socket = (Vector2)transform.position + (Vector2)(rot * new Vector3(0f, -reach, 0f));
        foreach (var p in Players)
        {
            if (!Live(p, out _, out var box)) continue;
            if (CircleHitsBox(socket + (Vector2)(rot * BobCentre), BobRadius, box)) { KillAll(); return; }
            foreach (var b in Blade)
                if (CircleHitsBox(socket + (Vector2)(rot * b), BladeRadius, box)) { KillAll(); return; }
        }
    }

    Vector3 BobWorld(float a) => transform.position + Quaternion.Euler(0f, 0f, a) * new Vector3(0f, -reach, 0f);

    void LateUpdate()
    {
        float t = DrawT;
        float a = Angle(t);
        if (arm != null) arm.localRotation = Quaternion.Euler(0f, 0f, a);
        if (bob == null || bobFrames == null || bobFrames.Length < 3) return;
        float omega = Omega(t);
        float speed = Mathf.Abs(omega) * Mathf.Deg2Rad * (reach + 40f * Px);   // at the bob's centre
        bob.sprite = bobFrames[speed > heavySmear ? 2 : speed > lightSmear ? 1 : 0];
        bob.flipX = omega < 0f;   // the smear trails behind: drawn for a bob moving right
    }
}
