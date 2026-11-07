using UnityEngine;

// Twin checkpoint lamp (tools/make_checkpoint_twin.py): a post with a lantern
// per player, Tick's (orange) on the left and Tock's (teal) on the right. Both
// lanes' lamps of a checkpoint show the same state, read from GameFlow:
// - a lantern lights dim once its player has reached their lane's checkpoint
//   of this index (ignite, then the dim loop);
// - both flare bright once the checkpoint is current, i.e. both players have
//   reached it (brighten, then the bright loop).
// The flames flicker on the lamp's lane time (GameManager.GetWorldSpeed).
public class CheckpointLamp : MonoBehaviour
{
    public Checkpoint checkpoint;
    [Tooltip("Lantern overlays by player: 0 = Tick, 1 = Tock (GameFlow lanes).")]
    public SpriteRenderer[] heads = new SpriteRenderer[2];
    [Tooltip("19 frames each: 0 off, 1-5 ignite, 6-9 dim, 10-14 brighten, 15-18 bright.")]
    public Sprite[] tickFrames;
    public Sprite[] tockFrames;
    public float fps = 10f;

    enum State { Off, Dim, Bright }

    // Frame ranges (first, last) of the sheet's tags.
    const int Ignite = 1, Dim = 6, DimEnd = 9, Brighten = 10, Bright = 15, BrightEnd = 18;

    readonly State[] state = new State[2];
    readonly float[] time = new float[2];

    private void Start()
    {
        for (int i = 0; i < heads.Length; i++) Show(i, 0);
    }

    private void Update()
    {
        var flow = GameFlow.Instance;
        if (flow == null || checkpoint == null) return;
        float speed = GameManager.INSTANCE != null ? GameManager.INSTANCE.GetWorldSpeed(checkpoint.lane) : 1f;
        for (int i = 0; i < heads.Length; i++)
        {
            var target = flow.CurrentCheckpoint >= checkpoint.index ? State.Bright
                : flow.Reached(i) >= checkpoint.index ? State.Dim : State.Off;
            if (target != state[i])
            {
                state[i] = target;
                time[i] = 0f;
            }
            time[i] += Time.deltaTime * speed;
            Show(i, Frame(state[i], Mathf.FloorToInt(time[i] * fps)));
        }
    }

    // Play the intro once, then loop.
    int Frame(State s, int f)
    {
        switch (s)
        {
            case State.Dim: return Loop(f, Ignite, Dim, DimEnd);
            case State.Bright: return Loop(f, Brighten, Bright, BrightEnd);
            default: return 0;
        }
    }

    static int Loop(int f, int intro, int loop, int loopEnd)
    {
        int introLength = loop - intro;
        return f < introLength ? intro + f : loop + (f - introLength) % (loopEnd - loop + 1);
    }

    void Show(int head, int frame)
    {
        var frames = head == 0 ? tickFrames : tockFrames;
        if (heads[head] != null && frames != null && frame < frames.Length)
            heads[head].sprite = frames[frame];
    }
}
