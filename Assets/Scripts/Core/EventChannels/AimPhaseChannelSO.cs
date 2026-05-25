using System;
using UnityEngine;

/// <summary>
/// 玩家瞄准阶段事件通道。
///
/// 发 4 种事件：
///   OnBegan      — 玩家按下并锁定在笔上（payload: 接触点世界坐标）
///   OnUpdated    — 每帧瞄准数据（payload: AimSample）
///   OnReleased   — 松手发射（payload: final force [0,1]）
///   OnCancelled  — 右键 / 中断（无 payload）
///
/// 发布者：IdleState（经 BattleContext.AimChannel 注入的同一 SO 实例）
/// 订阅者：PenDragVisuals, PenLaunchArrowView, PenPressFeedback, PenAimThresholdFX, PenHoverPresenter
///
/// 设计参考：Ryan Hipple, Unite Austin 2017 "Game Architecture with Scriptable Objects"
/// 选用 SO 的理由：IdleState 是纯 C# 类无法 [SerializeField]，SO 让发布/订阅两端通过
/// 共用 asset 建立弱耦合，增加订阅者零侵入。
/// </summary>
[CreateAssetMenu(fileName = "AimPhaseChannel", menuName = "Pen/Channels/Aim Phase Channel", order = 0)]
public class AimPhaseChannelSO : ScriptableObject
{
    public event Action<Vector3> OnBegan;
    public event Action<AimSample> OnUpdated;
    public event Action<float> OnReleased;
    public event Action OnCancelled;

    public void RaiseBegan(Vector3 contactPoint) => OnBegan?.Invoke(contactPoint);
    public void RaiseUpdated(AimSample sample) => OnUpdated?.Invoke(sample);
    public void RaiseReleased(float force) => OnReleased?.Invoke(force);
    public void RaiseCancelled() => OnCancelled?.Invoke();

    /// <summary>
    /// 清空订阅者。SO 在 Domain Reload 之后可能残留上一次 Play 的闭包引用，
    /// 导致 "MissingReferenceException: The object of type 'X' has been destroyed"。
    /// OnEnable 在 SO 加载 / Domain Reload 后触发，重置所有事件避免脏订阅。
    /// </summary>
    private void OnEnable()
    {
        OnBegan = null;
        OnUpdated = null;
        OnReleased = null;
        OnCancelled = null;
    }
}
