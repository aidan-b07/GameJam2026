using UnityEngine;

// Steam vent: a two-tile floor grate that idles (warm glow), warns (bars
// rattle, wisps) and then blasts a 4-tile column of steam that lifts players
// standing in it: up and over a wall too high to jump. The cycle is a pure
// function of lane time, so a fast lane (partner running) gives frequent short
// bursts and a slow one long waits. The lift itself is a push in real time
// (steam is steam): upward acceleration `lift` on top of gravity, up to
// `maxRise`, while the player's feet are in the column.
// Art from tools/make_steamvent.py: the grate (8 frames, 10 fps, idle 0-3,
// warn 4-7) over the ground's top row, which keeps its collider, and the
// steam column (16 frames, 12 fps, start 0-3, loop 4-11, stop 12-15).
// LevelMap.Lane.SteamVent builds it.
public class SteamVent : LaneObstacle
{
    [Tooltip("Lane seconds idle, warning and blowing (the loop, between start and stop).")]
    public float idleTime = 2.2f, warnTime = 0.9f, blowTime = 1.6f;
    [Tooltip("Upward acceleration in the column (units/s^2; gravity on players is 29.4).")]
    public float lift = 75f;
    [Tooltip("Fastest the steam pushes a player up (units/s).")]
    public float maxRise = 9f;
    [Tooltip("World x of the column's centre, y of its bottom, its width and height.")]
    public float columnX, columnBottom, columnWidth = 1.25f, columnHeight = 4f;

    [Header("Art")]
    public SpriteRenderer grate;
    public Sprite[] grateFrames;
    public SpriteRenderer steam;
    public Sprite[] steamFrames;

    const float GrateFps = 10f, SteamFps = 12f;
    const float EdgeTime = 4f / SteamFps;   // start and stop clips

    float Cycle => idleTime + warnTime + EdgeTime + blowTime + EdgeTime;

    // 0 idle, 1 warn, 2 start, 3 blow, 4 stop.
    int Phase(float t, out float into)
    {
        into = Mathf.Repeat(t, Cycle);
        float[] spans = { idleTime, warnTime, EdgeTime, blowTime, EdgeTime };
        for (int i = 0; i < spans.Length; i++)
        {
            if (into < spans[i]) return i;
            into -= spans[i];
        }
        return 4;
    }

    int lastPhase = -1;

    protected override void Step(float dt)
    {
        int phase = Phase(T, out float into);
        if (lastPhase >= 0 && phase != lastPhase)
        {
            var at = new Vector3(columnX, columnBottom + 0.5f, 0f);
            if (phase == 1) Sfx.Play("plunger_warn", at, 0.4f, lane: lane);
            else if (phase == 2) Sfx.Play("steam_hiss", at, 0.8f, lane: lane);
        }
        lastPhase = phase;

        // Lifts from the second half of the start clip until the stop clip begins.
        bool blowing = phase == 3 || (phase == 2 && into > EdgeTime / 2f);
        if (!blowing) return;
        foreach (var p in Players)
        {
            if (!Live(p, out var rb, out var box)) continue;
            if (Mathf.Abs(box.center.x - columnX) > (columnWidth + box.size.x) / 2f - 0.2f) continue;
            if (box.min.y < columnBottom - 0.3f || box.min.y > columnBottom + columnHeight) continue;
            var v = rb.linearVelocity;
            if (v.y < maxRise) rb.linearVelocity = new Vector2(v.x, Mathf.Min(maxRise, v.y + lift * dt));
        }
    }

    void LateUpdate()
    {
        float t = DrawT;
        int phase = Phase(t, out float into);
        Show(grate, grateFrames, phase == 1 ? Loop(into, GrateFps, 4, 4) : Loop(t, GrateFps, 0, 4));
        if (steam == null) return;
        steam.enabled = phase >= 2;
        if (phase == 2) Show(steam, steamFrames, Mathf.Min(3, (int)(into * SteamFps)));
        else if (phase == 3) Show(steam, steamFrames, Loop(into, SteamFps, 4, 8));
        else if (phase == 4) Show(steam, steamFrames, 12 + Mathf.Min(3, (int)(into * SteamFps)));
    }
}
