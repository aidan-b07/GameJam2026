using UnityEngine;

// The mid background as a row of painted sections (tools/make_bg_parts.py)
// instead of one repeating picture. Each section has a still per look (day,
// dusk, night) and moving parts drawn from frame sheets: gears and wheels
// turn, pendulum and bell swing, a rose window pulses, lamps flicker, hoists
// lift their loads and a train runs along its bridge. All of it runs on the
// lane's time, so a slow lane moves slowly. LaneSky.Apply passes the hour and
// the look blend (Show): the "from" look is opaque, the "to" look fades in on
// top, for the stills and every part alike. Parts sit on sortingOrder
// partOrder and are layered by z (closer to the camera draws on top).
// Built by the level builder (MidSectionsBuilder); pixels are art pixels,
// 32 per unit, section y from the top.
[ExecuteAlways]
[DefaultExecutionOrder(-20)]
public class MidSections : MonoBehaviour
{
    public enum Kind { Spin, Swing, Flicker, Front, Pulley, Crate, Train }

    [System.Serializable]
    public class Part
    {
        public string id;
        public Kind kind;
        [Tooltip("Renderer for the look being faded from (opaque) and to (alpha).")]
        public SpriteRenderer a, b;
        [Tooltip("Day frames, then dusk, then night.")]
        public Sprite[] frames;
        [Tooltip("Pixel box of frame 0 in the section, from its top-left.")]
        public RectInt box;
        [Tooltip("Cycles per lane second (spin, swing).")]
        public float rate;
        [Tooltip("Random start in the cycle, 0-1.")]
        public float phase;
        // Crate
        public float travel, period;
        public int ropeX, ropeTop;
        public SpriteRenderer rope;
        public Color[] ropeColours;
        // Pulley: the crate part it turns with.
        public int hoist = -1;
        public float radius;
        public int spokes = 4;
        // Train
        public int track0, track1;
        public float speed;
        public Vector2 mouth;
        [Tooltip("Index of the train's chimney in the section's ChimneySmoke.")]
        public int smokeIndex = -1;
    }

    [System.Serializable]
    public class Section
    {
        public string name;
        public Transform root;
        public int width = 592;
        public SpriteRenderer day, dusk, night;
        public Part[] parts;
        public TowerClocks clocks;
        public ChimneySmoke smoke;
    }

    public Section[] sections;
    [Tooltip("Lane whose clock runs the parts in play mode (0 Tick, 1 Tock).")]
    public int lane = -1;
    [Tooltip("Lane camera; parts of sections out of its view are not updated.")]
    public Transform cameraTransform;
    public int stillOrder = -10;
    public int partOrder = -9;
    [Tooltip("Lane time shown outside play mode.")]
    public float previewTime = 3f;
    public const float PixelsPerUnit = 32f;
    public const int Height = 360;

    LaneSky.Look from = LaneSky.Look.Dusk, to = LaneSky.Look.Dusk;
    float blend;
    float hour = -1f;

    public void Show(float hour, LaneSky.Look from, LaneSky.Look to, float t)
    {
        this.hour = hour;
        bool changed = from != this.from || to != this.to || !Mathf.Approximately(t, blend);
        this.from = from;
        this.to = to;
        blend = t;
        if (sections == null) return;
        foreach (var s in sections)
        {
            if (s.clocks != null) s.clocks.Show(hour, from, to, t);
            if (s.smoke != null) s.smoke.Show(from, to, t);
            if (!changed) continue;
            foreach (var look in new[] { LaneSky.Look.Day, LaneSky.Look.Dusk, LaneSky.Look.Night })
            {
                var sr = look == LaneSky.Look.Day ? s.day : look == LaneSky.Look.Dusk ? s.dusk : s.night;
                if (sr == null) continue;
                bool isFrom = look == from, isTo = look == to && to != from;
                sr.enabled = isFrom || (isTo && t > 0f);
                sr.sortingOrder = stillOrder + (isTo ? 1 : 0);
                sr.color = new Color(1f, 1f, 1f, isTo ? t : 1f);
            }
        }
        if (changed) UpdateParts(true);
    }

    void LateUpdate() => UpdateParts(false);

    float LaneTime()
    {
        if (!Application.isPlaying) return previewTime;
        return lane >= 0 && GameManager.INSTANCE != null ? GameManager.INSTANCE.GetLaneTime(lane) : Time.time;
    }

    void UpdateParts(bool all)
    {
        if (sections == null) return;
        float t = LaneTime();
        float camLocal = cameraTransform != null ? transform.InverseTransformPoint(cameraTransform.position).x : float.NaN;
        foreach (var s in sections)
        {
            if (s.parts == null || s.root == null) continue;
            float left = s.root.localPosition.x, w = s.width / PixelsPerUnit;
            // Only sections in (or next to) the lane camera's view.
            if (!all && !float.IsNaN(camLocal) && (camLocal < left - 24f || camLocal > left + w + 24f)) continue;
            foreach (var p in s.parts) UpdatePart(s, p, t);
        }
    }

