using UnityEngine;

// Shared side-scrolling camera for both lanes. Tick's and Tock's cameras keep
// their own height but always share one X, so the two halves of the screen
// scroll together and both players have to make progress.
//
// Classic platformer follow: the camera stays put while the players' midpoint
// moves inside a dead zone, then glides after it, centred a little ahead
// (forwardBias) so more of the level ahead is on screen. It never scrolls so
// far that a player would drop off the opposite edge: a player can only drag
// the screen forward as far as the trailing partner allows. ScreenLeash keeps
// each player inside the view with a springy band at the edges.
//
// Runs before LaneSky and Parallax, so the sun, moon and backgrounds use this
// frame's camera position.
[DefaultExecutionOrder(-50)]
public class CoopCamera : MonoBehaviour
{
    [Tooltip("Lane cameras (Tick, Tock). All share the X this rig computes.")]
    public Camera[] cameras;
    [Tooltip("Players to keep on screen (Tick, Tock). Missing or inactive ones are ignored.")]
    public Transform[] players;

    [Header("Follow")]
    [Tooltip("Half-width of the dead zone (units) around the focus point; the camera holds still while the players' midpoint stays inside it.")]
    public float deadZone = 2.5f;
    [Tooltip("The camera sits this far ahead (right) of the players' midpoint.")]
    public float forwardBias = 3f;
    [Tooltip("Time (s) the camera takes to catch up with its target.")]
    public float smoothTime = 0.25f;
    public float maxSpeed = 40f;

    [Header("Edges")]
    [Tooltip("Distance (units) from the screen edge to where the elastic band starts.")]
    public float edgeMargin = 2f;
    [Tooltip("Width (units) of the elastic band a player can push into past that point.")]
    public float elasticWidth = 1.25f;

    [Header("Level")]
    [Tooltip("World X of the level's left and right ends; the view never shows past them.")]
    public float levelLeft = -2f;
    public float levelRight = 202f;
    [Tooltip("Snap the camera to whole art pixels so pixel art does not shimmer. 0 turns it off.")]
    public float pixelsPerUnit = 32f;

    float x, velocity;

    public float X => x;

    // Half the width of the view in world units (every lane camera has the same).
    public float HalfWidth
    {
        get
        {
            var cam = cameras != null && cameras.Length > 0 ? cameras[0] : null;
            return cam != null ? cam.orthographicSize * cam.aspect : 10f;
        }
    }

    // Where the elastic band starts on each side of the current view.
    public float SoftLeft => x - HalfWidth + edgeMargin;
    public float SoftRight => x + HalfWidth - edgeMargin;

    void Start()
    {
        x = cameras != null && cameras.Length > 0 && cameras[0] != null ? cameras[0].transform.position.x : 0f;
        x = Target(x);
        velocity = 0f;
        Apply();
    }

    void LateUpdate()
    {
        if (panning)
        {
            panTime += Time.deltaTime;
            float u = Mathf.Clamp01(panTime / panDuration);
            x = Mathf.Lerp(panFrom, panTo, u * u * (3f - 2f * u));
            if (u >= 1f) panning = false;
        }
        else
        {
            float target = Target(x);
            x = Mathf.SmoothDamp(x, target, ref velocity, smoothTime, maxSpeed, Time.deltaTime);
        }
        Apply();
    }

    float panFrom, panTo, panTime, panDuration;
    bool panning;

    // Eases the view (no follow, no dead zone) to where it would settle around
    // the players' current positions, for example after a respawn moved them.
    // Takes distance / speed seconds, clamped to [minTime, maxTime]; returns it.
    public float PanToPlayers(float speed, float minTime, float maxTime)
    {
        panFrom = x;
        panTo = Target(Focus(x));
        panDuration = Mathf.Clamp(Mathf.Abs(panTo - panFrom) / Mathf.Max(speed, 0.01f), minTime, maxTime);
        panTime = 0f;
        velocity = 0f;
        panning = panDuration > 0f;
        return panDuration;
    }

    // Players' midpoint shifted forward (current when there are no players).
    float Focus(float current)
    {
        float min = float.PositiveInfinity, max = float.NegativeInfinity;
        if (players != null)
            foreach (var p in players)
            {
                if (p == null || !p.gameObject.activeInHierarchy) continue;
                min = Mathf.Min(min, p.position.x);
                max = Mathf.Max(max, p.position.x);
            }
        return min <= max ? (min + max) / 2f + forwardBias : current;
    }

    // Where the camera wants to be, given where it is now.
    float Target(float current)
    {
        float min = float.PositiveInfinity, max = float.NegativeInfinity;
        if (players != null)
            foreach (var p in players)
            {
                if (p == null || !p.gameObject.activeInHierarchy) continue;
                min = Mathf.Min(min, p.position.x);
                max = Mathf.Max(max, p.position.x);
            }

        float target = current;
        if (min <= max)
        {
            // Dead zone around the focus point (players' midpoint, shifted forward).
            float focus = (min + max) / 2f + forwardBias;
            if (focus > current + deadZone) target = focus - deadZone;
            else if (focus < current - deadZone) target = focus + deadZone;

            // Keep every player inside the soft edges: the trailing player caps how
            // far the camera may scroll forward, the leader how far back.
            float reach = HalfWidth - edgeMargin;
            float hi = min + reach, lo = max - reach;
            // Both pushing outwards: split the overflow so each sits equally deep in its band.
            target = lo > hi ? (lo + hi) / 2f : Mathf.Clamp(target, lo, hi);
        }

        float half = HalfWidth;
        float left = levelLeft + half, right = levelRight - half;
        return left <= right ? Mathf.Clamp(target, left, right) : (levelLeft + levelRight) / 2f;
    }

    void Apply()
    {
        float shown = pixelsPerUnit > 0f ? Mathf.Round(x * pixelsPerUnit) / pixelsPerUnit : x;
        if (cameras == null) return;
        foreach (var cam in cameras)
        {
            if (cam == null) continue;
            var p = cam.transform.position;
            cam.transform.position = new Vector3(shown, p.y, p.z);
        }
    }
}
