using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// 按住指定键（默认 X）在 Game 视图显示笔的质心（世界空间小球）。
/// 仅战斗阶段生效（PenAssembly.BarrelCollider != null）。
/// 开发调试工具，不参与物理。
/// </summary>
[RequireComponent(typeof(Rigidbody))]
[RequireComponent(typeof(PenAssembly))]
public class PenCOMVisualizer : MonoBehaviour
{
    [Header("显示参数")]
    [SerializeField] private float _markerRadius = 0.1f;
    [SerializeField] private Color _markerColor = new Color(1f, 0.2f, 0.2f);

    [Header("激活键")]
    [Tooltip("按住此键显示 COM。使用 Input System 的 Keyboard 通道。")]
    [SerializeField] private Key _holdKey = Key.X;

    private Rigidbody _rb;
    private PenAssembly _assembly;
    private GameObject _marker;

    private void Awake()
    {
        _rb = GetComponent<Rigidbody>();
        _assembly = GetComponent<PenAssembly>();
        _marker = CreateMarker();
        _marker.SetActive(false);
    }

    private void OnDestroy()
    {
        if (_marker != null) Destroy(_marker);
    }

    private void Update()
    {
        bool pressed = Keyboard.current != null &&
                       Keyboard.current[_holdKey].isPressed;

        // 战斗阶段守卫：BarrelCollider 只在 BuildBattleView 后非空
        bool inBattle = _assembly.BarrelCollider != null;

        bool shouldShow = pressed && inBattle;
        if (_marker.activeSelf != shouldShow) _marker.SetActive(shouldShow);
    }

    private void LateUpdate()
    {
        // LateUpdate 在物理步之后，读到的是本帧 Aggregator 设好的 COM
        if (_marker.activeSelf)
            _marker.transform.position = _rb.worldCenterOfMass;
    }

    private GameObject CreateMarker()
    {
        var go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        go.name = $"COM_Marker ({name})";

        // 去掉 collider：不参与物理
        var col = go.GetComponent<Collider>();
        if (col != null) Destroy(col);

        go.transform.SetParent(null, false);
        go.transform.localScale = Vector3.one * (_markerRadius * 2f);

        var mat = new Material(Shader.Find("Unlit/Color"));
        mat.color = _markerColor;
        go.GetComponent<MeshRenderer>().sharedMaterial = mat;

        return go;
    }
}
