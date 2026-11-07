using UnityEngine;

public class GameManager : MonoBehaviour
{
    public static GameManager INSTANCE;

    [Tooltip("Lane speed while the partner stands still. 0 freezes the lane.")]
    [SerializeField] private float idleWorldSpeed = 0f;
    [Tooltip("Lane speed while the partner keeps moving.")]
    [SerializeField] private float maxWorldSpeed = 6f;

    [Header("Ramp Up (while the partner is moving)")]
    [Tooltip("Seconds of continuous movement from idle to full speed.")]
    [SerializeField] private float rampDuration = 0.3f;
    [Tooltip("Ease-out power: 1 is a straight line, higher jumps in faster and settles into full speed.")]
    [Range(1f, 5f)]
    [SerializeField] private float rampSharpness = 2f;

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

    void Update() {
        float dt = Time.deltaTime;
        player1WorldSpeedModifier = Mathf.Lerp(idleWorldSpeed, maxWorldSpeed, Step(0, dt));
        player2WorldSpeedModifier = Mathf.Lerp(idleWorldSpeed, maxWorldSpeed, Step(1, dt));
        for (int lane = 0; lane < laneTime.Length; lane++) {
            laneTime[lane] += dt * GetWorldSpeed(lane);
        }
    }

    public float GetLaneTime(int lane) => laneTime[lane];

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

    public float GetWorldSpeed(int lane) {
        switch (lane) {
            case 0: return player1WorldSpeedModifier;
            case 1: return player2WorldSpeedModifier;
            default: Debug.LogError("Invalid Lane");
                return 1;
        }
    }

    // Boost 0..1: eases out up to 1 while the partner moves, holds for
    // holdTime once they stop, then winds down to 0 on a smoothstep. Each step
    // reads the progress back off the current boost, so switching mid-curve
    // carries on smoothly from where the boost is.
    private float Step(int lane, float dt) {
        float boost = laneBoost[lane];

        if (laneInput[lane] > movingThreshold) {
            laneHold[lane] = holdTime;
            float progress = InverseRamp(boost);
            progress = Mathf.Min(1f, progress + dt / Mathf.Max(0.0001f, rampDuration));
            boost = Ramp(progress);
        } else if (laneHold[lane] > 0f) {
            laneHold[lane] -= dt;
        } else if (boost > 0f) {
            float progress = InverseSmoothStep(1f - boost);
            progress = Mathf.Min(1f, progress + dt / Mathf.Max(0.0001f, windDownDuration));
            boost = 1f - SmoothStep(progress);
            if (boost < 0.0001f)
                boost = 0f;
        }

        laneBoost[lane] = boost;
        return boost;
    }

    // 1 - (1 - p)^k: 0 at p = 0, 1 at p = 1, steep at the start, flat at the end.
    private float Ramp(float p) => 1f - Mathf.Pow(1f - p, rampSharpness);

    private float InverseRamp(float v) => 1f - Mathf.Pow(1f - Mathf.Clamp01(v), 1f / rampSharpness);

    // 3p^2 - 2p^3: flat at both ends, so the wind-down starts gently and comes
    // to rest without a jolt.
    private static float SmoothStep(float p) => p * p * (3f - 2f * p);

    private static float InverseSmoothStep(float v) =>
        0.5f - Mathf.Sin(Mathf.Asin(1f - 2f * Mathf.Clamp01(v)) / 3f);
}
