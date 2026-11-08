using System;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;

[RequireComponent(typeof(Rigidbody2D))]
public class PlayerMovement : MonoBehaviour
{
    [Header("Player")]
    [Tooltip("0 uses the first controller plugged in, 1 uses the second.")]
    [SerializeField] private int playerIndex = 0;
    [Tooltip("If this player's controller isn't connected, use the keyboard instead. P1: A/D + W. P2: Left/Right + Up arrow.")]
    [SerializeField] private bool keyboardFallback = true;
    [SerializeField] private float stickDeadzone = 0.2f;

    [Header("Movement")]
    [SerializeField] private float moveSpeed = 8f;

    [Header("Jumping")]
    [SerializeField] private float jumpForce = 14f;
    [Tooltip("Grace period after walking off a ledge where a jump still counts.")]
    [SerializeField] private float coyoteTime = 0.1f;
    [Tooltip("Jump presses this close to landing are remembered and fire on touchdown.")]
    [SerializeField] private float jumpBufferTime = 0.1f;
    [Tooltip("Upward velocity is multiplied by this when jump is released early.")]
    [Range(0f, 1f)]
    [SerializeField] private float jumpCutMultiplier = 0.5f;

    [Header("Ground Check")]
    [SerializeField] private Transform groundCheck;
    [SerializeField] private float groundCheckRadius = 0.15f;
    [SerializeField] private LayerMask groundLayer;

    [Header("Step Up")]
    [Tooltip("Ledges up to this tall (units; 1 tile = 1) are stepped onto instead of blocking the player. Covers the few-pixel bumps between ground tiles.")]
    [SerializeField] private float maxStepHeight = 0.25f;

    public float MoveSpeed => moveSpeed;

    private Rigidbody2D rb;
    private Collider2D body;
    private ContactFilter2D groundFilter;
    private readonly RaycastHit2D[] castHits = new RaycastHit2D[4];
    private float horizontalInput;
    private bool jumpPressed;
    private bool jumpReleased;
    private float coyoteTimer;
    private float jumpBufferTimer;
    private bool facingRight = true;

    private void Awake()
    {
        rb = GetComponent<Rigidbody2D>();
        rb.freezeRotation = true;
        body = GetComponent<Collider2D>();
        groundFilter = new ContactFilter2D { useTriggers = false };
        groundFilter.SetLayerMask(groundLayer);
    }

    private void Update()
    {
        ReadInput();

        // Coyote time
        if (IsGrounded())
            coyoteTimer = coyoteTime;
        else
            coyoteTimer -= Time.deltaTime;

        // Jump buffer
        if (jumpPressed)
            jumpBufferTimer = jumpBufferTime;
        else
            jumpBufferTimer -= Time.deltaTime;

        if (jumpBufferTimer > 0f && coyoteTimer > 0f)
        {
            rb.linearVelocity = new Vector2(rb.linearVelocity.x, jumpForce);
            jumpBufferTimer = 0f;
            coyoteTimer = 0f;
        }

        // Releasing jump early gives a shorter hop
        if (jumpReleased && rb.linearVelocity.y > 0f)
            rb.linearVelocity = new Vector2(rb.linearVelocity.x, rb.linearVelocity.y * jumpCutMultiplier);

        if ((horizontalInput > 0f && !facingRight) || (horizontalInput < 0f && facingRight))
            Flip();
    }

    private void FixedUpdate()
    {
        if (horizontalInput != 0f)
            TryStepUp(Mathf.Sign(horizontalInput), Mathf.Abs(horizontalInput) * moveSpeed * Time.fixedDeltaTime);

        float vx = horizontalInput * moveSpeed;

        // In the air, pushing into a wall pins the player there by friction.
        // Stop pushing so they fall.
        if (vx != 0f && !IsGrounded() && TouchingWall(Mathf.Sign(vx)))
            vx = 0f;

        rb.linearVelocity = new Vector2(vx, rb.linearVelocity.y);
        UpdateWorldSpeedModifiers();
    }

    private bool TouchingWall(float dir)
    {
        if (body == null)
            return false;

        int count = body.Cast(new Vector2(dir, 0f), groundFilter, castHits, 0.05f);
        for (int i = 0; i < count; i++)
        {
            // One-way platforms are open at the sides.
            if (castHits[i].collider.usedByEffector)
                continue;
            if (Mathf.Abs(castHits[i].normal.x) > 0.5f)
                return true;
        }
        return false;
    }

