using MoreMountains.Feedbacks;
using UnityEngine;

/// <summary>
/// 满拉阈值反馈。订阅 <see cref="AimPhaseChannelSO"/> 的 OnUpdated，根据 AimSample.OverThreshold
/// 的升沿/降沿驱动一个 <see cref="MMF_Player"/>：
///   过阈值（上升沿）→ Play
///   回到阈值下 / 松手 / 取消 → Stop
///
/// <para>为什么用 MMF 而不是直接调 Gamepad：屏幕震动是 PC 玩家的主要触觉通道，手柄震动
/// 只是锦上添花。MMF_Player 让设计师在一个地方同时配 Cinemachine Impulse / 相机位移
/// shake / 可选 <c>MMF_Rumble</c>（如果 Feel 包有该 feedback），代码只负责"什么时候播
/// 什么时候停"。</para>
///
/// <para>建议 MMF 内容（持续类震动）：
///   - <c>MMF_WiggleShaker</c> / <c>MMF_CameraShake</c>（duration ≥ 9f，循环模式或长 duration）
///   - 可选 <c>MMF_Rumble</c>（手柄用户额外感受）
///   代码在退出阈值时调 <see cref="MMF_Player.StopFeedbacks"/> 截断，无需把 duration 精调。</para>
///
/// <para>SRP：本组件只管"何时进入/离开满拉状态"并触发 MMF；
/// 满拉视觉（颜色变红）由 PenDragVisuals / PenLaunchArrowView 各自读 OverThreshold 处理。</para>
/// </summary>
public class PenAimThresholdFX : MonoBehaviour
{
    [Header("Channel")]
    [SerializeField] private AimPhaseChannelSO _channel;

    [Header("Feedback")]
    [Tooltip("过阈值时播放的 MMF_Player。设计师在里面配相机震动 / 手柄震动等。\n" +
             "代码在退出阈值/松手/取消时会调 StopFeedbacks() 截断持续效果。")]
    [SerializeField] private MMF_Player _overThresholdFeedbacks;

    private bool _active;

    private void OnEnable()
    {
        if (_channel != null)
        {
            _channel.OnUpdated += HandleUpdated;
            _channel.OnReleased += HandleReleased;
            _channel.OnCancelled += HandleCancelled;
        }
    }

    private void OnDisable()
    {
        if (_channel != null)
        {
            _channel.OnUpdated -= HandleUpdated;
            _channel.OnReleased -= HandleReleased;
            _channel.OnCancelled -= HandleCancelled;
        }
        StopFx();
    }

    private void HandleUpdated(AimSample s)
    {
        if (s.OverThreshold) StartFx();
        else StopFx();
    }

    private void HandleReleased(float _) => StopFx();
    private void HandleCancelled() => StopFx();

    private void StartFx()
    {
        if (_active) return;
        if (_overThresholdFeedbacks != null) _overThresholdFeedbacks.PlayFeedbacks();
        _active = true;
    }

    private void StopFx()
    {
        if (!_active) return;
        if (_overThresholdFeedbacks != null) _overThresholdFeedbacks.StopFeedbacks();
        _active = false;
    }
}
