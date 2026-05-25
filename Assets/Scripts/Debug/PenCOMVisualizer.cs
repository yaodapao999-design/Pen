using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// 按住指定键（默认 X）在 Game 视图显示笔的质心（世界空间小球）。
/// 瞄准拖拽期间也可由 IdleState 自动打开，显示质心、接触点、偏心杠杆与旋转趋势。
/// 仅战斗阶段生效（PenAssembly.BarrelCollider != null）。
/// 开发调试工具，不参与物理。
/// </summary>
[RequireComponent(typeof(Rigidbody))]
[RequireComponent(typeof(PenAssembly))]
public class PenCOMVisualizer : MonoBehaviour
{
    [Header("显示参数")]
    [SerializeField] private float _markerRadius = 0.045f;
    [SerializeField] private Color _markerColor = new Color(0.25f, 0.9f, 1f, 1f);
    [SerializeField] private float _visualYOffset = 0.045f;

    [Header("瞄准提示")]
    [SerializeField] private float _contactMarkerRadius = 0.038f;
    [SerializeField] private Color _contactMarkerColor = new Color(0.1f, 0.85f, 1f);
    [SerializeField] private Color _leverColor = new Color(1f, 0.85f, 0.1f);
    [SerializeField] private Color _spinHintColor = new Color(1f, 0.45f, 0.05f);
    [SerializeField] private float _leverLineWidth = 0.012f;
    [SerializeField] private float _spinHintLineWidth = 0.016f;
    [SerializeField] private float _spinHintLength = 0.26f;
    [SerializeField] private float _minSpinForce = 0.03f;
    [SerializeField] private float _minLeverLength = 0.03f;

    [Header("激活键")]
    [Tooltip("按住此键显示 COM。使用 Input System 的 Keyboard 通道。")]
    [SerializeField] private Key _holdKey = Key.X;
    [Tooltip("是否在瞄准时自动显示 COM / 接触点 / 力臂调试线。正式 UI 建议关闭，只在调试物理时打开。")]
    [SerializeField] private bool _showDuringAim;

    private Rigidbody _rb;
    private PenAssembly _assembly;
    private GameObject _marker;
    private GameObject _contactMarker;
    private LineRenderer _leverLine;
    private LineRenderer _spinHintLine;

    private Material _markerMaterial;
    private Material _contactMaterial;
    private Material _leverMaterial;
    private Material _spinHintMaterial;

    private bool _aimActive;
    private Vector3 _aimContactWorld;
    private Vector3 _aimLaunchDirection;
    private float _aimForce;

    private void Awake()
    {
        _rb = GetComponent<Rigidbody>();
        _assembly = GetComponent<PenAssembly>();
        ApplyBattleStyleDefaults();
        _marker = CreateMarker("COM_Marker", _markerRadius, _markerColor, out _markerMaterial);
        _contactMarker = CreateMarker("Aim_Contact_Marker", _contactMarkerRadius, _contactMarkerColor, out _contactMaterial);
        _leverLine = CreateLine("Aim_COM_Lever", _leverColor, _leverLineWidth, out _leverMaterial);
        _spinHintLine = CreateLine("Aim_Spin_Hint", _spinHintColor, _spinHintLineWidth, out _spinHintMaterial);

        _marker.SetActive(false);
        SetAimObjectsActive(false);
    }

    private void OnDestroy()
    {
        if (_marker != null) Destroy(_marker);
        if (_contactMarker != null) Destroy(_contactMarker);
        if (_leverLine != null) Destroy(_leverLine.gameObject);
        if (_spinHintLine != null) Destroy(_spinHintLine.gameObject);

        if (_markerMaterial != null) Destroy(_markerMaterial);
        if (_contactMaterial != null) Destroy(_contactMaterial);
        if (_leverMaterial != null) Destroy(_leverMaterial);
        if (_spinHintMaterial != null) Destroy(_spinHintMaterial);
    }

    private void OnDisable()
    {
        _aimActive = false;
        if (_marker != null) _marker.SetActive(false);
        SetAimObjectsActive(false);
    }

