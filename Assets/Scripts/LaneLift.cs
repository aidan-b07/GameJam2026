using UnityEngine;

// Lift platform hung on two chains (Level 2): waits at the bottom, rises to a
// ledge, waits, sinks back. All timing runs on lane time, so a lift takes
// about 8.5 s round at normal speed and well under 2 s while the partner runs.
// One-way (PlatformEffector2D): players jump up through it and are never
// crushed under it. Art from tools/make_gate_lift.py; LevelMap.Lane.Lift
// builds it.
[RequireComponent(typeof(Rigidbody2D))]
public class LaneLift : MonoBehaviour
{
    [Tooltip("Lane whose world speed drives the lift (0 = Tick, 1 = Tock).")]
    [SerializeField] private int lane;
    [Tooltip("Height the deck rises from its bottom position (units).")]
    [SerializeField, Min(0f)] private float travel = 5f;
    [SerializeField, Min(0f)] private float bottomPause = 2.5f;
    [SerializeField, Min(0.05f)] private float riseTime = 2.5f;
    [SerializeField, Min(0f)] private float topPause = 1.5f;
    [SerializeField, Min(0.05f)] private float sinkTime = 2f;
    [Tooltip("Lane seconds to shift this lift's cycle by.")]
    [SerializeField, Min(0f)] private float cycleOffset;

    [Header("Chains")]
    [Tooltip("Tiled chain sprites (children of the deck) stretched up to chainTop.")]
    [SerializeField] private SpriteRenderer[] chains;
    [Tooltip("World y where the chains hang from.")]
    [SerializeField] private float chainTop;

    private Rigidbody2D rb;
    private Vector2 bottom;
    private float laneTime;

    private float Cycle => bottomPause + riseTime + topPause + sinkTime;

    private void Awake()
    {
        rb = GetComponent<Rigidbody2D>();
        bottom = rb.position;
    }

    private void FixedUpdate()
    {
        float speed = GameManager.INSTANCE != null ? GameManager.INSTANCE.GetWorldSpeed(lane) : 1f;
        laneTime += Time.fixedDeltaTime * speed;
        rb.MovePosition(bottom + Vector2.up * (Height(laneTime + cycleOffset) * travel));
    }

    private void LateUpdate()
    {
        foreach (var chain in chains)
            if (chain != null)
                chain.size = new Vector2(chain.size.x, Mathf.Max(0f, chainTop - chain.transform.position.y));
    }

    // Deck height 0..1 at lane time t: pause, eased rise, pause, eased sink.
    private float Height(float t)
    {
        t = Mathf.Repeat(t, Cycle);
        if (t < bottomPause) return 0f;
        t -= bottomPause;
        if (t < riseTime) return Mathf.SmoothStep(0f, 1f, t / riseTime);
        t -= riseTime;
        if (t < topPause) return 1f;
        t -= topPause;
        return Mathf.SmoothStep(1f, 0f, t / sinkTime);
    }
}
