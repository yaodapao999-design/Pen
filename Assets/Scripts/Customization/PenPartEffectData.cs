using UnityEngine;

/// <summary>
/// 特殊效果的抽象基类（ScriptableObject）
/// 后续电锯、口香糖等特殊组件继承此类并实现 Apply/Remove
/// </summary>
public abstract class PenPartEffectData : ScriptableObject
{
    public abstract void Apply(PenEntity pen);
    public abstract void Remove(PenEntity pen);
}
