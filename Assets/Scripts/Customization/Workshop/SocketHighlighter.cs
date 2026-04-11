using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 拖拽零件时高亮兼容 Socket。
/// 从笔杆 WorkshopPart 容器上查找 PartSocket（不依赖 PenAssembly）。
///
/// 两级反馈：
///   HighlightMaterial — 兼容空闲 socket（"这里能放"）
///   SnapMaterial      — 最近且在吸附范围内的 socket（"松手即装配"）
/// </summary>
public class SocketHighlighter : MonoBehaviour
{
    [Header("指示器")]
    public GameObject IndicatorPrefab;
    public float SocketIndicatorScale = 0.05f;  // 零件 socket 高亮大小
    public float BarrelIndicatorScale = 0.15f;  // 笔杆改装台高亮大小

    [Header("材质")]
    public Material HighlightMaterial;   // 空闲 socket（绿色）
    public Material SnapMaterial;        // 最近可吸附 socket（黄色）
    public Material OccupiedMaterial;    // 已占用 socket，可替换（橙色，可选）

    private readonly Dictionary<PartSocket, (GameObject go, Renderer renderer)> _indicators = new();
    private PartSocket _nearestSocket;
    private GameObject _barrelIndicator;
    private Renderer _barrelRenderer;
    private bool _barrelInSnap;
    private bool _barrelOccupied; // 已占用时锁定红色，不切换

    /// <summary>为所有兼容空闲 socket 创建高亮指示器（从笔杆容器上查找）</summary>
    public void ShowCompatible(PenPartData partData)
    {
        HideAll();
        if (partData == null) return;

        var barrelWP = WorkshopPartRegistry.Instance?.GetAssembledBarrel();
        if (barrelWP == null) return;

        foreach (var socket in barrelWP.GetComponentsInChildren<PartSocket>())
        {
            if (socket.SocketType != partData.PlugsInto) continue;

            bool occupied = socket.GetComponentInChildren<WorkshopPart>() != null;

            var indicator = CreateIndicator(socket);
            var r = indicator.GetComponentInChildren<Renderer>();
            if (r != null)
            {
                // 已占用用橙色（可替换），空闲用绿色
                var mat = occupied ? (OccupiedMaterial != null ? OccupiedMaterial : HighlightMaterial)
                                   : HighlightMaterial;
                if (mat != null) r.material = mat;
            }

            _indicators[socket] = (indicator, r);
        }
    }

    /// <summary>更新最近 socket 的高亮状态（用碰撞体最近点判定）</summary>
    public void UpdateNearest(Collider partCollider, float snapDist)
    {
        PartSocket best = null;
        float bestDist = snapDist;

        foreach (var (socket, _) in _indicators)
        {
            float dist = Vector3.Distance(
                partCollider.ClosestPoint(socket.transform.position),
                socket.transform.position);
            if (dist < bestDist)
            {
                bestDist = dist;
                best = socket;
            }
        }

        if (best == _nearestSocket) return;

        if (_nearestSocket != null
            && _indicators.TryGetValue(_nearestSocket, out var old)
            && old.renderer != null && HighlightMaterial != null)
            old.renderer.material = HighlightMaterial;

        _nearestSocket = best;

        if (_nearestSocket != null && SnapMaterial != null
            && _indicators.TryGetValue(_nearestSocket, out var snap)
            && snap.renderer != null)
            snap.renderer.material = SnapMaterial;
    }

    /// <summary>笔杆拖拽时在改装台位置显示高亮</summary>
    /// <param name="occupied">改装台已有笔杆时用橙色提示"不可放置"</param>
    public void ShowBarrelSlot(Vector3 snapPosition, bool occupied = false)
    {
        HideAll();

        GameObject indicator;
        if (IndicatorPrefab != null)
        {
            indicator = Instantiate(IndicatorPrefab);
            indicator.transform.position = snapPosition;
        }
        else
        {
            indicator = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            indicator.transform.position = snapPosition;
            indicator.transform.localScale = Vector3.one * BarrelIndicatorScale;
            if (indicator.TryGetComponent<Collider>(out var col))
                Destroy(col);
        }

        var r = indicator.GetComponentInChildren<Renderer>();
        if (r != null)
        {
            // 已占用 → 橙色（不可放置），空闲 → 绿色
            var mat = occupied
                ? (OccupiedMaterial != null ? OccupiedMaterial : HighlightMaterial)
                : HighlightMaterial;
            if (mat != null) r.material = mat;
        }

        _barrelIndicator = indicator;
        _barrelRenderer = r;
        _barrelInSnap = false;
        _barrelOccupied = occupied;
    }

    /// <summary>笔杆拖拽中两级反馈（已占用时锁定红色不变）</summary>
    public void UpdateBarrelNearest(Vector3 barrelPos, float snapDist)
    {
        if (_barrelIndicator == null || _barrelRenderer == null) return;
        if (_barrelOccupied) return; // 已占用 → 始终红色，不切换

        float dist = Vector3.Distance(barrelPos, _barrelIndicator.transform.position);
        bool inSnap = dist < snapDist;

        if (inSnap == _barrelInSnap) return;
        _barrelInSnap = inSnap;

        var mat = inSnap ? SnapMaterial : HighlightMaterial;
        if (mat != null) _barrelRenderer.material = mat;
    }

    public void HideAll()
    {
        foreach (var (_, entry) in _indicators)
        {
            if (entry.go != null) Destroy(entry.go);
        }
        _indicators.Clear();
        _nearestSocket = null;

        if (_barrelIndicator != null)
        {
            Destroy(_barrelIndicator);
            _barrelIndicator = null;
            _barrelRenderer = null;
            _barrelInSnap = false;
            _barrelOccupied = false;
        }
    }

    private GameObject CreateIndicator(PartSocket socket)
    {
        GameObject indicator;

        if (IndicatorPrefab != null)
        {
            indicator = Instantiate(IndicatorPrefab, socket.transform);
            indicator.transform.SetLocalPositionAndRotation(Vector3.zero, Quaternion.identity);
        }
        else
        {
            indicator = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            indicator.transform.SetParent(socket.transform);
            indicator.transform.SetLocalPositionAndRotation(Vector3.zero, Quaternion.identity);
            indicator.transform.localScale = Vector3.one * SocketIndicatorScale;

            if (indicator.TryGetComponent<Collider>(out var col))
                Destroy(col);
        }

        return indicator;
    }
}
