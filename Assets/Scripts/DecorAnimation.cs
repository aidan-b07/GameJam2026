using UnityEngine;

// Looping animated decor (the scenery landmarks from tools/make_landmarks.py:
// Tick's fountain, Tock's orrery). Plays its frames on lane time, so the
// piece slows with its lane and races when the partner runs. An optional
// glow overlay (night light, faded by DecorGlow) shows the matching frame.
// LevelMap.Lane.Landmark builds it.
public class DecorAnimation : MonoBehaviour
{
    public SpriteRenderer body;
    public Sprite[] frames;
    public SpriteRenderer glow;
    public Sprite[] glowFrames;
    public float fps = 12f;
    public int lane;

    private float laneTime;
    private int shown = -1;

    private void Update()
    {
        if (frames == null || frames.Length == 0 || body == null) return;
        float speed = GameManager.INSTANCE != null ? GameManager.INSTANCE.GetWorldSpeed(lane) : 1f;
        laneTime += Time.deltaTime * speed;
        int f = (int)(laneTime * fps) % frames.Length;
        if (f == shown) return;
        shown = f;
        body.sprite = frames[f];
        if (glow != null && glowFrames != null && f < glowFrames.Length) glow.sprite = glowFrames[f];
    }
}
