using UnityEngine;

// Night light of a decor piece (a street lamp's glow): an untinted overlay
// that fades in with the lane's night (dusk counts half), from the lane's
// LaneSky. LevelMap.Lane.Decor adds it when a `<name>_glow` sprite exists.
public class DecorGlow : MonoBehaviour
{
    public SpriteRenderer glow;

    private LaneSky sky;

    private void Start()
    {
        sky = transform.root.GetComponentInChildren<LaneSky>();
    }

    private void Update()
    {
        if (glow == null) return;
        float night = 1f;
        if (sky != null)
        {
            var (from, to, t) = LaneSky.Blend(sky.Hour);
            night = Weight(from) * (1f - t) + Weight(to) * t;
        }
        var c = glow.color;
        c.a = night;
        glow.color = c;
        glow.enabled = night > 0.01f;
    }

    private static float Weight(LaneSky.Look look) =>
        look == LaneSky.Look.Night ? 1f : look == LaneSky.Look.Dusk ? 0.5f : 0f;
}
