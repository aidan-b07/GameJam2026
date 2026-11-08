using System.Collections;
using System.Collections.Generic;
using UnityEngine;

// Death and respawn for the pair (DESIGN.md: one dies, both restart from the
// last checkpoint). Any player's CrushDetector, a player falling out of the
// map (fallDepth below its lane's ground), a lane's rift reaching its player
// (LaneRift), or a hazard calling KillAll starts
// the sequence:
// 1. both players freeze and play their death animation;
// 2. the camera eases back to the current checkpoint pair;
// 3. each player's light leaves their lantern on the checkpoint lamp as a
//    wisp (SpawnWisp; it starts as the pan ends) and bursts at the respawn
//    point;
// 4. both play the respawn animation there, then get control back.
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
    // Set by the ending: nothing kills any more.
    public bool Finished { get; set; }

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
    private readonly Dictionary<Checkpoint, CheckpointLamp> lamps = new Dictionary<Checkpoint, CheckpointLamp>();
    private SpawnWisp wisp;

    private void Awake()
    {
        Instance = this;
        foreach (var c in FindObjectsByType<Checkpoint>())
            checkpoints[(c.index, c.lane)] = c;
        foreach (var l in FindObjectsByType<CheckpointLamp>())
            if (l.checkpoint != null) lamps[l.checkpoint] = l;
        wisp = GetComponent<SpawnWisp>();
        if (wisp == null) wisp = gameObject.AddComponent<SpawnWisp>();

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
        if (Respawning || Finished) return;
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
        if (!Respawning && !Finished) StartCoroutine(DieAndRespawn());
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
        var at = new Checkpoint[list.Count];
        for (int lane = 0; lane < list.Count; lane++)
            at[lane] = MoveToCheckpoint(list[lane], lane);
        LevelReset?.Invoke();

        float pan = cameraRig != null ? cameraRig.PanToPlayers(panSpeed, minPan, maxPan) : 0f;
        // The lanterns flare while the camera settles; the wisps burst as the
        // respawn animation starts.
        float lead = wisp != null ? Mathf.Min(wisp.Lead, pan) : 0f;
        yield return new WaitForSeconds(pan - lead);
        if (wisp != null)
        {
            for (int lane = 0; lane < list.Count; lane++)
            {
                if (list[lane].rb == null || at[lane] == null) continue;
                lamps.TryGetValue(at[lane], out var lamp);
                wisp.Play(lane, lamp, new Vector3(list[lane].rb.position.x, at[lane].feet.y, 0f));
            }
            yield return new WaitForSeconds(wisp.Duration);
        }

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

    private Checkpoint MoveToCheckpoint(Player p, int lane)
    {
        if (p.rb == null) return null;
        // Highest checkpoint at or below the current one that exists in this lane.
        Checkpoint c = null;
        for (int i = CurrentCheckpoint; i >= 0 && c == null; i--)
            checkpoints.TryGetValue((i, lane), out c);
        if (c == null) return null;

        Vector2 pos = c.feet - new Vector2(0f, FeetOffset(p));
        p.rb.position = pos;
        p.rb.transform.position = new Vector3(pos.x, pos.y, p.rb.transform.position.z);
        p.rb.linearVelocity = Vector2.zero;
        return c;
    }

    // Feet height relative to the body's position.
    private static float FeetOffset(Player p)
    {
        return p.box != null ? (p.box.offset.y - p.box.size.y / 2f) * p.rb.transform.lossyScale.y : 0f;
    }

    private static float Feet(Player p) => p.rb.position.y + FeetOffset(p);

    // Top of the lane's player collider (world y).
    public float Top(int lane)
    {
        var p = list[lane];
        if (p.rb == null) return float.NegativeInfinity;
        float top = p.box != null ? (p.box.offset.y + p.box.size.y / 2f) * p.rb.transform.lossyScale.y : 0f;
        return p.rb.position.y + top;
    }

    // The lane's player's horizontal speed as a fraction (0..1) of their run speed.
    public float HorizontalSpeed(int lane)
    {
        var p = list[lane];
        if (p.rb == null || p.movement == null) return 0f;
        return Mathf.Clamp01(Mathf.Abs(p.rb.linearVelocity.x) / Mathf.Max(0.01f, p.movement.MoveSpeed));
    }

    private static void SetFrozen(Player p, bool frozen)
    {
        p.rb.linearVelocity = Vector2.zero;
        p.rb.simulated = !frozen;
        if (p.movement != null) p.movement.enabled = !frozen;
    }
}
