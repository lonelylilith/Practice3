using UnityEngine;

public class KartEngine : MonoBehaviour
{
    [Header("Base Settings")]
    [SerializeField] private float _idleRpm = 1000f; 

    private float _revLimiterRpm; 
    
    [Header("Runtime Values (Read Only)")]
    public float MaxRpm;
    public AnimationCurve TorqueCurve;
    public float FlywheelInertia;

    [Header("Losses")]
    [SerializeField] private float _engineFrictionCoeff = 0.02f;
    [SerializeField] private float _loadTorqueCoeff = 5f;
    [SerializeField] private float _throttleResponse = 5f;

    // Телеметрия
    public float CurrentRpm { get; private set; }
    public float CurrentTorque { get; private set; }
    
    private float _invInertiaFactor;
    private float _smoothedThrottle;
    private float _revLimiterFactor = 1f;

    // Загрузка из конфига
    public void ApplyConfig(KartConfig config)
    {
        MaxRpm = config.maxRpm;
        TorqueCurve = config.engineTorqueCurve;
        FlywheelInertia = config.engineInertia;
        
        RecalculatePhysicsParams();
    }

    private void Awake()
    {
        CurrentRpm = _idleRpm;
        
        if (TorqueCurve == null || TorqueCurve.length == 0)
        {
             FlywheelInertia = Mathf.Max(FlywheelInertia, 0.2f);
             if (MaxRpm == 0) MaxRpm = 8000f;
        }

        RecalculatePhysicsParams();
    }

    private void RecalculatePhysicsParams()
    {
        _invInertiaFactor = 60f / (2f * Mathf.PI * Mathf.Max(FlywheelInertia, 0.0001f));
        _revLimiterRpm = MaxRpm - 500f; 
    }

    public float Simulate(float throttleInput, float forwardSpeed, float deltaTime)
    {
        float targetThrottle = Mathf.Clamp01(throttleInput);
        _smoothedThrottle = Mathf.MoveTowards(_smoothedThrottle, targetThrottle, _throttleResponse * deltaTime);

        UpdateRevLimiterFactor();

        float maxTorqueAtRpm = 0f;
        if (TorqueCurve != null && TorqueCurve.length > 0)
        {
            maxTorqueAtRpm = TorqueCurve.Evaluate(CurrentRpm);
        }

        float effectiveThrottle = _smoothedThrottle * _revLimiterFactor;
        float driveTorque = maxTorqueAtRpm * effectiveThrottle;

        float frictionTorque = _engineFrictionCoeff * CurrentRpm;
        float loadTorque = _loadTorqueCoeff * Mathf.Abs(forwardSpeed);

        float netTorque = driveTorque - frictionTorque - loadTorque;
        float rpmDot = netTorque * _invInertiaFactor;
        
        CurrentRpm += rpmDot * deltaTime;
        
        if (CurrentRpm < _idleRpm) CurrentRpm = _idleRpm;
        if (CurrentRpm > MaxRpm) CurrentRpm = MaxRpm;

        CurrentTorque = driveTorque;
        return CurrentTorque;
    }

    private void UpdateRevLimiterFactor()
    {
        if (CurrentRpm <= _revLimiterRpm) { _revLimiterFactor = 1f; return; }
        if (CurrentRpm >= MaxRpm) { _revLimiterFactor = 0f; return; }
        
        float t = (CurrentRpm - _revLimiterRpm) / (MaxRpm - _revLimiterRpm);
        _revLimiterFactor = 1f - t;
    }
}