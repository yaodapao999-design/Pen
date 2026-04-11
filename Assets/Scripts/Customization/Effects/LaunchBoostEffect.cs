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
        // 额外施加力 = 原始力 × (倍率 - 1)
        context.Rigidbody.AddForce(direction * (force * (Multiplier - 1f)), ForceMode.Impulse);
    }
}
