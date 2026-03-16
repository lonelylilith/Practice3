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

        float hudWidth = 350;
        float hudHeight = 260; 
        float margin = 20;

        Rect panelRect = new Rect(Screen.width - hudWidth - margin, Screen.height - hudHeight - margin, hudWidth, hudHeight);

        GUI.color = new Color(0, 0, 0, 0.7f);
        GUI.DrawTexture(panelRect, _whiteTexture);
        GUI.color = Color.white;

        GUILayout.BeginArea(panelRect);
        
        DrawShadowText(new Rect(10, 5, 300, 30), "RACE TELEMETRY", _headerStyle, Color.white);

        float speedAlongForward = Vector3.Dot(_rb.linearVelocity, transform.forward);
        float kmh = speedAlongForward * 3.6f;
        DrawShadowText(new Rect(10, 40, 150, 60), $"{Mathf.Abs(kmh):0}", _valueStyle, Color.cyan);
        GUI.Label(new Rect(160, 65, 50, 20), "KM/H", _labelStyle);

        float rpm = _engine.CurrentRpm;
        float maxRpm = _engine.MaxRpm;
        float rpmPercent = Mathf.Clamp01(rpm / maxRpm);

        Rect rpmBarRect = new Rect(10, 100, 330, 24); 
        GUI.color = new Color(0.2f, 0.2f, 0.2f, 1f);
        GUI.DrawTexture(rpmBarRect, _whiteTexture);

        Rect rpmFillRect = new Rect(10, 100, 330 * rpmPercent, 24);
        Color rpmColor = Color.Lerp(Color.green, Color.red, rpmPercent);
        if (rpm > _defaultConfig.maxRpm - 500) rpmColor = new Color(1f, 0.2f, 0f); 
        GUI.color = rpmColor;
        GUI.DrawTexture(rpmFillRect, _whiteTexture);
        GUI.color = Color.white;

        DrawShadowText(new Rect(15, 102, 300, 20), $"{rpm:0} RPM", _labelStyle, Color.white);

        GUILayout.BeginArea(new Rect(10, 140, 180, 80)); 
        GUILayout.Space(5);
        DrawParam("Torque:", $"{_engine.CurrentTorque:0} Nm", _engine.CurrentTorque > 300 ? Color.green : Color.white);
        DrawParam("Throttle:", $"{_throttleInput:F2}", Color.white);
        DrawParam("Steer:", $"{_steerInput:F2}", Color.white);
        GUILayout.EndArea();

        Rect gForceRect = new Rect(220, 140, 80, 80); 
        GUI.color = new Color(1, 1, 1, 0.1f);
        GUI.DrawTexture(gForceRect, _whiteTexture);
        
        Vector2 center = gForceRect.center;
        
        float visualScale = 0.02f;
        float visFy = _telemetryFrontFySum; 
        float visFx = _telemetryRearFxSum;

        Vector2 forcePos = center + new Vector2(visFy * visualScale, -visFx * visualScale);
        forcePos.x = Mathf.Clamp(forcePos.x, gForceRect.x, gForceRect.xMax - 4);
        forcePos.y = Mathf.Clamp(forcePos.y, gForceRect.y, gForceRect.yMax - 4);

        GUI.color = Color.yellow;
        GUI.DrawTexture(new Rect(forcePos.x - 2, forcePos.y - 2, 4, 4), _whiteTexture);
        GUI.color = Color.white;
        
        GUI.Label(new Rect(220, 225, 80, 20), "G-FORCE", _labelStyle);

        if (_isHandbrake)
        {
            Rect hbRect = new Rect(10, 230, 150, 24); 
            GUI.color = Color.red;
            GUI.DrawTexture(hbRect, _whiteTexture);
            GUI.color = Color.white;
            
            var centerStyle = new GUIStyle(_labelStyle);
            centerStyle.alignment = TextAnchor.MiddleCenter;
            GUI.Label(hbRect, "HANDBRAKE ACTIVE", centerStyle);
        }

        GUILayout.EndArea();
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
}