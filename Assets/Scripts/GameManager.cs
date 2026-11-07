using UnityEngine;

public class GameManager : MonoBehaviour
{
    public static GameManager INSTANCE;

    private float player1WorldSpeedModifier { get; set; } = 1f;
    private float player2WorldSpeedModifier { get; set; } = 1f;
    
    void Awake() {
        if(INSTANCE != null) {
            Destroy(INSTANCE.gameObject);
        }

        INSTANCE = this;
    }
}
