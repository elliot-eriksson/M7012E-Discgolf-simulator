using System.Collections.Generic;
using TMPro;
using Unity.VisualScripting;
using UnityEngine;
//using static DiscThrowSimulation;


// Class representing the disc's physical and aerodynamic properties,
// and methods for computing forces and simulating flight.
public class DiscGolfDisc : MonoBehaviour
{
    public TextMeshProUGUI flightControllerText;    
    // Physical properties.
    float Mass;       // in kg
    float Diameter;   // in m
    float Area;       // disc area in m^2
    float I_xy;       // moment of inertia about disc plane
    float I_z;        // moment of inertia about spin axis

    // Aerodynamic coefficients (assumed constant for this example).
    float Cd;         // Drag coefficient
    float Cl;         // Lift coefficient
    float Cm;         // Moment coefficient
    float Spin;       // Constant spin rate (rad/s)

    // Environmental constants.
    float g = 9.81f;         // gravitational acceleration (m/s^2)
    float AirDensity = 1.225f; // kg/m^3
    public DiscState DiscState;
    public DiscState CurrentState { get; private set; }


    // Constructor.
    public DiscGolfDisc(float mass, float diameter, float I_xy, float I_z,
                            float Cd, float Cl, float Cm, float spin)
    {
        Mass = mass;
        Diameter = diameter;
        Area = Mathf.PI * diameter * diameter / 4.0f;
        this.I_xy = I_xy;
        this.I_z = I_z;
        this.Cd = Cd;
        this.Cl = Cl;
        this.Cm = Cm;
        Spin = spin;

        CurrentState = new DiscState(); // Ensure state exists

    }

    // Build a rotation matrix from Euler angles (roll, pitch, yaw)
    // using ZYX convention: R = Rz(yaw) * Ry(pitch) * Rx(roll).
    public static Matrix4x4 RotationMatrix(float roll, float pitch, float yaw)
    {
        float cr = Mathf.Cos(roll);
        float sr = Mathf.Sin(roll);
        float cp = Mathf.Cos(pitch);
        float sp = Mathf.Sin(pitch);
        float cy = Mathf.Cos(yaw);
        float sy = Mathf.Sin(yaw);

        // Rotation about X (roll)
        Matrix4x4 Rx = new Matrix4x4();
        Rx.SetRow(0, new Vector4(1, 0, 0, 0));
        Rx.SetRow(1, new Vector4(0, cr, -sr, 0));
        Rx.SetRow(2, new Vector4(0, sr, cr, 0));
        Rx.SetRow(3, new Vector4(0, 0, 0, 1));

        // Rotation about Y (pitch)
        Matrix4x4 Ry = new Matrix4x4();
        Ry.SetRow(0, new Vector4(cp, 0, sp, 0));
        Ry.SetRow(1, new Vector4(0, 1, 0, 0));
        Ry.SetRow(2, new Vector4(-sp, 0, cp, 0));
        Ry.SetRow(3, new Vector4(0, 0, 0, 1));

        // Rotation about Z (yaw)
        Matrix4x4 Rz = new Matrix4x4();
        Rz.SetRow(0, new Vector4(cy, -sy, 0, 0));
        Rz.SetRow(1, new Vector4(sy, cy, 0, 0));
        Rz.SetRow(2, new Vector4(0, 0, 1, 0));
        Rz.SetRow(3, new Vector4(0, 0, 0, 1));

        // Combined rotation.
        Matrix4x4 R = Rz * Ry * Rx;
        return R;
    }

    // Compute aerodynamic forces and moment.
    // Returns acceleration (world frame) and derivative of roll.
    public (Vector3 acceleration, float dRoll) ComputeDerivatives(DiscState state)
    {
        // Transform world velocity into disc's body frame.
        Matrix4x4 R = RotationMatrix(state.Attitude.x, state.Attitude.y, state.Attitude.z);
        Matrix4x4 R_T = R.transpose; // Unity's Matrix4x4 has a built-in transpose.
        Vector3 v_body = R_T.MultiplyPoint3x4(state.Velocity);

        // Compute angle of attack (alpha): assume disc's x-axis is forward, z-axis is up.
        float alpha = Mathf.Atan2(-v_body.z, v_body.x);

        float speed = state.Velocity.magnitude;
        float q = 0.5f * AirDensity * speed * speed;

        float F_drag = q * Area * Cd;
        float F_lift = q * Area * Cl;
        float M_aero = q * Area * Diameter * Cm;

        // In disc's body frame, drag acts in negative x, lift in positive z.
        Vector3 F_aero_body = new Vector3(-F_drag, 0, F_lift);
        // Transform aerodynamic force back to world frame.
        Vector3 F_aero_world = R.MultiplyPoint3x4(F_aero_body);

        // Gravity force in world frame.
        Vector3 F_gravity = new Vector3(0, -Mass * g, 0);

        // Total acceleration.
        Vector3 acceleration = (F_aero_world + F_gravity) / Mass;

        // Compute roll rate derivative.
        float dRoll = -M_aero / (Spin * (I_xy - I_z));

        return (acceleration, dRoll);
    }

    // Initialize the disc's state directly with a velocity vector.
    // position: initial position in world coordinates.
    // velocity: initial velocity vector (x, y, z) in m/s.
    // The initial attitude is set to zero (no initial rotation) by default.
    public DiscState InitializeShotWithVelocity(Vector3 velocity, Vector3 position, Vector3 attitude)
    {
        
        CurrentState = new DiscState(); // Store in the class
        CurrentState.Position = position;
        CurrentState.Velocity = velocity;
        CurrentState.Attitude = new Vector3(attitude.x * Mathf.Deg2Rad,
                                            attitude.y * Mathf.Deg2Rad,
                                            attitude.z * Mathf.Deg2Rad);
        return CurrentState;
    }

    // Run a simple Euler integration to simulate flight.
    // dt: time step (s), totalTime: maximum simulation time (s).
    // Stops early if the disc's height (z) reaches or drops below 0.
    public List<DiscState> Simulate(DiscState initialState, float dt, float totalTime)
    {
        int steps = Mathf.CeilToInt(totalTime / dt);
        List<DiscState> states = new List<DiscState>(steps);
        states.Add(initialState);
        DiscState current = initialState;



        for (int i = 1; i < steps; i++)
        {
            DiscState next = new DiscState();
            next.Position = current.Position + current.Velocity * dt;
            (Vector3 acc, float dRoll) = ComputeDerivatives(current);
            next.Velocity = current.Velocity + acc * dt;
            // Update only the roll angle; pitch and yaw remain constant in this simple simulation.
            next.Attitude = new Vector3(current.Attitude.x + dRoll * dt, current.Attitude.y, current.Attitude.z);
            states.Add(next);
            current = next;
            if (current.Position.y <= 0)
                break;
        }


        return states;

    }


}

