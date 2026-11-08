using System.Collections;
using System.Collections.Generic;
using UnityEngine;

// Death and respawn for the pair (DESIGN.md: one dies, both restart from the
// last checkpoint). Any player's CrushDetector, a player falling out of the
// map (fallDepth below its lane's ground), or a hazard calling KillAll starts
// the sequence:
// 1. both players freeze and play their death animation;
// 2. the camera eases back to the current checkpoint pair;
// 3. both play the respawn animation there, then get control back.
//
// The current checkpoint is the highest index both players have reached, each
// in their own lane; the spawn points are checkpoint 0.
public class GameFlow : MonoBehaviour
{
    public static GameFlow Instance { get; private set; }

    [Tooltip("Players by lane: 0 = Tick, 1 = Tock (Checkpoint.lane).")]
    public Transform[] players;
    public CoopCamera cameraRig;

    [Header("Falling out of the map")]
    [Tooltip("Ground surface y of each lane, by lane (set by the builder).")]
    public float[] laneGroundY = { 0f, -40f };
    [Tooltip("A player whose feet drop this far below its lane's ground dies " +
             "(the ground is 5 deep and the view ends about 2 below the surface).")]
    public float fallDepth = 4f;

    [Header("Timing (seconds)")]
    [Tooltip("Pause after the death animation ends before the camera moves.")]
    public float afterDeath = 0.25f;
    [Tooltip("Camera pan speed back to the checkpoint (units per second); the pan " +
             "takes between minPan and maxPan.")]
    public float panSpeed = 30f;
    public float minPan = 0.5f;
    public float maxPan = 2f;

    public int CurrentCheckpoint { get; private set; }
    // Raised during a death, once both players are back at their checkpoints
    // (still hidden): buttons let go and their gates shut.
    public static event System.Action LevelReset;
    public bool Respawning { get; private set; }

    // Highest checkpoint index each lane's player has reached.
    private int[] reached;

    private class Player
    {
        public Rigidbody2D rb;
        public PlayerMovement movement;
        public PlayerSprites sprites;
        public CrushDetector crush;
        public BoxCollider2D box;
    }

    private readonly List<Player> list = new List<Player>();
    private readonly Dictionary<(int index, int lane), Checkpoint> checkpoints = new Dictionary<(int, int), Checkpoint>();

    private void Awake()
    {
        Instance = this;
        foreach (var c in FindObjectsByType<Checkpoint>())
            checkpoints[(c.index, c.lane)] = c;

        foreach (var t in players)
        {
            var p = new Player();
            if (t != null)
            {
                p.rb = t.GetComponent<Rigidbody2D>();
                p.movement = t.GetComponent<PlayerMovement>();
                p.sprites = t.GetComponentInChildren<PlayerSprites>();
                p.crush = t.GetComponent<CrushDetector>();
                p.box = t.GetComponent<BoxCollider2D>();
                if (p.crush != null) p.crush.Crushed += _ => KillAll();
            }
            list.Add(p);
        }
        reached = new int[list.Count];
    }

    private void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    private void FixedUpdate()
    {
        if (Respawning) return;
        for (int lane = 0; lane < list.Count && lane < laneGroundY.Length; lane++)
        {
            var p = list[lane];
            if (p.rb != null && Feet(p) < laneGroundY[lane] - fallDepth)
            {
                KillAll();
                return;
            }
        }
    }

    // Highest checkpoint index the player of a lane has reached (0 before any).
    public int Reached(int lane) => reached != null && lane >= 0 && lane < reached.Length ? reached[lane] : 0;

    public void Reach(Checkpoint c)
    {
        if (Respawning || c.lane < 0 || c.lane >= reached.Length) return;
        reached[c.lane] = Mathf.Max(reached[c.lane], c.index);

        // Lanes without a player do not hold the checkpoint back.
        int both = int.MaxValue;
        for (int lane = 0; lane < list.Count; lane++)
            if (list[lane].rb != null) both = Mathf.Min(both, reached[lane]);
        if (both != int.MaxValue && both > CurrentCheckpoint)
        {
            CurrentCheckpoint = both;
            Sfx.PlayCentred("checkpoint", 0.8f);
        }
    }

    // Kills both players and respawns them at the current checkpoint.
    [ContextMenu("Kill All")]
    public void KillAll()
    {
        if (!Respawning) StartCoroutine(DieAndRespawn());
    }

    private IEnumerator DieAndRespawn()
    {
        Respawning = true;
        Sfx.PlayCentred("death");
        float death = 0f;
        foreach (var p in list)
        {
            if (p.rb == null) continue;
            SetFrozen(p, true);
            if (p.sprites != null)
            {
                p.sprites.PlayDeath();
                death = Mathf.Max(death, p.sprites.DeathDuration);
            }
        }
        yield return new WaitForSeconds(death + afterDeath);

        // The death animation ends empty, so the move back is not seen.
        for (int lane = 0; lane < list.Count; lane++)
            MoveToCheckpoint(list[lane], lane);
        LevelReset?.Invoke();

        if (cameraRig != null)
            yield return new WaitForSeconds(cameraRig.PanToPlayers(panSpeed, minPan, maxPan));

        Sfx.PlayCentred("respawn");
        float respawn = 0f;
        foreach (var p in list)
        {
            if (p.sprites == null) continue;
            p.sprites.PlayRespawn();
            respawn = Mathf.Max(respawn, p.sprites.RespawnDuration);
        }
        yield return new WaitForSeconds(respawn);

        foreach (var p in list)
        {
            if (p.rb == null) continue;
            SetFrozen(p, false);
            if (p.crush != null) p.crush.Rearm();
        }
        Respawning = false;
    }

    private void MoveToCheckpoint(Player p, int lane)
    {
        if (p.rb == null) return;
        // Highest checkpoint at or below the current one that exists in this lane.
        Checkpoint c = null;
        for (int i = CurrentCheckpoint; i >= 0 && c == null; i--)
            checkpoints.TryGetValue((i, lane), out c);
        if (c == null) return;

        Vector2 pos = c.feet - new Vector2(0f, FeetOffset(p));
        p.rb.position = pos;
        p.rb.transform.position = new Vector3(pos.x, pos.y, p.rb.transform.position.z);
        p.rb.linearVelocity = Vector2.zero;
    }

    // Feet height relative to the body's position.
    private static float FeetOffset(Player p)
    {
        return p.box != null ? (p.box.offset.y - p.box.size.y / 2f) * p.rb.transform.lossyScale.y : 0f;
    }

    private static float Feet(Player p) => p.rb.position.y + FeetOffset(p);

    private static void SetFrozen(Player p, bool frozen)
    {
        p.rb.linearVelocity = Vector2.zero;
        p.rb.simulated = !frozen;
        if (p.movement != null) p.movement.enabled = !frozen;
    }
}
