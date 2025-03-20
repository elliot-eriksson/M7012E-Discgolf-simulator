using UnityEngine;
using System.Collections.Generic;
using System.Collections;
using TMPro;

// Used to display the flight path, and replay it
public class FlightPath : MonoBehaviour
{
    public Transform discObject; // Assign your disc in Unity
    public LineRenderer trajectoryLine; // Assign a LineRenderer to visualize the trajectory


    private List<Vector3> replayDataPoints = new List<Vector3>(); // Store trajectory points
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
        replayDataPoints = trajectoryPoints; // Store the trajectory points

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
            discObject.position = trajectoryPoints[i];
            yield return new WaitForSeconds(0.01f); // Adjust the delay to suit the desired speed
        }
    }


    public void ReplayFlightPath()
    {
        if (replayDataPoints.Count == 0)
        {
            Debug.LogWarning("No flight path data available to replay!");
            return;
        }

        // 1) Clear the existing line
        trajectoryLine.positionCount = 0;

        // 2) (Optional) move the disc to the starting point
        discObject.position = replayDataPoints[0];

        // 3) Reset our replay index
        currentReplayIndex = 0;

        // 4) Start the coroutine to show the line incrementally
        StartCoroutine(ReplayCoroutine());
    }


    public void resetPosition()
    {
        discObject.position = new Vector3(0f, 1.5f, 0f);
        trajectoryLine.positionCount = 0;
        replayDataPoints.Clear();
    }


    private IEnumerator ReplayCoroutine()
    {
        isReplaying = true;
        trajectoryLine.positionCount = replayDataPoints.Count;
        // Go through each point in replayDataPoints
        while (currentReplayIndex < replayDataPoints.Count)
        {
            // Increase the total points on the line by 1
            trajectoryLine.positionCount = currentReplayIndex + 1;

            // Set the next position on the line
            trajectoryLine.SetPosition(currentReplayIndex, replayDataPoints[currentReplayIndex]);

            // Also move the disc to that position
            discObject.position = replayDataPoints[currentReplayIndex];

            currentReplayIndex++;
            yield return new WaitForSeconds(0.05f); // Adjust speed as desired
        }

        isReplaying = false;
    }


}
