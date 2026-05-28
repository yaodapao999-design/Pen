using MoreMountains.Feedbacks;
using UnityEngine;

/// <summary>
/// 挂载在每支 PenEntity 上，负责检测笔对笔碰撞并触发 Feel 反馈。
/// 当前阶段：镜头抖动。
/// </summary>
[RequireComponent(typeof(PenEntity))]
public class PenCollisionFeedback : MonoBehaviour
{
    [Header("反馈配置")]
    [Tooltip("碰撞时播放的 MMF_Player，需在场景中配置 CameraShake 等 Feedback")]
    [SerializeField] private MMF_Player collisionFeedbacks;

    [Header("碰撞强度映射")]
    [Tooltip("低于此相对速度不触发反馈")]
    [SerializeField] private float minImpactVelocity = 0.9f;
    [Tooltip("达到此相对速度时反馈强度为 1（最大）。建议设为预期最大碰撞速度，如 15")]
    [SerializeField] private float maxImpactVelocity = 15f;
    [Tooltip("冲量强度映射上限。碰撞反馈会综合相对速度和冲量，让重笔帽真的更有重量。")]
    [SerializeField] private float maxImpactImpulse = 7.5f;
    [Tooltip("同一支笔连续碰撞反馈的冷却，避免玩家笔和复制 enemy 双播造成反馈糊成一团。")]
    [SerializeField] private float feedbackCooldown = 0.08f;

    [Header("碰撞过滤")]
    [Tooltip("主过滤 Tag（通常为 Pen），保留兼容")]
    [SerializeField] private string penTag = "Pen";
    [Tooltip("额外可响应的 Tag 白名单（如 ShopBook）；和 penTag 取并集")]
    [SerializeField] private string[] additionalTags;

    [Header("慢动作配置")]
    [Tooltip("触发慢动作的强度阈值 (0~1)")]
    [Range(0f, 1f)]
    [SerializeField] private float slowMotionThreshold = 0.8f;

    private PenEntity _pen;
    private float _lastFeedbackTime = -999f;

    private void Awake()
    {
        _pen = GetComponent<PenEntity>();
    }

    // ─────────────────────────────────────────────────────────────
    // TODO: 以下 OnCollisionEnter 方式为临时实现，
    //       后续将改为监听战斗逻辑脚本（如 ActionState / BattleStateMachine）
    //       派发的碰撞事件，届时删除此方法并添加对应事件订阅。
    // ─────────────────────────────────────────────────────────────
    private void OnCollisionEnter(Collision collision)
    {
        if (!IsAcceptedCollider(collision.gameObject)) return;

        float impactVelocity = collision.relativeVelocity.magnitude;
        if (impactVelocity < minImpactVelocity) return;

        if (Time.time - _lastFeedbackTime < Mathf.Max(0f, feedbackCooldown))
            return;

        float intensity = ComputeImpactIntensity(collision, impactVelocity);
        Vector3 contactPoint = collision.contacts[0].point;

        TriggerCollisionFeedbacks(intensity, contactPoint);
        _lastFeedbackTime = Time.time;
    }

    private bool IsAcceptedCollider(GameObject go)
    {
        if (!string.IsNullOrEmpty(penTag) && go.CompareTag(penTag)) return true;
        if (additionalTags == null) return false;
        for (int i = 0; i < additionalTags.Length; i++)
        {
            var t = additionalTags[i];
            if (!string.IsNullOrEmpty(t) && go.CompareTag(t)) return true;
        }
        return false;
    }

    /// <summary>
    /// 统一触发入口：接收归一化强度（0~1）和碰撞位置。
    /// 后续战斗逻辑脚本改为直接调用此方法。
    /// </summary>
    public void TriggerCollisionFeedbacks(float intensity, Vector3 contactPoint)
    {
        if (collisionFeedbacks == null) return;

        // ── 统一播放反馈 ────────────────────────────────────────
        intensity = Mathf.Clamp01(intensity);
        collisionFeedbacks.FeedbacksIntensity = intensity;

        // ── 慢动作（Bullet Time）逻辑控制 ───────────────────────────
        // 仅在强度超过阈值且该反馈本身已启用时触发
        var timescaleFeedback = collisionFeedbacks.GetFeedbackOfType<MMF_TimescaleModifier>();
        bool originalActiveState = timescaleFeedback != null && timescaleFeedback.Active;

        if (originalActiveState && intensity < slowMotionThreshold)
        {
            timescaleFeedback.Active = false;
        }

        collisionFeedbacks.PlayFeedbacks(contactPoint);

        // 恢复原始状态，以便下次碰撞时重新判断
        if (timescaleFeedback != null)
        {
            timescaleFeedback.Active = originalActiveState;
        }

        // ── 碰撞粒子特效（4.1 已实现） ────────────────────────────
        // 在 MMF_Player 中添加 MMF_ParticlesInstantiation Feedback，
        // 将 VFX_CollisionSparks.prefab 赋值到 ParticlesPrefab 字段，
        // PositionMode 设为 FeedbackPosition，即可跟随碰撞接触点自动生成火花。
        // 生成该 Prefab：菜单 Tools / Pen / Create Collision Particle Prefab

        // ── 碰撞音效（4.2 已实现） ────────────────────────────────
        // 在 MMF_Player 中添加 MMF_AudioSource Feedback，
        // 将碰撞 SFX AudioClip 赋值到 TargetAudioSource，
        // 已配置 Pitch ±0.1 随机化与 UseIntensityForVolume 联动。
        // 一键配置：菜单 Tools / Pen / Setup Collision Audio Feedback

        // ── 碰撞卡顿（4.3 已实现） ────────────────────────────
        // 将在 MMF_Player 中添加 MMF_FreezeFrame Feedback 后自动生效。


        // ── 聚焦镜头运动（4.5 已实现） ──────────────────────────────────
        // FocusCameraController 挂载在 FocusCameraSystem 节点上，
        // 碰撞时触发 FOV Punch，镜头始终保持两笔在视野内。
        // 一键创建：菜单 Tools / Pen / Setup Focus Camera
        FocusCameraController.Instance?.FocusOn(contactPoint, intensity);

        // ── TODO: 特写镜头 ────────────────────────────────────────
        // 在 CloseupCameraController.cs 实现后，在此处或 ResultState 中触发。
    }

    private float ComputeImpactIntensity(Collision collision, float impactVelocity)
    {
        float speed01 = Mathf.InverseLerp(minImpactVelocity, maxImpactVelocity, impactVelocity);
        float impulseMagnitude = collision.impulse.magnitude;

        if (impulseMagnitude <= 0.0001f && _pen != null && _pen.rb != null)
            impulseMagnitude = impactVelocity * Mathf.Max(0.05f, _pen.rb.mass);

        float impulse01 = Mathf.InverseLerp(minImpactVelocity * 0.08f, Mathf.Max(0.1f, maxImpactImpulse), impulseMagnitude);
        return Mathf.Clamp01(Mathf.Max(speed01, impulse01));
    }

    private void OnValidate()
    {
        if (minImpactVelocity <= 0.001f)
            minImpactVelocity = 0.9f;
        if (maxImpactVelocity <= minImpactVelocity)
            maxImpactVelocity = minImpactVelocity + 0.5f;
        if (maxImpactImpulse <= 0.001f)
            maxImpactImpulse = 7.5f;
        if (feedbackCooldown < 0f)
            feedbackCooldown = 0.08f;
    }
}
