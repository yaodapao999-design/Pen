/// <summary>
/// 摩擦效果
/// OverrideGlobal = true  → 覆盖整只笔的全局 PhysicsMaterial
/// OverrideGlobal = false → 局部摩擦，仅影响该部件自身 Collider
/// </summary>
public class FrictionEffect : IPenPartEffect
{
    private readonly UnityEngine.PhysicsMaterial _material;
    private readonly bool _overrideGlobal;
    private readonly UnityEngine.Collider _localCollider;

    public FrictionEffect(UnityEngine.PhysicsMaterial material, bool overrideGlobal, UnityEngine.Collider localCollider = null)
    {
        _material = material;
        _overrideGlobal = overrideGlobal;
        _localCollider = localCollider;
    }

    public void Apply(PenPhysicsState state)
    {
        if (_overrideGlobal)
        {
            state.GlobalPhysicsMaterial = _material;
        }
        else if (_localCollider != null)
        {
            // 局部摩擦直接应用到该部件的 Collider，不影响全局
            _localCollider.material = _material;
        }
    }
}
