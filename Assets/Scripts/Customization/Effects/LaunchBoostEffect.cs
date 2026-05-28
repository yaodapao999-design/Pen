using UnityEngine;

/// <summary>
/// 弹射力加成效果。
/// 用法：创建 SO（Create → GameData → Effects → LaunchBoost），设置 Multiplier，拖到零件的 Effects 列表。
/// </summary>
[CreateAssetMenu(fileName = "LaunchBoost", menuName = "GameData/Effects/LaunchBoost")]
public class LaunchBoostEffect : PenPartEffect
{
    [Tooltip("弹射力倍率（1.0 = 无加成，2.0 = 双倍）")]
    public float Multiplier = 1.0f;

    public override void OnLaunch(PenEffectContext context, Vector3 direction, float force)
    {
        if (Mathf.Approximately(Multiplier, 1f)) return;
        if (context == null || context.Rigidbody == null || context.Entity == null) return;

        Vector3 flatDirection = direction;
        flatDirection.y = 0f;
        if (flatDirection.sqrMagnitude < 1e-6f) return;
        flatDirection.Normalize();

        // Match PenEntity's velocity-driven launch unit:
        // extra impulse = launch velocity * mass * extra multiplier.
        float baseVelocity = context.Entity.EstimateLaunchVelocity(force);
        float extraImpulse = baseVelocity * context.Rigidbody.mass * Mathf.Max(0f, Multiplier - 1f);
        context.Rigidbody.AddForce(flatDirection * extraImpulse, ForceMode.Impulse);
    }
}
