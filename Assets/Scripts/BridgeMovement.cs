using UnityEngine;

[RequireComponent(typeof(Rigidbody2D))]
[RequireComponent(typeof(HingeJoint2D))]
public class BridgeMovement2D : MonoBehaviour
{
    private Rigidbody2D rb2d;
    private HingeJoint2D hingeJoint;

    [SerializeField] 
    private float baseMotorSpeed = 50f;

    [SerializeField] 
    private float speedMut = 1f;

    [SerializeField] 
    private float maxTorque = 10000f;

    [SerializeField]
    private float minAngle = 0f;    // Starting angle

    [SerializeField]
    private float maxAngle = 90f;   // Target angle (e.g., 90 degrees away)

    private int direction = 1; // 1 for moving forward, -1 for reversing

    void Awake()
    {
        rb2d = GetComponent<Rigidbody2D>();
        hingeJoint = GetComponent<HingeJoint2D>();

        rb2d.gravityScale = 0f;
        hingeJoint.useMotor = true;
    }

    void Update()
    {
        float currentAngle = hingeJoint.jointAngle;

        // If moving forward and we hit or exceed maxAngle, switch to reverse
        if (direction == 1 && currentAngle >= maxAngle)
        {
            direction = -1; 
        }
        // If moving in reverse and we hit or go below minAngle, switch to forward
        else if (direction == -1 && currentAngle <= minAngle)
        {
            direction = 1;  
        }

        // Apply motor speed, multiplier, and current direction
        JointMotor2D motor = hingeJoint.motor;
        motor.motorSpeed = baseMotorSpeed * speedMut * direction;
        motor.maxMotorTorque = maxTorque;
        hingeJoint.motor = motor;
    }
}
