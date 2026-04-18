using System.Collections.Generic;
using Unity.Cinemachine;
using UnityEngine;

/// <summary>
/// 聚焦镜头运动系统（plan 4.5）
/// 支持任意数量的笔作为追踪目标，并在笔被淘汰时动态剔除。
///
/// 场景配置方式：
///   1. 菜单 Tools / Pen / Setup Focus Camera 一键创建所有依赖节点
///   2. 将所有参赛笔的 Transform 拖入 InitialPens 列表，或运行时调用 RegisterPen()
///   3. 调整 MinFOV / MaxFOV / Damping 等参数
///
/// 淘汰接口：
///   FocusCameraController.Instance?.RemovePen(pen.transform);
/// </summary>
public class FocusCameraController : MonoBehaviour
{
    // ── 单例 ────────────────────────────────────────────────────────
    public static FocusCameraController Instance { get; private set; }

    // ── Cinemachine 引用 ─────────────────────────────────────────────
    [Header("Cinemachine 引用")]
    [Tooltip("包含参战笔的 CinemachineTargetGroup（Follow 目标）")]
    [SerializeField] private CinemachineTargetGroup targetGroup;

    [Tooltip("聚焦虚拟摄像机（需挂有 CinemachinePositionComposer）")]
    [SerializeField] private CinemachineCamera focusCamera;

    // ── 初始目标（可扩展为任意数量） ─────────────────────────────────
    [Header("初始笔目标（支持任意数量）")]
    [Tooltip("所有参战笔的 Transform，运行时也可通过 RegisterPen() 动态添加")]
    [SerializeField] private List<Transform> initialPens = new();

    [Tooltip("各笔在 TargetGroup 中的权重")]
    [SerializeField] private float penWeight = 1f;

    [Tooltip("各笔在 TargetGroup 中的包围半径")]
    [SerializeField] private float penRadius = 0.5f;

    // ── FOV 约束 ────────────────────────────────────────────────────
    [Header("FOV 动态调整")]
    [Tooltip("FOV 下限（所有笔最近时）")]
    [Range(5f, 89f)]
    [SerializeField] private float minFOV = 25f;

    [Tooltip("FOV 上限（所有笔最远时）")]
    [Range(5f, 89f)]
    [SerializeField] private float maxFOV = 70f;

    [Tooltip("所有笔中最大两两间距达到此值时 FOV 达到上限")]
    [SerializeField] private float maxDistance = 12f;

    [Tooltip("FOV 平滑插值速度")]
    [SerializeField] private float fovDampingSpeed = 3f;

    // ── 碰撞冲击 FOV Punch ──────────────────────────────────────────
    [Header("碰撞冲击 FOV 效果")]
    [Tooltip("碰撞时 FOV 临时扩张的最大幅度（强度=1 时）")]
    [SerializeField] private float maxFovPunch = 8f;

    [Tooltip("FOV Punch 恢复时间（秒）")]
    [SerializeField] private float fovPunchDecay = 0.4f;

    // ── 内部状态 ────────────────────────────────────────────────────
    /// <summary>当前仍在追踪的笔列表（已淘汰的会被移除）</summary>
    private readonly List<Transform> _activePens = new();

    private float _currentFOV;
    private float _punchFOV;

    // ────────────────────────────────────────────────────────────────
    #region Unity 生命周期

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;

        if (focusCamera != null)
            _currentFOV = focusCamera.Lens.FieldOfView;
    }

    private void Start()
    {
        // 注册 Inspector 中指定的所有初始笔
        foreach (var pen in initialPens)
            RegisterPen(pen);

        // 镜头 Priority 由 GameManager 的阶段调度统一管理，这里不再设置
    }

    private void LateUpdate()
    {
        UpdateFOV();
    }

    private void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    #endregion

    // ────────────────────────────────────────────────────────────────
    #region 公共接口

    /// <summary>
    /// 注册一支新笔到追踪列表。可在运行时动态调用（如多人模式动态加入）。
    /// </summary>
    public void RegisterPen(Transform pen, float weight = -1f, float radius = -1f)
    {
        if (pen == null || _activePens.Contains(pen)) return;

        _activePens.Add(pen);

        if (targetGroup != null)
            targetGroup.AddMember(pen, weight >= 0 ? weight : penWeight, radius >= 0 ? radius : penRadius);
    }

    /// <summary>
    /// 将指定笔从追踪列表中剔除（淘汰时调用）。
    /// TargetGroup 中该笔的权重会立即归零，使镜头平滑聚焦剩余笔。
    /// </summary>
    public void RemovePen(Transform pen)
    {
        if (pen == null || !_activePens.Contains(pen)) return;

        _activePens.Remove(pen);

        if (targetGroup != null)
            targetGroup.RemoveMember(pen);

        Debug.Log($"[FocusCameraController] 已将 {pen.name} 从追踪目标中移除，剩余 {_activePens.Count} 支笔。");
    }

    /// <summary>
    /// 清空所有追踪目标（回合重置时调用）。
    /// </summary>
    public void ClearAllPens()
    {
        foreach (var pen in _activePens)
        {
            if (targetGroup != null && pen != null)
                targetGroup.RemoveMember(pen);
        }
        _activePens.Clear();
    }

    /// <summary>
    /// 由 PenCollisionFeedback 在碰撞时调用，触发短暂 FOV Punch 冲击感。
    /// </summary>
    public void FocusOn(Vector3 contactPoint, float intensity)
    {
        _punchFOV = maxFovPunch * intensity;
    }

    /// <summary>当前仍在追踪的笔数量（只读）</summary>
    public int ActivePenCount => _activePens.Count;

    #endregion

    // ────────────────────────────────────────────────────────────────
    #region 内部逻辑

    /// <summary>
    /// 每帧计算目标 FOV（基于所有笔的最大两两间距 + Punch 偏移），平滑应用到 CinemachineCamera。
    /// </summary>
    private void UpdateFOV()
    {
        if (focusCamera == null) return;

        float baseFOV = CalculateBaseFOV();

        _punchFOV = Mathf.MoveTowards(_punchFOV, 0f,
            (maxFovPunch / Mathf.Max(fovPunchDecay, 0.01f)) * Time.deltaTime);

        float targetFOV = Mathf.Clamp(baseFOV + _punchFOV, minFOV, maxFOV + maxFovPunch);
        _currentFOV = Mathf.Lerp(_currentFOV, targetFOV, Time.deltaTime * fovDampingSpeed);

        var lens = focusCamera.Lens;
        lens.FieldOfView = _currentFOV;
        focusCamera.Lens = lens;
    }

    /// <summary>
    /// 计算所有活跃笔中最大的两两间距，映射为 [minFOV, maxFOV] 范围内的 FOV 值。
    /// 当只剩 1 支笔或无笔时，返回中间值。
    /// </summary>
    private float CalculateBaseFOV()
    {
        if (_activePens.Count <= 1)
            return (_activePens.Count == 0) ? minFOV : (minFOV + maxFOV) * 0.5f;

        float maxDist = 0f;
        for (int i = 0; i < _activePens.Count; i++)
        {
            for (int j = i + 1; j < _activePens.Count; j++)
            {
                if (_activePens[i] == null || _activePens[j] == null) continue;
                float d = Vector3.Distance(_activePens[i].position, _activePens[j].position);
                if (d > maxDist) maxDist = d;
            }
        }

        float t = Mathf.InverseLerp(0f, maxDistance, maxDist);
        return Mathf.Lerp(minFOV, maxFOV, t);
    }

    #endregion
}
