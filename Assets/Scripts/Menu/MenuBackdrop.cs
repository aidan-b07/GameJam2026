using System;
using UnityEngine;

// The moving scenery behind the main menu: the level's far and mid backgrounds
// and a strip of ground tiles scrolling left at their own speeds (fast
// parallax, as if the camera were running right), Tick and Tock running on the
// ground, and a day that cycles through dusk and night on LaneSky.
//
// Positions snap to whole art pixels so the pixel art never shimmers. Each
// strip is drawn at least one wrap length wider than the view and jumps back
// by exactly one wrap length, where the art repeats, so the loop is seamless.
public class MenuBackdrop : MonoBehaviour
{
    [Serializable]
    public class Strip
    {
        public Transform transform;
        [Tooltip("Scroll speed in art pixels per second.")]
        public float speed;
        [Tooltip("Distance in units after which the art repeats (one picture or tile pattern wide).")]
        public float wrap = 20f;
        [NonSerialized] public float startX;
    }

    [Serializable]
    public class Runner
    {
        public SpriteRenderer renderer;
        public Sprite[] frames;
        [Tooltip("Frame offset, so the two players don't step in sync.")]
        public int phase;
    }

    public Strip[] strips;
    public Runner[] runners;
    [Tooltip("Run cycle frames per second.")]
    public float runFps = 12f;
    [Tooltip("Art pixels per world unit.")]
    public float pixelsPerUnit = 32f;
    [Tooltip("Multiplies all scrolling and running. Lower it for a calmer menu.")]
    [Range(0f, 1f)] public float motion = 1f;

    [Header("Day cycle")]
    public LaneSky sky;
    [Tooltip("Run the clock; off holds the sky at Start Hour.")]
    public bool cycle = true;
    [Range(0f, 24f)] public float startHour = 10f;   // Tick's lane starts at 10:00
    [Tooltip("Real seconds for a full 24 hours.")]
    public float secondsPerDay = 48f;

    float scroll;   // seconds of scrolling, scaled by motion
    float hour;

    void Awake()
    {
        if (strips != null)
            foreach (var s in strips)
                if (s.transform != null) s.startX = s.transform.localPosition.x;
        hour = startHour;
    }

    void Update()
    {
        float dt = Time.unscaledDeltaTime;
        scroll += dt * motion;

        if (strips != null)
            foreach (var s in strips)
            {
                if (s.transform == null || s.wrap <= 0f) continue;
                float offset = Mathf.Repeat(scroll * s.speed / pixelsPerUnit, s.wrap);
                var p = s.transform.localPosition;
                p.x = Snap(s.startX - offset);
                s.transform.localPosition = p;
            }

        if (runners != null)
        {
            int step = Mathf.FloorToInt(scroll * runFps);
            foreach (var r in runners)
                if (r.renderer != null && r.frames != null && r.frames.Length > 0)
                    r.renderer.sprite = r.frames[(step + r.phase) % r.frames.Length];
        }

        if (sky != null)
        {
            if (cycle) hour = Mathf.Repeat(hour + dt * 24f / Mathf.Max(1f, secondsPerDay), 24f);
            else hour = startHour;
            sky.Hour = hour;
        }
    }

    float Snap(float x) => Mathf.Round(x * pixelsPerUnit) / pixelsPerUnit;
}
