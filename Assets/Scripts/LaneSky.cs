using UnityEngine;
using UnityEngine.Tilemaps;

// Time-of-day look for one lane. Set Hour (0-24) and the lane's backgrounds
// crossfade between day, dusk and night, the sun and moon move along an arc
// above the lane camera, and the lane's tilemaps are tinted to match.
// Purely visual and a pure function of Hour; runs in edit mode too.
// In play mode Hour follows the lane's clock (GameManager.GetLaneHour).
[ExecuteAlways]
public class LaneSky : MonoBehaviour
{
    public enum Look { Day, Dusk, Night }

    [Range(0f, 24f)] public float hour = 10f;
    [Tooltip("Lane whose clock sets the hour in play mode (0 Tick, 1 Tock); -1 keeps hour as set.")]
    public int lane = -1;

    [System.Serializable]
    public class Layer
    {
        public SpriteRenderer day, dusk, night;
        public int sortingOrder;
        [Tooltip("Live hands for the tower clocks painted in this layer.")]
        public TowerClocks clocks;
        [Tooltip("Smoke from the chimneys painted in this layer.")]
        public ChimneySmoke smoke;
        [Tooltip("Pigeons, leaves, pennant, candle windows and fireflies on this layer.")]
        public MidLayerLife life;
        [Tooltip("A row of painted sections instead of one tiled picture (day, dusk and night left empty).")]
        public MidSections sections;
    }

    public Layer[] layers;
    public Tilemap[] tilemaps;
    [Tooltip("Sprites tinted like the tilemaps (obstacles drawn with sprites).")]
    public SpriteRenderer[] sprites;
    public SpriteRenderer sun, moon;
    [Tooltip("Drifting clouds, coloured for the look.")]
    public SkyClouds clouds;
    [Tooltip("Birds, airship, bats and stars.")]
    public SkyLife life;
    [Tooltip("Sun and moon are placed relative to this (the lane camera).")]
    public Transform skyAnchor;
    [Tooltip("Arc half-width, horizon height and peak height, relative to the anchor.")]
    public Vector3 arc = new Vector3(16f, -3.5f, 4f);

    // The ending draws its own sun: hide the sun and moon sprites.
    [System.NonSerialized] public bool hideBodies;

    public Color dayTint = Color.white;
    public Color duskTint = new Color(1f, 0.82f, 0.82f);
    public Color nightTint = new Color(0.72f, 0.72f, 0.9f);

    // (hour, look): the sky holds a look between equal neighbours and blends otherwise.
    static readonly (float h, Look look)[] Keys =
    {
        (0f, Look.Night), (4.5f, Look.Night), (6f, Look.Dusk), (7.5f, Look.Day),
        (16.5f, Look.Day), (18f, Look.Dusk), (19.5f, Look.Night), (24f, Look.Night),
    };

    public float Hour
    {
        get => hour;
        set { hour = Mathf.Repeat(value, 24f); Apply(); }
    }

    void OnValidate() => Apply();
    void LateUpdate()
    {
        if (Application.isPlaying && lane >= 0 && GameManager.INSTANCE != null)
            hour = GameManager.INSTANCE.GetLaneHour(lane);
        Apply();
    }

    // Which two looks are on screen at this hour, and how far from one to the other.
    public static (Look from, Look to, float t) Blend(float h)
    {
        h = Mathf.Repeat(h, 24f);
        for (int i = 0; i < Keys.Length - 1; i++)
        {
            var a = Keys[i];
            var b = Keys[i + 1];
            if (h <= b.h) return (a.look, b.look, Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(a.h, b.h, h)));
        }
        return (Look.Night, Look.Night, 0f);
    }

    public void Apply()
    {
        var (from, to, t) = Blend(hour);

        if (layers != null)
        {
            foreach (var layer in layers)
            {
                // The "from" look is opaque underneath; the "to" look fades in on top.
                foreach (var look in new[] { Look.Day, Look.Dusk, Look.Night })
                {
                    var sr = Get(layer, look);
                    if (sr == null) continue;
                    bool isFrom = look == from, isTo = look == to && to != from;
                    sr.enabled = isFrom || (isTo && t > 0f);
                    sr.sortingOrder = layer.sortingOrder + (isTo ? 1 : 0);
                    sr.color = new Color(1f, 1f, 1f, isTo ? t : 1f);
                }
                if (layer.clocks != null) layer.clocks.Show(hour, from, to, t);
                if (layer.smoke != null) layer.smoke.Show(from, to, t);
                if (layer.life != null) layer.life.Show(from, to, t);
                if (layer.sections != null) layer.sections.Show(hour, from, to, t);
            }
        }
        if (clouds != null) clouds.Show(from, to, t);
        if (life != null) life.Show(from, to, t);

        var tint = Color.Lerp(Tint(from), Tint(to), t);
        if (tilemaps != null)
            foreach (var tm in tilemaps)
                if (tm != null) tm.color = tint;
        if (sprites != null)
            foreach (var sr in sprites)
                if (sr != null) sr.color = tint;

        // Sun is up 06:00-18:00, moon 18:00-06:00; each crosses its arc in 12 hours.
        Place(sun, (hour - 6f) / 12f);
        Place(moon, Mathf.Repeat(hour - 18f, 24f) / 12f);
        if (hideBodies)
        {
            if (sun != null) sun.enabled = false;
            if (moon != null) moon.enabled = false;
        }
    }

    void Place(SpriteRenderer body, float s)
    {
        if (body == null) return;
        // Slightly past each end so the body sinks below the horizon instead of popping.
        body.enabled = s > -0.05f && s < 1.05f;
        if (!body.enabled) return;
        Vector3 origin = skyAnchor != null ? skyAnchor.position : transform.position;
        float x = Mathf.Lerp(-arc.x, arc.x, s);
        float y = arc.y + Mathf.Sin(Mathf.PI * Mathf.Clamp01(s)) * (arc.z - arc.y);
        body.transform.position = new Vector3(origin.x + x, origin.y + y, 0f);
    }

    Color Tint(Look look) => look == Look.Day ? dayTint : look == Look.Dusk ? duskTint : nightTint;

    static SpriteRenderer Get(Layer layer, Look look) =>
        look == Look.Day ? layer.day : look == Look.Dusk ? layer.dusk : layer.night;
}
