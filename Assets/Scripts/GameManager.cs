using UnityEngine;

public class GameManager : MonoBehaviour
{
    public static GameManager INSTANCE;

    [SerializeField] 
    private float maxWorldSpeedModifier = 5f;
    
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
    readonly float[] laneTime = new float[2];

    void Awake() {
        if (INSTANCE != null) {
            Destroy(INSTANCE.gameObject);
        }

        INSTANCE = this;
    }

    void Update() {
        for (int lane = 0; lane < laneTime.Length; lane++) {
            laneTime[lane] += Time.deltaTime * GetWorldSpeed(lane);
        }
    }

    public float GetLaneTime(int lane) => laneTime[lane];

    // Time of day (0-24) on the lane's clock; drives its sky and tower clocks.
    public float GetLaneHour(int lane) =>
        Mathf.Repeat(startHours[lane] + laneTime[lane] * minutesPerSecond / 60f, 24f);
    
    public void UpdateWorldSpeed(int player, float speedNormalised) {
        switch (player) {
            case 0: player2WorldSpeedModifier = 1 + speedNormalised * maxWorldSpeedModifier; break;
            case 1: player1WorldSpeedModifier = 1 + speedNormalised * maxWorldSpeedModifier; break;
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
}
