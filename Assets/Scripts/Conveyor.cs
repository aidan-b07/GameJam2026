using UnityEngine;

// Flush conveyor belt (Level 2): a row of belt sprites over the ground's top
// row that runs on lane time and carries players standing on it, so they
// can't simply wait on it. Art from tools/make_conveyor.py (conveyor_flush_*:
// 8 frames, the belt moves 1 px per frame, so the frame is the belt's travel
// in pixels mod 8). LevelMap.Lane.Conveyor builds it over normal ground tiles,
// which keep the collider.
// Runs after PlayerMovement, which sets the player's horizontal velocity from
// input every step, and before ScreenLeash (100): the belt adds its own speed.
[DefaultExecutionOrder(50)]
public class Conveyor : MonoBehaviour
{
    [Tooltip("Lane whose world speed drives the belt (0 = Tick, 1 = Tock).")]
    public int lane;
    [Tooltip("Belt speed at 1x in units per lane second; negative runs left.")]
    public float speed = -0.75f;
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
        players = FindObjectsByType<PlayerMovement>(FindObjectsSortMode.None);
    }

    private float LaneSpeed => GameManager.INSTANCE != null ? GameManager.INSTANCE.GetWorldSpeed(lane) : 1f;

    private void FixedUpdate()
    {
        float v = speed * LaneSpeed;
        travel += v * Time.fixedDeltaTime;
        foreach (var p in players)
        {
            if (p == null || !p.enabled) continue;   // frozen (death, respawn)
            var rb = p.GetComponent<Rigidbody2D>();
            var col = p.GetComponent<Collider2D>();
            if (rb == null || col == null || !rb.simulated) continue;
            if (OnBelt(col.bounds, rb.linearVelocity.y))
                rb.linearVelocity += new Vector2(v, 0f);
        }
    }

    // Feet on the surface and the body's centre over the belt.
    private bool OnBelt(Bounds b, float vy) =>
        vy <= 0.5f && b.center.x >= left && b.center.x <= left + width
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
