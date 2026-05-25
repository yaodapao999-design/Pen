using System;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// 悬停检测 Presenter —— 鼠标指向自己的笔时发 HoverChanged(true)。
///
/// <para>只在"战斗 Idle 且未瞄准且未暂停"时工作；其余时刻强制 false。</para>
/// <para>命中判定复用 <see cref="PenDragInteractor"/>，与点击响应保证同一套 hit 范围，
/// 不会出现"鼠标能高亮但点下去没反应"的撕裂感。</para>
///
/// <para>SRP：只产出悬停状态事件，不碰视觉。视觉由 <see cref="PenHoverVisuals"/> 消费。</para>
/// </summary>
[RequireComponent(typeof(PenDragInteractor))]
public class PenHoverPresenter : MonoBehaviour
{
    [Header("Refs")]
    [Tooltip("战斗状态机。用于只在 Idle && !Paused 时启用悬停。")]
    [SerializeField] private BattleStateMachine _battle;

    [Tooltip("瞄准事件通道。进入瞄准时强制关闭悬停，松手/取消恢复。")]
    [SerializeField] private AimPhaseChannelSO _aimChannel;

    /// <summary>悬停态变化事件。true = 鼠标刚进入笔范围；false = 离开或组件被禁用。</summary>
    public event Action<bool> HoverChanged;

    public bool IsHovered { get; private set; }

    private PenDragInteractor _interactor;
    private bool _aiming;

    private void Awake()
    {
        _interactor = GetComponent<PenDragInteractor>();
    }

    private void OnEnable()
    {
        if (_aimChannel != null)
        {
            _aimChannel.OnBegan += OnAimBegan;
            _aimChannel.OnReleased += OnAimEnded;
            _aimChannel.OnCancelled += OnAimCancelled;
        }
    }

    private void OnDisable()
    {
        if (_aimChannel != null)
        {
            _aimChannel.OnBegan -= OnAimBegan;
            _aimChannel.OnReleased -= OnAimEnded;
            _aimChannel.OnCancelled -= OnAimCancelled;
        }
        SetHover(false);
    }

    private void OnAimBegan(Vector3 _) { _aiming = true; SetHover(false); }
    private void OnAimEnded(float _) { _aiming = false; }
    private void OnAimCancelled() { _aiming = false; }

    private void Update()
    {
        if (_aiming) return;
        if (_battle == null || !_battle.IsIdle || _battle.IsPaused) { SetHover(false); return; }

        var mouse = Mouse.current;
        if (mouse == null) { SetHover(false); return; }

        Camera cam = Camera.main;
        if (cam == null) { SetHover(false); return; }

        Vector2 screenPos = mouse.position.ReadValue();
        var ray = ScreenHelper.ScreenPointToRay(cam, screenPos);
        bool hit = _interactor != null && _interactor.TryHit(cam, screenPos, ray, out _);
        SetHover(hit);
    }

    private void SetHover(bool value)
    {
        if (IsHovered == value) return;
        IsHovered = value;
        HoverChanged?.Invoke(value);
    }
}
