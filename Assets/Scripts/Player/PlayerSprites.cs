using System;
using UnityEngine;

// Plays the player's sprite frames (idle, run, jump and death) on the Visual
// child and keeps the wind-up key on the body's back, turning. The state comes
// from the Rigidbody2D and PlayerMovement each frame. Frames and key attach
// pixels are from tools/make_player.py and make_death.py
// (Assets/Art/Characters, KeyAttach.md).
//
// Lane time isn't wired up yet, so the key turns at a fixed default rate
// (keyFps x KeyRate, KeyRate = 1). Later the lane's clock should set KeyRate.
public class PlayerSprites : MonoBehaviour
{
    public enum Character { Tick, Tock }

    [Serializable]
    public struct Frame
    {
        public Sprite sprite;
        [Tooltip("Pixel in the 32x32 cell (x from the left, y from the top) where the key's pivot goes.")]
        public Vector2Int keyPixel;
    }

    [Serializable]
    public class Look
    {
        public Frame[] idle;
        public Frame[] run;
        public Frame jump;
        [Tooltip("Key is baked into these frames, so the separate key is hidden.")]
        public Sprite[] death;
    }

    [SerializeField] private Character character;
    [SerializeField] private Look tick;
    [SerializeField] private Look tock;
    [SerializeField] private Sprite[] keyFrames;

    [Header("Renderers")]
    [SerializeField] private SpriteRenderer body;
    [SerializeField] private SpriteRenderer key;

    [Header("Timing (frames per second)")]
    [SerializeField] private float idleFps = 4f;
    [SerializeField] private float runFps = 12f;
    [SerializeField] private float deathFps = 12f;
    [Tooltip("Default key turning speed until lane time drives KeyRate.")]
    [SerializeField] private float keyFps = 10f;
    [Tooltip("Horizontal speed above which the run cycle plays.")]
    [SerializeField] private float runThreshold = 0.1f;

    // Multiplier on keyFps; 1 until the lane clock sets it (0 stops the key).
    public float KeyRate { get; set; } = 1f;
    public bool IsDead { get; private set; }

    private const int Cell = 32;
    private const float Ppu = 32f;

    private Rigidbody2D rb;
    private PlayerMovement movement;
    private Frame[] current;
    private float stateTime;
    private float keyTime;

    private Look Active => character == Character.Tick ? tick : tock;

    private void Awake()
    {
        rb = GetComponentInParent<Rigidbody2D>();
        movement = GetComponentInParent<PlayerMovement>();
    }

    private void Update()
    {
        keyTime += Time.deltaTime * keyFps * KeyRate;
        if (keyFrames.Length > 0)
            key.sprite = keyFrames[Mod((int)keyTime, keyFrames.Length)];

        stateTime += Time.deltaTime;

        if (IsDead)
        {
            Sprite[] death = Active.death;
            body.sprite = death[Mathf.Min((int)(stateTime * deathFps), death.Length - 1)];
            key.enabled = false;
            return;
        }

        Frame[] next;
        float fps;
        bool grounded = movement == null || movement.IsGrounded();
        if (!grounded)
        {
            next = null;
            fps = 0f;
        }
        else if (rb != null && Mathf.Abs(rb.linearVelocity.x) > runThreshold)
        {
            next = Active.run;
            fps = runFps;
        }
        else
        {
            next = Active.idle;
            fps = idleFps;
        }

        if (next != current)
        {
            current = next;
            stateTime = 0f;
        }

        Frame frame = current == null ? Active.jump : current[(int)(stateTime * fps) % current.Length];
        body.sprite = frame.sprite;
        key.enabled = true;
        key.transform.localPosition = new Vector3((frame.keyPixel.x - Cell / 2) / Ppu, (Cell - frame.keyPixel.y) / Ppu, 0f);
    }

    // Plays the death animation once; it holds the last frame until Revive.
    [ContextMenu("Play Death")]
    public void PlayDeath()
    {
        IsDead = true;
        stateTime = 0f;
    }

    [ContextMenu("Revive")]
    public void Revive()
    {
        IsDead = false;
        current = null;
        stateTime = 0f;
    }

    private static int Mod(int a, int n) => ((a % n) + n) % n;
}
