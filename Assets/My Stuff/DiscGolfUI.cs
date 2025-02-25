using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class DiscGolfUI : MonoBehaviour
{
    private ThrowData lastThrow; // Store the latest throw data

    public void StartNewThrow()
    {
        Debug.Log("Starting a new throw...");
        // Logic to capture new throw data
        lastThrow = CaptureThrow();
    }

    public void ReplayLastThrow()
    {
        if (lastThrow != null)
        {
            Debug.Log("Replaying last throw...");
            ReplayThrow(lastThrow);
        }
        else
        {
            Debug.Log("No throw data available.");
        }
    }

    private ThrowData CaptureThrow()
    {
        return new ThrowData(0, 0, new Dictionary<string, double>());
        // Replace with actual sensor data retrieval
        //return new ThrowData { speed = 10f, angle = 45f };
    }

    private void ReplayThrow(ThrowData throwData)
    {
        // Logic to replay the throw using stored sensor data
        //Debug.Log($"Replaying throw with speed: {throwData.speed}, angle: {throwData.angle}");
    }
}