    private void Update()
    {
        bool pressed = IsHoldKeyPressed();

        // 战斗阶段守卫：BarrelCollider 只在 BuildBattleView 后非空
        bool inBattle = _assembly.BarrelCollider != null;
        if (!inBattle) _aimActive = false;

        bool shouldShow = (pressed || (_showDuringAim && _aimActive)) && inBattle;
        if (_marker.activeSelf != shouldShow) _marker.SetActive(shouldShow);

        SetAimObjectsActive((pressed || _showDuringAim) && _aimActive && inBattle);
    }

    private void LateUpdate()
    {
        // LateUpdate 在物理步之后，读到的是本帧 Aggregator 设好的 COM
        if (!_marker.activeSelf) return;

        Vector3 comWorld = _rb.worldCenterOfMass;
        Vector3 comVisual = comWorld + Vector3.up * _visualYOffset;
        _marker.transform.position = comVisual;

        if (_aimActive)
            UpdateAimGeometry(comWorld, comVisual);
    }

    public void BeginAimVisualization(Vector3 contactPointWorld)
    {
        _aimActive = true;
        _aimContactWorld = contactPointWorld;
        _aimLaunchDirection = Vector3.zero;
        _aimForce = 0f;
        SetAimObjectsActive(_showDuringAim);
    }

    public void UpdateAimVisualization(Vector3 contactPointWorld, Vector3 launchDirection, float force)
    {
        _aimActive = true;
        _aimContactWorld = contactPointWorld;
        _aimLaunchDirection = launchDirection;
        _aimForce = Mathf.Clamp01(force);
        SetAimObjectsActive(_showDuringAim);
    }

    public void EndAimVisualization()
    {
        _aimActive = false;
        SetAimObjectsActive(false);
        if (!IsHoldKeyPressed() && _marker != null)
            _marker.SetActive(false);
    }

    private void UpdateAimGeometry(Vector3 comWorld, Vector3 comVisual)
    {
        Vector3 contactVisual = _aimContactWorld + Vector3.up * _visualYOffset;
        _contactMarker.transform.position = contactVisual;
        SetLine(_leverLine, comVisual, contactVisual);
        UpdateSpinHint(comWorld, comVisual, contactVisual);
    }

    private void UpdateSpinHint(Vector3 comWorld, Vector3 comVisual, Vector3 contactVisual)
    {
        Vector3 lever = _aimContactWorld - comWorld;
        lever.y = 0f;
        Vector3 launchDir = _aimLaunchDirection;
        launchDir.y = 0f;

        if (_aimForce < _minSpinForce ||
            lever.sqrMagnitude < _minLeverLength * _minLeverLength ||
            launchDir.sqrMagnitude < 0.0001f)
        {
            if (_spinHintLine.gameObject.activeSelf) _spinHintLine.gameObject.SetActive(false);
            return;
        }

        if (!_spinHintLine.gameObject.activeSelf) _spinHintLine.gameObject.SetActive(true);

        Vector3 leverDir = lever.normalized;
        Vector3 launchDirNorm = launchDir.normalized;
        float torqueSign = Mathf.Sign(Vector3.Dot(Vector3.up, Vector3.Cross(leverDir, launchDirNorm)));
        if (Mathf.Approximately(torqueSign, 0f)) torqueSign = 1f;

        Vector3 tangent = Vector3.Cross(Vector3.up * torqueSign, leverDir).normalized;
        float hintLength = _spinHintLength * Mathf.Lerp(0.45f, 1f, _aimForce);
        Vector3 start = Vector3.Lerp(comVisual, contactVisual, 0.62f);
        Vector3 tip = start + tangent * hintLength;
        float headLength = hintLength * 0.32f;
        Vector3 headA = tip + (-tangent + leverDir * 0.55f).normalized * headLength;
        Vector3 headB = tip + (-tangent - leverDir * 0.55f).normalized * headLength;

        _spinHintLine.positionCount = 5;
        _spinHintLine.SetPosition(0, start);
        _spinHintLine.SetPosition(1, tip);
        _spinHintLine.SetPosition(2, headA);
        _spinHintLine.SetPosition(3, tip);
        _spinHintLine.SetPosition(4, headB);
    }

