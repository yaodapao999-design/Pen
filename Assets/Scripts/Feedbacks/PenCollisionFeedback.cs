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
    [SerializeField] private float minImpactVelocity = 1f;
    [Tooltip("达到此相对速度时反馈强度为 1（最大）。建议设为预期最大碰撞速度，如 15")]
    [SerializeField] private float maxImpactVelocity = 15f;

    [Header("碰撞过滤")]
    [Tooltip("只响应带有此 Tag 的碰撞体（笔对笔碰撞）")]
    [SerializeField] private string penTag = "Pen";

    // ─────────────────────────────────────────────────────────────
    // TODO: 以下 OnCollisionEnter 方式为临时实现，
    //       后续将改为监听战斗逻辑脚本（如 ActionState / BattleStateMachine）
    //       派发的碰撞事件，届时删除此方法并添加对应事件订阅。
    // ─────────────────────────────────────────────────────────────
    private void OnCollisionEnter(Collision collision)
    {
        if (!collision.gameObject.CompareTag(penTag)) return;

        float impactVelocity = collision.relativeVelocity.magnitude;
        if (impactVelocity < minImpactVelocity) return;

        float intensity = Mathf.InverseLerp(minImpactVelocity, maxImpactVelocity, impactVelocity);
        Vector3 contactPoint = collision.contacts[0].point;

        TriggerCollisionFeedbacks(intensity, contactPoint);
    }

    /// <summary>
    /// 统一触发入口：接收归一化强度（0~1）和碰撞位置。
    /// 后续战斗逻辑脚本改为直接调用此方法。
    /// </summary>
    public void TriggerCollisionFeedbacks(float intensity, Vector3 contactPoint)
    {
        if (collisionFeedbacks == null) return;

        // ── 镜头抖动（首期实现） ──────────────────────────────────
        collisionFeedbacks.FeedbacksIntensity = intensity;
        collisionFeedbacks.PlayFeedbacks(contactPoint);

        // ── 碰撞粒子特效（4.1 已实现） ────────────────────────────
        // 在 MMF_Player 中添加 MMF_ParticlesInstantiation Feedback，
        // 将 VFX_CollisionSparks.prefab 赋值到 ParticlesPrefab 字段，
        // PositionMode 设为 FeedbackPosition，即可跟随碰撞接触点自动生成火花。
        // 生成该 Prefab：菜单 Tools / Pen / Create Collision Particle Prefab

        // ── TODO: 碰撞音效 ────────────────────────────────────────
        // 将在 MMF_Player 中添加 MMF_AudioSource Feedback 后自动生效。

        // ── TODO: 碰撞卡顿（Hit Stop） ────────────────────────────
        // 将在 MMF_Player 中添加 MMF_FreezeFrame Feedback 后自动生效。

        // ── TODO: 慢动作（Bullet Time） ───────────────────────────
        // 将在 MMF_Player 中添加 MMF_TimescaleModifier Feedback 后自动生效。
        // 建议仅在 intensity > 0.8 时触发，可通过 Feedback 的 Timing 条件配置。

        // ── TODO: 聚焦镜头运动 ────────────────────────────────────
        // 在 FocusCameraController.cs 实现后，在此处调用：
        // FocusCameraController.Instance?.FocusOn(contactPoint, intensity);

        // ── TODO: 特写镜头 ────────────────────────────────────────
        // 在 CloseupCameraController.cs 实现后，在此处或 ResultState 中触发。
    }
}
