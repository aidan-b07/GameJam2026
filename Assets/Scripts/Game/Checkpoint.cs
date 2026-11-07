using UnityEngine;

// Respawn point for one lane. Checkpoints come in pairs, one per lane with the
// same index (0 is the spawn point). Once both players have reached their own
// lane's checkpoint of an index, GameFlow makes that index current, and after a
// death both players come back at their own lane's checkpoint of that index.
//
// The trigger collider (if any) is the reach line; the spawn point has none.
public class Checkpoint : MonoBehaviour
{
    [Tooltip("Order along the level; 0 is the spawn point.")]
    public int index;
    [Tooltip("Lane: 0 = Tick (top), 1 = Tock (bottom). Matches GameFlow.players.")]
    public int lane;
    [Tooltip("World point the player's feet are put back on.")]
    public Vector2 feet;

    private void OnTriggerEnter2D(Collider2D other)
    {
        if (other.attachedRigidbody != null && other.attachedRigidbody.GetComponent<PlayerMovement>() != null)
            GameFlow.Instance?.Reach(this);
    }

    private void OnDrawGizmos()
    {
        Gizmos.color = new Color(1f, 0.4f, 0.7f, 0.9f);
        Gizmos.DrawWireSphere(feet, 0.2f);
    }
}
