using UnityEngine;
using System.Collections.Generic;
using System.Text;
using System.Globalization;
using System.IO;
using System;
using TMPro;


public class DiscThrowSimulation : MonoBehaviour
{
    public static event Action<List<Vector3>> OnThrowSimulated;
    public TextMeshProUGUI simulationText; // UI element for displaying connection status
    public TextMeshProUGUI tempText;



    public float simulationTime = 6f; // seconds
    public float dt = 0.01f;          // time step in seconds
    private List<DiscState> trajectory;
    private DiscGolfDisc disc;
    public Vector3 initialVelocity;
    public Vector3 initialPosition = new Vector3(0f, 1.5f, 0f);
    public Vector3 initialAttitude;
    public float spinrate = 0;

    // Disc properties 
    public float mass = 0.175f;
    public float diameter = 0.205f;
    public float I_xy = 0.002f;
    public float I_z = 0.003f;
    public float coefficient_drag = 0.08f;
    public float coefficient_lift = 0.15f;
    public float coefficient_mass = 0.01f;

    private Vector3 initialAcceleration;
    private Vector3 initialAngularVelocity;

    private float timeOfThrow;
    public float throwTimeDelta;

    private ThrowData currentThrow;
    private List<Vector3> logDataPoints;

    void OnEnable()
    {
        SensorBluetooth.OnThrowDetected += SimulateThrow;
    }

    void OnDisable()
    {
        SensorBluetooth.OnThrowDetected -= SimulateThrow;
    }


    // Retrieve the throw data from the Bluetooth script and map it to the disc properties
    void SimulateThrow(ThrowData throwData)
    {
        currentThrow = throwData;

        initialAcceleration = new Vector3((float)throwData.throwDictionary["ax"], (float)throwData.throwDictionary["ay"], (float)throwData.throwDictionary["az"]);
        initialAngularVelocity = new Vector3((float)throwData.throwDictionary["wx"], (float)throwData.throwDictionary["wy"], (float)throwData.throwDictionary["wz"]);
        spinrate = (float)throwData.throwDictionary["wz"];

        timeOfThrow = throwData.timeOfThrow;
        throwTimeDelta = throwData.throwTimeDelta;

        initialVelocity = new Vector3(MathF.Abs((float)throwData.throwDictionary["vx"]), (float)throwData.throwDictionary["vy"], MathF.Abs((float)throwData.throwDictionary["vz"]));
        initialAttitude = new Vector3((float)throwData.throwDictionary["roll"], (float)throwData.throwDictionary["pitch"], (float)throwData.throwDictionary["yaw"]);


        disc = new DiscGolfDisc(mass, diameter, I_xy, I_z, coefficient_drag, coefficient_lift, coefficient_mass, spinrate);

        RunSimulation();

    }

    // Run the simulation with the given parameters
    private void RunSimulation()
    {
        //TempTextStatus($"Simulating throw...  {initialPosition}");

        DiscState initialState = disc.InitializeShotWithVelocity(initialVelocity, initialPosition, initialAttitude);
        trajectory = disc.Simulate(initialState, dt, simulationTime);

        int totalPoints = trajectory.Count;
        if (logDataPoints != null)
        { 
            logDataPoints.Clear();
        }
        logDataPoints = new List<Vector3>(totalPoints);


        for (int i = 0; i < totalPoints; i++)
        {
            // Linearly interpolate the index.
            float tNorm = i / (float)(totalPoints - 1);
            int idx = Mathf.RoundToInt(tNorm * (totalPoints - 1));
            DiscState s = trajectory[idx];
            // Convert coordinates: simulation uses (x, y, z) with z as elevation.
            // We want (x, y, z) with y as elevation, so swap y and z.
            Vector3 loggedPoint = new Vector3(s.Position.x, s.Position.y, s.Position.z);
            logDataPoints.Add(loggedPoint);

        }


        OnThrowSimulated?.Invoke(logDataPoints);

    }

}
