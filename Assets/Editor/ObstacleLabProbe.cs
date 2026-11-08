using System.Reflection;
using System.Text;
using UnityEditor;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;

// Play-mode probe for ObstacleLab.unity, used over MCP execute_code while the
// Editor is paused and stepped (it does not tick play mode unfocused):
//
//   ObstacleLabProbe.Begin(lane: 1, fast: false);   // in play mode
//   ObstacleLabProbe.Place(1, 9f);
//   return ObstacleLabProbe.Run(1, 6f, t => 1, t => false);
//
// Begin turns the rifts and the screen leash off and holds the tested lane's
// speed: the partner's PlayerMovement is disabled and the probe reports the
// partner as idle (lane 0.35x) or running (3x) every step.
public static class ObstacleLabProbe
{
    public const float Dt = 0.02f;
    public static int Lane;
    public static bool Fast;
    public static int Deaths;
    public static float T;
    static bool wasRespawning;
    static readonly FieldInfo jumpBuffer =
        typeof(PlayerMovement).GetField("jumpBufferTimer", BindingFlags.NonPublic | BindingFlags.Instance);

    public static void Begin(int lane, bool fast)
    {
        EditorApplication.isPaused = true;
        Time.captureDeltaTime = Dt;
        Lane = lane;
        Fast = fast;
        Deaths = 0;
        T = 0f;
        if (LaneRift.Instance != null) LaneRift.Instance.enabled = false;
        var leash = Object.FindAnyObjectByType<ScreenLeash>();
        if (leash != null) leash.enabled = false;
        Partner.enabled = false;
    }

    public static Rigidbody2D Body(int lane) => GameFlow.Instance.players[lane].GetComponent<Rigidbody2D>();
    static PlayerMovement Partner => GameFlow.Instance.players[1 - Lane].GetComponent<PlayerMovement>();
    public static float LaneY(int lane) => lane == 1 ? LevelMap.TockLaneY : 0f;

    // Feet height above the lane's ground and x of the lane's player.
    public static float Feet(int lane) => Body(lane).GetComponent<BoxCollider2D>().bounds.min.y - LaneY(lane);
    public static float X(int lane) => Body(lane).position.x;

    // Stands the lane's player with their feet at (x, feet above the lane's ground).
    public static void Place(int lane, float x, float feet = 0f)
    {
        var rb = Body(lane);
        var box = rb.GetComponent<BoxCollider2D>();
        float off = (box.offset.y - box.size.y / 2f) * rb.transform.lossyScale.y;
        var p = new Vector2(x, LaneY(lane) + feet - off + 0.02f);
        rb.position = p;
        rb.transform.position = p;
        rb.linearVelocity = Vector2.zero;
    }

    // One stepped frame: the tested player's input (direction, jump press),
    // the partner held idle or running. Returns true on a new death.
    public static bool Step(int dir, bool jump)
    {
        Partner.enabled = false;
        GameManager.INSTANCE.UpdateWorldSpeed(1 - Lane, Fast ? 1f : 0f);
        var keys = new System.Collections.Generic.List<Key>();
        bool p1 = Lane == 0;
        if (dir < 0) keys.Add(p1 ? Key.A : Key.LeftArrow);
        if (dir > 0) keys.Add(p1 ? Key.D : Key.RightArrow);
        if (jump) keys.Add(p1 ? Key.W : Key.UpArrow);
        InputSystem.QueueStateEvent(Keyboard.current, new KeyboardState(keys.ToArray()));
        InputSystem.Update();
        if (jump) jumpBuffer?.SetValue(GameFlow.Instance.players[Lane].GetComponent<PlayerMovement>(), 0.1f);
        EditorApplication.Step();
        T += Dt;
        bool r = GameFlow.Instance.Respawning;
        bool died = r && !wasRespawning;
        if (died) Deaths++;
        wasRespawning = r;
        return died;
    }

    // Runs for `seconds` with input from the plans (by time since the start);
    // logs a line every `every` seconds and each death.
    public static string Run(float seconds, System.Func<float, int> dir, System.Func<float, bool> jump,
                             float every = 0.5f, System.Func<string> extra = null)
    {
        var sb = new StringBuilder();
        float t0 = T, next = 0f;
        while (T - t0 < seconds)
        {
            float t = T - t0;
            if (Step(dir(t), jump(t))) sb.AppendLine($"t {t:F2} DEATH at x {X(Lane):F2} feet {Feet(Lane):F2}");
            if (t >= next)
            {
                next += every;
                sb.AppendLine($"t {t:F2} x {X(Lane):F2} feet {Feet(Lane):F2} speed {GameManager.INSTANCE.GetWorldSpeed(Lane):F2} {extra?.Invoke()}");
            }
        }
        return sb.ToString();
    }

    // Waits (stepping, no input) until cond holds or timeout; returns seconds waited or -1.
    public static float WaitFor(System.Func<bool> cond, float timeout = 20f)
    {
        float t0 = T;
        while (T - t0 < timeout)
        {
            if (cond()) return T - t0;
            Step(0, false);
        }
        return -1f;
    }

    // Lets a respawn finish (no input).
    public static void Settle()
    {
        WaitFor(() => !GameFlow.Instance.Respawning, 10f);
        Step(0, false);
    }

    // Renders a lane's own camera (where the co-op camera put it, with its
    // parallax) to a PNG; path under the project.
    public static string Render(int lane, string path, int width = 1280, int height = 720)
    {
        var cam = GameObject.Find(lane == 0 ? "Camera_Tick" : "Camera_Tock").GetComponent<Camera>();
        var rect = cam.rect;
        var rt = new RenderTexture(width, height, 24);
        cam.rect = new Rect(0f, 0f, 1f, 1f);
        cam.targetTexture = rt;
        cam.Render();
        RenderTexture.active = rt;
        var tex = new Texture2D(width, height, TextureFormat.RGB24, false);
        tex.ReadPixels(new Rect(0, 0, width, height), 0, 0);
        tex.Apply();
        RenderTexture.active = null;
        System.IO.File.WriteAllBytes(path, tex.EncodeToPNG());
        cam.targetTexture = null;
        cam.rect = rect;
        Object.DestroyImmediate(rt);
        Object.DestroyImmediate(tex);
        return path;
    }
}
