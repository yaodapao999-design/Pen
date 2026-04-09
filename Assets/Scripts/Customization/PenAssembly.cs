using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 笔的装配管理器：管理所有已装配部件，驱动物理聚合
/// 挂在 PenEntity 同一个 GameObject 上
/// </summary>
public class PenAssembly : MonoBehaviour
{
    private readonly List<PenPartInstance> _parts = new();
    private PenPhysicsAggregator _aggregator;
    private Rigidbody _rb;
    private GameObject _barrelRoot;

    public IReadOnlyList<PenPartInstance> Parts => _parts;
    public PenPartData CurrentBarrelData { get; private set; }

    private void Awake()
    {
        _rb = GetComponent<Rigidbody>();
    }

    public CapsuleCollider BarrelCollider { get; private set; }

    /// <summary>设置笔杆（实例化 Barrel 预制体，作为所有 Socket 的宿主）</summary>
    public void SetBarrel(PenPartData barrelData)
    {
        if (barrelData.Category != PartType.Barrel)
        {
            Debug.LogWarning($"{barrelData.DisplayName} 不是 Barrel 类型");
            return;
        }

        if (_barrelRoot != null)
        {
            _parts.Clear();
            Destroy(_barrelRoot);
        }

        _barrelRoot = Instantiate(barrelData.VisualPrefab, transform);
        _barrelRoot.transform.localPosition = Vector3.zero;
        _barrelRoot.transform.localRotation = Quaternion.identity;

        BarrelCollider = _barrelRoot.GetComponentInChildren<CapsuleCollider>();
        if (BarrelCollider == null)
        {
            Debug.LogError($"Barrel 预制体 {barrelData.DisplayName} 缺少 CapsuleCollider");
            return;
        }

        _aggregator = new PenPhysicsAggregator(_rb, BarrelCollider);

        CurrentBarrelData = barrelData;
        RefreshPhysics();
    }

    /// <summary>从笔杆子物体里自动查找指定类型的 Socket</summary>
    public PartSocket GetSocket(SocketType type)
    {
        if (_barrelRoot == null)
        {
            Debug.LogWarning("还没有设置 Barrel，无法查找 Socket");
            return null;
        }

        foreach (var socket in _barrelRoot.GetComponentsInChildren<PartSocket>())
        {
            if (socket.SocketType == type && !socket.IsOccupied)
                return socket;
        }

        return null;
    }

    /// <summary>添加部件到指定 socket，并刷新物理</summary>
    public bool AddPart(PenPartData data, PartSocket socket)
    {
        if (!socket.CanAccept(data))
        {
            Debug.LogWarning($"Socket {socket.SocketType} 无法接受部件 {data.DisplayName}");
            return false;
        }

        var go = Instantiate(data.VisualPrefab, socket.transform);
        go.transform.localPosition = Vector3.zero;
        go.transform.localRotation = Quaternion.identity;

        var instance = new PenPartInstance(data, go);
        instance.AttachTo(socket);
        _parts.Add(instance);

        RefreshPhysics();
        return true;
    }

    /// <summary>移除指定部件，并刷新物理</summary>
    public void RemovePart(PenPartInstance part)
    {
        if (!_parts.Contains(part)) return;

        part.Detach();
        Destroy(part.GameObject);
        _parts.Remove(part);

        RefreshPhysics();
    }

    /// <summary>
    /// 仅从装配列表中移除部件（不销毁 GameObject），供 Workshop 模式使用。
    /// 调用方负责处理 GameObject 的生命周期。
    /// </summary>
    public void DetachPart(PenPartInstance part)
    {
        if (!_parts.Contains(part)) return;

        part.Detach();
        _parts.Remove(part);

        RefreshPhysics();
    }

    /// <summary>获取弹射倍率（供 PenEntity.Launch 使用）</summary>
    public float GetLaunchMultiplier()
    {
        if (_aggregator == null) return 1f;
        return _aggregator.GetLaunchMultiplier(_parts);
    }

    /// <summary>重新聚合所有部件物理属性并应用</summary>
    public void RefreshPhysics()
    {
        if (_aggregator == null) return;
        _aggregator.Recalculate(_parts);
    }
}

