using UnityEngine;

// The clock door at the end of a lane (art from tools/make_clockdoor.py: the
// door sheet, a light overlay that skips the night tint, and two clock hands).
// Closed until the lane's player walks up to it (triggerX); then it unlocks
// (bolts slide back) and holds that lane's rift back, so the first to arrive
// can wait for the partner. DawnEnding opens both doors once both are
// unlocked. A death relocks it (GameFlow.LevelReset), like the buttons.
// The hands show the lane's hour (GameManager.GetLaneHour).
public class ClockDoor : MonoBehaviour
{
    public enum State { Closed, Unlocking, Unlocked, Opening, Open }

    [Tooltip("0 Tick, 1 Tock.")]
    public int lane;
    [Tooltip("World x a player's position must reach to unlock the door.")]
    public float triggerX;
    [Tooltip("World x where the player stops in front of the door in the ending.")]
    public float markX;

    public SpriteRenderer body, glow;
    public Sprite[] bodyFrames, lightFrames;
    public Transform hourHand, minuteHand;
    public float fps = 12f;

    // Frame ranges (first, count) per tag of the sheet.
    public Vector2Int closedTag = new Vector2Int(0, 4);
    public Vector2Int unlockTag = new Vector2Int(4, 8);
    public Vector2Int openTag = new Vector2Int(12, 12);
    public Vector2Int openedTag = new Vector2Int(24, 4);

    public State Current { get; private set; }
    public bool Reached => Current != State.Closed;

    float time;
    int lastUnlockFrame = -1;

    void OnEnable() => GameFlow.LevelReset += Relock;
    void OnDisable() => GameFlow.LevelReset -= Relock;

    void Relock()
    {
        if (Current == State.Opening || Current == State.Open) return;
        Current = State.Closed;
        time = 0f;
        if (LaneRift.Instance != null) LaneRift.Instance.SetSafe(lane, false);
    }

    public void Open()
    {
        if (Current == State.Opening || Current == State.Open) return;
        Current = State.Opening;
        time = 0f;
        Sfx.Play("bridge_slam", transform.position, 0.5f, false);
    }

    void Update()
    {
        var flow = GameFlow.Instance;
        if (Current == State.Closed && flow != null && !flow.Respawning && !flow.Finished
            && lane < flow.players.Length && flow.players[lane] != null && flow.players[lane].position.x >= triggerX)
        {
            Current = State.Unlocking;
            time = 0f;
            lastUnlockFrame = -1;
            if (LaneRift.Instance != null) LaneRift.Instance.SetSafe(lane, true);
            Sfx.Play("checkpoint", transform.position, 0.8f, false);
        }

        time += Time.deltaTime;
        int frame = (int)(time * fps);
        switch (Current)
        {
            case State.Closed: frame = closedTag.x + frame % closedTag.y; break;
            case State.Unlocking:
                // A click per bolt (every other frame of the slide).
                if (frame != lastUnlockFrame && frame % 2 == 1 && frame < unlockTag.y)
                    Sfx.Play("ratchet_click", transform.position, 0.6f, false);
                lastUnlockFrame = frame;
                if (frame >= unlockTag.y) { Current = State.Unlocked; frame = unlockTag.y - 1; }
                frame = unlockTag.x + frame;
                break;
            case State.Unlocked: frame = unlockTag.x + unlockTag.y - 1; break;
            case State.Opening:
                if (frame >= openTag.y) { Current = State.Open; time = 0f; frame = 0; goto case State.Open; }
                frame = openTag.x + frame;
                break;
            case State.Open: frame = openedTag.x + frame % openedTag.y; break;
        }
        if (body != null && bodyFrames != null && frame < bodyFrames.Length) body.sprite = bodyFrames[frame];
        if (glow != null && lightFrames != null && frame < lightFrames.Length) glow.sprite = lightFrames[frame];

        float hour = GameManager.INSTANCE != null ? GameManager.INSTANCE.GetLaneHour(lane) : 12f;
        if (hourHand != null) hourHand.localRotation = Quaternion.Euler(0f, 0f, -Mathf.Repeat(hour, 12f) / 12f * 360f);
        if (minuteHand != null) minuteHand.localRotation = Quaternion.Euler(0f, 0f, -Mathf.Repeat(hour, 1f) * 360f);
    }
}
