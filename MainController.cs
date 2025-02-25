using UnityEngine;
using System.Collections.Generic;

public class MainController : MonoBehaviour
{
    // Start is called once before the first execution of Update after the MonoBehaviour is created
    void Start()
    {
        GameObject inputObj = new GameObject("InputController");
        TempInputDataGenerator inputProvider = inputObj.AddComponent<TempInputDataGenerator>(); // Input data

        Vector3 initialVelocity = inputProvider.GetInitialVelocity();
        Vector3 initialPosition = inputProvider.GetInitialPosition();
        Vector3 initialAttitude = inputProvider.GetInitialAttitude();
        float spinrate = inputProvider.GetSpinRate();

        GameObject discConstantObj = new GameObject("DiscController");
        DiscConstantController discConstantProvider = discConstantObj.AddComponent<DiscConstantController>(); // Disc constants

        float mass = discConstantProvider.GetMass();
        float diameter = discConstantProvider.GetDiameter();
        float I_xy = discConstantProvider.GetI_xy();
        float I_z = discConstantProvider.GetI_z();
        float coefficient_drag = discConstantProvider.GetCoefficientDrag();
        float coefficient_lift = discConstantProvider.GetCoefficientLift();
        float coefficient_mass = discConstantProvider.GetCoefficientMass();

        GameObject simObj = new GameObject("SimulationController");
        DiscThrowSimulation simulation = simObj.AddComponent<DiscThrowSimulation>(); // Simulation

        simulation.initialVelocity = initialVelocity;
        simulation.initialPosition = initialPosition;
        simulation.initialAttitude = initialAttitude;
        simulation.spinrate = spinrate;

        simulation.mass = mass;
        simulation.diameter = diameter;
        simulation.I_xy = I_xy;
        simulation.I_z = I_z;
        simulation.coefficient_drag = coefficient_drag;
        simulation.coefficient_lift = coefficient_lift;
        simulation.coefficient_mass = coefficient_mass;

        simulation.RunSimulation();

        List<Vector3> datapoints = simulation.getFlightpathDataPoints();
        
    }

}
