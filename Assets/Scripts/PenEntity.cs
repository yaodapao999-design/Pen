using UnityEngine;
using UnityEngine.Serialization;

/// <summary>
/// 笔的物理本体。
///
/// 设计原则：让 Unity 物理引擎做它擅长的事，本类只做玩法入口 + 装配无关的全局调参。
///   - mass / centerOfMass / PhysicMaterial 由 <see cref="PenPhysicsAggregator"/> 从装配数据聚合
///   - inertiaTensor 由 Unity 根据 Collider 几何 + 质量分布自动算（长轴惯性小、短轴大，符合笔的真实物理）
///   - 减速只靠桌面 PhysicMaterial 摩擦，不加任何空气阻力式的指数衰减（真实世界里笔在桌上就是这样）
///   - 发射用 ForceMode.Impulse：冲量守恒，重笔起步慢、碰撞动量大
///
/// 这样的设计让"装配越重 = 碰撞动量越大、姿态越稳"（而不是"越慢越拖沓"），
/// 玩家换装能真实感知到每个零件的影响。
/// </summary>
public class PenEntity : MonoBehaviour
{
    // ─── 弹射参数 ────────────────────────────────────────────────────────────
    [Header("弹射参数")]

    [Tooltip("满拖时施加的冲量 (N·s = kg·m/s)。Impulse 模式 ⇒ 速度 = 冲量 / 总质量。\n" +
             "重笔同冲量下更慢但动量更大（碰撞顶敌更远）。典型范围 3-12。\n" +
             "实际冲量 = maxLaunchImpulse × dragForce × 装配 LaunchMultiplier 聚合")]
    [FormerlySerializedAs("baseForce")]
    [FormerlySerializedAs("maxLaunchSpeed")]
    public float maxLaunchImpulse = 5f;

    [Tooltip("玩家拖拽多远算'满拖'(世界单位 m)。按桌面大小设：\n" +
             "桌子 1m 宽建议 0.3；桌子 3m 宽建议 1.0。\n" +
             "拖拽距离 / 这个值 = dragForce ∈ [0, 1]")]
    public float maxDragDistance = 2.0f;

    [Tooltip("拖点偏离 COM 产生的旋转响应系数 ∈ [0, 1]。\n" +
             "0 = 任何点都当作点 COM（纯平移无自旋）；1 = 完全真实物理（末端点击 = 陀螺爆炸）。\n" +
             "0.3 起步：保留'点末端会转'的直觉，但角速度压在可控范围内。")]
    [Range(0f, 1f)]
    public float spinResponseFactor = 0.3f;

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

    private void Awake()
    {
        rb = GetComponent<Rigidbody>();
        Assembly = GetComponent<PenAssembly>();

        // 空中无阻尼，减速全交给桌面摩擦
        rb.linearDamping = 0f;
        rb.angularDamping = 0f;

        // 惯性张量让 Unity 根据 Collider 几何 + 聚合质量自动算 ——
        // 胶囊笔杆天然得到"长轴惯性小、短轴惯性大"的真实张量
        rb.automaticInertiaTensor = true;
    }

    /// <summary>
    /// 施加弹射。ForceMode.Impulse ⇒ 冲量守恒（v = J/m），重笔起步慢但动量大。
    /// 偏心施力产生绕 COM 的旋转，强度由 spinResponseFactor 缩放：
    /// 真实 r = contactPoint − COM；实际力臂 = 真实 r × spinResponseFactor，并钳制在半胶囊长度内
    /// 以防点到凸出子零件时 r 过大导致自旋爆炸。
    /// </summary>
    public void Launch(Vector3 direction, float force, Vector3 contactPointWorld)
    {
        rb.linearVelocity = Vector3.zero;
        rb.angularVelocity = Vector3.zero;

        float launchMultiplier = Assembly != null ? Assembly.GetLaunchMultiplier() : 1f;
        Vector3 impulse = direction * (maxLaunchImpulse * force * launchMultiplier);

        // 有效力臂：先剥掉垂直分量（俯视角点中笔上表面会带 ~radius 的 Y 偏移，
        // 配合胶囊极小的长轴惯性张量会让笔绕自身长轴疯转，吃掉发射动能），
        // 再按系数缩放，再钳制到半胶囊长度
        Vector3 comWorld = rb.worldCenterOfMass;
        Vector3 offset = contactPointWorld - comWorld;
        offset.y = 0f;
        offset *= spinResponseFactor;
        CapsuleCollider cap = penCollider;
        if (cap != null)
        {
            float halfLen = cap.height * 0.5f;
            offset = Vector3.ClampMagnitude(offset, halfLen);
        }
        Vector3 effectiveContact = comWorld + offset;

        rb.AddForceAtPosition(impulse, effectiveContact, ForceMode.Impulse);
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
