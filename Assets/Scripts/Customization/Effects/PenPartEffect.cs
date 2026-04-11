using UnityEngine;

/// <summary>
/// 零件效果基类（ScriptableObject）。
/// 每个零件可以挂多个效果，效果定义零件"能做什么"。
///
/// 扩展方式：
///   1. 新建脚本继承 PenPartEffect
///   2. 重写需要的回调方法
///   3. 在 Inspector 里创建 SO 实例，拖到 PenPartData.Effects 列表
///
/// 所有方法默认空实现，子类只重写需要的。
/// context 参数提供运行时状态（笔实体、Rigidbody 等）。
/// </summary>
public abstract class PenPartEffect : ScriptableObject
{
    /// <summary>装配到笔上时调用（初始化效果）</summary>
    public virtual void OnAssembled(PenEffectContext context) { }

    /// <summary>从笔上拆下时调用（清理效果）</summary>
    public virtual void OnDetached(PenEffectContext context) { }

    /// <summary>弹射时调用（修改弹射行为）</summary>
    public virtual void OnLaunch(PenEffectContext context, Vector3 direction, float force) { }

    /// <summary>弹射后每帧调用（持续效果）</summary>
    public virtual void OnTick(PenEffectContext context, float deltaTime) { }

    /// <summary>碰撞时调用</summary>
    public virtual void OnCollision(PenEffectContext context, Collision collision) { }

    /// <summary>回合开始时调用</summary>
    public virtual void OnRoundStart(PenEffectContext context) { }

    /// <summary>回合结束时调用</summary>
    public virtual void OnRoundEnd(PenEffectContext context) { }
}
