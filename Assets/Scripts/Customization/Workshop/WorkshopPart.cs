using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// 改装场景零件统一交互
/// - Assembled：kinematic，跟随 socket
/// - Dragging：动态 Rigidbody + MovePosition，碰撞体生效不穿墙
/// - Loose：动态 Rigidbody + 重力，抽屉墙壁自然围住
///
/// 拖拽机制：
///   保持 Rigidbody 动态（非 kinematic），关闭重力、加高阻尼，
///   用 Rigidbody.MovePosition 在 FixedUpdate 中驱动移动。
///   物理引擎自动处理碰撞，零件不会穿墙。
/// </summary>
public class WorkshopPart : MonoBehaviour
{
    public enum PartState { Assembled, Dragging, Loose }

    [Header("配置")]
    public PenPartData PartData;
    public float SnapDistance = 0.15f;
    public float DragLiftHeight = 0.03f;
    public float DragDamping = 50f;
    public float DragSmoothSpeed = 30f;

    [Header("引用")]
    public PenAssembly TargetAssembly;
    public WorkshopSlot Slot;
    public BoxCollider DragArea;

    public PartState State { get; private set; } = PartState.Loose;

    private Rigidbody _rb;
    private Collider _col;
    private Camera _cam;
    private SocketHighlighter _highlighter;
    private bool _isDragging;
    private Plane _dragPlane;
    private Vector3 _dragOffset;
    private Vector3 _dragTarget;
    private PartState _stateBeforeDrag;
    private float _originalDrag;
    private float _originalAngularDrag;

    // Assembled 状态专用
    private PartSocket _originalSocket;
    private PenPartInstance _linkedInstance;

    private void Awake()
    {
        _rb = GetComponent<Rigidbody>();
        _col = GetComponent<Collider>();
        _cam = Camera.main;
        _highlighter = FindFirstObjectByType<SocketHighlighter>();
        _originalDrag = _rb.linearDamping;
        _originalAngularDrag = _rb.angularDamping;
    }

    public void SetAssembled(PenAssembly assembly, PenPartInstance linkedInstance)
    {
        TargetAssembly = assembly;
        _linkedInstance = linkedInstance;
        State = PartState.Assembled;
        _rb.isKinematic = true;
        _rb.Sleep();

        if (linkedInstance?.AttachedSocket != null)
        {
            transform.SetParent(linkedInstance.AttachedSocket.transform);
            transform.localPosition = Vector3.zero;
            transform.localRotation = Quaternion.identity;
        }
    }

    public void SetLoose()
    {
        State = PartState.Loose;
        _linkedInstance = null;
        transform.SetParent(null);
        _rb.isKinematic = false;
        _rb.useGravity = true;
        _rb.linearDamping = _originalDrag;
        _rb.angularDamping = _originalAngularDrag;
    }

    // ─── 输入处理 ─────────────────────────────────────────────────────────────

    private void Update()
    {
        if (_cam == null) return;
        var mouse = Mouse.current;
        if (mouse == null) return;

        // 按下：RaycastAll 穿透抽屉壁，找最近的 WorkshopPart
        if (!_isDragging && mouse.leftButton.wasPressedThisFrame)
        {
            var ray = _cam.ScreenPointToRay(mouse.position.ReadValue());
            float closestDist = float.MaxValue;
            WorkshopPart closest = null;
            foreach (var hit in Physics.RaycastAll(ray))
            {
                var wp = hit.collider.GetComponent<WorkshopPart>();
                if (wp != null && hit.distance < closestDist)
                {
                    closestDist = hit.distance;
                    closest = wp;
                }
            }

            if (closest == this)
                HandleMouseDown();
        }

        if (!_isDragging) return;

        // 持续拖拽：计算目标位置（实际移动在 FixedUpdate 由 MovePosition 执行）
        if (mouse.leftButton.isPressed)
        {
            var ray = _cam.ScreenPointToRay(mouse.position.ReadValue());
            if (_dragPlane.Raycast(ray, out float enter))
            {
                Vector3 target = ray.GetPoint(enter) + _dragOffset;

                if (DragArea != null)
                {
                    var b = DragArea.bounds;
                    target.x = Mathf.Clamp(target.x, b.min.x, b.max.x);
                    target.z = Mathf.Clamp(target.z, b.min.z, b.max.z);
                }

                target.y += DragLiftHeight;
                _dragTarget = target;
            }

            _highlighter?.UpdateNearest(transform.position, SnapDistance);
        }

        // 松手
        if (mouse.leftButton.wasReleasedThisFrame)
            HandleMouseUp();
    }

