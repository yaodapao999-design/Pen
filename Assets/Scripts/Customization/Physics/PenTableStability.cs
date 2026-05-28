using UnityEngine;

/// <summary>
/// 条件性姿态稳定 —— 替代硬 FreezeRotationX+Z 约束的物理正统解法。
///
/// 问题：FreezeRotationX+Z 让笔永远水平，就算 90% 悬在桌外也不翻 = 明显失真。
/// 硬约束 = 牺牲边缘真实感换稳定性。
///
/// 解法："支撑感知"的条件稳定。
///   - 笔的 COM 在桌面投影内 → 笔是被支撑的 → 强制水平姿态（等价于约束）
///   - 笔的 COM 超出桌面投影 → 笔悬空 → 放开约束，重力自然把悬空端拉下去、翻落
///
/// 物理意义：真实世界里"不翻"的原因也是"被桌面支撑"；一旦支点离开 COM 投影，翻落是必然。
/// 我们只是把 PhysX 没建模的"宏观静支撑刚度"补上。
///
/// 实现：在 FixedUpdate 里直接把 rotation 的 pitch/roll 归零，yaw 保留。
/// 相当于每物理步重置姿态 —— 代价极小，数值稳定。
/// </summary>
[RequireComponent(typeof(Rigidbody))]
[RequireComponent(typeof(PenEntity))]
public class PenTableStability : MonoBehaviour
{
    [Tooltip("笔的支撑平面 Collider（一般是战斗桌 Table）。留空会在 Start 时自动找名为 'Table' 的 GameObject。")]
    public Collider supportSurface;

    [Tooltip("COM 在桌面投影内缩进多少米才认为'被支撑'。\n" +
             "0 = 笔 COM 刚刚跨过桌沿就放开约束（真实感最强）；\n" +
             "正值 = 提前放开（手感更容易掉）；\n" +
             "负值 = 延迟放开（COM 需要飞离桌沿更多才翻）。默认 0。")]
    public float edgeInset = 0f;

    private Rigidbody _rb;

    private void Awake()
    {
        _rb = GetComponent<Rigidbody>();
    }

    private void Start()
    {
        if (supportSurface == null)
        {
            var table = GameObject.Find("Table");
            if (table != null) supportSurface = table.GetComponent<Collider>();
            if (supportSurface == null)
                Debug.LogWarning($"[PenTableStability] {name}: supportSurface 未赋值、也找不到名为 'Table' 的 Collider。笔将永远处于'悬空可翻'状态。");
        }
    }

    private void FixedUpdate()
    {
        if (_rb == null || _rb.isKinematic) return;
        if (supportSurface == null) return;
        Bounds tb = supportSurface.bounds;
        Vector3 com = _rb.worldCenterOfMass;

        bool supported =
            com.x >= tb.min.x + edgeInset && com.x <= tb.max.x - edgeInset &&
            com.z >= tb.min.z + edgeInset && com.z <= tb.max.z - edgeInset;

        if (!supported) return; // 悬空：让 PhysX 自然翻落

        // 被支撑：压平 pitch/roll，保留 yaw
        // 用 MoveRotation 替代直接 rotation= 能让 interpolation 正确过渡
        Quaternion cur = _rb.rotation;
        Vector3 eul = cur.eulerAngles;
        Quaternion levelOnly = Quaternion.Euler(0f, eul.y, 0f);
        _rb.MoveRotation(levelOnly);

        // 同时压掉 pitch/roll 的角速度（yaw 保留）
        Vector3 ω = _rb.angularVelocity;
        _rb.angularVelocity = new Vector3(0f, ω.y, 0f);
    }

#if UNITY_EDITOR
    private void OnDrawGizmosSelected()
    {
        if (supportSurface == null) return;
        Bounds tb = supportSurface.bounds;
        Vector3 min = new Vector3(tb.min.x + edgeInset, tb.center.y, tb.min.z + edgeInset);
        Vector3 max = new Vector3(tb.max.x - edgeInset, tb.center.y, tb.max.z - edgeInset);
        Vector3 center = (min + max) * 0.5f;
        Vector3 size = max - min; size.y = 0.01f;
        Gizmos.color = new Color(0f, 1f, 0f, 0.4f);
        Gizmos.DrawWireCube(center, size);
        if (_rb != null)
        {
            Gizmos.color = Color.cyan;
            Gizmos.DrawSphere(_rb.worldCenterOfMass, 0.05f);
        }
    }
#endif
}
