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

    void Awake() {
        if (INSTANCE != null) {
            Destroy(INSTANCE.gameObject);
        }

        INSTANCE = this;
    }
    
    public void UpdateWorldSpeed(int player, float speedNormalised) {
        switch (player) {
            case 0: player2WorldSpeedModifier = speedNormalised * maxWorldSpeedModifier; break;
            case 1: player1WorldSpeedModifier = speedNormalised * maxWorldSpeedModifier; break;
            default: Debug.LogError("Invalid player");
                break;
        }
    }
}
