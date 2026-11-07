using UnityEngine;

// A player's own sounds: footsteps while running, jump and land. Watches the
// Rigidbody2D and PlayerMovement, so neither needs to know about audio. Sfx
// adds it to every player at scene load. Not pitched by lane time: the
// players move at normal speed in both lanes.
public class PlayerAudio : MonoBehaviour
{
    [Tooltip("Seconds between footsteps at a run (the run cycle is 5 frames at 12 fps, two steps a cycle).")]
    public float stepInterval = 0.21f;
    public float stepVolume = 0.7f;
    [Tooltip("Upward speed that counts as a jump when leaving the ground.")]
    public float jumpSpeed = 2f;
    [Tooltip("Falling speed below which touching down is silent.")]
    public float landSpeed = 3f;
    [Tooltip("Horizontal speed above which footsteps play.")]
    public float runThreshold = 0.1f;

    Rigidbody2D rb;
    PlayerMovement movement;
    bool wasGrounded = true;
    float fallPeak;     // fastest fall since leaving the ground
    float stepTimer;
    int lastStep;

    void Awake()
    {
        rb = GetComponent<Rigidbody2D>();
        movement = GetComponent<PlayerMovement>();
    }

    void Update()
    {
        if (rb == null || movement == null) return;
        if (!rb.simulated || !movement.enabled)
        {
            // Frozen for death and respawn: no sounds, and no landing on revive.
            wasGrounded = true;
            fallPeak = 0f;
            stepTimer = 0f;
            return;
        }

        bool grounded = movement.IsGrounded();
        Vector2 v = rb.linearVelocity;

        if (wasGrounded && !grounded && v.y > jumpSpeed)
            Sfx.Play("jump", transform.position, 0.8f, pitched: false);
        // The body stops a frame before the ground check sees the ground, so
        // use the fastest fall of the whole airborne stretch.
        if (!grounded) fallPeak = Mathf.Max(fallPeak, -v.y);
        if (!wasGrounded && grounded)
        {
            if (fallPeak > landSpeed)
                Sfx.Play("land", transform.position, Mathf.Clamp01(fallPeak / 15f) * 0.8f + 0.2f, pitched: false);
            fallPeak = 0f;
        }

        if (grounded && Mathf.Abs(v.x) > runThreshold)
        {
            stepTimer -= Time.deltaTime;
            if (stepTimer <= 0f)
            {
                stepTimer += stepInterval;
                // Never the same step twice in a row.
                lastStep = (lastStep + Random.Range(1, 3)) % 3;
                Sfx.Play("footstep_" + (lastStep + 1), transform.position, stepVolume,
                         pitched: false, pitch: Random.Range(0.94f, 1.06f));
            }
        }
        else stepTimer = 0f;

        wasGrounded = grounded;
    }
}
