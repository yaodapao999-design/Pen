using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 物理聚合器：遍历所有部件，收集物理效果，一次性应用到 Rigidbody
/// </summary>
public class PenPhysicsAggregator
{
    private readonly Rigidbody _rb;
    private readonly Collider _mainCollider;

    // Inspector 里设置的基础值，永远不变
    private readonly float _baseMass;
    private readonly Vector3 _baseCenterOfMass;

    public PenPhysicsAggregator(Rigidbody rb, Collider mainCollider)
    {
        _rb = rb;
        _mainCollider = mainCollider;
        // 记录初始值作为基础
        _baseMass = rb.mass;
        _baseCenterOfMass = rb.centerOfMass;
    }

    /// <summary>在 Inspector 基础值上叠加所有部件属性</summary>
    public void Recalculate(IReadOnlyList<PenPartInstance> parts)
    {
        var state = new PenPhysicsState();

        foreach (var part in parts)
        {
            var effects = BuildEffects(part);
            foreach (var effect in effects)
                effect.Apply(state);
        }

        // 基础值 + 部件叠加值
        _rb.mass = _baseMass + state.TotalMass;

        if (state.TotalMass > 0f)
            _rb.centerOfMass = _baseCenterOfMass + (state.WeightedCenterOfMass / state.TotalMass);
        else
            _rb.centerOfMass = _baseCenterOfMass;

        if (state.GlobalPhysicsMaterial != null)
            _mainCollider.material = state.GlobalPhysicsMaterial;
    }

    /// <summary>从 PenPartData 构建该部件的物理效果列表</summary>
    private List<IPenPartEffect> BuildEffects(PenPartInstance part)
    {
        var effects = new List<IPenPartEffect>();
        var data = part.Data;

        // 质量 + 质心
        effects.Add(new MassEffect(data.Mass, data.CenterOfMassOffset));

        // 弹射倍率
        if (!Mathf.Approximately(data.LaunchPowerMultiplier, 1f))
            effects.Add(new LaunchEffect(data.LaunchPowerMultiplier));

        // 摩擦
        if (data.PhysicsMaterial != null)
        {
            var collider = part.GameObject.GetComponent<Collider>();
            effects.Add(new FrictionEffect(data.PhysicsMaterial, data.OverrideGlobalFriction, collider));
        }

        return effects;
    }

    /// <summary>从所有部件聚合弹射倍率（供 PenEntity.Launch 使用）</summary>
    public float GetLaunchMultiplier(IReadOnlyList<PenPartInstance> parts)
    {
        float multiplier = 1f;
        foreach (var part in parts)
            multiplier *= part.Data.LaunchPowerMultiplier;
        return multiplier;
    }
}
