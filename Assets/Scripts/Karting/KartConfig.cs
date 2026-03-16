 using UnityEngine;

[CreateAssetMenu(fileName = "KartConfig", menuName = "Kart/Configuration")]
public class KartConfig : ScriptableObject
{
    [Header("Power Unit")]
    public AnimationCurve engineTorqueCurve;

    [Min(0f)]
    public float engineInertia = 0.2f;

    [Min(0f)]
    public float maxRpm = 8000f;

    [Header("Transmission")]
    [Min(0f)]
    public float gearRatio = 8f;

    [Min(0f)]
    public float wheelRadius = 0.3f;

    [Header("Vehicle Body")]
    public float mass = 300f;

    [Range(0f, 5f)]
    public float frictionCoefficient = 1.0f;

    public float rollingResistance = 0.5f;

    [Header("Tire Grip")]
    public float frontLateralStiffness = 80f;
    public float rearLateralStiffness = 80f;

    [Header("Steering System")]
    [Range(0f, 60f)]
    public float maxSteerAngle = 30f;
}