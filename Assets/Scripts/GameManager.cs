using UnityEngine;

public class GameManager : MonoBehaviour
{
    public static GameManager INSTANCE;

    [Tooltip("Lane speed while the partner stands still: slow, but not frozen. 0 freezes the lane.")]
    [SerializeField] private float idleWorldSpeed = 0.35f;
    [Tooltip("Lane speed while the partner keeps moving.")]
    [SerializeField] private float maxWorldSpeed = 3f;

    [Header("Ramp Up (while the partner is moving)")]
    [Tooltip("Seconds of continuous movement from idle to full speed. The ramp is an S-curve: a short step barely changes the lane, steady running builds it up.")]
    [SerializeField] private float rampDuration = 1.5f;

    [Header("Wind Down (after the partner stops)")]
    [Tooltip("Seconds the lane keeps its speed after the partner stops, so short pauses don't stutter it.")]
    [SerializeField] private float holdTime = 0.25f;
    [Tooltip("Seconds from full speed to idle, on a smooth curve that lands exactly on idle.")]
    [SerializeField] private float windDownDuration = 1.2f;

    [Tooltip("Normalised player speed above this counts as moving.")]
    [SerializeField] private float movingThreshold = 0.1f;

    [SerializeField]
    public float player1WorldSpeedModifier = 1f;
    [SerializeField]
    public float player2WorldSpeedModifier = 1f;

    [Header("Lane clocks")]
    [Tooltip("Hour each lane's clock shows at the start: Tick (lane 0), Tock (lane 1).")]
    public float[] startHours = { 10f, 2f };
    [Tooltip("Clock minutes that pass per second of lane time.")]
    public float minutesPerSecond = 4f;

    // Seconds of lane time: real time scaled by the lane's world speed.
    // Not readonly, so a script reload in play mode keeps them.
    float[] laneTime = new float[2];

    // Per lane: latest normalised speed reported, the current boost (0..1)
    // and the hold time left before the wind-down starts.
    private float[] laneInput = new float[2];
    private float[] laneBoost = new float[2];
    private float[] laneHold = new float[2];

    // OnEnable, not Awake: the project enters play mode without a domain
    // reload, and a script compile during play reloads the domain, which
    // clears INSTANCE but only calls OnEnable again.
    void OnEnable() {
        if (INSTANCE != null && INSTANCE != this) {
            Destroy(INSTANCE.gameObject);
        }

        INSTANCE = this;
    }

    void OnDisable() {
        if (INSTANCE == this) {
            INSTANCE = null;
        }
    }

    // Scripted sequences (the ending) take the lane clocks over: Update stops
    // driving them, and SetLane sets each lane's clock and world speed.
    public bool Scripted { get; set; }

    void Update() {
        if (Scripted)
            return;
        float dt = Time.deltaTime;
        player1WorldSpeedModifier = Mathf.Lerp(idleWorldSpeed, maxWorldSpeed, Step(0, dt));
        player2WorldSpeedModifier = Mathf.Lerp(idleWorldSpeed, maxWorldSpeed, Step(1, dt));
        for (int lane = 0; lane < laneTime.Length; lane++) {
            laneTime[lane] += dt * GetWorldSpeed(lane);
        }
    }

    public float GetLaneTime(int lane) => laneTime[lane];

    // Hours on the lane's clock since midnight of the first day (not wrapped at 24).
    public float GetLaneHoursTotal(int lane) => startHours[lane] + laneTime[lane] * minutesPerSecond / 60f;

    // Sets the lane's clock to hoursTotal (as GetLaneHoursTotal; keep it rising,
    // lane time never runs backwards) and its world speed, which turns the
    // players' keys. For Scripted sequences.
    public void SetLane(int lane, float hoursTotal, float worldSpeed) {
        laneTime[lane] = Mathf.Max(laneTime[lane], (hoursTotal - startHours[lane]) * 60f / minutesPerSecond);
        if (lane == 0) player1WorldSpeedModifier = worldSpeed;
        else player2WorldSpeedModifier = worldSpeed;
    }

    // Time of day (0-24) on the lane's clock; drives its sky and tower clocks.
    public float GetLaneHour(int lane) =>
        Mathf.Repeat(startHours[lane] + laneTime[lane] * minutesPerSecond / 60f, 24f);

    // Player 0 drives lane 1 and player 1 drives lane 0, same as before.
    public void UpdateWorldSpeed(int player, float speedNormalised) {
        switch (player) {
            case 0: laneInput[1] = speedNormalised; break;
            case 1: laneInput[0] = speedNormalised; break;
            default: Debug.LogError("Invalid player");
                break;
        }
    }

    public float IdleWorldSpeed => idleWorldSpeed;
    public float MaxWorldSpeed => maxWorldSpeed;

    public float GetWorldSpeed(int lane) {
        switch (lane) {
            case 0: return player1WorldSpeedModifier;
            case 1: return player2WorldSpeedModifier;
            default: Debug.LogError("Invalid Lane");
                return 1;
        }
    }

    // Boost 0..1: climbs an S-curve while the partner moves (slow start, so a
    // short step barely changes the lane), capped by how fast they move (an
    // analog stick held halfway gives about half the boost); holds for
    // holdTime once they stop, then winds down on a smoothstep. Each step reads
    // the progress back off the current boost, so switching mid-curve carries
    // on smoothly from where the boost is.
    private float Step(int lane, float dt) {
        float boost = laneBoost[lane];
        float input = laneInput[lane];
        float cap = input > movingThreshold ? Mathf.Clamp01((input - movingThreshold) / (1f - movingThreshold)) : 0f;
        if (cap > 0f && boost < cap) {
            laneHold[lane] = holdTime;
            float progress = InverseSmoothStep(boost);
            progress = Mathf.Min(1f, progress + dt / Mathf.Max(0.0001f, rampDuration));
            boost = Mathf.Min(cap, SmoothStep(progress));
        } else if (cap > 0f && boost - cap < 0.0001f) {
            laneHold[lane] = holdTime;
        } else if (laneHold[lane] > 0f) {
            laneHold[lane] -= dt;
        } else if (boost > cap) {
            float progress = InverseSmoothStep(1f - boost);
            progress = Mathf.Min(1f, progress + dt / Mathf.Max(0.0001f, windDownDuration));
            boost = Mathf.Max(cap, 1f - SmoothStep(progress));
            if (boost < 0.0001f)
                boost = 0f;
        }
        laneBoost[lane] = boost;
        return boost;
    }

    // 3p^2 - 2p^3: flat at both ends, so the wind-down starts gently and comes
    // to rest without a jolt.
    private static float SmoothStep(float p) => p * p * (3f - 2f * p);

    private static float InverseSmoothStep(float v) =>
        0.5f - Mathf.Sin(Mathf.Asin(1f - 2f * Mathf.Clamp01(v)) / 3f);
}
