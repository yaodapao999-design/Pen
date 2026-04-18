using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 物理聚合器（只管"零件影响什么"）。
///
/// 零件影响的物理属性：
///   ① 质心（COM）：Mass-加权 = Σ(m_i × pos_i) / Σm_i —— "重头偏心"的玩法手感
///   ② 接触摩擦：每个 Collider 挂自己 PartData 的 PhysicsMaterial —— Unity 在接触时
///      自动选接触点那个 Collider 的材质
///
/// 零件 NOT 影响的（由 PenEntity 固定，与配置无关）：
///   - 惯性张量（旋转的"重感"）
///   - 角向/线性阻力（停止时间）
///
/// 这样的设计让"装/卸零件"只改变重心分布和摩擦特性，旋转手感本身永远稳定一致。
/// </summary>
public class PenPhysicsAggregator
{
    private readonly Rigidbody _rb;
    private readonly Collider _barrelCollider;

    public PenPhysicsAggregator(Rigidbody rb, Collider barrelCollider)
    {
        _rb = rb;
        _barrelCollider = barrelCollider;
    }

    public void Recalculate(
        PenPartData barrelData,
        IReadOnlyList<PenPartInstance> parts)
    {
        float totalMass = 0f;
        Vector3 massWeightedPosSumWorld = Vector3.zero;

        // Barrel
        if (barrelData != null && _barrelCollider != null)
        {
            ApplyContactMaterial(_barrelCollider, barrelData);
            float m = Mathf.Max(barrelData.Mass, 0f);
            if (m > 0f)
            {
                totalMass += m;
                massWeightedPosSumWorld += _barrelCollider.bounds.center * m;
            }
        }

        // 子零件
        foreach (var part in parts)
        {
            if (part == null || part.Data == null) continue;
            var col = part.GameObject.GetComponent<Collider>();
            if (col == null)
            {
                Debug.LogWarning($"[PenPhysicsAggregator] {part.Data.DisplayName} 的 VisualPrefab 缺少 Collider——" +
                                 " 不会贡献 COM 或接触摩擦。请给该 prefab 添加原生 Collider。");
                continue;
            }
            ApplyContactMaterial(col, part.Data);
            float m = Mathf.Max(part.Data.Mass, 0f);
            if (m > 0f)
            {
                totalMass += m;
                massWeightedPosSumWorld += col.bounds.center * m;
            }
        }

        if (totalMass <= 0f) return;

        // COM 是手算 Mass 加权——必须关掉 Unity 的自动重算，否则它会按"体积加权"覆盖
        _rb.automaticCenterOfMass = false;

        _rb.mass = totalMass;
        Vector3 comWorld = massWeightedPosSumWorld / totalMass;
        _rb.centerOfMass = _rb.transform.InverseTransformPoint(comWorld);

        // 注意：不碰 inertiaTensor —— 由 PenEntity.Awake 设为固定值，零件加减不影响
    }

    /// <summary>
    /// 把 PartData 的接触材质挂到 Collider。
    /// PhysicsMaterial 留空 → collider.material = null → Unity 使用 Physics Settings 里的默认材质。
    /// isTrigger = false 强制战斗期 Collider 参与物理。
    /// </summary>
    private static void ApplyContactMaterial(Collider col, PenPartData data)
    {
        if (col == null || data == null) return;
        col.isTrigger = false;
        col.material = data.PhysicsMaterial;
    }

    /// <summary>
    /// 聚合弹射倍率（加法叠加）。
    /// 每个零件 LaunchPowerMultiplier 的"偏离 1.0"被当作百分比 bonus 累加：
    ///   bonus = Σ(mult_i - 1)；最终倍率 = 1 + bonus
    /// </summary>
    public float GetLaunchMultiplier(IReadOnlyList<PenPartInstance> parts)
    {
        float bonus = 0f;
        foreach (var part in parts)
        {
            if (part == null || part.Data == null) continue;
            bonus += part.Data.LaunchPowerMultiplier - 1f;
        }
        return Mathf.Max(1f + bonus, 0.1f);
    }
}