    private void SetAimObjectsActive(bool active)
    {
        if (_contactMarker != null && _contactMarker.activeSelf != active)
            _contactMarker.SetActive(active);
        if (_leverLine != null && _leverLine.gameObject.activeSelf != active)
            _leverLine.gameObject.SetActive(active);
        if (_spinHintLine != null && _spinHintLine.gameObject.activeSelf != active)
            _spinHintLine.gameObject.SetActive(active);
    }

    private static void SetLine(LineRenderer line, Vector3 from, Vector3 to)
    {
        line.positionCount = 2;
        line.SetPosition(0, from);
        line.SetPosition(1, to);
    }

    private bool IsHoldKeyPressed()
    {
        return Keyboard.current != null &&
               Keyboard.current[_holdKey].isPressed;
    }

    private void ApplyBattleStyleDefaults()
    {
        if (Approximately(_markerColor, new Color(1f, 0.2f, 0.2f, 1f)))
            _markerColor = new Color(0.25f, 0.9f, 1f, 1f);
        if (Mathf.Abs(_markerRadius - 0.1f) < 0.001f ||
            Mathf.Abs(_markerRadius - 0.085f) < 0.001f)
            _markerRadius = 0.045f;
        if (Mathf.Abs(_contactMarkerRadius - 0.065f) < 0.001f)
            _contactMarkerRadius = 0.038f;
        if (Mathf.Abs(_leverLineWidth - 0.025f) < 0.001f)
            _leverLineWidth = 0.012f;
        if (Mathf.Abs(_spinHintLineWidth - 0.03f) < 0.001f)
            _spinHintLineWidth = 0.016f;
        if (Mathf.Abs(_spinHintLength - 0.35f) < 0.001f)
            _spinHintLength = 0.26f;
        if (Mathf.Abs(_visualYOffset - 0.075f) < 0.001f)
            _visualYOffset = 0.045f;
    }

    private GameObject CreateMarker(string label, float radius, Color color, out Material material)
    {
        var go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        go.name = $"{label} ({name})";

        // 去掉 collider：不参与物理
        var col = go.GetComponent<Collider>();
        if (col != null) Destroy(col);

        go.transform.SetParent(null, false);
        go.transform.localScale = Vector3.one * (radius * 2f);

        material = CreateMaterial(color);
        go.GetComponent<MeshRenderer>().sharedMaterial = material;

        return go;
    }

    private LineRenderer CreateLine(string label, Color color, float width, out Material material)
    {
        var go = new GameObject($"{label} ({name})");
        go.transform.SetParent(null, false);

        var line = go.AddComponent<LineRenderer>();
        line.useWorldSpace = true;
        line.positionCount = 0;
        line.widthMultiplier = width;
        line.numCapVertices = 4;
        line.numCornerVertices = 4;
        line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        line.receiveShadows = false;

        material = CreateMaterial(color);
        line.sharedMaterial = material;
        line.startColor = color;
        line.endColor = color;

        return line;
    }

    private static Material CreateMaterial(Color color)
    {
        Shader shader = Shader.Find("Universal Render Pipeline/Unlit");
        if (shader == null) shader = Shader.Find("Unlit/Color");
        if (shader == null) shader = Shader.Find("Sprites/Default");

        var mat = new Material(shader);
        if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", color);
        if (mat.HasProperty("_Color")) mat.SetColor("_Color", color);
        mat.color = color;
        return mat;
    }

    private static bool Approximately(Color a, Color b)
    {
        const float eps = 0.01f;
        return Mathf.Abs(a.r - b.r) < eps &&
               Mathf.Abs(a.g - b.g) < eps &&
               Mathf.Abs(a.b - b.b) < eps &&
               Mathf.Abs(a.a - b.a) < eps;
    }
}
