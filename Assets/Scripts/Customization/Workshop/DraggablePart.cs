using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// 挂在抽屉里散落的零件 GameObject 上
/// 玩家点击后可拖拽，松手时检测最近可用 Socket 并吸附
/// </summary>
public class DraggablePart : MonoBehaviour
{
    [Header("配置")]
    public PenPartData PartData;
    public float SnapDistance = 0.3f;   // 吸附检测半径

    [Header("引用")]
    public PenAssembly TargetAssembly;

    private Camera _cam;
    private bool _isDragging;
    private Vector3 _dragOffset;
    private float _dragDepth;
    private SocketHighlighter _highlighter;

    private void Awake()
    {
        _cam = Camera.main;
        _highlighter = FindFirstObjectByType<SocketHighlighter>();
    }

    private void OnMouseDown()
    {
        _dragDepth = Vector3.Distance(_cam.transform.position, transform.position);
        _dragOffset = transform.position - GetMouseWorldPos();
        _isDragging = true;
        _highlighter?.ShowCompatible(PartData);
    }

    private void OnMouseDrag()
    {
        if (!_isDragging) return;
        transform.position = GetMouseWorldPos() + _dragOffset;
    }

    private void OnMouseUp()
    {
        if (!_isDragging) return;
        _isDragging = false;
        _highlighter?.HideAll();

        TrySnap();
    }

    private void TrySnap()
    {
        if (TargetAssembly == null) return;

        PartSocket best = null;
        float bestDist = SnapDistance;

        foreach (var socket in TargetAssembly.GetComponentsInChildren<PartSocket>())
        {
            if (!socket.CanAccept(PartData)) continue;
            float dist = Vector3.Distance(transform.position, socket.transform.position);
            if (dist < bestDist)
            {
                bestDist = dist;
                best = socket;
            }
        }

        if (best != null)
        {
            TargetAssembly.AddPart(PartData, best);
            Destroy(gameObject);
        }
    }

    private Vector3 GetMouseWorldPos()
    {
        var mousePos = Mouse.current.position.ReadValue();
        var screenPos = new Vector3(mousePos.x, mousePos.y, _dragDepth);
        return _cam.ScreenToWorldPoint(screenPos);
    }
}
