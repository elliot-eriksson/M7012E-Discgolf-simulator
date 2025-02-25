using UnityEngine;
using System.Collections.Generic;
using System.Collections;


public class FlightPath : MonoBehaviour
{
    public Transform discObject; // Assign your disc in Unity
    public LineRenderer trajectoryLine; // Assign a LineRenderer to visualize the trajectory

    private List<Vector3> logDataPoints = new List<Vector3>(); // Store trajectory points
    private bool isReplaying = false;
    private int currentReplayIndex = 0;
    void OnEnable()
    {
        // Subscribe to the event from DiscThrowSimulation
        DiscThrowSimulation.OnThrowSimulated += UpdateFlightPath;
    }

    void OnDisable()
    {
        // Unsubscribe from the event to prevent memory leaks
        DiscThrowSimulation.OnThrowSimulated -= UpdateFlightPath;
    }

    // This method updates the flight path when a throw simulation is done
    void UpdateFlightPath(List<Vector3> trajectoryPoints)
    {
        if (trajectoryPoints == null || trajectoryPoints.Count == 0)
        {
            Debug.LogWarning("No trajectory points received!");
            return;
        }
        logDataPoints = trajectoryPoints; // Store the trajectory points

        DisplayTrajectory(trajectoryPoints);
    }

    public void DisplayTrajectory(List<Vector3> trajectoryPoints)
    {
        StartCoroutine(DisplayTrajectoryGradually(trajectoryPoints));
    }

    private IEnumerator DisplayTrajectoryGradually(List<Vector3> trajectoryPoints)
    {
        trajectoryLine.positionCount = trajectoryPoints.Count;

        // Gradually set each position with a small delay
        for (int i = 0; i < trajectoryPoints.Count; i++)
        {
            trajectoryLine.SetPosition(i, trajectoryPoints[i]);
            yield return new WaitForSeconds(0.05f); // Adjust the delay to suit the desired speed
        }
    }

    // Method to start/replay the flight path
    public void ReplayFlightPath()
    {
        if (logDataPoints.Count == 0)
        {
            Debug.LogWarning("No flight path data available to replay!");
            return;
        }

        // Reset the replay index
        currentReplayIndex = 0;

        // Start a coroutine to replay the path over time
        StartCoroutine(ReplayCoroutine());
    }

    // Coroutine to handle the replay of the flight path
    private IEnumerator ReplayCoroutine()
    {
        isReplaying = true;
        trajectoryLine.positionCount = logDataPoints.Count;

        while (currentReplayIndex < logDataPoints.Count)
        {
            // Set the current position for the LineRenderer
            trajectoryLine.SetPosition(currentReplayIndex, logDataPoints[currentReplayIndex]);

            // Wait for a short period before moving to the next point in the trajectory
            currentReplayIndex++;
            yield return new WaitForSeconds(0.05f); // Adjust the delay for replay speed
        }

        isReplaying = false;
    }
}
