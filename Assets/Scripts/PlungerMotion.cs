using NUnit.Framework;
using UnityEngine;
using UnityEngine.Serialization;

[RequireComponent(typeof(Rigidbody2D))]
public class PlungerMotion : MonoBehaviour
{
    [Header("Movement")]
    [Tooltip("Distance the plunger travels from its raised position.")]
    [SerializeField, Min(0f)] private float travelDistance = 2f;
    [Tooltip("Direction the plunger slams in. The value is normalized.")]
    [SerializeField] private Vector2 slamDirection = Vector2.down;

    [Header("Timing (seconds of lane time)")]
    [Tooltip("Wait at the raised position before slamming.")]
    [SerializeField, Min(0f)] private float raisedPause = 2f;
    [Tooltip("Time for the plunger to slam down.")]
    [SerializeField, Min(0.05f)] private float slamDuration = 0.25f;
    [Tooltip("Time spent making the small rebound after the slam.")]
    [SerializeField, Min(0f)] private float bounceDuration = 0.2f;
    [Tooltip("Time for the plunger to return to its raised position.")]
    [SerializeField, Min(0.05f)] private float returnDuration = 0.8f;
    [Tooltip("Height of the small rebound after the slam.")]
    [SerializeField, Min(0f)] private float bounceDistance = 0.08f;

    [SerializeField]
    [Tooltip("Which lane is the object in")]
    [UnityEngine.Range(0,1)]
    private int lane;
    private enum Phase
    {
        RaisedPause,
        Slamming,
        Bouncing,
        Returning
    }

    private const float MaxSpeedMultiplier = 4f;

    private Rigidbody2D rb;
    private Vector2 raisedPosition;
    private Vector2 slamVector;
    private Phase phase = Phase.RaisedPause;
    private float phaseTime;



    private void Awake()
    {
        rb = GetComponent<Rigidbody2D>();
        rb.bodyType = RigidbodyType2D.Kinematic;
        rb.interpolation = RigidbodyInterpolation2D.Interpolate;
        rb.linearVelocity = Vector2.zero;
        rb.angularVelocity = 0f;

        raisedPosition = rb.position;
        slamVector = slamDirection.sqrMagnitude > Mathf.Epsilon
            ? slamDirection.normalized
            : Vector2.down;
        rb.position = raisedPosition;
    }

    private void FixedUpdate()
    {
        phaseTime += Time.fixedDeltaTime * GameManager.INSTANCE.GetWorldSpeed(lane);
        AdvancePhase();
        rb.MovePosition(PositionForPhase());
    }

    private void AdvancePhase()
    {
        for (int guard = 0; guard < 4; guard++)
        {
            float length = PhaseLength(phase);
            if (phaseTime < length) return;

            phaseTime -= length;
            phase = (Phase)(((int)phase + 1) % 4);
        }
    }

    private float PhaseLength(Phase currentPhase)
    {
        switch (currentPhase)
        {
            case Phase.RaisedPause: return raisedPause;
            case Phase.Slamming: return slamDuration;
            case Phase.Bouncing: return bounceDuration;
            default: return returnDuration;
        }
    }

    private Vector2 PositionForPhase()
    {
        Vector2 bottomPosition = raisedPosition + slamVector * travelDistance;

        switch (phase)
        {
            case Phase.Slamming:
                return Vector2.Lerp(raisedPosition, bottomPosition,
                    Fall(phaseTime / slamDuration));

            case Phase.Bouncing:
            {
                if (bounceDuration <= 0f) return bottomPosition;

                float progress = Mathf.Clamp01(phaseTime / bounceDuration);
                float rebound = Mathf.Sin(Mathf.PI * progress) * bounceDistance;
                return bottomPosition - slamVector * rebound;
            }

            case Phase.Returning:
                return Vector2.Lerp(bottomPosition, raisedPosition,
                    Smooth(phaseTime / returnDuration));

            default:
                return raisedPosition;
        }
    }

    private static float Fall(float progress)
    {
        progress = Mathf.Clamp01(progress);
        return progress * progress;
    }

    private static float Smooth(float progress)
    {
        progress = Mathf.Clamp01(progress);
        return progress * progress * (3f - 2f * progress);
    }
}
