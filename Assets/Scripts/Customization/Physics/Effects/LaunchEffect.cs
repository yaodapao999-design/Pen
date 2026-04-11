/// <summary>弹射力倍率效果</summary>
public class LaunchEffect : IPenPartEffect
{
    private readonly float _multiplier;

    public LaunchEffect(float multiplier)
    {
        _multiplier = multiplier;
    }

    public void Apply(PenPhysicsState state)
    {
        state.LaunchMultiplier *= _multiplier;
    }
}
