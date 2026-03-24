using UnityEngine;

public class ActionState : IEntityState
{
    private readonly BattleStateMachine stateMachine;
    private readonly BattleContext ctx;

    [Header("弹射参数")]
    private const float BaseForce = 10f;        // 基础弹射力
    private const float StopVelocityThreshold = 0.05f;  // 低于此速度判定为停止
    private const float StopAngularThreshold = 0.05f;   // 低于此角速度判定为停止
    private const float StopCheckDelay = 0.5f;  // 弹出后多久开始检测停止（防止刚弹出就判定）

    private float elapsedTime;

    public ActionState(BattleStateMachine stateMachine, BattleContext ctx)
    {
        this.stateMachine = stateMachine;
        this.ctx = ctx;
    }

    public void Enter()
    {
        elapsedTime = 0f;
        ExecuteLaunch();
    }

    private void ExecuteLaunch()
    {
        Rigidbody rb = ctx.penRigidbody;
        rb.linearVelocity = Vector3.zero;
        rb.angularVelocity = Vector3.zero;

        // 计算力的作用点
        // contactOffset 范围 -1 ~ 1，沿笔的长轴方向偏移
        // offset = 0 → 作用在重心 → 纯平移
        // offset = ±1 → 作用在笔头/笔尾 → 平移 + 最大旋转
        Vector3 penAxis = GetPenAxis();
        float halfHeight = (ctx.penCollider.height / 2f) - ctx.penCollider.radius;
        Vector3 forcePosition = rb.worldCenterOfMass + penAxis * (ctx.contactOffset * halfHeight);

        // 施加冲量
        Vector3 force = ctx.launchDirection * (BaseForce * ctx.launchForce);
        rb.AddForceAtPosition(force, forcePosition, ForceMode.Impulse);

        Debug.Log($"施力点偏移: {ctx.contactOffset:F3} | 力: {force.magnitude:F2} | 作用点: {forcePosition}");
    }

    public void Update()
    {
        elapsedTime += Time.deltaTime;

        // 延迟后开始检测笔是否停止运动
        if (elapsedTime < StopCheckDelay) return;

        Rigidbody rb = ctx.penRigidbody;
        bool hasStopped = rb.linearVelocity.magnitude < StopVelocityThreshold &&
                          rb.angularVelocity.magnitude < StopAngularThreshold;

        if (hasStopped)
        {
            rb.linearVelocity = Vector3.zero;
            rb.angularVelocity = Vector3.zero;
            Debug.Log("笔停止运动，回到等待状态");
            stateMachine.ChangeState(new IdleState(stateMachine, ctx));
        }
    }

    public void Exit()
    {
        Debug.Log("离开弹射状态");
    }

    /// <summary>
    /// 获取笔的长轴方向（世界坐标）
    /// 对应 CapsuleCollider.direction: 0=X轴, 1=Y轴, 2=Z轴
    /// </summary>
    private Vector3 GetPenAxis()
    {
        Transform t = ctx.penObject.transform;
        return ctx.penCollider.direction switch
        {
            0 => t.right,
            1 => t.up,
            2 => t.forward,
            _ => t.right
        };
    }
}
