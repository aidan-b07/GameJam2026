using System;
using UnityEngine;

// Latching floor button (main level). A player standing on the plate presses
// it; an iron pawl then holds it down, so it stays pressed when they step off.
// A death resets every button (GameFlow.LevelReset), so the pair has to press
// them again after a respawn. ButtonGate opens once all of its buttons are
// down. The button is a mechanism, not part of the lane's clockwork: it plays
// in real time and works while the lane is frozen.
// Art from tools/make_button.py (14 frames: idle 0-3, press 4-9, pressed
// 10-13); LevelMap.Lane.Button builds it over normal ground tiles, which keep
// the collider.
public class FloorButton : MonoBehaviour
{
    [Tooltip("World x of the plate's left edge and its width in units.")]
    public float left, width;
    [Tooltip("World y of the walking surface over the plate (the ground collider's top).")]
    public float top;
    [Tooltip("How far above or below the surface a player's feet still count as on the plate.")]
    public float footTolerance = 0.15f;
    [Tooltip("Animation frames per second.")]
    public float fps = 12f;

    [Header("Art")]
    public SpriteRenderer body;
    [Tooltip("Untinted lamp overlay (amber up, green pressed).")]
    public SpriteRenderer lamp;
    public Sprite[] bodyFrames, lampFrames;

    const int IdleFirst = 0, IdleCount = 4, PressFirst = 4, PressCount = 6, PressedFirst = 10, PressedCount = 4;

    public bool Pressed { get; private set; }
    public event Action Changed;

    private PlayerMovement[] players;
    private float pressedAt;
    private int shownFrame = -1;

    private void Start() => players = FindObjectsByType<PlayerMovement>();

    private void OnEnable() => GameFlow.LevelReset += Release;
    private void OnDisable() => GameFlow.LevelReset -= Release;

    private void FixedUpdate()
    {
        if (Pressed || players == null) return;
        foreach (var p in players)
        {
            if (p == null || !p.enabled) continue;   // frozen (death, respawn)
            var col = p.GetComponent<Collider2D>();
            if (col == null) continue;
            var b = col.bounds;
            // Any part of the feet over the plate, feet on the surface.
            if (b.max.x > left && b.min.x < left + width && Mathf.Abs(b.min.y - top) <= footTolerance)
            {
                Press();
                return;
            }
        }
    }

    [ContextMenu("Press")]
    public void Press()
    {
        if (Pressed) return;
        Pressed = true;
        pressedAt = Time.time;
        Sfx.Play("ratchet_click", transform.position, 1f, pitched: false);
        Sfx.Play("ui_confirm", transform.position, 0.6f, pitched: false);
        Changed?.Invoke();
    }

    [ContextMenu("Release")]
    public void Release()
    {
        if (!Pressed) return;
        Pressed = false;
        Changed?.Invoke();
    }

    private void Update()
    {
        int frame;
        if (!Pressed)
            frame = IdleFirst + (int)(Time.time * fps / 3f) % IdleCount;   // slow glint
        else
        {
            int f = (int)((Time.time - pressedAt) * fps);
            frame = f < PressCount ? PressFirst + f : PressedFirst + (f - PressCount) / 3 % PressedCount;
        }
        if (frame == shownFrame) return;
        shownFrame = frame;
        if (body != null && bodyFrames != null && frame < bodyFrames.Length) body.sprite = bodyFrames[frame];
        if (lamp != null && lampFrames != null && frame < lampFrames.Length) lamp.sprite = lampFrames[frame];
    }
}
