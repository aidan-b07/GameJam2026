using UnityEngine;

// Flush conveyor belt (Level 2): a row of belt sprites over the ground's top
// row that runs on lane time and carries players standing on it, so they
// can't simply wait on it. Art from tools/make_conveyor.py (conveyor_flush_*:
// 8 frames, the belt moves 1 px per frame, so the frame is the belt's travel
// in pixels mod 8). LevelMap.Lane.Conveyor builds it over normal ground tiles,
// which keep the collider.
// Riders are moved by position, not velocity: friction would eat part of an
// added velocity, and the 1-2 px steps between ground tiles stop a player who
// is not pressing a direction (PlayerMovement only steps up on input). Runs
// after PlayerMovement and before ScreenLeash (100).
[DefaultExecutionOrder(50)]
public class Conveyor : MonoBehaviour
{
    [Tooltip("Lane whose world speed drives the belt (0 = Tick, 1 = Tock).")]
    public int lane;
    [Tooltip("Belt speed in units per second while the lane runs at its idle (slow) speed; negative runs left.")]
    public float speed = -2f;
    [Tooltip("Belt speed (magnitude) while the lane runs at full speed; in between it follows the lane speed linearly.")]
    public float fastSpeed = 4f;
    [Tooltip("World x of the belt's left end and its width in units.")]
    public float left, width;
    [Tooltip("World y of the belt's walking surface (the ground collider's top).")]
    public float top;
    [Tooltip("How far above or below the surface a player's feet still count as on the belt.")]
    public float footTolerance = 0.12f;

    [Header("Art")]
    [Tooltip("One renderer per tile, left to right.")]
    public SpriteRenderer[] cells;
    public Sprite[] leftFrames, midFrames, rightFrames;

    const float PixelsPerUnit = 32f;

    private PlayerMovement[] players;
    private float travel;   // belt travel in units, signed
    private int shownFrame = -1;

    private void Start()
    {
        players = FindObjectsByType<PlayerMovement>();
    }

    // Belt velocity for the lane's speed: `speed` at the idle lane speed,
    // `fastSpeed` at full speed, linear between, so a slow lane's belt still
    // carries players briskly. Below idle (a scripted stop) it scales down
    // to 0 with the lane.
    private float Velocity()
    {
        var gm = GameManager.INSTANCE;
        if (gm == null) return speed;
        float lane = gm.GetWorldSpeed(this.lane), idle = gm.IdleWorldSpeed, max = gm.MaxWorldSpeed;
        if (lane <= idle) return idle > 0f ? speed * lane / idle : speed;
        float t = max > idle ? (lane - idle) / (max - idle) : 1f;
        return Mathf.Sign(speed) * Mathf.LerpUnclamped(Mathf.Abs(speed), fastSpeed, t);
    }

    private void FixedUpdate()
    {
        float v = Velocity();
        travel += v * Time.fixedDeltaTime;
        foreach (var p in players)
        {
            if (p == null || !p.enabled) continue;   // frozen (death, respawn)
            var rb = p.GetComponent<Rigidbody2D>();
            var col = p.GetComponent<Collider2D>();
            if (rb == null || col == null || !rb.simulated) continue;
            if (OnBelt(col.bounds, rb.linearVelocity.y))
                rb.position += new Vector2(v * Time.fixedDeltaTime, 0f);
        }
    }

    // Feet on the surface and any part of the body over the belt, so a rider
    // is carried all the way off the end (into a hole there) instead of
    // stopping half over the edge once the body's centre leaves the belt.
    private bool OnBelt(Bounds b, float vy) =>
        vy <= 0.5f && b.max.x > left && b.min.x < left + width
        && Mathf.Abs(b.min.y - top) <= footTolerance;

    private void Update()
    {
        int frame = (int)Mathf.Repeat(Mathf.Floor(travel * PixelsPerUnit), 8f);
        if (frame == shownFrame || cells == null) return;
        shownFrame = frame;
        for (int i = 0; i < cells.Length; i++)
        {
            var frames = i == 0 ? leftFrames : i == cells.Length - 1 ? rightFrames : midFrames;
            if (cells[i] != null && frames != null && frame < frames.Length) cells[i].sprite = frames[frame];
        }
    }
}
