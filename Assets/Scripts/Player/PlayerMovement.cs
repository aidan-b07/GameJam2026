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
    [Tooltip("If this player's controller isn't connected, use the keyboard instead. P1: A/D + Space. P2: Arrows + Right Ctrl.")]
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

    private Rigidbody2D rb;
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
        rb.linearVelocity = new Vector2(horizontalInput * moveSpeed, rb.linearVelocity.y);
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
            if(jumpPressed) Debug.Log("Jumped");
        }
        else if (keyboardFallback && Keyboard.current != null)
        {
            Keyboard kb = Keyboard.current;
            bool isP1 = playerIndex == 0;

            KeyControl left = isP1 ? kb.aKey : kb.leftArrowKey;
            KeyControl right = isP1 ? kb.dKey : kb.rightArrowKey;
            KeyControl jump = isP1 ? kb.spaceKey : kb.rightCtrlKey;

            if (left.isPressed) horizontalInput -= 1f;
            if (right.isPressed) horizontalInput += 1f;

            jumpPressed = jump.wasPressedThisFrame;
            jumpReleased = jump.wasReleasedThisFrame;
        }
    }

    private bool IsGrounded()
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

    private void OnDrawGizmosSelected()
    {
        if (groundCheck == null)
            return;

        Gizmos.color = Color.yellow;
        Gizmos.DrawWireSphere(groundCheck.position, groundCheckRadius);
    }
}