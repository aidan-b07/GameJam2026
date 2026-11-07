using UnityEngine;

// Portcullis winched up by lane speed (Level 2). While the partner runs, their
// running speeds this lane up and the winch lifts the gate; at normal speed it
// sinks back under its own weight. A gate that sinks onto a player crushes
// them (CrushDetector), so the partner has to keep moving until they are
// through. With a sideways direction it is a sliding bridge instead: it runs
// out over a pit while the lane is fast and pulls back into the wall at normal
// speed, dropping whoever is still on it. The lamp shows the winch: green
// while it opens or stands open, amber blinking while it closes, red when shut.
// Art from tools/make_gate_lift.py; LevelMap.Lane.TimeGate builds it.
[RequireComponent(typeof(Rigidbody2D))]
public class TimeGate : MonoBehaviour
{
    [Tooltip("Lane whose world speed drives the winch (0 = Tick, 1 = Tock).")]
    [SerializeField] private int lane;
    [Tooltip("How far the gate moves when fully open (units).")]
    [SerializeField, Min(0f)] private float travel = 3.5f;
    [Tooltip("Direction it opens in: up for a gate, right for a bridge.")]
    [SerializeField] private Vector2 direction = Vector2.up;
    [Tooltip("Openness gained per real second at full lane speed.")]
    [SerializeField, Min(0f)] private float winchRate = 1.6f;
    [Tooltip("Openness lost per real second (the gate's weight).")]
    [SerializeField, Min(0f)] private float sinkRate = 0.5f;
    [Tooltip("Lane speed at which the winch pulls at full rate.")]
    [SerializeField, Min(1.01f)] private float fullSpeed = 6f;

    [Header("Lamp")]
    [SerializeField] private SpriteRenderer lamp;
    [SerializeField] private Color lampShut = new Color(0.95f, 0.25f, 0.2f);
    [SerializeField] private Color lampOpen = new Color(0.4f, 1f, 0.45f);
    [SerializeField] private Color lampSinking = new Color(1f, 0.7f, 0.15f);
    [SerializeField, Min(1f)] private float blinkRate = 6f;

    // 0 = shut (bridge pulled in), 1 = fully open (bridge across).
    public float Open { get; private set; }

    private Rigidbody2D rb;
    private Vector2 shut;
    private float change;

    private void Awake()
    {
        rb = GetComponent<Rigidbody2D>();
        shut = rb.position;
    }

    private void FixedUpdate()
    {
        float speed = GameManager.INSTANCE != null ? GameManager.INSTANCE.GetWorldSpeed(lane) : 1f;
        float pull = winchRate * Mathf.Clamp01((speed - 1f) / (fullSpeed - 1f));
        float before = Open;
        Open = Mathf.Clamp01(Open + (pull - sinkRate) * Time.fixedDeltaTime);
        change = Open - before;
        rb.MovePosition(shut + direction.normalized * (Open * travel));
    }

    private void Update()
    {
        if (lamp == null) return;
        if (change < 0f)
            lamp.color = Mathf.FloorToInt(Time.time * blinkRate) % 2 == 0 ? lampSinking : lampSinking * 0.45f;
        else
            lamp.color = Open > 0f ? lampOpen : lampShut;
    }
}
