using UnityEngine;
using System.Collections.Generic;

public class DiscThrowSimulation : MonoBehaviour
{
    public float simulationTime = 5f; // seconds
    public float dt = 0.01f;          // time step in seconds
    private List<DiscState> trajectory;
    public Vector3 initialVelocity, initialPosition, initialAttitude;
    public float spinrate; 
    public float mass, diameter, I_xy, I_z, coefficient_drag, coefficient_lift, coefficient_mass; // disc constants
    public DiscState DiscState;
    public FlightPathController.DiscGolfDisc disc;
    public SaveToCSV SaveToCSV;
    private int sampleCount = 100;
    private List<Vector3> logDataPoints = new List<Vector3>(100);

    public void RunSimulation(){
        disc = new FlightPathController.DiscGolfDisc(mass, diameter, I_xy, I_z, coefficient_drag, coefficient_lift, coefficient_mass, spinrate);

        DiscState initialState = disc.InitializeShotWithVelocity(initialVelocity, initialPosition, initialAttitude);

        trajectory = disc.Simulate(initialState, dt, simulationTime);

        int totalPoints = trajectory.Count;
        for (int i = 0; i < sampleCount; i++)
        {
            // Linearly interpolate the index.
            float tNorm = i / (float)(sampleCount - 1);
            int idx = Mathf.RoundToInt(tNorm * (totalPoints - 1));
            DiscState s = trajectory[idx];
            // Convert coordinates: simulation uses (x, y, z) with z as elevation.
            // We want (x, y, z) with y as elevation, so swap y and z.
            Vector3 loggedPoint = new Vector3(s.Position.x, s.Position.z, s.Position.y);
            logDataPoints.Add(loggedPoint);
        }

        SaveToCSV.SaveToCSVFile(logDataPoints);
    }
    public List<Vector3> getFlightpathDataPoints(){
        return this.logDataPoints;
    }
}
