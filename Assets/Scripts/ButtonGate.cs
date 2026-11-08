using UnityEngine;

// Portcullis opened by floor buttons (main level). It winches up once every
// one of its buttons is pressed, in any order and in either lane, and slams
// shut again when a death resets the buttons. Like the buttons it runs in
// real time, so it opens while its lane is frozen. The lamp shows the state:
// red while shut, amber blinking while it winches, green when open. Same art
// as TimeGate (tools/make_gate_lift.py); LevelMap.Lane.ButtonGate builds it
// and the level builder wires the buttons.
[RequireComponent(typeof(Rigidbody2D))]
public class ButtonGate : MonoBehaviour
{
    [Tooltip("Buttons that must all be pressed to open the gate.")]
    public FloorButton[] buttons;
    [Tooltip("How far the gate rises when fully open (units).")]
    [Min(0f)] public float travel = 3.5f;
    [Tooltip("Seconds from shut to fully open.")]
    [Min(0.05f)] public float openTime = 1.6f;
    [Tooltip("Seconds between winch clicks while it opens.")]
    [Min(0.02f)] public float clickInterval = 0.16f;

    [Header("Lamp")]
    public SpriteRenderer lamp;
    public Color lampShut = new Color(0.95f, 0.25f, 0.2f);
    public Color lampOpen = new Color(0.4f, 1f, 0.45f);
    public Color lampOpening = new Color(1f, 0.7f, 0.15f);
    [Min(1f)] public float blinkRate = 6f;

    // 0 = shut, 1 = fully open.
    public float Open { get; private set; }

    private Rigidbody2D rb;
    private Vector2 shut;
    private float nextClick;

    private bool AllPressed
    {
        get
        {
            if (buttons == null || buttons.Length == 0) return false;
            foreach (var b in buttons)
                if (b == null || !b.Pressed) return false;
            return true;
        }
    }

    private void Awake()
    {
        rb = GetComponent<Rigidbody2D>();
        shut = rb.position;
    }

    private void OnEnable() => GameFlow.LevelReset += Shut;
    private void OnDisable() => GameFlow.LevelReset -= Shut;

    // Back down at once: the players are away at their checkpoints by now.
    private void Shut()
    {
        Open = 0f;
        rb.position = shut;
        transform.position = shut;
    }

    private void FixedUpdate()
    {
        if (AllPressed && Open < 1f)
        {
            Open = Mathf.Min(1f, Open + Time.fixedDeltaTime / openTime);
            if (Time.time >= nextClick)
            {
                nextClick = Time.time + clickInterval;
                Sfx.Play("ratchet_click", transform.position, 0.7f, pitched: false);
            }
        }
        rb.MovePosition(shut + Vector2.up * (Open * travel));
    }

    private void Update()
    {
        if (lamp == null) return;
        if (Open >= 1f) lamp.color = lampOpen;
        else if (AllPressed)
            lamp.color = Mathf.FloorToInt(Time.time * blinkRate) % 2 == 0 ? lampOpening : lampOpening * 0.45f;
        else
            lamp.color = lampShut;
    }
}
