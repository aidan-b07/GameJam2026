using UnityEngine;

// Moves a background layer along with the camera by a fraction of the camera's
// own movement, so distant layers drift slowly behind the level and the scroll
// reads as depth. depth 0 stays fixed in the world (like the ground), 1 moves
// with the camera (infinitely far, like the sun and moon). Horizontal only:
// the lane cameras never move vertically.
//
// The layer sits where the level builder put it while the camera is at the X
// it had when the scene loaded, so the scene looks the same in the editor.
[DefaultExecutionOrder(-40)]
public class Parallax : MonoBehaviour
{
    public Transform cameraTransform;
    [Range(0f, 1f)] public float depth = 0.5f;
    [Tooltip("Snap to whole art pixels so pixel art does not shimmer. 0 turns it off.")]
    public float pixelsPerUnit = 32f;

    float originX, cameraOriginX;

    void Awake()
    {
        originX = transform.position.x;
        if (cameraTransform != null) cameraOriginX = cameraTransform.position.x;
    }

    void LateUpdate()
    {
        if (cameraTransform == null) return;
        // Snap the offset relative to the snapped camera, so the layer moves in
        // whole pixels on screen.
        float cam = cameraTransform.position.x;
        float shift = (cam - cameraOriginX) * depth;
        if (pixelsPerUnit > 0f) shift = Mathf.Round(shift * pixelsPerUnit) / pixelsPerUnit;
        var p = transform.position;
        transform.position = new Vector3(originX + shift, p.y, p.z);
    }
}
