using UnityEngine;

// The scenic stretch in the middle of the main level (section 3, decor only):
// a breather. While both players are inside their lane's range, the
// difficulty pauses: GameManager.Calm eases both lanes to real time (1x,
// the partner's movement no longer changes it) and LaneRift.Calm retracts
// both rifts, so the players can take in the scenery. Once either player
// leaves (or a death starts) the lanes ease back to the partner-driven speed
// and the rifts can grow again. CalmHalo (added here at runtime) shows it.
public class ScenicZone : MonoBehaviour
{
    [Tooltip("World x range per lane (Tick, Tock): x = start, y = end. The player's root x must be inside.")]
    public Vector2[] ranges = { new Vector2(44f, 81f), new Vector2(38f, 81f) };

    public bool Active { get; private set; }

    void Start()
    {
        if (GetComponent<CalmHalo>() == null) gameObject.AddComponent<CalmHalo>().zone = this;
    }

    void Update()
    {
        var flow = GameFlow.Instance;
        bool active = flow != null && !flow.Respawning && !flow.Finished && BothInside(flow);
        if (active == Active) return;
        Active = active;
        if (GameManager.INSTANCE != null) GameManager.INSTANCE.Calm = active;
        if (LaneRift.Instance != null) LaneRift.Instance.Calm = active;
        if (active) Sfx.Play("checkpoint", Camera.main != null ? Camera.main.transform.position : transform.position, 0.5f, false);
    }

    bool BothInside(GameFlow flow)
    {
        if (flow.players == null || flow.players.Length < ranges.Length) return false;
        for (int lane = 0; lane < ranges.Length; lane++)
        {
            var p = flow.players[lane];
            if (p == null || !p.gameObject.activeInHierarchy) return false;
            float x = p.position.x;
            if (x < ranges[lane].x || x > ranges[lane].y) return false;
        }
        return true;
    }

    void OnDisable()
    {
        if (!Active) return;
        Active = false;
        if (GameManager.INSTANCE != null) GameManager.INSTANCE.Calm = false;
        if (LaneRift.Instance != null) LaneRift.Instance.Calm = false;
    }
}
