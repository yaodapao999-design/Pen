using UnityEngine;

public class PenEntity : MonoBehaviour
{
    [Header("弹射参数")]
    public float baseForce = 10f;
    public float stopVelocityThreshold = 0.05f;
    public float stopAngularThreshold = 0.05f;
    public float stopCheckDelay = 0.5f;
    public float maxDragDistance = 2.0f;

    [Header("掉落检测")]
    public float fallYThreshold = -2f;

    [Header("固定旋转/平移阻力（与零件加减无关，纯笔本体的属性）")]
    [Tooltip("线性阻力 (N)。停止时间 = 冲量/这个值。值越大越快停")]
    [SerializeField] private float _linearStoppingForce = 8f;
    [Tooltip("角阻力扭矩 (N·m)。停止时间 = 角冲量/这个值。值越大越快停")]
    [SerializeField] private float _angularStoppingTorque = 0.5f;
    [Tooltip("惯性张量（各向同性）。决定笔旋转的'重感'。固定不变，不受零件影响")]
    [SerializeField] private float _fixedInertia = 0.2f;

    public Rigidbody rb { get; private set; }
    public CapsuleCollider penCollider => Assembly != null ? Assembly.BarrelCollider : null;
    public PenAssembly Assembly { get; private set; }
    public bool HasFallen => rb != null && rb.position.y < fallYThreshold;

    private void Awake()
    {
        rb = GetComponent<Rigidbody>();
        Assembly = GetComponent<PenAssembly>();

        // 笔本体的物理属性——零件加减不影响这些
        rb.linearDamping = 0f;       // 不要 Unity 指数衰减；用我们的恒定阻力
        rb.angularDamping = 0f;
        rb.automaticInertiaTensor = false;
        rb.inertiaTensor = new Vector3(_fixedInertia, _fixedInertia, _fixedInertia);
        rb.inertiaTensorRotation = Quaternion.identity;
    }

    /// <summary>
    /// 每物理步施加恒定反向力 / 扭矩（真实接触摩擦模型）。
    /// 因为惯性张量是固定的，停止时间 = 角冲量/τ_stop 是真正的"配置无关常数"。
    /// </summary>
    private void FixedUpdate()
    {
        ApplyConstantStopping();
    }

    private void ApplyConstantStopping()
    {
        // 线性
        Vector3 v = rb.linearVelocity;
        float vMag = v.magnitude;
        if (vMag > 1e-4f && _linearStoppingForce > 0f)
        {
            float dvMax = _linearStoppingForce / Mathf.Max(rb.mass, 1e-4f) * Time.fixedDeltaTime;
            if (dvMax >= vMag)
                rb.linearVelocity = Vector3.zero;
            else
                rb.AddForce(-v.normalized * _linearStoppingForce);
        }

        // 角向
        Vector3 w = rb.angularVelocity;
        float wMag = w.magnitude;
        if (wMag > 1e-4f && _angularStoppingTorque > 0f)
        {
            float dwMax = _angularStoppingTorque / _fixedInertia * Time.fixedDeltaTime;
            if (dwMax >= wMag)
                rb.angularVelocity = Vector3.zero;
            else
                rb.AddTorque(-w.normalized * _angularStoppingTorque);
        }
    }

    /// <summary>
    /// 施加弹射冲量。施力点直接用玩家点击时的世界坐标命中点。
    /// 扭矩由 Unity 的 AddForceAtPosition 相对 rb.worldCenterOfMass 自动计算。
    /// </summary>
    public void Launch(Vector3 direction, float force, Vector3 contactPointWorld)
    {
        rb.linearVelocity = Vector3.zero;
        rb.angularVelocity = Vector3.zero;

        float launchMultiplier = Assembly != null ? Assembly.GetLaunchMultiplier() : 1f;
        rb.AddForceAtPosition(
            direction * (baseForce * force * launchMultiplier),
            contactPointWorld,
            ForceMode.Impulse);
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
