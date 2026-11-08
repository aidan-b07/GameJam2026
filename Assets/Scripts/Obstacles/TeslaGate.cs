using UnityEngine;

// Tesla gate: two coil pylons that cycle idle, charge (sparks crawl up the
// coils, lamps blink amber) and arc (a bolt across the gap at chest height).
// The phase is a pure function of lane time. The arc kills both players on
// touch, so the gap is only passable while the gate is off; a slow lane
// (partner standing) stretches that window to seconds, a fast one leaves
// almost none. The pylons are scenery (players walk past in front of them).
// Art from tools/make_teslagate.py: per pylon an fx and a lamp overlay (12
// frames at 12 fps, idle 0-1, charge 2-5, arc 6-11) and a row of arc segments
// (6 frames at 18 fps). LevelMap.Lane.TeslaGate builds it.
public class TeslaGate : LaneObstacle
{
    [Tooltip("Lane seconds off, charging and arcing.")]
    public float idleTime = 1.6f, chargeTime = 1f, arcTime = 1.2f;
    [Tooltip("World x span and y of the arc's centre line.")]
    public float arcLeft, arcRight, arcY;
    [Tooltip("Half height of the arc's kill band (units).")]
    public float arcHalfHeight = 0.22f;

    [Header("Art")]
    public SpriteRenderer[] fx;
    public SpriteRenderer[] lamps;
    public Sprite[] fxFrames, lampFrames;
    public SpriteRenderer[] arcSegments;
    public Sprite[] arcFrames;

    const float Fps = 12f, ArcFps = 18f;

    float Cycle => idleTime + chargeTime + arcTime;

    // 0 idle, 1 charging, 2 arcing; `into` is lane seconds into that phase.
    int Phase(float t, out float into)
    {
        into = Mathf.Repeat(t, Cycle);
        if (into < idleTime) return 0;
        into -= idleTime;
        if (into < chargeTime) return 1;
        into -= chargeTime;
        return 2;
    }

    int lastPhase = -1;

    protected override void Step(float dt)
    {
        int phase = Phase(T, out _);
        if (lastPhase >= 0 && phase != lastPhase)
        {
            var at = new Vector3((arcLeft + arcRight) / 2f, arcY, 0f);
            if (phase == 1) Sfx.Play("tesla_charge", at, 0.6f, lane: lane);
            else if (phase == 2) Sfx.Play("tesla_zap", at, 0.8f, lane: lane);
        }
        lastPhase = phase;
        if (phase != 2) return;

        foreach (var p in Players)
        {
            if (!Live(p, out _, out var box)) continue;
            if (box.max.x > arcLeft && box.min.x < arcRight
                && box.max.y > arcY - arcHalfHeight && box.min.y < arcY + arcHalfHeight)
            {
                KillAll();
                return;
            }
        }
    }

    void LateUpdate()
    {
        float t = DrawT;
        int phase = Phase(t, out float into);
        int frame = phase == 0 ? Loop(into, Fps, 0, 2) : phase == 1 ? Loop(into, Fps, 2, 4) : Loop(into, Fps, 6, 6);
        foreach (var sr in fx) Show(sr, fxFrames, frame);
        foreach (var sr in lamps) Show(sr, lampFrames, frame);
        if (arcSegments == null) return;
        for (int i = 0; i < arcSegments.Length; i++)
        {
            var sr = arcSegments[i];
            if (sr == null) continue;
            sr.enabled = phase == 2;
            // Neighbouring segments start at different frames so the bolt does not repeat.
            if (phase == 2) Show(sr, arcFrames, (int)Mathf.Repeat(Mathf.Floor(into * ArcFps) + i * 4 + i * i, 6f));
        }
    }
}
