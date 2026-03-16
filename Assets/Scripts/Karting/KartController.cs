using UnityEngine;
using UnityEngine.InputSystem;

[RequireComponent(typeof(Rigidbody))]
public class KartController : MonoBehaviour
{
    [Header("Config Asset")]
    [SerializeField] private KartConfig _defaultConfig; 

    [Header("Wheel Transforms")]
    [SerializeField] private Transform _frontLeftWheel;
    [SerializeField] private Transform _frontRightWheel;
    [SerializeField] private Transform _rearLeftWheel;
    [SerializeField] private Transform _rearRightWheel;

    [Header("Engine Link")]
    [SerializeField] private KartEngine _engine;

    [Header("Input")]
    [SerializeField] private InputActionAsset _playerInput;
    
    private Rigidbody _rb;
    private float _gravity = 9.81f;
    private float _mass;
    private float _frontAxisShare = 0.5f; 
    
    private float _frictionCoeff;
    private float _frontStiffness;
    private float _rearStiffness;
    private float _rollResist;
    private float _maxSteer;
    private float _gearRatio;
    private float _wheelRadius;
    private float _drivetrainEff = 0.9f;
    
    private float _throttleInput;
    private float _steerInput;
    private bool _isHandbrake;
    private InputAction _moveAction;
    private InputAction _brakeAction;

    private float _flNormal, _frNormal, _rlNormal, _rrNormal;
    private Quaternion _flInitRot, _frInitRot;

    private float _telemetryRearFxSum;   
    private float _telemetryFrontFySum;  
    private float _telemetryFLSlip, _telemetryFRSlip, _telemetryRLSlip, _telemetryRRSlip;

    private Texture2D _whiteTexture;
    private GUIStyle _headerStyle;
    private GUIStyle _valueStyle;
    private GUIStyle _labelStyle;

    private void Awake()
    {
        _rb = GetComponent<Rigidbody>();
        
        var map = _playerInput.FindActionMap("Kart");
        _moveAction = map.FindAction("Move");
        _brakeAction = map.FindAction("Brake"); 

        if (_frontLeftWheel) _flInitRot = _frontLeftWheel.localRotation;
        if (_frontRightWheel) _frInitRot = _frontRightWheel.localRotation;

        if (_defaultConfig != null) ApplyKartConfig(_defaultConfig);
    }

    public void ApplyKartConfig(KartConfig config)
    {
        _rb.mass = config.mass;
        _mass = config.mass;
        _frictionCoeff = config.frictionCoefficient;
        _frontStiffness = config.frontLateralStiffness;
        _rearStiffness = config.rearLateralStiffness; 
        _rollResist = config.rollingResistance;
        _maxSteer = config.maxSteerAngle;
        _gearRatio = config.gearRatio;
        _wheelRadius = config.wheelRadius;

        ComputeStaticWheelLoad();
    }

    private void ComputeStaticWheelLoad()
    {
        float totalWeight = _mass * _gravity;
        float frontWeight = totalWeight * _frontAxisShare;
        float rearWeight = totalWeight * (1f - _frontAxisShare);

        _flNormal = frontWeight * 0.5f;
        _frNormal = frontWeight * 0.5f;
        _rlNormal = rearWeight * 0.5f;
        _rrNormal = rearWeight * 0.5f;
    }

    private void OnEnable()
    {
        _playerInput.Enable();
        if (_moveAction != null) _moveAction.Enable();
        if (_brakeAction != null) _brakeAction.Enable();
    }
    private void OnDisable() => _playerInput.Disable();

    private void Update()
    {
        Vector2 move = _moveAction.ReadValue<Vector2>();
        _steerInput = Mathf.Clamp(move.x, -1f, 1f);
        _throttleInput = Mathf.Clamp(move.y, -1f, 1f);
        _isHandbrake = _brakeAction != null && _brakeAction.IsPressed();

        RotateFrontWheels();
    }

