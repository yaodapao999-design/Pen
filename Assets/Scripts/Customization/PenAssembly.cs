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
        public string SocketId;
        public PartEntry(PenPartData data, SocketType socket, string socketId = null)
        {
            Data = data;
            Socket = socket;
            SocketId = socketId;
        }
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
    // 上帧 lossyScale，用于检测 pop-in / 其他 scale 动画后重算 inertiaTensor
    // （inertiaTensor 在 Aggregator 里按 scale 手算，scale 变了必须刷新，否则 pop-in
    // 未结束就 Launch 会用"零尺寸张量"导致笔爆飞）
    private Vector3 _lastAggregatedScale;

    public CapsuleCollider BarrelCollider { get; private set; }
    public IReadOnlyList<PenPartInstance> BattleParts => _battleParts;

    /// <summary>
    /// 世界坐标系下从笔尾指向笔头的单位向量。
    /// BuildBattleView 完成后有效；由 Tip Socket 位置相对胶囊中心的方向决定，
    /// 不依赖 prefab 作者对胶囊 direction 轴向的约定。
    /// </summary>
    public Vector3 TipAxisWorld { get; private set; } = Vector3.right;

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
            var socket = FindSocket(_barrelRoot, entry.Socket, entry.SocketId);
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

        ComputeTipAxisWorld();
        RefreshPhysics();
    }

    /// <summary>
    /// 根据笔杆上"接笔头端" Socket（SocketType.BarrelFront）的位置，确定"从笔尾指向笔头"的世界轴向，
    /// 消除 prefab 作者对胶囊 direction 轴向的任意约定（+axis 到底朝哪端）。
    /// 若 prefab 没有 BarrelFront，退化 BarrelRear 取反；都没有就保持胶囊 direction 原始正向。
    /// </summary>
    private void ComputeTipAxisWorld()
    {
        if (BarrelCollider == null) return;

        Vector3 rawAxis = GetCapsuleAxisWorld(BarrelCollider);
        TipAxisWorld = rawAxis;

        Vector3 capsuleCenter = BarrelCollider.transform.TransformPoint(BarrelCollider.center);

        // 优先用 BarrelFront（接笔头/笔芯那端）判断
        foreach (var socket in _barrelRoot.GetComponentsInChildren<PartSocket>())
        {
            if (socket.SocketType != SocketType.BarrelFront) continue;
            Vector3 toFront = socket.transform.position - capsuleCenter;
            TipAxisWorld = Vector3.Dot(toFront, rawAxis) >= 0f ? rawAxis : -rawAxis;
            return;
        }

        // 退化：用 BarrelRear（接笔帽那端）取反推出笔头方向
        foreach (var socket in _barrelRoot.GetComponentsInChildren<PartSocket>())
        {
            if (socket.SocketType != SocketType.BarrelRear) continue;
            Vector3 toRear = socket.transform.position - capsuleCenter;
            TipAxisWorld = Vector3.Dot(toRear, rawAxis) >= 0f ? -rawAxis : rawAxis;
            return;
        }
        // 都没找到：保持默认胶囊方向正向（只会发生在空笔杆 prefab 这种边缘情况）
    }

    private static Vector3 GetCapsuleAxisWorld(CapsuleCollider col)
    {
        return col.direction switch
        {
            0 => col.transform.right,
            1 => col.transform.up,
            2 => col.transform.forward,
            _ => col.transform.right
        };
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
        _aggregator.Recalculate(BarrelData, _battleParts);
        _lastAggregatedScale = transform.lossyScale;
    }

    /// <summary>
    /// pop-in 或其他 scale 动画期间，聚合出的 inertiaTensor 是按动画中 scale 算的，
    /// scale 变到 1 之后张量跟不上物理形状。监听 scale 变化，每次超出阈值就刷新一次。
    /// </summary>
    private void LateUpdate()
    {
        if (_aggregator == null) return;
        Vector3 s = transform.lossyScale;
        if ((s - _lastAggregatedScale).sqrMagnitude > 1e-4f)
            RefreshPhysics();
    }

    public float GetLaunchMultiplier()
    {
        if (_aggregator == null) return 1f;
        return _aggregator.GetLaunchMultiplier(_battleParts);
    }

    // ─── 工具 ────────────────────────────────────────────────────────────────

    private static PartSocket FindSocket(GameObject root, SocketType type, string socketId)
    {
        PartSocket firstAvailable = null;
        foreach (var socket in root.GetComponentsInChildren<PartSocket>())
        {
            if (socket.SocketType != type || socket.IsOccupied)
                continue;

            if (!string.IsNullOrWhiteSpace(socketId))
            {
                if (socket.SocketId == socketId)
                    return socket;
                continue;
            }

            firstAvailable ??= socket;
        }
        return firstAvailable;
    }
}
