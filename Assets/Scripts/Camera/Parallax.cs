using UnityEngine;

// Moves a background layer along with the camera by a fraction of the camera's
// own movement, so distant layers drift slowly behind the level and the scroll
// reads as depth. depth 0 stays fixed in the world (like the ground), 1 moves
// with the camera (infinitely far, like the sun and moon). Vertically the layer
// moves by verticalDepth of the camera's rise (CoopCamera lifts a lane camera
// when its player is flung high); the pictures are exactly as tall as the
// view, so anything below 1 would show their top edge.
//
// The layer sits where the level builder put it while the camera is where it
// was when the scene loaded, so the scene looks the same in the editor.
[DefaultExecutionOrder(-40)]
public class Parallax : MonoBehaviour
{
    public Transform cameraTransform;
    [Range(0f, 1f)] public float depth = 0.5f;
    [Range(0f, 1f)] public float verticalDepth = 1f;
    [Tooltip("Snap to whole art pixels so pixel art does not shimmer. 0 turns it off.")]
    public float pixelsPerUnit = 32f;

    float originX, originY, cameraOriginX, cameraOriginY;

    void Awake()
    {
        originX = transform.position.x;
        originY = transform.position.y;
        if (cameraTransform != null)
        {
            cameraOriginX = cameraTransform.position.x;
            cameraOriginY = cameraTransform.position.y;
        }
    }

    void LateUpdate()
    {
        if (cameraTransform == null) return;
        // Snap the offset relative to the snapped camera, so the layer moves in
        // whole pixels on screen.
        var cam = cameraTransform.position;
        float shift = (cam.x - cameraOriginX) * depth;
        float lift = (cam.y - cameraOriginY) * verticalDepth;
        if (pixelsPerUnit > 0f)
        {
            shift = Mathf.Round(shift * pixelsPerUnit) / pixelsPerUnit;
            lift = Mathf.Round(lift * pixelsPerUnit) / pixelsPerUnit;
        }
        transform.position = new Vector3(originX + shift, originY + lift, transform.position.z);
    }
}
