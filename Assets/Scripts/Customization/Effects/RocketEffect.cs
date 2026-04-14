using UnityEngine;

/// <summary>
/// 火箭推进效果示例。
/// 弹射后持续施加推力，让笔飞起来。
/// </summary>
[CreateAssetMenu(fileName = "RocketEffect", menuName = "GameData/Effects/Rocket")]
public class RocketEffect : PenPartEffect
{
    [Tooltip("推力大小")]
    public float ThrustForce = 5f;
    [Tooltip("推力持续时间（秒）")]
    public float Duration = 2f;
    [Tooltip("向上推力比例")]
    [Range(0f, 1f)] public float LiftRatio = 0.3f;

    // 运行时状态用 context.Entity 的 GameObject 上的临时组件追踪
    // 这样 SO 本身保持无状态

    public override void OnLaunch(PenEffectContext context, Vector3 direction, float force)
    {
        // 启动推力协程（通过 MonoBehaviour 挂载）
        var runner = context.Entity.gameObject.GetComponent<RocketEffectRunner>();
        if (runner == null)
            runner = context.Entity.gameObject.AddComponent<RocketEffectRunner>();
        runner.StartThrust(direction, ThrustForce, Duration, LiftRatio, context.Rigidbody);
    }

    public override void OnDetached(PenEffectContext context)
    {
        var runner = context.Entity.gameObject.GetComponent<RocketEffectRunner>();
        if (runner != null)
            Object.Destroy(runner);
    }
}
