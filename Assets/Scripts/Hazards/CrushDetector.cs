using System;
using System.Collections.Generic;
using UnityEngine;

// Generic squash check for any body (the players, later anything else that can
// be crushed). Nothing needs to mark itself as a crusher: a body is crushed when
// solid things press it from opposite sides (a drawbridge leaf onto the floor,
// a piston onto the ground, a moving wall into a wall) or when a solid collider
// has been driven deep into it. Raises Crushed once; Rearm arms it again.
//
// Only static and kinematic colliders squash: other dynamic bodies (the other
// player, loose props) just get pushed out of the way by the solver.
[RequireComponent(typeof(Rigidbody2D))]
public class CrushDetector : MonoBehaviour
{
    [Tooltip("Total overlap (units) of two opposing contacts that counts as being squeezed.")]
    [SerializeField] private float squeezeDepth = 0.08f;
    [Tooltip("Overlap (units) with any single solid collider that counts as crushed, whatever the direction.")]
    [SerializeField] private float overlapDepth = 0.3f;
    [Tooltip("Normals at least this opposed (cosine of the angle between them, negated) count as opposite sides.")]
    [SerializeField, Range(0f, 1f)] private float opposition = 0.6f;
    [Tooltip("Physics steps the squeeze must last before it kills. A fast leaf can push a body through the ground in two steps, so keep this low.")]
    [SerializeField, Min(1)] private int stepsToCrush = 1;

    public event Action<CrushDetector> Crushed;
    public bool IsCrushed { get; private set; }

    private Rigidbody2D rb;
    private Collider2D body;
    private ContactFilter2D solidFilter;
    private readonly List<ContactPoint2D> contacts = new List<ContactPoint2D>();
    private readonly List<Collider2D> overlaps = new List<Collider2D>();
    private int squeezedSteps;

    private void Awake()
    {
        rb = GetComponent<Rigidbody2D>();
        body = GetComponent<Collider2D>();
        solidFilter = new ContactFilter2D { useTriggers = false };
    }

    private void FixedUpdate()
    {
        if (IsCrushed || body == null || !rb.simulated) return;

        squeezedSteps = Squeezed() ? squeezedSteps + 1 : 0;
        if (squeezedSteps < stepsToCrush) return;

        IsCrushed = true;
        Crushed?.Invoke(this);
    }

    // Arms the detector again (after a respawn).
    public void Rearm()
    {
        IsCrushed = false;
        squeezedSteps = 0;
    }

    private bool Squeezed()
    {
        // Opposite sides: two contacts with other solids whose normals face each other.
        int n = body.GetContacts(solidFilter, contacts);
        for (int i = 0; i < n; i++)
        {
            if (!Squeezes(contacts[i].collider)) continue;
            for (int j = i + 1; j < n; j++)
            {
                if (!Squeezes(contacts[j].collider)) continue;
                if (Vector2.Dot(contacts[i].normal, contacts[j].normal) > -opposition) continue;
                float depth = -Mathf.Min(contacts[i].separation, 0f) - Mathf.Min(contacts[j].separation, 0f);
                if (depth >= squeezeDepth) return true;
            }
        }

        // Driven deep inside: something moved through the body faster than the
        // solver could push it out (a fast swing landing on it).
        int m = body.Overlap(solidFilter, overlaps);
        for (int i = 0; i < m; i++)
        {
            if (!Squeezes(overlaps[i])) continue;
            if (body.Distance(overlaps[i]).distance <= -overlapDepth) return true;
        }
        return false;
    }

    private bool Squeezes(Collider2D other)
    {
        if (other == null || other.isTrigger || other.usedByEffector) return false;
        var orb = other.attachedRigidbody;
        return orb != rb && (orb == null || orb.bodyType != RigidbodyType2D.Dynamic);
    }
}
