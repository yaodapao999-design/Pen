using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 效果运行器，挂在笔实体上。
/// 管理所有已装配零件效果的生命周期（装配/拆卸/弹射/碰撞/每帧）。
///
/// PenAssembly 负责数据，PenEffectRunner 负责运行时效果执行。
/// 战斗视图构建后调用 RebuildEffects() 激活所有效果。
/// </summary>
[RequireComponent(typeof(PenEntity))]
public class PenEffectRunner : MonoBehaviour
{
    private PenEntity _entity;
    private PenEffectContext _context;
    private readonly List<PenPartEffect> _activeEffects = new();
    private bool _inBattle;

    private void Awake()
    {
        _entity = GetComponent<PenEntity>();
    }

    /// <summary>战斗视图构建后调用，激活所有零件效果</summary>
    public void RebuildEffects()
    {
        ClearEffects();

        _context = new PenEffectContext(_entity);
        var assembly = _entity.Assembly;
        if (assembly == null) return;

        // 收集所有已装配零件的效果
        foreach (var entry in assembly.AssembledParts)
        {
            if (entry.Data.Effects == null) continue;
            foreach (var effect in entry.Data.Effects)
            {
                if (effect == null) continue;
                _activeEffects.Add(effect);
                effect.OnAssembled(_context);
            }
        }

        // 笔杆自身的效果
        if (assembly.BarrelData != null && assembly.BarrelData.Effects != null)
        {
            foreach (var effect in assembly.BarrelData.Effects)
            {
                if (effect == null) continue;
                _activeEffects.Add(effect);
                effect.OnAssembled(_context);
            }
        }

        _inBattle = true;
    }

    /// <summary>退出战斗或进入改装时调用</summary>
    public void ClearEffects()
    {
        if (_context != null)
        {
            foreach (var effect in _activeEffects)
                effect.OnDetached(_context);
        }
        _activeEffects.Clear();
        _inBattle = false;
    }

    // ─── 事件分发（由战斗系统调用） ────────────────────────────────────────────

    /// <summary>弹射时调用</summary>
    public void NotifyLaunch(Vector3 direction, float force)
    {
        if (!_inBattle) return;
        foreach (var effect in _activeEffects)
            effect.OnLaunch(_context, direction, force);
    }

    /// <summary>碰撞时调用</summary>
    public void NotifyCollision(Collision collision)
    {
        if (!_inBattle) return;
        foreach (var effect in _activeEffects)
            effect.OnCollision(_context, collision);
    }

    /// <summary>回合开始时调用</summary>
    public void NotifyRoundStart()
    {
        if (!_inBattle) return;
        foreach (var effect in _activeEffects)
            effect.OnRoundStart(_context);
    }

    /// <summary>回合结束时调用</summary>
    public void NotifyRoundEnd()
    {
        if (!_inBattle) return;
        foreach (var effect in _activeEffects)
            effect.OnRoundEnd(_context);
    }

    private void Update()
    {
        if (!_inBattle) return;
        foreach (var effect in _activeEffects)
            effect.OnTick(_context, Time.deltaTime);
    }
}
