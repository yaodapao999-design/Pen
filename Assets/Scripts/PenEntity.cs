using UnityEngine;
using UnityEngine.Serialization;

/// <summary>
/// 笔的物理本体。
///
/// 设计原则（冰球式俯视物理）：
///   - Rigidbody.constraints 锁 X+Z 两轴 pitch/roll，只保留 Y yaw：笔在桌上只会滑+转，
///     不会翻/立起来/卷滚（没有这个约束，偏心冲量或飞出桌后会让笔翻滚）。
///   - mass / centerOfMass / inertiaTensor / PhysicsMaterial 由 <see cref="PenPhysicsAggregator"/>
///     从装配数据聚合
///   - 空中零阻尼，减速靠桌面 PhysicsMaterial 摩擦
///   - 发射用"速度驱动的冲量"：Impulse = direction × maxLaunchVelocity × mass × force × 装配加成
///     → 所有质量的笔在满拖时都以相同的速度离手（手感公平）
///     → 重笔动量大（撞对手推得远）、轻笔动量小（撞对手推不动）
///     这是所有俯视动量撞击游戏（冰壶、Beer Pong、俄罗斯台球）的标准做法
/// </summary>
public class PenEntity : MonoBehaviour
{
    // ─── 弹射参数 ────────────────────────────────────────────────────────────
    [Header("弹射参数")]

    [Tooltip("满拖时笔的离手速度 (m/s)。\n" +
             "这是速度而不是冲量：冲量 = v × mass × force × 装配加成，使所有质量的笔都以此速度离手。\n" +
             "典型范围 6-12。桌面 8m 宽时，v=8.5 给满拖约 2.9m 行程。")]
    [FormerlySerializedAs("baseForce")]
    [FormerlySerializedAs("maxLaunchSpeed")]
    [FormerlySerializedAs("maxLaunchImpulse")]
    public float maxLaunchVelocity = 8.5f;

    [Tooltip("玩家拖拽多远算'满拖'(世界单位 m)。按桌面大小设：\n" +
             "桌子 1m 宽建议 0.3；桌子 3m 宽建议 1.0。\n" +
             "拖拽距离 / 这个值 = dragForce ∈ [0, 1]")]
    public float maxDragDistance = 2.0f;

    // ─── 停止检测阈值（一般不用调） ──────────────────────────────────────────
    [Header("停止检测")]

    [Tooltip("线速度低于这个值视为'已停下' (m/s)。用于战斗结束检测")]
    public float stopVelocityThreshold = 0.05f;

    [Tooltip("角速度低于这个值视为'已停下' (rad/s)。用于战斗结束检测")]
    public float stopAngularThreshold = 0.05f;

    [Tooltip("弹射后多久才开始检测是否已停 (秒)。防止刚弹射就立即判停")]
    public float stopCheckDelay = 0.5f;

    // ─── 掉落检测 ──────────────────────────────────────────────────────────
    [Header("掉落检测")]

    [Tooltip("笔 Y 坐标低于这个值视为'掉出桌外'(= 失败)。按你桌面高度配")]
    public float fallYThreshold = -2f;

    // ─── 运行时（不在 Inspector） ──────────────────────────────────────────
    public Rigidbody rb { get; private set; }
    public CapsuleCollider penCollider => Assembly != null ? Assembly.BarrelCollider : null;
    public PenAssembly Assembly { get; private set; }
    public bool HasFallen => rb != null && rb.position.y < fallYThreshold;

    /// <summary>
    /// 发射瞬间事件，参数 force ∈ [0,1]。供反馈层订阅（PenLaunchFeedback 等），
    /// 把"发射时刻"变成一级事件，避免把手感层硬编码进 PenEntity。
    /// </summary>
    public event System.Action<float> OnLaunched;

    private void Awake()
    {
        rb = GetComponent<Rigidbody>();
        Assembly = GetComponent<PenAssembly>();

        // 空中无阻尼，减速全交给桌面摩擦 + PenTableStability
        rb.linearDamping = 0f;
        rb.angularDamping = 0f;

        // 惯性张量：走 Unity 自动计算作为默认安全网。
        // 当 PenPhysicsAggregator.Recalculate 被调用时（有完整装配数据），
        // 它会自己把 automaticInertiaTensor=false 并写入手算张量。
        // 对于没装配数据的裸笔（Enemy 占位），这里保留 auto=true 才有合理张量。
        // rb.automaticInertiaTensor 不强制覆写
    }

    /// <summary>
    /// 施加弹射。速度驱动（冲量 = direction × velocity × mass × force × 装配加成）：
    /// 所有质量的笔在同 force 下都以相同速度离手，但重笔带着更大动量（撞对手推得远）。
    /// AddForceAtPosition 用有效施力点算偏心扭矩（yaw 由 Y 轴自由度承接）。
    /// </summary>
    public void Launch(Vector3 direction, float force, Vector3 contactPointWorld)
    {
        rb.linearVelocity = Vector3.zero;
        rb.angularVelocity = Vector3.zero;

        Vector3 impulse = direction * (EstimateLaunchVelocity(force) * rb.mass);

        rb.AddForceAtPosition(impulse, GetEffectiveLaunchContactPoint(contactPointWorld), ForceMode.Impulse);

        OnLaunched?.Invoke(force);
    }

    /// <summary>
    /// 战斗是俯视桌面物理：玩家点的是笔表面，但有效扭矩应该来自水平平面里的偏心量。
    /// 把施力点投到当前 COM 高度，保留 XZ 偏移产生的 yaw，滤掉表面高度带来的 pitch/roll 噪声。
    /// 真实发射和预测轨迹必须共用这个点，玩家才会逐渐信任预判线。
    /// </summary>
    public Vector3 GetEffectiveLaunchContactPoint(Vector3 contactPointWorld)
    {
        if (rb == null)
            return contactPointWorld;

        contactPointWorld.y = rb.worldCenterOfMass.y;
        return contactPointWorld;
    }

    /// <summary>
    /// 当前装配和力度下的离手速度。真实发射和预测轨迹共用，避免调参后两边漂移。
    /// </summary>
    public float EstimateLaunchVelocity(float force)
    {
        float launchMultiplier = Assembly != null ? Assembly.GetLaunchMultiplier() : 1f;
        return maxLaunchVelocity * Mathf.Clamp01(force) * launchMultiplier;
    }

    /// <summary>是否已停稳</summary>
    public bool IsStopped()
    {
        return rb.linearVelocity.magnitude < stopVelocityThreshold &&
               rb.angularVelocity.magnitude < stopAngularThreshold;
    }

    /// <summary>强制停止</summary>
    public void Stop()
    {
        rb.linearVelocity = Vector3.zero;
        rb.angularVelocity = Vector3.zero;
    }

    /// <summary>
    /// 世界坐标系下从笔尾指向笔头的单位向量。
    /// 由 PenAssembly.ComputeTipAxisWorld 根据 Tip Socket 位置确定。
    /// </summary>
    public Vector3 GetPenAxis()
    {
        if (Assembly != null && Assembly.BarrelCollider != null)
            return Assembly.TipAxisWorld;

        if (penCollider == null) return transform.right;
        return penCollider.direction switch
        {
            0 => transform.right,
            1 => transform.up,
            2 => transform.forward,
            _ => transform.right
        };
    }

    public float GetOffsetAlongCapsuleAxis(Vector3 localPoint)
    {
        return penCollider.direction switch
        {
            0 => localPoint.x,
            1 => localPoint.y,
            2 => localPoint.z,
            _ => localPoint.y
        };
    }
}