    private void RotateFrontWheels()
    {
        float angle = _maxSteer * _steerInput;
        Quaternion rot = Quaternion.Euler(0, angle, 0);
        if (_frontLeftWheel) _frontLeftWheel.localRotation = _flInitRot * rot;
        if (_frontRightWheel) _frontRightWheel.localRotation = _frInitRot * rot;
    }

    private void FixedUpdate()
    {
        _telemetryRearFxSum = 0f;
        _telemetryFrontFySum = 0f;

        float speed = Vector3.Dot(_rb.linearVelocity, transform.forward);
        
        float engineInput = Mathf.Abs(_throttleInput); 
        float torque = _engine.Simulate(engineInput, speed, Time.fixedDeltaTime);
        
        float totalDriveForce = (torque * _gearRatio * _drivetrainEff) / _wheelRadius;

        float direction = _throttleInput >= 0 ? 1f : -1f;
        
        float forcePerRearWheel = (totalDriveForce * direction) * 0.5f;

        ApplyWheelForce(_frontLeftWheel, _flNormal, isSteer:true, driveForce:0f, stiffness:_frontStiffness, ref _telemetryFLSlip);
        ApplyWheelForce(_frontRightWheel, _frNormal, isSteer:true, driveForce:0f, stiffness:_frontStiffness, ref _telemetryFRSlip);

        ApplyWheelForce(_rearLeftWheel,  _rlNormal, isSteer:false, driveForce:forcePerRearWheel, stiffness:_rearStiffness, ref _telemetryRLSlip);
        ApplyWheelForce(_rearRightWheel, _rrNormal, isSteer:false, driveForce:forcePerRearWheel, stiffness:_rearStiffness, ref _telemetryRRSlip);
    }

    void ApplyWheelForce(Transform wheel, float N, bool isSteer, float driveForce, float stiffness, ref float outSlip)
    {
        if (!wheel) return;

        Vector3 wPos = wheel.position;
        Vector3 wFwd = wheel.forward;
        Vector3 wRight = wheel.right;
        Vector3 vel = _rb.GetPointVelocity(wPos);

        float vLong = Vector3.Dot(vel, wFwd);
        float vLat = Vector3.Dot(vel, wRight);
        
        outSlip = vLat; 

        float Fx = 0f;
        float Fy = 0f;

        if (driveForce > 0 && vLong > 25f) 
            Fx += 0; 
        else 
            Fx += driveForce;
        
        Fx -= _rollResist * vLong;

        float currentStiffness = stiffness;
        if (!isSteer && _isHandbrake)
        {
            currentStiffness = 0f; 
            Fx *= 0.5f; 
        }

        Fy = -currentStiffness * vLat;

        float limit = _frictionCoeff * N;
        float len = Mathf.Sqrt(Fx*Fx + Fy*Fy);
        if (len > limit && len > 0.001f)
        {
            float scale = limit / len;
            Fx *= scale;
            Fy *= scale;
        }

        if (!isSteer) _telemetryRearFxSum += Fx; 
        else _telemetryFrontFySum += Fy;         

        Vector3 finalForce = wFwd * Fx + wRight * Fy;
        _rb.AddForceAtPosition(finalForce, wPos, ForceMode.Force);
    }

