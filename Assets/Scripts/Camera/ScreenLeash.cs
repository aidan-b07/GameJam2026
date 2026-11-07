using UnityEngine;

// Keeps the players inside the shared view of a CoopCamera. Past the soft edge
// (CoopCamera.edgeMargin in from the screen edge) a player walks into an
// elastic band: pushing outwards gets harder the deeper they go, a spring
// pulls them back when they let go, and the far side of the band is a hard
// wall. The camera only scrolls on when the partner has moved up, so the
// leader feels the edge give a little but cannot leave the partner behind.
//
// Runs after PlayerMovement, which sets the horizontal velocity from input
// every physics step, and bends that velocity before the physics step.
[DefaultExecutionOrder(100)]
[RequireComponent(typeof(CoopCamera))]
public class ScreenLeash : MonoBehaviour
{
    [Tooltip("Pull back towards the soft edge, in units per second per unit of overshoot.")]
    public float spring = 8f;

    CoopCamera rig;
    Rigidbody2D[] bodies;

    void Start()
    {
        rig = GetComponent<CoopCamera>();
        var players = rig.players ?? new Transform[0];
        bodies = new Rigidbody2D[players.Length];
        for (int i = 0; i < players.Length; i++)
            if (players[i] != null) bodies[i] = players[i].GetComponent<Rigidbody2D>();
    }

    void FixedUpdate()
    {
        float width = Mathf.Max(rig.elasticWidth, 0.01f);
        foreach (var rb in bodies)
        {
            if (rb == null || !rb.gameObject.activeInHierarchy) continue;
            float px = rb.position.x;
            // Overshoot past the soft edge: positive is outwards, dir is which way out is.
            float over = px - rig.SoftRight, dir = 1f;
            if (px < rig.SoftLeft) { over = rig.SoftLeft - px; dir = -1f; }
            if (over <= 0f) continue;

            // Past the band: back onto the wall, with no further outward speed.
            if (over > width)
            {
                rb.position = new Vector2(px - dir * (over - width), rb.position.y);
                over = width;
            }

            var v = rb.linearVelocity;
            float outward = v.x * dir;
            if (outward > 0f)
            {
                float give = 1f - over / width;
                outward *= give * give;
            }
            outward -= spring * over;
            rb.linearVelocity = new Vector2(outward * dir, v.y);
        }
    }
}
