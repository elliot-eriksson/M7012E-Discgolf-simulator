using UnityEngine;

public class TempInputDataGenerator : MonoBehaviour
{
    // These fields could be set via the Inspector or computed at runtime.
    public Vector3 initialVelocity = new Vector3(23.2f, 0f, 6.2f);
    public Vector3 initialPosition = new Vector3(0f, 0f, 1.5f);
    public Vector3 initialAttitude = new Vector3(15.5f, 21.8f, -31.6f); // (roll, pitch, yaw) in radians

    public float spinrate = 20f;

    // Methods to retrieve the input data:
    public Vector3 GetInitialVelocity() => initialVelocity;
    public Vector3 GetInitialPosition() => initialPosition;
    public Vector3 GetInitialAttitude() => initialAttitude;

    public float GetSpinRate() => spinrate;
}
