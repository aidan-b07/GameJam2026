using UnityEngine;

// Twin checkpoint lamp (Victorian design, tools/make_checkpoint_victorian.py
// --no-clock --vivid): a cast-iron post with a lantern per player, Tick's (orange) on the left and Tock's (teal) on the right. Both
// lanes' lamps of a checkpoint show the same state, read from GameFlow:
// - a lantern lights dim once its player has reached their lane's checkpoint
//   of this index (ignite, then the dim loop);
// - both flare bright once the checkpoint is current, i.e. both players have
//   reached it (brighten, then the bright loop).
// The flames flicker on the lamp's lane time (GameManager.GetWorldSpeed).
// On a respawn SpawnWisp takes a lantern's flame (Douse: dark, the other
// lantern drops to its single-lit look) and gives it back (Relight: ignite,
// then brighten if the checkpoint is current), both in real time.
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
    readonly bool[] doused = new bool[2];
    // Relit: play the ignite frames, then run the next intro in real time.
    readonly bool[] igniting = new bool[2];
    readonly bool[] realTime = new bool[2];

    // Lantern flame centre in the 96x128 frame (pixels from the bottom left,
    // bottom-centre pivot): x 28 and 68, burner glow 49 rows from the top.
    static readonly Vector2[] FlamePixel = { new Vector2(28f, 79f), new Vector2(68f, 79f) };

    // World position of a lantern's flame (head: 0 = Tick, 1 = Tock).
    public Vector3 FlamePosition(int head)
    {
        var t = heads[head] != null ? heads[head].transform : transform;
        var p = (FlamePixel[head] - new Vector2(48f, 0f)) / 32f;
        return t.TransformPoint(new Vector3(p.x, p.y, 0f));
    }

    // Puts a lantern out (its flame has left as a wisp).
    public void Douse(int head)
    {
        if (head < 0 || head >= heads.Length) return;
        doused[head] = true;
        igniting[head] = false;
    }

    // Lights a lantern again: ignite frames, then its normal look.
    public void Relight(int head)
    {
        if (head < 0 || head >= heads.Length) return;
        doused[head] = false;
        igniting[head] = true;
        time[head] = 0f;
        // Both lanterns run their next intro (brighten) in step, in real time.
        for (int i = 0; i < realTime.Length; i++) realTime[i] = true;
    }

    public bool IsDoused(int head) => head >= 0 && head < doused.Length && doused[head];

    private void Start()
    {
        for (int i = 0; i < heads.Length; i++) Show(i, 0);
    }

    private void Update()
    {
        var flow = GameFlow.Instance;
        if (flow == null || checkpoint == null) return;
        float speed = GameManager.INSTANCE != null ? GameManager.INSTANCE.GetWorldSpeed(checkpoint.lane) : 1f;
        bool anyDoused = false, anyIgniting = false;
        foreach (var d in doused) anyDoused |= d;
        foreach (var g in igniting) anyIgniting |= g;
        for (int i = 0; i < heads.Length; i++)
        {
            if (igniting[i])
            {
                time[i] += Time.deltaTime;
                int f = Mathf.FloorToInt(time[i] * fps);
                if (f < Dim - Ignite)
                {
                    Show(i, Ignite + f);
                    continue;
                }
                // Ignited: carry on as a lit lantern past its intro.
                igniting[i] = false;
                state[i] = State.Dim;
                time[i] = (Dim - Ignite) / fps;
            }
            var target = doused[i] ? State.Off
                : flow.CurrentCheckpoint >= checkpoint.index && !anyDoused && !anyIgniting ? State.Bright
                : flow.Reached(i) >= checkpoint.index || flow.CurrentCheckpoint >= checkpoint.index ? State.Dim
                : State.Off;
            if (target != state[i])
            {
                // Bright to dim while the partner is doused: straight to the loop.
                bool skipIntro = target == State.Dim && state[i] == State.Bright;
                state[i] = target;
                time[i] = skipIntro ? (Dim - Ignite) / fps : 0f;
            }
            float intro = state[i] == State.Bright ? (Bright - Brighten) / fps : (Dim - Ignite) / fps;
            if (realTime[i] && !anyIgniting && time[i] >= intro) realTime[i] = false;
            time[i] += Time.deltaTime * (realTime[i] ? Mathf.Max(1f, speed) : speed);
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
