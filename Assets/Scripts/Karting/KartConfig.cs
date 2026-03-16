 using UnityEngine;

[CreateAssetMenu(fileName = "NewKartConfig", menuName = "Kart/Config")]
public class KartConfig : ScriptableObject
{
    [Header("Physics")]
    public float mass = 300f;
    public float frictionCoefficient = 1.0f;
    public float frontLateralStiffness = 80f;
    public float rearLateralStiffness = 80f;
    public float rollingResistance = 0.5f;

    [Header("Steering")]
    public float maxSteerAngle = 30f;

    [Header("Engine")]
    public AnimationCurve engineTorqueCurve;
    public float engineInertia = 0.2f;
    public float maxRpm = 8000f;
    
    [Header("Drivetrain")]
    public float gearRatio = 8f;
    public float wheelRadius = 0.3f;
}