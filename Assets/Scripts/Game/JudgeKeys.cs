using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;

// Hidden shortcuts for showing the level to the jam judges (PLAN.md milestone
// 6), in the Editor and in player builds, only on the F keys:
//   F1-F9  both players to checkpoint 1-9 (made current, lamps light; the
//          respawn sequence runs, ScreenLeash off meanwhile: GameFlow.WarpToCheckpoint).
//          Read from the level's Checkpoint components, so new checkpoints
//          get a key without changes here; keys past the last one do nothing;
//   F10    both players a few steps before their clock doors (checkpoint set
//          to the last one): walk right to trigger the ending;
//   F12    toggles a small readout: lane speeds, lane hours, rift depths, fps.
// The warps do nothing while paused, during a respawn or once the ending runs.
// Creates itself on scene load in scenes with a GameFlow, like PauseMenu.
public class JudgeKeys : MonoBehaviour
{
    [Tooltip("F4 puts each player this many units left of their door's trigger.")]
    public float doorLead = 2.5f;

    // Kept across scene loads and restarts.
    static bool showReadout;

    float fps;
    GUIStyle style;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Bootstrap()
    {
        SceneManager.sceneLoaded -= OnSceneLoaded;
        SceneManager.sceneLoaded += OnSceneLoaded;
        Setup(SceneManager.GetActiveScene());
    }

    static void OnSceneLoaded(Scene scene, LoadSceneMode mode) => Setup(scene);

    static void Setup(Scene scene)
    {
        var existing = FindAnyObjectByType<JudgeKeys>();
        if (existing != null && existing.gameObject.scene == scene) return;
        var flow = GameFlow.Instance != null ? GameFlow.Instance : FindAnyObjectByType<GameFlow>();
        if (flow == null || flow.gameObject.scene != scene) return;
        var go = new GameObject("JudgeKeys");
        SceneManager.MoveGameObjectToScene(go, scene);
        go.AddComponent<JudgeKeys>();
    }

    void Update()
    {
        float dt = Time.unscaledDeltaTime;
        if (dt > 0f) fps = fps <= 0f ? 1f / dt : Mathf.Lerp(fps, 1f / dt, 0.05f);

        var kb = Keyboard.current;
        if (kb == null) return;
        if (kb.f12Key.wasPressedThisFrame) showReadout = !showReadout;

        int checkpoint = 0;
        for (int i = 0; i < CheckpointKeys.Length; i++)
            if (kb[CheckpointKeys[i]].wasPressedThisFrame) checkpoint = i + 1;
        bool doors = kb.f10Key.wasPressedThisFrame;
        if ((checkpoint == 0 && !doors) || !CanWarp(out var flow)) return;

        if (checkpoint > 0)
        {
            if (!flow.WarpToCheckpoint(checkpoint))
                Debug.Log($"[JudgeKeys] No checkpoint {checkpoint} in this level (last is {flow.HighestCheckpoint}).");
            return;
        }
        WarpToDoors(flow);
    }

    // Checkpoint n warps on CheckpointKeys[n - 1].
    static readonly Key[] CheckpointKeys =
    {
        Key.F1, Key.F2, Key.F3, Key.F4, Key.F5, Key.F6, Key.F7, Key.F8, Key.F9,
    };

    static bool CanWarp(out GameFlow flow)
    {
        flow = GameFlow.Instance;
        if (flow == null || flow.Respawning || flow.Finished || PauseMenu.IsPaused) return false;
        var ending = FindAnyObjectByType<DawnEnding>();
        return ending == null || !ending.Running;
    }

    void WarpToDoors(GameFlow flow)
    {
        int lanes = flow.players != null ? flow.players.Length : 0;
        var feet = new Vector2?[lanes];
        bool any = false;
        foreach (var door in FindObjectsByType<ClockDoor>())
        {
            if (door == null || door.lane < 0 || door.lane >= lanes) continue;
            float ground = door.lane < flow.laneGroundY.Length ? flow.laneGroundY[door.lane] : door.transform.position.y;
            float x = door.triggerX - doorLead;
            feet[door.lane] = new Vector2(x, GroundAt(x, ground));
            any = true;
        }
        if (!any)
        {
            Debug.Log("[JudgeKeys] No clock doors in this level.");
            return;
        }
        flow.WarpTo(feet, flow.HighestCheckpoint);
    }

    // Top of the ground (layer Ground, not a player or trigger) under x, searched
    // from 10 units above the lane's ground; the lane's ground if nothing is hit.
    static float GroundAt(float x, float laneGround)
    {
        int mask = LayerMask.GetMask("Ground");
        if (mask == 0) mask = Physics2D.DefaultRaycastLayers;
        var hits = Physics2D.RaycastAll(new Vector2(x, laneGround + 10f), Vector2.down, 20f, mask);
        foreach (var hit in hits)
        {
            if (hit.collider == null || hit.collider.isTrigger) continue;
            if (hit.collider.GetComponentInParent<PlayerMovement>() != null) continue;
            return hit.point.y;
        }
        return laneGround;
    }

    void OnGUI()
    {
        if (!showReadout) return;
        if (style == null)
        {
            style = new GUIStyle(GUI.skin.label) { richText = false, wordWrap = false };
            style.normal.textColor = new Color(1f, 0.93f, 0.75f);
        }
        style.fontSize = Mathf.Max(10, Mathf.RoundToInt(Screen.height / 720f * 13f));

        var gm = GameManager.INSTANCE;
        var rift = LaneRift.Instance;
        var flow = GameFlow.Instance;
        string text = $"{fps:0} fps   checkpoint {(flow != null ? flow.CurrentCheckpoint : 0)}"
            + (PauseMenu.IsPaused ? "   PAUSED" : "");
        string[] names = { "Tick", "Tock" };
        for (int lane = 0; lane < 2; lane++)
        {
            string speed = gm != null ? $"{gm.GetWorldSpeed(lane):0.00}x" : "-";
            string hour = "-";
            if (gm != null)
            {
                float h = gm.GetLaneHour(lane);
                int minutes = Mathf.FloorToInt(h * 60f) % (24 * 60);
                hour = $"{minutes / 60:00}:{minutes % 60:00}";
            }
            string depth = rift != null ? $"{rift.Depth(lane):0.00}" : "-";
            text += $"\n{names[lane]} lane  speed {speed}  clock {hour}  rift {depth}";
        }

        var size = style.CalcSize(new GUIContent(text));
        float pad = style.fontSize * 0.5f;
        var box = new Rect(Screen.width / 2f - size.x / 2f - pad, pad, size.x + pad * 2f, size.y + pad);
        var old = GUI.color;
        GUI.color = new Color(0f, 0f, 0f, 0.6f);
        GUI.DrawTexture(box, Texture2D.whiteTexture);
        GUI.color = old;
        GUI.Label(new Rect(box.x + pad, box.y + pad / 2f, size.x, size.y), text, style);
    }
}
