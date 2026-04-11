/// <summary>质量效果：叠加部件质量到笔的 Rigidbody</summary>
public class MassEffect : IPenPartEffect
{
    private readonly float _mass;

    public MassEffect(float mass)
    {
        _mass = mass;
    }

    public void Apply(PenPhysicsState state)
    {
        state.TotalMass += _mass;
    }
}
