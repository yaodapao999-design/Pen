using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 在拖拽零件时，为所有兼容空闲 Socket 生成高亮指示器。
/// 只显示"能放"的位置，减少视觉噪声，引导玩家操作。
///
/// 两级反馈：
///   HighlightMaterial — 兼容空闲 socket（"这里能放"）
///   SnapMaterial      — 最近且在吸附范围内的 socket（"松手即装配"）
///
/// 指示器为运行时创建的 GameObject，parent 到 socket 自动跟随笔的位置。
/// 优先使用 IndicatorPrefab；未设置时使用默认球体。
/// </summary>
public class SocketHighlighter : MonoBehaviour
{
    [Header("指示器")]
    public GameObject IndicatorPrefab;
    public float DefaultIndicatorScale = 0.05f;

    [Header("材质")]
    public Material HighlightMaterial;
    public Material SnapMaterial;

    private PenAssembly _assembly;
    private readonly Dictionary<PartSocket, (GameObject go, Renderer renderer)> _indicators = new();
    private PartSocket _nearestSocket;

    private void Awake()
    {
        _assembly = FindFirstObjectByType<PenAssembly>();
    }

    /// <summary>
    /// 拖拽开始时调用。为所有兼容空闲 socket 创建高亮指示器。
    /// 调用前应先完成 DetachFromPen，确保原 socket 已腾出。
    /// </summary>
    public void ShowCompatible(PenPartData partData)
    {
        HideAll();
        if (_assembly == null || partData == null) return;

        foreach (var socket in _assembly.GetComponentsInChildren<PartSocket>())
        {
            if (!socket.CanAccept(partData)) continue;

            var indicator = CreateIndicator(socket);
            var r = indicator.GetComponentInChildren<Renderer>();
            if (r != null && HighlightMaterial != null)
                r.material = HighlightMaterial;

            _indicators[socket] = (indicator, r);
        }
    }

    /// <summary>
    /// 拖拽中每帧调用。将最近的可吸附 socket 切换为 SnapMaterial，
    /// 其余保持 HighlightMaterial。只在 nearest 变化时切换，避免每帧开销。
    /// </summary>
    public void UpdateNearest(Vector3 worldPos, float snapDist)
    {
        PartSocket best = null;
        float bestDist = snapDist;

        foreach (var (socket, _) in _indicators)
        {
            float dist = Vector3.Distance(worldPos, socket.transform.position);
            if (dist < bestDist)
            {
                bestDist = dist;
                best = socket;
            }
        }

        if (best == _nearestSocket) return;

        // 还原旧 nearest
        if (_nearestSocket != null
            && _indicators.TryGetValue(_nearestSocket, out var old)
            && old.renderer != null && HighlightMaterial != null)
            old.renderer.material = HighlightMaterial;

        _nearestSocket = best;

        // 新 nearest 强调
        if (_nearestSocket != null && SnapMaterial != null
            && _indicators.TryGetValue(_nearestSocket, out var snap)
            && snap.renderer != null)
            snap.renderer.material = SnapMaterial;
    }

    /// <summary>松手时调用，销毁所有指示器。</summary>
    public void HideAll()
    {
        foreach (var (_, entry) in _indicators)
        {
            if (entry.go != null) Destroy(entry.go);
        }
        _indicators.Clear();
        _nearestSocket = null;
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
            // 默认：半透明球体
            indicator = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            indicator.transform.SetParent(socket.transform);
            indicator.transform.SetLocalPositionAndRotation(Vector3.zero, Quaternion.identity);
            indicator.transform.localScale = Vector3.one * DefaultIndicatorScale;

            // 移除碰撞体，避免干扰射线检测
            if (indicator.TryGetComponent<Collider>(out var col))
                Destroy(col);
        }

        return indicator;
    }
}