    private void FixedUpdate()
    {
        if (_isDragging)
            _rb.MovePosition(_dragTarget);
    }

    private void HandleMouseDown()
    {
        _stateBeforeDrag = State;

        if (_stateBeforeDrag == PartState.Assembled)
            _originalSocket = _linkedInstance?.AttachedSocket;

        // 创建拖拽平面（部件当前高度的水平面）
        _dragPlane = new Plane(Vector3.up, transform.position);

        // 在平面上计算拖拽偏移，防止零件跳到鼠标中心
        var ray = _cam.ScreenPointToRay(Mouse.current.position.ReadValue());
        if (_dragPlane.Raycast(ray, out float enter))
            _dragOffset = transform.position - ray.GetPoint(enter);
        else
            _dragOffset = Vector3.zero;

        if (_stateBeforeDrag == PartState.Assembled)
            DetachFromPen();

        // 动态 Rigidbody：关重力、加高阻尼，MovePosition 驱动且碰撞生效
        _rb.isKinematic = false;
        _rb.useGravity = false;
        _rb.linearDamping = DragDamping;
        _rb.angularDamping = DragDamping;
        _rb.interpolation = RigidbodyInterpolation.Interpolate;
        _rb.collisionDetectionMode = CollisionDetectionMode.Continuous;
        _dragTarget = transform.position;

        _isDragging = true;
        State = PartState.Dragging;

        _highlighter?.ShowCompatible(PartData);
    }

    private void HandleMouseUp()
    {
        _isDragging = false;
        _highlighter?.HideAll();

        // 恢复物理参数
        _rb.useGravity = true;
        _rb.linearDamping = _originalDrag;
        _rb.angularDamping = _originalAngularDrag;
        _rb.interpolation = RigidbodyInterpolation.None;
        _rb.collisionDetectionMode = CollisionDetectionMode.Discrete;

        bool inSlot = Slot != null && Slot.Contains(transform.position);

        if (inSlot)
        {
            if (!TrySnap())
            {
                if (_stateBeforeDrag == PartState.Assembled
                    && _originalSocket != null
                    && _originalSocket.CanAccept(PartData))
                {
                    SnapToSocket(_originalSocket);
                }
                else
                {
                    // 无可用 socket → 恢复物理，抽屉墙壁围住
                    State = PartState.Loose;
                }
            }
        }
        else
        {
            // Slot 外 → 恢复物理，抽屉碰撞体自然阻挡
            State = PartState.Loose;
            _linkedInstance = null;
            transform.SetParent(null);
        }

        _originalSocket = null;
    }

    // ─── 核心逻辑 ────────────────────────────────────────────────────────────

    private bool TrySnap()
    {
        if (TargetAssembly == null) return false;

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

        if (best == null) return false;

        SnapToSocket(best);
        return true;
    }

    private void SnapToSocket(PartSocket socket)
    {
        if (!TargetAssembly.AddPart(PartData, socket))
        {
            Debug.LogWarning($"WorkshopPart: AddPart 失败，socket={socket.SocketType}");
            return;
        }

        _linkedInstance = socket.OccupiedBy;
        if (_linkedInstance?.GameObject != null)
            _linkedInstance.GameObject.SetActive(false);

        transform.SetParent(socket.transform);
        transform.localPosition = Vector3.zero;
        transform.localRotation = Quaternion.identity;

        _rb.collisionDetectionMode = CollisionDetectionMode.Discrete;
        _rb.isKinematic = true;
        State = PartState.Assembled;
    }

    private void DetachFromPen()
    {
        if (TargetAssembly == null) return;

        PenPartInstance target = null;
        bool linkedStillInAssembly = false;
        if (_linkedInstance != null)
            foreach (var p in TargetAssembly.Parts)
                if (p == _linkedInstance) { linkedStillInAssembly = true; break; }

        if (linkedStillInAssembly)
        {
            target = _linkedInstance;
        }
        else
        {
            foreach (var p in TargetAssembly.Parts)
            {
                if (p.Data == PartData) { target = p; break; }
            }
        }

        if (target != null)
        {
            TargetAssembly.DetachPart(target);
            if (target.GameObject != null)
                Destroy(target.GameObject);
        }

        _linkedInstance = null;
        transform.SetParent(null);
    }
}
