using MoreMountains.Feedbacks;
using UnityEngine;

/// <summary>
/// 按下瞬间（玩家点中笔、进入瞄准）的手感反馈。
///
/// 订阅 <see cref="AimPhaseChannelSO"/> 的 OnBegan，播放挂载的 <see cref="MMF_Player"/>。
/// MMF 内容由设计师在 Inspector 组合，建议配置：
///   - MMF_Scale (Target = pen transform)：punch 0.97 → 1.0，duration 0.12s（"咬住"感）
///   - MMF_AudioSource：tap 音效（release 的反面，短促、干）
///   - MMF_CameraFOV（可选）：FOV punch -1° 持续 0.08s，极轻的"镜头吸入"
///
/// <para>独立于 PenLaunchFeedback：那个响应 <c>OnLaunched</c>（松手瞬间），
/// 本组件响应 <c>OnBegan</c>（按下瞬间）。两个 MMF 职责不同，分开配置避免串味。</para>
/// </summary>
public class PenPressFeedback : MonoBehaviour
{
    [Header("Channel")]
    [SerializeField] private AimPhaseChannelSO _channel;

    [Header("Feedback")]
    [Tooltip("按下瞬间播放的 MMF_Player。留空 = 无反馈，不崩。")]
    [SerializeField] private MMF_Player _pressFeedbacks;

    private void OnEnable()
    {
        if (_channel != null) _channel.OnBegan += HandleBegan;
    }

    private void OnDisable()
    {
        if (_channel != null) _channel.OnBegan -= HandleBegan;
    }

    private void HandleBegan(Vector3 contactPoint)
    {
        if (_pressFeedbacks != null) _pressFeedbacks.PlayFeedbacks(contactPoint);
    }
}
