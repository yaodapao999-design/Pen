using UnityEngine;
using UnityEngine.InputSystem;

public class IdleState : IEntityState
{
    private readonly BattleStateMachine stateMachine;
    private readonly BattleContext ctx;

    // 拖拽状态
    private bool isDragging;
    private Vector3 dragStartWorldPos;

    // 可调参数
    private const float MaxDragDistance = 2.0f; // 最大拖拽距离（单位：米）

    public IdleState(BattleStateMachine stateMachine, BattleContext ctx)
    {
        this.stateMachine = stateMachine;
        this.ctx = ctx;
    }

    public void Enter() 
    {
        isDragging = false;
        Debug.Log("进入了等待状态，现在可以准备弹射了");
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

    private void TryStartDrag(Vector2 screenPos)
    {
        Ray ray = Camera.main.ScreenPointToRay(screenPos);

        // 只响应点击在笔的碰撞体上
        if (!ctx.penCollider.Raycast(ray, out RaycastHit hit, Mathf.Infinity))
            return;

        isDragging = true;
        dragStartWorldPos = GetWorldPositionOnPenPlane(screenPos);

        // 计算点击点相对于笔重心的偏移
        // 转换到本地坐标，消除笔的旋转影响
        Vector3 localHitPoint = ctx.penObject.transform
            .InverseTransformPoint(hit.point);

        // 根据 CapsuleCollider 的朝向轴取对应分量
        float rawOffset = GetOffsetAlongCapsuleAxis(localHitPoint);

        // 归一化到 -1 ~ 1（除以胶囊体半高）
        float halfHeight = (ctx.penCollider.height / 2f) - ctx.penCollider.radius;
        ctx.contactOffset = Mathf.Clamp(rawOffset / halfHeight, -1f, 1f);

        Debug.Log($"点击偏移: {ctx.contactOffset:F3}（-1=笔尾, 0=中心, 1=笔头）");
    }

    private void UpdateDrag(Vector2 screenPos)
    {
        Vector3 currentWorldPos = GetWorldPositionOnPenPlane(screenPos);
        Vector3 dragVector = currentWorldPos - dragStartWorldPos;

        // 限制最大拖拽距离
        if (dragVector.magnitude > MaxDragDistance)
            dragVector = dragVector.normalized * MaxDragDistance;

        // 弹弓逻辑：弹射方向与拖拽方向相反
        ctx.launchDirection = -dragVector.normalized;
        // 力度为拖拽距离的比例 0~1，ActionState 负责乘以力的系数
        ctx.launchForce = dragVector.magnitude / MaxDragDistance;
    }

    private void FinishDrag()
    {
        isDragging = false;
        Debug.Log($"弹射！方向: {ctx.launchDirection}, 力度: {ctx.launchForce:F2}, 偏移: {ctx.contactOffset:F3}");
        stateMachine.ChangeState(new ActionState(stateMachine, ctx));
    }

    /// <summary>
    /// 将屏幕坐标投影到笔所在的水平面（俯视角专用）
    /// </summary>
    private Vector3 GetWorldPositionOnPenPlane(Vector2 screenPos)
    {
        Ray ray = Camera.main.ScreenPointToRay(screenPos);
        Plane penPlane = new Plane(Vector3.up, ctx.penObject.transform.position);
        penPlane.Raycast(ray, out float distance);
        return ray.GetPoint(distance);
    }

    /// <summary>
    /// 根据 CapsuleCollider 的 direction 轴取本地坐标的对应分量
    /// 0 = X轴, 1 = Y轴, 2 = Z轴
    /// </summary>
    private float GetOffsetAlongCapsuleAxis(Vector3 localPoint)
    {
        return ctx.penCollider.direction switch
        {
            0 => localPoint.x,
            1 => localPoint.y,
            2 => localPoint.z,
            _ => localPoint.y
        };
    }

    public void Exit()
    {
        Debug.Log("离开了等待状态");
    }
}
