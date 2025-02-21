using UnityEngine;

public class rotationShow : MonoBehaviour
{

    public sensor2 sensorScript;

    // Start is called once before the first execution of Update after the MonoBehaviour is created
   public Transform disc;

    void Update()
    {
        if (sensorScript != null)
        {
            // 1) Read angles from the sensor
            float roll = sensorScript.Roll;   // degrees
            float pitch = sensorScript.Pitch; // degrees
            float yaw = sensorScript.Yaw;   // degrees

            // 2) Convert them to a Quaternion. 
            Quaternion rotation = Quaternion.Euler(roll, yaw, pitch);

            // 3) Apply to the disc's transform
            if (disc != null)
            {
                disc.localRotation = rotation;
            }
        }
    }
}
