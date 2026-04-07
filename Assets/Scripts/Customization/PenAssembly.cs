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

    public IReadOnlyList<PenPartInstance> Parts => _parts;

    private void Start()
    {
        var pen = GetComponent<PenEntity>();
        _aggregator = new PenPhysicsAggregator(pen.rb, pen.penCollider);
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
        Object.Destroy(part.GameObject);
        _parts.Remove(part);

        RefreshPhysics();
    }

    /// <summary>获取弹射倍率（供 PenEntity.Launch 使用）</summary>
    public float GetLaunchMultiplier() => _aggregator.GetLaunchMultiplier(_parts);

    /// <summary>重新聚合所有部件物理属性并应用</summary>
    public void RefreshPhysics() => _aggregator.Recalculate(_parts);
}