    static float Frac(float x) => x - Mathf.Floor(x);

    static Vector3 Local(float px, float py, float z) => new Vector3(px / PixelsPerUnit, (Height - py) / PixelsPerUnit, z);

    void UpdatePart(Section s, Part p, float t)
    {
        if (p.a == null || p.frames == null || p.frames.Length < 3) return;
        int n = p.frames.Length / 3;
        int k = 0;
        bool on = true;
        float dx = 0f, dy = 0f;
        switch (p.kind)
        {
            case Kind.Spin:
            case Kind.Swing:
                k = Mathf.FloorToInt(Frac(t * p.rate + p.phase) * n) % n;
                break;
            case Kind.Flicker:
            {
                // A gas lamp: steady most of the time, sputtering in short bursts.
                bool burst = Mathf.Sin(t * 0.9f + p.phase * 40f) > 0.55f;
                on = burst && Hash(Mathf.FloorToInt(t * 9f), Mathf.FloorToInt(p.phase * 1000f)) < 0.45f;
                break;
            }
            case Kind.Pulley:
            {
                var crate = p.hoist >= 0 && p.hoist < s.parts.Length ? s.parts[p.hoist] : null;
                float rise = crate != null ? CrateRise(crate, t) : 0f;
                float step = 2f * Mathf.PI / Mathf.Max(1, p.spokes);
                k = Mathf.FloorToInt(Frac(rise / Mathf.Max(1f, p.radius) / step) * n) % n;
                break;
            }
            case Kind.Crate:
                dy = -CrateRise(p, t);
                break;
            case Kind.Train:
            {
                float span = p.track1 - p.track0 + p.box.width;
                float x = p.track0 - p.box.width + Mathf.Floor(Frac(t * p.speed / span + p.phase) * span);
                dx = x - p.box.x;
                if (s.smoke != null && p.smokeIndex >= 0)
                {
                    float mx = x + p.mouth.x;
                    s.smoke.SetMouth(p.smokeIndex, new Vector2(mx, p.mouth.y), mx > p.track0 + 3 && mx < p.track1 - 3);
                }
                break;
            }
        }
        var pos = Local(p.box.x + dx, p.box.y + p.box.height + dy, 0f);
        Set(p.a, p.frames[(int)from * n + k], on, pos, p.a.transform.localPosition.z, 1f);
        if (p.b != null)
            Set(p.b, p.frames[(int)to * n + k], on && to != from && blend > 0f, pos, p.b.transform.localPosition.z, blend);
        if (p.kind == Kind.Crate && p.rope != null)
        {
            // From under the pulley down to the load's top.
            float len = p.box.y + dy - p.ropeTop;
            p.rope.enabled = len > 0f;
            p.rope.transform.localPosition = Local(p.ropeX + 0.5f, p.box.y + dy - len / 2f, p.rope.transform.localPosition.z);
            var size = p.rope.sprite != null ? (Vector2)p.rope.sprite.bounds.size : Vector2.one;
            p.rope.transform.localScale = new Vector3(1f / PixelsPerUnit / size.x, Mathf.Max(0f, len) / PixelsPerUnit / size.y, 1f);
            if (p.ropeColours != null && p.ropeColours.Length == 3)
                p.rope.color = Color.Lerp(p.ropeColours[(int)from], p.ropeColours[(int)to], blend);
        }
    }

    // Pixels the load is above its painted spot: up and back down over its period, easing at both ends.
    static float CrateRise(Part p, float t) =>
        Mathf.Round((0.5f - 0.5f * Mathf.Cos(2f * Mathf.PI * (t / Mathf.Max(0.1f, p.period) + p.phase))) * p.travel);

    static void Set(SpriteRenderer sr, Sprite sprite, bool on, Vector3 pos, float z, float alpha)
    {
        sr.enabled = on;
        if (!on) return;
        if (sr.sprite != sprite) sr.sprite = sprite;
        pos.z = z;
        if (sr.transform.localPosition != pos) sr.transform.localPosition = pos;
        var c = new Color(1f, 1f, 1f, alpha);
        if (sr.color != c) sr.color = c;
    }

    static float Hash(int a, int b)
    {
        unchecked
        {
            uint h = (uint)a * 374761393u ^ (uint)b * 668265263u;
            h = (h ^ (h >> 13)) * 1274126177u;
            return (h ^ (h >> 16)) / 4294967296f;
        }
    }
}
