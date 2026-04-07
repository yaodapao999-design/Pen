/// <summary>质量与质心效果</summary>
public class MassEffect : IPenPartEffect
{
    private readonly float _mass;
    private readonly UnityEngine.Vector3 _centerOfMassOffset;

    public MassEffect(float mass, UnityEngine.Vector3 centerOfMassOffset)
    {
        _mass = mass;
        _centerOfMassOffset = centerOfMassOffset;
    }

    public void Apply(PenPhysicsState state)
    {
        // 加权质心累加（最后除以总质量得到真实质心）
        state.WeightedCenterOfMass += _centerOfMassOffset * _mass;
        state.TotalMass += _mass;
    }
}
