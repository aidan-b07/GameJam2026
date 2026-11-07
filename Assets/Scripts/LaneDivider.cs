using UnityEngine;
using UnityEngine.UI;

// The Unraveling crack drawn across the screen between Tick's lane (top) and
// Tock's (bottom). Lives on a screen-space overlay canvas, so it stays on the
// seam between the two camera viewports. Keeps both textures tiled at their
// pixel size and slowly drifts the sparkles along the crack.
[ExecuteAlways]
public class LaneDivider : MonoBehaviour
{
    public RawImage crack;
    public RawImage sparkles;
    [Tooltip("Sparkle drift in texture widths per second (real time).")]
    public float drift = 0.02f;

    void Update()
    {
        Tile(crack, 0f);
        Tile(sparkles, Application.isPlaying ? Time.unscaledTime * drift : 0f);
    }

    // One texture pixel per canvas unit horizontally, so the art keeps its scale.
    static void Tile(RawImage image, float offset)
    {
        if (image == null || image.texture == null) return;
        float repeats = image.rectTransform.rect.width / image.texture.width;
        image.uvRect = new Rect(offset, 0f, repeats, 1f);
    }
}
