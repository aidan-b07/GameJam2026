using UnityEngine;

// Trap door over a 4-tile pit: two short drawbridge leaves hinged on opposite
// lips, facing each other. Closed, they lie level with the ground and players
// walk across; then the lamps blink amber, the leaves judder, the bolt pulls
// back and both drop and slap flat against the pit walls; a ratchet winch
// hauls them back up to level and the bolt latches.
//
// Everything runs on lane time, so the door slows down with its lane (a held
// partner freezes it) and speeds up while the partner runs. The art is one
// sheet of 192x128 frames from tools/make_trapdoor.py (20 fps, tags closed 0,
// warn 1-14, drop 15-37, open 38, winch 39-68, latch 69-74) plus an untinted
// lamp overlay with the same frames. The closed and open frames are held for
// closedTime and openTime. Each leaf is a kinematic body on its hinge pin,
// turned to the angle the frame shows. LevelMap.Lane.TrapDoor builds it.
public class TrapDoor : MonoBehaviour
{
    [Tooltip("Lane whose world speed drives the door (0 = Tick, 1 = Tock).")]
    [SerializeField] private int lane;
    [Tooltip("Lane seconds the door stays closed between drops.")]
    [SerializeField, Min(0f)] private float closedTime = 3f;
    [Tooltip("Lane seconds the leaves hang open before the winch starts.")]
    [SerializeField, Min(0f)] private float openTime = 1.5f;
    [Tooltip("Lane seconds to shift this door's cycle by.")]
    [SerializeField, Min(0f)] private float cycleOffset;

    [SerializeField] private SpriteRenderer body;
    [SerializeField] private SpriteRenderer lamp;
    [SerializeField] private Sprite[] bodyFrames;
    [SerializeField] private Sprite[] lampFrames;
    [Tooltip("Leaf bodies, origin on the hinge pin; the left one turns clockwise to drop.")]
    [SerializeField] private Rigidbody2D leftLeaf;
    [SerializeField] private Rigidbody2D rightLeaf;

    const float Fps = 20f;
    const int OpenFrame = 38;
    const int LastFrame = 74;

    // Leaf angle below level in each frame (degrees), as tools/make_trapdoor.py
    // draws it: gravity drop with one bounce off the wall, then 6 winch clicks.
    static readonly float[] Angles =
    {
        0.0f, 0.0f, 1.4f, 0.0f, 0.0f, 0.0f, 1.4f, 0.0f, 0.0f, 0.0f, 1.4f, 0.0f, 1.4f, 0.0f, 1.4f,
        1.9f, 7.1f, 15.8f, 27.8f, 42.8f, 60.2f, 79.4f, 87.9f, 83.1f, 78.7f, 75.0f, 72.1f, 70.3f, 69.6f, 70.2f,
        71.9f, 74.6f, 78.3f, 82.6f, 87.4f, 90.0f, 90.0f, 90.0f, 90.0f,
        81.7f, 76.7f, 75.0f, 76.5f, 75.0f, 66.7f, 61.7f, 60.0f, 61.5f, 60.0f, 51.7f, 46.7f, 45.0f, 46.5f, 45.0f,
        36.7f, 31.7f, 30.0f, 31.5f, 30.0f, 21.7f, 16.7f, 15.0f, 16.5f, 15.0f, 6.7f, 1.7f, 0.0f, 1.5f, 0.0f,
        0.0f, 0.0f, 0.0f, 0.0f, 0.0f, 0.0f,
    };

    private float laneTime;
    private int frame = -1;

    private float DropTime => (OpenFrame - 1) / Fps;
    private float WinchTime => (LastFrame - OpenFrame) / Fps;
    private float Cycle => closedTime + DropTime + openTime + WinchTime;

    private void FixedUpdate()
    {
        float speed = GameManager.INSTANCE != null ? GameManager.INSTANCE.GetWorldSpeed(lane) : 1f;
        laneTime += Time.fixedDeltaTime * speed;
        int f = FrameAt(laneTime + cycleOffset);
        if (f != frame) Enter(f);
        float a = Angles[f];
        if (leftLeaf != null) leftLeaf.MoveRotation(-a);
        if (rightLeaf != null) rightLeaf.MoveRotation(a);
    }

    private void Update()
    {
        if (frame < 0) return;
        if (body != null && frame < bodyFrames.Length) body.sprite = bodyFrames[frame];
        if (lamp != null && frame < lampFrames.Length) lamp.sprite = lampFrames[frame];
    }

    // Sheet frame at lane time t: closed held, drop played, open held, winch and latch played.
    private int FrameAt(float t)
    {
        t = Mathf.Repeat(t, Cycle);
        if (t < closedTime) return 0;
        t -= closedTime;
        if (t < DropTime) return 1 + (int)(t * Fps);
        t -= DropTime;
        if (t < openTime) return OpenFrame;
        t -= openTime;
        return Mathf.Min(LastFrame, OpenFrame + 1 + (int)(t * Fps));
    }

    // Sounds keyed to the frames the sheet shows them in, played once per frame change.
    private void Enter(int f)
    {
        bool first = frame < 0;
        frame = f;
        if (first) return;
        Vector3 at = transform.position + new Vector3(3f, 3f, 0f);
        if (f == 1 || f == 7 || f == 13) Sfx.Play("plunger_warn", at, 0.5f, lane: lane);   // amber blinks
        else if (f == 22) Sfx.Play("bridge_slam", at, 0.8f, lane: lane);                    // leaves hit the walls
        else if (f > OpenFrame && f < 69 && (f - OpenFrame - 1) % 5 == 3) Sfx.Play("ratchet_click", at, 0.6f, lane: lane);
        else if (f == 71) Sfx.Play("ratchet_click", at, 1f, lane: lane);                    // bolt latches
    }
}
