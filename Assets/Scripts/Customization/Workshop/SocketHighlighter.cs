using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 高亮显示与当前拖拽零件兼容的 Socket
/// 挂在场景里任意一个管理器 GameObject 上
/// </summary>
public class SocketHighlighter : MonoBehaviour
{
    [Header("高亮材质")]
    public Material HighlightMaterial;
    public Material OccupiedMaterial;

    private PenAssembly _assembly;
    private readonly Dictionary<PartSocket, Renderer> _socketRenderers = new();

    private void Awake()
    {
        _assembly = FindFirstObjectByType<PenAssembly>();
    }

    /// <summary>显示所有与 partData 兼容的空闲 Socket</summary>
    public void ShowCompatible(PenPartData partData)
    {
        HideAll();
        if (_assembly == null) return;

        foreach (var socket in _assembly.GetComponentsInChildren<PartSocket>())
        {
            var r = socket.GetComponent<Renderer>();
            if (r == null) continue;

            _socketRenderers[socket] = r;
            r.enabled = true;
            r.material = socket.CanAccept(partData) ? HighlightMaterial : OccupiedMaterial;
        }
    }

    /// <summary>隐藏所有 Socket 高亮</summary>
    public void HideAll()
    {
        foreach (var (_, r) in _socketRenderers)
        {
            if (r != null) r.enabled = false;
        }
        _socketRenderers.Clear();
    }
}
