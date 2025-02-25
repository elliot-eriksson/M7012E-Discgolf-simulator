using UnityEngine;
using System.Collections.Generic;
using System.Text;
using System.Globalization;
using System.IO;
using System;


public class DiscThrowSimulation : MonoBehaviour
{
    public static event Action<List<Vector3>> OnThrowSimulated;

    public float simulationTime = 5f; // seconds
    public float dt = 0.01f;          // time step in seconds
    private List<DiscState> trajectory;
    private FlightPathController.DiscGolfDisc disc;
    public Vector3 initialVelocity;
    public Vector3 initialPosition;
    public Vector3 initialAttitude;
    public float spinrate;
    public float mass, diameter, I_xy, I_z, coefficient_drag, coefficient_lift, coefficient_mass; // disc constants

    private Vector3 initialAcceleration;
    private Vector3 initialAngularVelocity;

    private float timeOfThrow;
    public float throwTimeDelta;

    private ThrowData currentThrow;
    private int sampleCount = 100;
    private List<Vector3> logDataPoints = new List<Vector3>(100);

    void OnEnable()
    {
        SensorBluetooth.OnThrowDetected += SimulateThrow;
    }

    void OnDisable()
    {
        SensorBluetooth.OnThrowDetected -= SimulateThrow;
    }



    void SimulateThrow(ThrowData throwData)
    {
        currentThrow = throwData;

        initialAcceleration = new Vector3((float)throwData.throwDictionary["ax"], (float)throwData.throwDictionary["ay"], (float)throwData.throwDictionary["az"]);
        initialAngularVelocity = new Vector3((float)throwData.throwDictionary["wx"], (float)throwData.throwDictionary["wy"], (float)throwData.throwDictionary["wz"]);
        spinrate = (float)throwData.throwDictionary["wx"];

        timeOfThrow = throwData.timeOfThrow;
        throwTimeDelta = throwData.throwTimeDelta;

        initialVelocity = new Vector3((float)throwData.throwDictionary["vx"], (float)throwData.throwDictionary["vy"], (float)throwData.throwDictionary["vz"]);
        initialAttitude = new Vector3((float)throwData.throwDictionary["roll"], (float)throwData.throwDictionary["pitch"], (float)throwData.throwDictionary["yaw"]);

        RunSimulation();

    }    


    private void RunSimulation()
    {
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

        OnThrowSimulated?.Invoke(logDataPoints);

    }

}
