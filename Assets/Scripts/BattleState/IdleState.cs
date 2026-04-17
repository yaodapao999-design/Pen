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

    public IdleState(BattleStateMachine stateMachine, BattleContext ctx)
    {
        this.stateMachine = stateMachine;
        this.ctx = ctx;
    }

    // ─── 状态生命周期 ──────────────────────────────────────────────────────────

    public void Enter()
    {
        isDragging = false;
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
            if (mouse.leftButton.wasReleasedThisFrame)
                FinishDrag();
        }
    }

    public void Exit() { }

    // ─── 拖拽流程 ──────────────────────────────────────────────────────────────

    /// <summary>尝试在点击位置开始拖拽，仅当点击落在笔的 Viewport 判定范围内才生效。</summary>
    private void TryStartDrag(Vector2 screenPos)
    {
        CapsuleCollider col = ctx.pen.penCollider;
        if (col == null) return;

        Camera cam = Camera.main;
        Vector2 clickVP = ScreenHelper.ScreenToViewport(screenPos);

        // 笔中心与笔尖的 Viewport 坐标
        Vector2 penVP = ScreenHelper.WorldToViewport2D(cam, ctx.pen.rb.worldCenterOfMass);
        Vector2 tipVP = ScreenHelper.WorldToViewport2D(cam,
            ctx.pen.rb.worldCenterOfMass + ctx.pen.GetPenAxis() * (col.height / 2f));

        // 命中判定（Viewport 空间）
        float penHalfLenVP = Vector2.Distance(penVP, tipVP);
        float clickRadius = Mathf.Max(penHalfLenVP, 0.03f) + 0.03f;

        if (Vector2.Distance(clickVP, penVP) > clickRadius)
            return;

        // 开始拖拽
        isDragging = true;
        dragStartWorldPos = ScreenHelper.ScreenToWorldOnPlane(cam, screenPos, ctx.pen.transform.position);

        // 接触偏移（-1 = 笔尾，0 = 中心，1 = 笔头）
        ctx.ContactOffset = CalculateContactOffset(clickVP, penVP, tipVP, penHalfLenVP);
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
        stateMachine.ChangeState(new ActionState(stateMachine, ctx));
    }

    // ─── 工具 ──────────────────────────────────────────────────────────────────

    /// <summary>
    /// 在 Viewport 空间沿笔轴方向计算点击偏移。
    /// 返回值范围 [-1, 1]，-1 = 笔尾，0 = 中心，1 = 笔头。
    /// </summary>
    private static float CalculateContactOffset(Vector2 clickVP, Vector2 penVP, Vector2 tipVP, float penHalfLenVP)
    {
        Vector2 axisDir = tipVP - penVP;
        float axisDirLen = axisDir.magnitude;
        if (axisDirLen < 0.001f)
            return 0f;

        float projection = Vector2.Dot(clickVP - penVP, axisDir / axisDirLen);
        return Mathf.Clamp(projection / Mathf.Max(penHalfLenVP, 0.001f), -1f, 1f);
    }
}
