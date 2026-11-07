using UnityEngine;

public class GameManager : MonoBehaviour
{
    public static GameManager INSTANCE;

    [SerializeField]
    private float maxWorldSpeedModifier = 5f;

    [Header("Ramp Up (while the player is moving)")]
    [Tooltip("Seconds of continuous movement to reach the max boost.")]
    [SerializeField] private float rampDuration = 0.6f;
    [Tooltip("How exponential the ramp is. Near 0 is a straight line, higher starts slower and finishes sharper.")]
    [Range(0f, 10f)]
    [SerializeField] private float rampSteepness = 8f;

    [Header("Decay (after the player stops)")]
    [Tooltip("Seconds for the boost to drop to half. Lower falls back faster.")]
    [SerializeField] private float decayHalfLife = 0.25f;

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

    // Per lane: latest normalised speed reported, and the current boost (0..1).
    private float[] laneInput = new float[2];
    private float[] laneBoost = new float[2];

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
        player1WorldSpeedModifier = 1f + Step(0, dt) * maxWorldSpeedModifier;
        player2WorldSpeedModifier = 1f + Step(1, dt) * maxWorldSpeedModifier;
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

    private float Step(int lane, float dt) {
        float boost = laneBoost[lane];

        if (laneInput[lane] > movingThreshold) {
            // Work out how far along the ramp the current boost is, step forward,
            // and read the boost back off the curve. Starting from the current
            // boost means moving again mid decay carries on smoothly.
            float progress = InverseRamp(boost);
            progress = Mathf.Min(1f, progress + dt / Mathf.Max(0.0001f, rampDuration));
            boost = Ramp(progress);
        } else {
            boost *= Mathf.Pow(0.5f, dt / Mathf.Max(0.0001f, decayHalfLife));
            if (boost < 0.001f)
                boost = 0f;
        }

        laneBoost[lane] = boost;
        return boost;
    }

    // (e^(k*p) - 1) / (e^k - 1): 0 at p = 0, 1 at p = 1, flat at the start, steep at the end.
    private float Ramp(float p) {
        float k = rampSteepness;
        if (k < 0.01f)
            return p;
        return (Mathf.Exp(k * p) - 1f) / (Mathf.Exp(k) - 1f);
    }

    private float InverseRamp(float v) {
        float k = rampSteepness;
        if (k < 0.01f)
            return v;
        return Mathf.Log(1f + v * (Mathf.Exp(k) - 1f)) / k;
    }
}