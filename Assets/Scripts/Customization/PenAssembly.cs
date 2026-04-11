using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 笔的装配管理器
/// 数据层：始终存在，记录笔杆和零件的装配关系
/// 战斗视图：仅战斗时存在，从数据构建物理笔实体
/// 改装期间视图由 WorkshopPart 容器独立管理，PenAssembly 只提供数据读写
/// </summary>
public class PenAssembly : MonoBehaviour
{
    // ─── 数据层（始终存在） ───────────────────────────────────────────────────

    public PenPartData BarrelData { get; private set; }

    [System.Serializable]
    public struct PartEntry
    {
        public PenPartData Data;
        public SocketType Socket;
        public PartEntry(PenPartData data, SocketType socket) { Data = data; Socket = socket; }
    }

    private readonly List<PartEntry> _assembledParts = new();
    public IReadOnlyList<PartEntry> AssembledParts => _assembledParts;

    /// <summary>设置完整装配数据（Workshop 退出时调用）</summary>
    public void SetData(PenPartData barrel, List<PartEntry> parts)
    {
        BarrelData = barrel;
        _assembledParts.Clear();
        _assembledParts.AddRange(parts);
    }

    /// <summary>初始化数据（游戏启动 / 战斗系统传入）</summary>
    public void InitData(PenPartData barrel, PenPartData[] parts)
    {
        BarrelData = barrel;
        _assembledParts.Clear();
        if (parts == null) return;
        foreach (var p in parts)
        {
            if (p != null && p.PlugsInto != SocketType.None)
                _assembledParts.Add(new PartEntry(p, p.PlugsInto));
        }
    }

    // ─── 战斗视图（仅战斗时存在） ────────────────────────────────────────────

    private GameObject _barrelRoot;
    private readonly List<PenPartInstance> _battleParts = new();
    private PenPhysicsAggregator _aggregator;
    private Rigidbody _rb;

    public CapsuleCollider BarrelCollider { get; private set; }
    public IReadOnlyList<PenPartInstance> BattleParts => _battleParts;

    private void Awake()
    {
        _rb = GetComponent<Rigidbody>();
    }

    /// <summary>从数据构建战斗视图（进入战斗时调用）</summary>
    public void BuildBattleView()
    {
        ClearBattleView();
        if (BarrelData == null || BarrelData.VisualPrefab == null) return;

        // 笔杆
        _barrelRoot = Instantiate(BarrelData.VisualPrefab, transform);
        _barrelRoot.transform.localPosition = Vector3.zero;
        _barrelRoot.transform.localRotation = Quaternion.identity;

        BarrelCollider = _barrelRoot.GetComponentInChildren<CapsuleCollider>();
        if (BarrelCollider == null)
        {
            Debug.LogError($"Barrel 预制体 {BarrelData.DisplayName} 缺少 CapsuleCollider");
            return;
        }

        _aggregator = new PenPhysicsAggregator(_rb, BarrelCollider);

        // 零件
        foreach (var entry in _assembledParts)
        {
            var socket = FindSocket(_barrelRoot, entry.Socket);
            if (socket == null)
            {
                Debug.LogWarning($"找不到 Socket {entry.Socket}，跳过 {entry.Data.DisplayName}");
                continue;
            }

            var go = Instantiate(entry.Data.VisualPrefab, socket.transform);
            go.transform.localPosition = Vector3.zero;
            go.transform.localRotation = Quaternion.identity;

            var instance = new PenPartInstance(entry.Data, go);
            instance.AttachTo(socket);
            _battleParts.Add(instance);
        }

        RefreshPhysics();
    }

    /// <summary>销毁战斗视图（进入改装时调用）</summary>
    public void ClearBattleView()
    {
        _battleParts.Clear();
        if (_barrelRoot != null)
        {
            Destroy(_barrelRoot);
            _barrelRoot = null;
        }
        BarrelCollider = null;
        _aggregator = null;
    }

    // ─── 物理（战斗用） ──────────────────────────────────────────────────────

    public void RefreshPhysics()
    {
        if (_aggregator == null) return;
        _aggregator.Recalculate(_battleParts);
    }

    public float GetLaunchMultiplier()
    {
        if (_aggregator == null) return 1f;
        return _aggregator.GetLaunchMultiplier(_battleParts);
    }

    // ─── 工具 ────────────────────────────────────────────────────────────────

    private static PartSocket FindSocket(GameObject root, SocketType type)
    {
        foreach (var socket in root.GetComponentsInChildren<PartSocket>())
        {
            if (socket.SocketType == type && !socket.IsOccupied)
                return socket;
        }
        return null;
    }
}
