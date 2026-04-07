/// <summary>
/// 部件物理效果接口
/// 所有物理效果（质量、摩擦、弹射倍率等）都实现此接口
/// </summary>
public interface IPenPartEffect
{
    void Apply(PenPhysicsState state);
}

/// <summary>
/// 聚合过程中的可变物理状态
/// 所有部件的效果叠加到这个对象上，最终一次性应用到 Rigidbody
/// </summary>
public class PenPhysicsState
{
    public float TotalMass;
    public float LaunchMultiplier;
    public UnityEngine.PhysicsMaterial GlobalPhysicsMaterial;

    public PenPhysicsState()
    {
        TotalMass = 0f;
        LaunchMultiplier = 1f;
        GlobalPhysicsMaterial = null;
    }
}
