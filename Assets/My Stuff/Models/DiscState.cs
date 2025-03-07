using UnityEngine;

public class DiscState
{
    public Vector3 Position;  // (x, y, z) in simulation coordinates (z is elevation)
    public Vector3 Velocity;  // (vx, vy, vz)
                              // Attitude: Euler angles (roll, pitch, yaw) in radians.
    public Vector3 Attitude;
}