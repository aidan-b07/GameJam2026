using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;

// Play-mode test bot for the main level: drives both players with scripted,
// reactive input plans while the Editor is paused and stepped (the Editor does
// not tick play mode while unfocused). Use it over MCP execute_code:
//
//   LevelBot.Begin();                         // in play mode: pause, 0.02 s steps
//   LevelBot.Place(0, 20f); LevelBot.Place(1, 18f);
//   return LevelBot.Run(LevelBot.Seq(LevelBot.RunTo(0, 30f)), null, 10f);
//
// Tick (lane 0) is keyboard player 1 (A, D, W), Tock (lane 1) player 2
// (arrows). Plans are sequences of In (direction and jump) per step; the
// helpers below build them and can wait on the world (crusher heads, the
// drawbridge angle, gates). Run returns a log: a line every logEvery seconds
// plus events (deaths, checkpoints, buttons, gates, marks).
public static class LevelBot
{
    public struct In
    {
        public int dir;
        public bool jump;
        public In(int dir, bool jump = false) { this.dir = dir; this.jump = jump; }
    }

    public const float Dt = 0.02f;
    public static float T;                  // seconds since Begin
    public static int Deaths;
    public static float logEvery = 0.5f;
    public static bool manualInputUpdate = true;
    static readonly bool[] jumpHeld = new bool[2];
    static readonly System.Reflection.FieldInfo jumpBuffer =
        typeof(PlayerMovement).GetField("jumpBufferTimer", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
    static readonly StringBuilder log = new StringBuilder();

    public static void Begin()
    {
        EditorApplication.isPaused = true;
        Time.captureDeltaTime = Dt;
        T = 0f;
        Deaths = 0;
    }

    public static Rigidbody2D Body(int lane) => GameFlow.Instance.players[lane].GetComponent<Rigidbody2D>();
    public static Vector2 Pos(int lane) => Body(lane).position;
    public static float X(int lane) => Pos(lane).x;
    public static float Feet(int lane)
    {
        var box = Body(lane).GetComponent<BoxCollider2D>();
        return box.bounds.min.y - (lane == 1 ? LevelMap.TockLaneY : 0f);
    }
    public static bool Grounded(int lane) => GameFlow.Instance.players[lane].GetComponent<PlayerMovement>().IsGrounded();
    public static float Speed(int lane) => GameManager.INSTANCE.GetWorldSpeed(lane);
    public static float Rift(int lane) => LaneRift.Instance != null ? LaneRift.Instance.Depth(lane) : 0f;
    // Head clearance: units between the player's head and the lowest rift teeth.
    public static float Clear(int lane)
    {
        var flow = GameFlow.Instance;
        var cam = flow.cameraRig.cameras[lane];
        return cam.transform.position.y + cam.orthographicSize - LaneRift.Instance.TipReach(lane) - flow.Top(lane);
    }

    static string LaneName(int lane) => lane == 0 ? "Tick" : "Tock";
    public static GameObject Find(int lane, string name) => GameObject.Find($"Lane_{LaneName(lane)}/Placeholders/{name}");
    // Height of a crusher head's bottom above the lane's ground.
    public static float Head(int lane, int n)
    {
        var rb = Find(lane, $"Crusher_{n}").GetComponent<Rigidbody2D>();
        return rb.position.y - 1f - (lane == 1 ? LevelMap.TockLaneY : 0f);
    }
    public static BridgeMovement Bridge(int lane, int n = 1) => Find(lane, $"Drawbridge_{n}").GetComponent<BridgeMovement>();
    public static float Gate(int lane, int n) => Find(lane, $"ButtonGate_{n}").GetComponent<ButtonGate>().Open;
    public static bool Button(int lane, int n) => Find(lane, $"Button_{n}").GetComponent<FloorButton>().Pressed;

    // Puts a player's feet on the lane's ground (or y) at x, closes the rifts and
    // lets the camera settle with the leash off.
    public static void Place(int lane, float x, float y = 0f)
    {
        var rb = Body(lane);
        var box = rb.GetComponent<BoxCollider2D>();
        float feetOffset = (box.offset.y - box.size.y / 2f) * rb.transform.lossyScale.y;
        var p = new Vector2(x, y + (lane == 1 ? LevelMap.TockLaneY : 0f) - feetOffset + 0.02f);
        rb.position = p;
        rb.transform.position = new Vector3(p.x, p.y, rb.transform.position.z);
        rb.linearVelocity = Vector2.zero;
    }

    public static void Settle(float seconds = 1.5f)
    {
        var leash = GameFlow.Instance.cameraRig.GetComponent<ScreenLeash>();
        leash.enabled = false;
        for (float t = 0f; t < seconds; t += Dt) Frame(new In(), new In());
        leash.enabled = true;
        LaneRift.Instance?.Close();
    }

    public static void Reach(int index)
    {
        foreach (var c in UnityEngine.Object.FindObjectsByType<Checkpoint>())
            if (c.index <= index) GameFlow.Instance.Reach(c);
    }

    static void Frame(In tick, In tock)
    {
        var keys = new List<Key>();
        if (tick.dir < 0) keys.Add(Key.A);
        if (tick.dir > 0) keys.Add(Key.D);
        if (tick.jump) keys.Add(Key.W);
        if (tock.dir < 0) keys.Add(Key.LeftArrow);
        if (tock.dir > 0) keys.Add(Key.RightArrow);
        if (tock.jump) keys.Add(Key.UpArrow);
        InputSystem.QueueStateEvent(Keyboard.current, new KeyboardState(keys.ToArray()));
        if (manualInputUpdate) InputSystem.Update();
        // The stepped Editor misses wasPressedThisFrame, so a jump press goes
        // straight into the player's jump buffer (always a full jump).
        bool[] jump = { tick.jump, tock.jump };
        for (int lane = 0; lane < 2; lane++)
        {
            if (jump[lane] && !jumpHeld[lane])
                jumpBuffer.SetValue(GameFlow.Instance.players[lane].GetComponent<PlayerMovement>(), 0.1f);
            jumpHeld[lane] = jump[lane];
        }
        EditorApplication.Step();
        T += Dt;
    }

    public static void Mark(string text) => log.AppendLine($"{T,6:F2} * {text}");

    // Runs both plans (null = stand still) for at most maxSeconds. Stops at a
    // death when stopOnDeath, or once both plans are done.
    public static string Run(IEnumerable<In> tick, IEnumerable<In> tock, float maxSeconds, bool stopOnDeath = true)
    {
        log.Clear();
        var a = (tick ?? Enumerable.Empty<In>()).GetEnumerator();
        var b = (tock ?? Enumerable.Empty<In>()).GetEnumerator();
        bool aDone = tick == null, bDone = tock == null;
        bool wasDead = GameFlow.Instance.Respawning;
        int cp = GameFlow.Instance.CurrentCheckpoint;
        var buttons = UnityEngine.Object.FindObjectsByType<FloorButton>();
        var pressed = buttons.Select(x => x.Pressed).ToArray();
        float nextLog = 0f, start = T;
        while (T - start < maxSeconds)
        {
            var ia = new In();
            var ib = new In();
            if (!aDone) { if (a.MoveNext()) ia = a.Current; else { aDone = true; Mark("Tick plan done"); } }
            if (!bDone) { if (b.MoveNext()) ib = b.Current; else { bDone = true; Mark("Tock plan done"); } }
            if (aDone && bDone) break;
            Frame(ia, ib);

            bool dead = GameFlow.Instance.Respawning;
            if (dead && !wasDead)
            {
                Deaths++;
                Mark($"DEATH tick ({X(0):F2},{Feet(0):F2}) clear {Clear(0):F2}  tock ({X(1):F2},{Feet(1):F2}) clear {Clear(1):F2}");
                if (stopOnDeath) break;
            }
            wasDead = dead;
            if (GameFlow.Instance.CurrentCheckpoint != cp)
            {
                cp = GameFlow.Instance.CurrentCheckpoint;
                Mark($"checkpoint {cp}");
            }
            for (int i = 0; i < buttons.Length; i++)
                if (buttons[i].Pressed != pressed[i])
                {
                    pressed[i] = buttons[i].Pressed;
                    Mark($"{buttons[i].transform.parent.parent.name}/{buttons[i].name} {(pressed[i] ? "pressed" : "released")}");
                }
            if (T - start >= nextLog)
            {
                nextLog += logEvery;
                log.AppendLine($"{T,6:F2} tick ({X(0),6:F2},{Feet(0),5:F2}) s{Speed(0):F2} r{Rift(0):F2} c{Clear(0):F2} | tock ({X(1),6:F2},{Feet(1),5:F2}) s{Speed(1):F2} r{Rift(1):F2} c{Clear(1):F2}");
            }
        }
        log.AppendLine($"{T,6:F2} end tick ({X(0):F2},{Feet(0):F2}) tock ({X(1):F2},{Feet(1):F2}) deaths {Deaths}");
        return log.ToString();
    }

    // ---- Plan pieces ----

    public static IEnumerable<In> Seq(params IEnumerable<In>[] parts)
    {
        foreach (var p in parts)
            foreach (var i in p) yield return i;
    }

    public static IEnumerable<In> Do(Action action)
    {
        action();
        yield break;
    }

    public static IEnumerable<In> Say(string text) => Do(() => Mark(text));

    public static IEnumerable<In> Hold(float seconds, int dir = 0, bool jump = false)
    {
        for (float t = 0f; t < seconds - 0.001f; t += Dt) yield return new In(dir, jump);
    }

    public static IEnumerable<In> Until(Func<bool> done, int dir = 0, float timeout = 60f)
    {
        for (float t = 0f; !done() && t < timeout; t += Dt) yield return new In(dir);
    }

    // Runs to x and stops there.
    public static IEnumerable<In> RunTo(int lane, float x, float timeout = 20f)
    {
        int dir = Math.Sign(x - X(lane));
        for (float t = 0f; t < timeout && Math.Sign(x - X(lane)) == dir && dir != 0; t += Dt)
            yield return new In(dir);
    }

    // Runs back and forth between x0 and x1 until done (keeps the rift away).
    public static IEnumerable<In> Pace(int lane, float x0, float x1, Func<bool> done, float timeout = 60f)
    {
        int dir = X(lane) < (x0 + x1) / 2f ? 1 : -1;
        for (float t = 0f; t < timeout && !done(); t += Dt)
        {
            if (X(lane) >= x1) dir = -1;
            else if (X(lane) <= x0) dir = 1;
            yield return new In(dir);
        }
    }

    // Jump holding the key for hold seconds while running dir, then keep
    // running dir until landed (or for at most air seconds).
    public static IEnumerable<In> Jump(int lane, int dir, float hold = 0.5f, float air = 3f)
    {
        yield return new In(dir);   // key up first, so the press registers
        for (float t = 0f; t < hold; t += Dt) yield return new In(dir, true);
        for (float t = 0f; t < air; t += Dt)
        {
            if (t > 0.1f && Grounded(lane) && Body(lane).linearVelocity.y <= 0.01f) yield break;
            yield return new In(dir);
        }
    }
}