    private void ReadInput()
    {
        horizontalInput = 0f;
        jumpPressed = false;
        jumpReleased = false;

        Gamepad pad = playerIndex < Gamepad.all.Count ? Gamepad.all[playerIndex] : null;

        if (pad != null)
        {
            // Left stick and d-pad both work
            float x = pad.leftStick.x.ReadValue() + pad.dpad.x.ReadValue();
            horizontalInput = Mathf.Abs(x) > stickDeadzone ? Mathf.Clamp(x, -1f, 1f) : 0f;

            // A on Xbox, Cross on PlayStation
            jumpPressed = pad.buttonSouth.wasPressedThisFrame;
            jumpReleased = pad.buttonSouth.wasReleasedThisFrame;
        }
        else if (keyboardFallback && Keyboard.current != null)
        {
            Keyboard kb = Keyboard.current;
            bool isP1 = playerIndex == 0;

            KeyControl left = isP1 ? kb.aKey : kb.leftArrowKey;
            KeyControl right = isP1 ? kb.dKey : kb.rightArrowKey;
            KeyControl jump = isP1 ? kb.wKey : kb.upArrowKey;

            if (left.isPressed) horizontalInput -= 1f;
            if (right.isPressed) horizontalInput += 1f;

            jumpPressed = jump.wasPressedThisFrame;
            jumpReleased = jump.wasReleasedThisFrame;
        }
    }

    // Lifts the player onto a low ledge in front of them, so small bumps in the
    // ground (or a vertex seam the box catches on) don't stop them dead.
    private void TryStepUp(float dir, float distance)
    {
        if (body == null || maxStepHeight <= 0f || rb.linearVelocity.y > 0.1f || !IsGrounded())
            return;

        // Is something blocking the way at all?
        int count = body.Cast(new Vector2(dir, 0f), groundFilter, castHits, distance + 0.02f);
        RaycastHit2D wall = default;
        for (int i = 0; i < count; i++)
        {
            if (Mathf.Abs(castHits[i].normal.x) > 0.5f)
            {
                wall = castHits[i];
                break;
            }
        }
        if (wall.collider == null)
            return;

        // Find the top of the obstacle just past the contact point.
        Bounds b = body.bounds;
        Vector2 probe = new Vector2(wall.point.x + dir * 0.02f, b.min.y + maxStepHeight + 0.01f);
        RaycastHit2D top = Physics2D.Raycast(probe, Vector2.down, maxStepHeight + 0.01f, groundLayer);
        if (top.collider == null || top.normal.y < 0.7f)
            return;

        // A ledge level with the feet still blocks: the box snags on its corner
        // through the contact offset. Clear it by a little more than that.
        float skin = Physics2D.defaultContactOffset * 2f;
        float step = top.point.y - b.min.y;
        if (step < -skin || step > maxStepHeight)
            return;
        float rise = Mathf.Max(step, 0f) + skin;

        // Only step if the body fits there.
        Vector2 target = (Vector2)b.center + new Vector2(dir * 0.02f, rise);
        if (Physics2D.OverlapBox(target, (Vector2)b.size - Vector2.one * 0.05f, 0f, groundLayer) != null)
            return;

        rb.position += new Vector2(0f, rise);
        rb.linearVelocity = new Vector2(rb.linearVelocity.x, Mathf.Max(0f, rb.linearVelocity.y));
    }

    public bool IsGrounded()
    {
        if (groundCheck == null)
            return false;

        return Physics2D.OverlapCircle(groundCheck.position, groundCheckRadius, groundLayer);
    }

    private void Flip()
    {
        facingRight = !facingRight;
        Vector3 scale = transform.localScale;
        scale.x *= -1f;
        transform.localScale = scale;
    }

    private void UpdateWorldSpeedModifiers()
    {
        if (GameManager.INSTANCE == null)
            return;

        float amount = Mathf.Clamp01(Mathf.Abs(rb.linearVelocity.magnitude) / moveSpeed);
        GameManager.INSTANCE.UpdateWorldSpeed(playerIndex, amount);
    }
    
    private void OnDrawGizmosSelected()
    {
        if (groundCheck == null)
            return;

        Gizmos.color = Color.yellow;
        Gizmos.DrawWireSphere(groundCheck.position, groundCheckRadius);
    }
}