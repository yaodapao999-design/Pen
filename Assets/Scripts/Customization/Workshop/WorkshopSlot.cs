using UnityEngine;

/// <summary>
/// 改装台区域定义
/// 提供边界检测方法，供 WorkshopPart 判断松手时是否在改装区内。
/// 不再负责自动拆卸（由 WorkshopPart.HandleMouseUp 统一管理）。
/// </summary>
[RequireComponent(typeof(BoxCollider))]
public class WorkshopSlot : MonoBehaviour
{
    private BoxCollider _col;

    private void Awake()
    {
        _col = GetComponent<BoxCollider>();
        _col.isTrigger = true;
    }

    /// <summary>点是否在改装区内</summary>
    public bool Contains(Vector3 worldPos) => _col.bounds.Contains(worldPos);

    /// <summary>XZ 平面上碰撞体 bounds 是否与改装区有任何重叠</summary>
    public bool OverlapsXZ(Bounds partBounds)
    {
        var b = _col.bounds;
        return partBounds.max.x >= b.min.x && partBounds.min.x <= b.max.x
            && partBounds.max.z >= b.min.z && partBounds.min.z <= b.max.z;
    }

#if UNITY_EDITOR
    private void OnDrawGizmos()
    {
        var col = GetComponent<BoxCollider>();
        if (col == null) return;
        Gizmos.matrix = transform.localToWorldMatrix;
        Gizmos.color = new Color(0f, 1f, 0.5f, 0.15f);
        Gizmos.DrawCube(col.center, col.size);
        Gizmos.color = new Color(0f, 1f, 0.5f, 0.8f);
        Gizmos.DrawWireCube(col.center, col.size);
    }
#endif
}
