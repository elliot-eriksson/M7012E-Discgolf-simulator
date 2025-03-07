using UnityEngine;

public class DiscConstantController : MonoBehaviour
{
    public float mass = 0.175f;
    public float diameter = 0.205f;
    public float I_xy = 0.002f;
    public float I_z = 0.003f;
    public float coefficient_drag = 0.08f;
    public float coefficient_lift = 0.15f;
    public float coefficient_mass = 0.01f;

    public float GetMass() => mass;
    public float GetDiameter() => diameter;
    public float GetI_xy() => I_xy;
    public float GetI_z() => I_z;
    public float GetCoefficientDrag() => coefficient_drag;
    public float GetCoefficientLift() => coefficient_lift;
    public float GetCoefficientMass() => coefficient_mass;
}