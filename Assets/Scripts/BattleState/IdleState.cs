using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// 等待状态：玩家可拖拽笔来蓄力弹射。
/// <para>
/// 坐标转换统一通过 <see cref="ScreenHelper"/> 处理，
/// 兼容 3DPixelCamera 双相机系统与普通单相机场景。
/// </para>
/// </summary>
public class IdleState : IEntityState
{
    private readonly BattleStateMachine stateMachine;
    private readonly BattleContext ctx;

    private bool isDragging;
    private Vector3 dragStartWorldPos;
    private readonly DragInputState _dragInput = new();

    public IdleState(BattleStateMachine stateMachine, BattleContext ctx)
    {
        this.stateMachine = stateMachine;
        this.ctx = ctx;
    }

    // ─── 状态生命周期 ──────────────────────────────────────────────────────────

    public void Enter()
    {
        isDragging = false;
        _dragInput.ForceEnd();
    }

    public void Update()
    {
        var mouse = Mouse.current;
        if (mouse == null) return;

        if (mouse.leftButton.wasPressedThisFrame)
            TryStartDrag(mouse.position.ReadValue());

        if (isDragging)
        {
            UpdateDrag(mouse.position.ReadValue());

            // 统一的 hold/click/cancel 手势
            var evt = _dragInput.Poll(
                mouse.leftButton.wasPressedThisFrame,
                mouse.leftButton.wasReleasedThisFrame,
                mouse.rightButton.wasPressedThisFrame);
            if (evt == DragInputState.Event.End) FinishDrag();
            else if (evt == DragInputState.Event.Cancel) CancelDrag();
        }
    }

    public void Exit() { }

    // ─── 拖拽流程 ──────────────────────────────────────────────────────────────

    /// <summary>尝试在点击位置开始拖拽。射线打到 pen 层级下任意 Collider（barrel 或子零件）都算命中。</summary>
    private void TryStartDrag(Vector2 screenPos)
    {
        // 已在拖拽（含"点击切换"下等待二次点击结束的 Clicked 模式）→ 不重启
        if (isDragging) return;

        CapsuleCollider barrelCol = ctx.pen.penCollider;
        if (barrelCol == null) { Debug.LogWarning("[IdleState] penCollider 为 null，无法响应点击"); return; }

        Camera cam = Camera.main;
        var ray = ScreenHelper.ScreenPointToRay(cam, screenPos);

        // 命中判定：扫描所有碰到的 Collider，筛选"属于 pen 层级下的"——barrel + 所有子零件
        // 复合刚体下，子零件各自带 Collider 参与物理，点哪个 Collider 都算点到笔
        RaycastHit hit = default;
        bool hasHit = false;
        float bestDist = float.MaxValue;
        foreach (var h in Physics.RaycastAll(ray, 1000f))
        {
            var penOnHit = h.collider.GetComponentInParent<PenEntity>();
            if (penOnHit != ctx.pen) continue;
            if (h.distance < bestDist) { bestDist = h.distance; hit = h; hasHit = true; }
        }

        if (!hasHit)
        {
            Debug.Log($"[IdleState] 点击未命中 pen 任意 Collider | 屏幕 {screenPos}。确认 barrel 和所有子零件 prefab 都有 Collider、且不在被忽略的 Physics 层上。");
            return;
        }

        // 直接把命中世界坐标作为"将来 AddForceAtPosition 的 position 参数"
        // 点哪里力就加在哪里，扭矩由 Unity 相对 rb.worldCenterOfMass 自动算，
        // 不再有"胶囊末端夹死、末端视觉区无感"的死区
        ctx.ContactPointWorld = hit.point;

        // 保留一个 offset 日志方便手感调试：−1=笔尾末端附近，+1=笔头末端附近
        // 仅用于开发可视化，不进入物理
        Vector3 penAxis = ctx.pen.GetPenAxis();
        Vector3 capsuleCenterWorld = barrelCol.transform.TransformPoint(barrelCol.center);
        float halfHeight = Mathf.Max((barrelCol.height / 2f) - barrelCol.radius, 0.001f);
        float offsetAlong = Vector3.Dot(hit.point - capsuleCenterWorld, penAxis);
        float offsetNormalized = offsetAlong / halfHeight; // 不再 Clamp，超出 ±1 代表点到了胶囊外的子零件视觉
        Debug.Log($"[IdleState] 命中 {hit.collider.name} | hit.point={hit.point} | 沿轴投影={offsetAlong:F3} / halfHeight={halfHeight:F3} → 相对偏移={offsetNormalized:F2} (−1=笔尾末端, 0=几何中心, +1=笔头末端；超出 ±1 = 点到了突出的子零件)");

        // 开始拖拽
        isDragging = true;
        _dragInput.NotifyBegin();
        dragStartWorldPos = ScreenHelper.ScreenToWorldOnPlane(cam, screenPos, ctx.pen.transform.position);
    }

    /// <summary>拖拽中持续更新弹射方向与力度。</summary>
    private void UpdateDrag(Vector2 screenPos)
    {
        Camera cam = Camera.main;
        Vector3 currentWorldPos = ScreenHelper.ScreenToWorldOnPlane(cam, screenPos, ctx.pen.transform.position);
        Vector3 dragVector = currentWorldPos - dragStartWorldPos;
        dragVector.y = 0f; // 限制在水平面

        if (dragVector.magnitude > ctx.pen.maxDragDistance)
            dragVector = dragVector.normalized * ctx.pen.maxDragDistance;

        ctx.LaunchDirection = -dragVector.normalized;
        ctx.LaunchForce = dragVector.magnitude / ctx.pen.maxDragDistance;
    }

    /// <summary>释放鼠标，执行弹射。</summary>
    private void FinishDrag()
    {
        isDragging = false;
        _dragInput.ForceEnd();
        stateMachine.ChangeState(new ActionState(stateMachine, ctx));
    }

    /// <summary>右键取消蓄力：丢弃当前 drag 向量，回到可重新瞄准的 Idle</summary>
    private void CancelDrag()
    {
        isDragging = false;
        _dragInput.ForceEnd();
        ctx.LaunchDirection = Vector3.zero;
        ctx.LaunchForce = 0f;
    }

}
