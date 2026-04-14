using UnityEngine;

/// <summary>
/// 效果运行时上下文。
/// 传递给所有 PenPartEffect 回调，提供效果需要的运行时引用。
/// 效果不直接 GetComponent，而是从 context 获取，解耦。
/// </summary>
public class PenEffectContext
{
    public PenEntity Entity { get; }
    public PenAssembly Assembly { get; }
    public Rigidbody Rigidbody { get; }
    public Transform Transform { get; }

    public PenEffectContext(PenEntity entity)
    {
        Entity = entity;
        Assembly = entity.Assembly;
        Rigidbody = entity.rb;
        Transform = entity.transform;
    }
}
