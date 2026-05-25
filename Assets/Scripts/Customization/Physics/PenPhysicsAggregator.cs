using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 物理聚合器（"零件影响什么"的唯一出口）。
///
/// 聚合的物理属性：
///   ① 总质量 = Σ m_i
///   ② 质心（COM）= Σ(m_i · pos_i) / Σ m_i —— "重头偏心"直接可感
///   ③ 惯性张量 = 复合刚体标准公式：每段当成实心圆柱算本体张量，
///      再用平行轴定理挪到合成 COM，最后按对角合成到 rb.inertiaTensor。
///      所有零件都沿笔 local 长轴串联（无 Y/Z 偏移），合成张量天然对角，
///      不需要特征值分解。
///   ④ 接触摩擦 = 每个 Collider 挂自己 PartData 的 PhysicsMaterial
///
/// 为什么要手动算张量（而不用 automaticInertiaTensor）：
///   Unity 的 automaticInertiaTensor 对复合胶囊的合成结果不正确——实测长轴分量 ≈ 0、
///   短轴分量比物理量小 6 倍。结果是任何偏心冲量都会让笔爆转，开发者只能在 Launch
///   里用 offset.y=0 + spinResponseFactor + ClampMagnitude 三重补丁压住。
///   本类给出正确的张量后那些补丁就全可以删。
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
        // Pass 1: 收集所有参与质量分布的段（barrel + 各零件）
        var segments = new List<Segment>(8);
        if (barrelData != null && _barrelCollider != null)
        {
            ApplyContactMaterial(_barrelCollider, barrelData);
            var seg = BuildSegment(_barrelCollider, barrelData.Mass);
            if (seg.HasValue) segments.Add(seg.Value);
        }

        foreach (var part in parts)
        {
            if (part == null || part.Data == null) continue;
            var col = part.GameObject.GetComponent<Collider>();
            if (col == null)
            {
                Debug.LogWarning($"[PenPhysicsAggregator] {part.Data.DisplayName} 的 VisualPrefab 缺少 Collider——" +
                                 " 不会贡献 COM/惯性/摩擦。请给该 prefab 添加原生 Collider。");
                continue;
            }
            ApplyContactMaterial(col, part.Data);
            var seg = BuildSegment(col, part.Data.Mass);
            if (seg.HasValue) segments.Add(seg.Value);
        }

        float totalMass = 0f;
        Vector3 weightedLocalPos = Vector3.zero;
        foreach (var s in segments)
        {
            totalMass += s.Mass;
            weightedLocalPos += s.CenterLocal * s.Mass;
        }
        if (totalMass <= 0f) return;
        Vector3 comLocal = weightedLocalPos / totalMass;

        // Pass 2: 复合惯性张量（body local 轴、绕合成 COM）
        // 所有零件都是沿笔 local X 串联的实心圆柱近似；合成张量对角化天然成立。
        //   单段本体张量（绕自身 COM）：
        //     I_x_self = 0.5 · m · r²
        //     I_y_self = I_z_self = (1/12) · m · (3r² + L²)
        //   平行轴平移（shift 到复合 COM，位移 d = center - comLocal）：
        //     d_x 只影响 I_y、I_z；d_y/d_z 只影响 I_x、I_z / I_x、I_y
        Vector3 inertia = Vector3.zero;
        foreach (var s in segments)
        {
            float Ix_self = 0.5f * s.Mass * s.Radius * s.Radius;
            float Iyz_self = (s.Mass / 12f) * (3f * s.Radius * s.Radius + s.Length * s.Length);

            Vector3 d = s.CenterLocal - comLocal;
            float dx2 = d.x * d.x;
            float dy2 = d.y * d.y;
            float dz2 = d.z * d.z;

            inertia.x += Ix_self + s.Mass * (dy2 + dz2);
            inertia.y += Iyz_self + s.Mass * (dx2 + dz2);
            inertia.z += Iyz_self + s.Mass * (dx2 + dy2);
        }

        // 数值保护：避免任意分量为 0（真实笔不可能绕某轴无惯性；0 会让 PhysX 除 0 导致爆转）
        const float kMinInertia = 1e-4f;
        inertia.x = Mathf.Max(inertia.x, kMinInertia);
        inertia.y = Mathf.Max(inertia.y, kMinInertia);
        inertia.z = Mathf.Max(inertia.z, kMinInertia);

        // 写入 Rigidbody
        _rb.automaticCenterOfMass = false;
        _rb.automaticInertiaTensor = false;
        _rb.mass = totalMass;
        _rb.centerOfMass = comLocal;
        _rb.inertiaTensor = inertia;
        _rb.inertiaTensorRotation = Quaternion.identity; // body local 轴即主轴
    }

    /// <summary>聚合弹射倍率（加法叠加）。bonus = Σ(mult_i - 1)，最终 = 1 + bonus，下限 0.1。</summary>
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

    // ─── 内部 ────────────────────────────────────────────────────────────────

    /// <summary>
    /// 一段的几何 + 质量，body local 空间。
    /// Length 是"等效圆柱"长度，近似取胶囊 height（含两端半球，略高估 Iyz 但可接受）；
    /// Radius 取胶囊 radius。CenterLocal 用真正的几何中心，而不是 AABB 中心。
    /// </summary>
    private struct Segment
    {
        public float Mass;
        public float Length;
        public float Radius;
        public Vector3 CenterLocal;
    }

    private Segment? BuildSegment(Collider col, float mass)
    {
        if (col == null) return null;
        if (mass <= 0f) return null;

        var cap = col as CapsuleCollider;
        if (cap == null)
        {
            // 非胶囊：只贡献点质量（Iyz 用 bounds 半对角近似，仍接平行轴）
            Vector3 sz = col.bounds.size;
            float eqLen = Mathf.Max(sz.x, sz.y, sz.z);
            float eqRad = 0.5f * Mathf.Min(sz.x, sz.y, sz.z);
            Vector3 wc = col.bounds.center;
            return new Segment
            {
                Mass = mass,
                Length = eqLen,
                Radius = eqRad,
                CenterLocal = _rb.transform.InverseTransformPoint(wc)
            };
        }

        // 胶囊几何中心：col.transform 本地的 center → 世界 → 笔 local
        Vector3 worldCenter = cap.transform.TransformPoint(cap.center);
        Vector3 localCenter = _rb.transform.InverseTransformPoint(worldCenter);

        // Length / Radius 考虑 transform scale（沿胶囊 direction 轴取 scale）
        Vector3 lossy = cap.transform.lossyScale;
        float axisScale = cap.direction switch { 0 => lossy.x, 1 => lossy.y, 2 => lossy.z, _ => lossy.x };
        float radialScale = cap.direction switch
        {
            0 => Mathf.Max(Mathf.Abs(lossy.y), Mathf.Abs(lossy.z)),
            1 => Mathf.Max(Mathf.Abs(lossy.x), Mathf.Abs(lossy.z)),
            2 => Mathf.Max(Mathf.Abs(lossy.x), Mathf.Abs(lossy.y)),
            _ => Mathf.Max(Mathf.Abs(lossy.y), Mathf.Abs(lossy.z))
        };

        return new Segment
        {
            Mass = mass,
            Length = Mathf.Max(cap.height * Mathf.Abs(axisScale), 1e-3f),
            Radius = Mathf.Max(cap.radius * radialScale, 1e-3f),
            CenterLocal = localCenter
        };
    }

    private static void ApplyContactMaterial(Collider col, PenPartData data)
    {
        if (col == null || data == null) return;
        col.isTrigger = false;
        col.material = data.PhysicsMaterial;
    }
}