    void OnGUI()
    {
        if (_engine == null) return;

        InitStyles();

        float margin = 20f;

        float speed = Vector3.Dot(_rb.linearVelocity, transform.forward) * 3.6f;
        float rpm = _engine.CurrentRpm;
        float maxRpm = _engine.MaxRpm;
        float rpm01 = Mathf.Clamp01(rpm / maxRpm);

        Rect block = new Rect(margin, margin, 260, 180);

        GUI.color = new Color(0f, 0f, 0f, 0.55f);
        GUI.DrawTexture(block, _whiteTexture);
        GUI.color = Color.white;

        GUILayout.BeginArea(block);

        GUILayout.Label("KART STATUS", _headerStyle);

        GUILayout.Space(5);

        GUILayout.Label($"Speed: {Mathf.Abs(speed):0} km/h", _labelStyle);
        GUILayout.Label($"RPM: {rpm:0}", _labelStyle);
        GUILayout.Label($"Torque: {_engine.CurrentTorque:0} Nm", _labelStyle);
        GUILayout.Label($"Throttle: {_throttleInput:F2}", _labelStyle);
        GUILayout.Label($"Steer: {_steerInput:F2}", _labelStyle);

        if (_isHandbrake)
        {
            var red = new GUIStyle(_labelStyle);
            red.normal.textColor = Color.red;
            GUILayout.Label("HANDBRAKE", red);
        }

        GUILayout.EndArea();

        float radius = 70f;
        Vector2 center = new Vector2(Screen.width - radius - margin, radius + margin);

        GUI.color = new Color(1, 1, 1, 0.15f);
        GUI.DrawTexture(new Rect(center.x - radius, center.y - radius, radius * 2, radius * 2), _whiteTexture);

        float angle = rpm01 * 270f - 135f;
        float rad = angle * Mathf.Deg2Rad;

        Vector2 needleEnd = center + new Vector2(Mathf.Cos(rad), Mathf.Sin(rad)) * (radius - 8f);

        DrawLine(center, needleEnd, Color.red, 3f);

        GUI.color = Color.white;
        GUI.Label(new Rect(center.x - 40, center.y - 12, 80, 24), $"{rpm:0}", _labelStyle);
    }

    void DrawShadowText(Rect rect, string text, GUIStyle style, Color color)
    {
        Color backup = style.normal.textColor;
        
        style.normal.textColor = Color.black;
        Rect shadowRect = rect;
        shadowRect.x += 2;
        shadowRect.y += 2;
        GUI.Label(shadowRect, text, style);

        style.normal.textColor = color;
        GUI.Label(rect, text, style);

        style.normal.textColor = backup;
    }

    void DrawParam(string label, string value, Color valColor)
    {
        GUILayout.BeginHorizontal();
        var style = new GUIStyle(_labelStyle);
        style.normal.textColor = Color.gray;
        GUILayout.Label(label, style, GUILayout.Width(60));
        
        style.normal.textColor = valColor;
        GUILayout.Label(value, style);
        GUILayout.EndHorizontal();
    }

    void InitStyles()
    {
        if (_whiteTexture == null)
        {
            _whiteTexture = new Texture2D(1, 1);
            _whiteTexture.SetPixel(0, 0, Color.white);
            _whiteTexture.Apply();
        }

        if (_headerStyle == null)
        {
            _headerStyle = new GUIStyle(GUI.skin.label);
            _headerStyle.fontSize = 22;
            _headerStyle.fontStyle = FontStyle.Bold;
            _headerStyle.alignment = TextAnchor.UpperLeft;
        }

        if (_valueStyle == null)
        {
            _valueStyle = new GUIStyle(GUI.skin.label);
            _valueStyle.fontSize = 48;
            _valueStyle.fontStyle = FontStyle.Bold;
            _valueStyle.alignment = TextAnchor.UpperLeft;
        }

        if (_labelStyle == null)
        {
            _labelStyle = new GUIStyle(GUI.skin.label);
            _labelStyle.fontSize = 13;
            _labelStyle.fontStyle = FontStyle.Bold;
            _labelStyle.normal.textColor = Color.white;
        }
    }
    void DrawLine(Vector2 a, Vector2 b, Color color, float width)
    {
        Matrix4x4 m = GUI.matrix;
        Color c = GUI.color;

        GUI.color = color;

        float angle = Vector3.Angle(b - a, Vector2.right);
        if (a.y > b.y) angle = -angle;

        GUIUtility.RotateAroundPivot(angle, a);
        GUI.DrawTexture(new Rect(a.x, a.y - width / 2, (b - a).magnitude, width), _whiteTexture);

        GUI.matrix = m;
        GUI.color = c;
    }
}