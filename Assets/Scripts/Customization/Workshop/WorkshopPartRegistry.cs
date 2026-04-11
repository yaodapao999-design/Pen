using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// WorkshopPart 注册中心，替代所有 FindObjectsByType 调用。
/// 所有 WorkshopPart 在 OnEnable/OnDisable 时自动注册/注销。
/// 提供 O(1) 的笔杆查找和 O(n) 的按状态过滤（n = 容器数量，远小于场景 GO 数量）。
/// </summary>
public class WorkshopPartRegistry : MonoBehaviour
{
    public static WorkshopPartRegistry Instance { get; private set; }

    private readonly HashSet<WorkshopPart> _all = new();
    private WorkshopPart _assembledBarrel;

    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
    }

    private void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    public void Register(WorkshopPart wp)
    {
        _all.Add(wp);
        if (wp.IsBarrel && wp.State == WorkshopPart.PartState.Assembled)
            _assembledBarrel = wp;
    }

    public void Unregister(WorkshopPart wp)
    {
        _all.Remove(wp);
        if (_assembledBarrel == wp)
            _assembledBarrel = null;
    }

    /// <summary>笔杆状态变更时调用，更新缓存</summary>
    public void NotifyStateChanged(WorkshopPart wp)
    {
        if (wp.IsBarrel)
        {
            if (wp.State == WorkshopPart.PartState.Assembled || wp.State == WorkshopPart.PartState.Snapping)
                _assembledBarrel = wp;
            else if (_assembledBarrel == wp)
                _assembledBarrel = null;
        }
    }

    /// <summary>O(1) 获取改装台上的笔杆</summary>
    public WorkshopPart GetAssembledBarrel() => _assembledBarrel;

    /// <summary>是否有笔杆在改装台上</summary>
    public bool HasAssembledBarrel() => _assembledBarrel != null;

    /// <summary>遍历所有容器（比 FindObjectsByType 快得多）</summary>
    public IReadOnlyCollection<WorkshopPart> All => _all;

    /// <summary>收集指定状态的容器</summary>
    public void GetByState(WorkshopPart.PartState state, List<WorkshopPart> result)
    {
        result.Clear();
        foreach (var wp in _all)
            if (wp.State == state) result.Add(wp);
    }
}
