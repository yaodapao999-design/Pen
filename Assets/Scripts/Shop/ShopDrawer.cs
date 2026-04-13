using UnityEngine;

/// <summary>
/// 商店抽屉：组合 DrawerAnimator + 落点感应区。
/// 动画部分委托给 DrawerAnimator（通用），自己只管"拖拽靠近自动开关 + 是否在落点内"。
/// </summary>
[RequireComponent(typeof(DrawerAnimator))]
public class ShopDrawer : MonoBehaviour
{
    [Header("落点感应区")]
    [Tooltip("判断组件是否进入抽屉的 Trigger（BoxCollider.isTrigger=true）")]
    public BoxCollider DropZone;

    [Tooltip("拖拽点靠近 DropZone 多近时自动打开（世界坐标距离）")]
    public float AutoOpenProximity = 0.25f;

    [Header("吸入")]
    [Tooltip("购买后若零件 XZ 飞出 InteriorFloor 的投影范围，才会被轻微磁吸拉回这里；应置于抽屉开口中心")]
    public Transform DropAnchor;

    [Header("落地判定")]
    [Tooltip("只有零件撞到这些 layer 才算'真正落入抽屉'，避免撞到桌面被误判")]
    public LayerMask InteriorLayers = ~0;
    [Tooltip("抽屉底的 Collider（Collider_Bottom），用于判定零件 XZ 是否在安全着陆区内；在区内磁吸不介入")]
    public Collider InteriorFloor;

    private DrawerAnimator _anim;

    public bool IsOpen => _anim != null && _anim.IsOpen;

    private void Awake()
    {
        _anim = GetComponent<DrawerAnimator>();
    }

    /// <summary>是否位于落点区内（仅判 XZ 投影，忽略 Y，允许拖拽浮空高度）</summary>
    public bool IsInDropZone(Vector3 worldPos)
    {
        if (DropZone == null) return false;
        var b = DropZone.bounds;
        return worldPos.x >= b.min.x && worldPos.x <= b.max.x
            && worldPos.z >= b.min.z && worldPos.z <= b.max.z;
    }

    /// <summary>根据拖拽点位置自动开/关（ShopPart 每帧调用）</summary>
    public void TryAutoOpenClose(Vector3 dragWorldPos)
    {
        if (DropZone == null || _anim == null) return;
        float sqr = SqrDistanceToBounds(DropZone.bounds, dragWorldPos);
        bool shouldOpen = sqr <= AutoOpenProximity * AutoOpenProximity;
        if (shouldOpen && !_anim.IsOpen) _anim.Open();
        else if (!shouldOpen && _anim.IsOpen) _anim.Close();
    }

    public void Open() => _anim?.Open();
    public void Close() => _anim?.Close();
    public void SnapClosed() => _anim?.SnapClosed();

    private static float SqrDistanceToBounds(Bounds b, Vector3 p)
    {
        Vector3 c = b.ClosestPoint(p);
        return (c - p).sqrMagnitude;
    }

#if UNITY_EDITOR
    private void OnDrawGizmosSelected()
    {
        if (DropZone != null)
        {
            Gizmos.color = new Color(0f, 1f, 0.4f, 0.25f);
            Gizmos.DrawCube(DropZone.bounds.center, DropZone.bounds.size);
            Gizmos.color = new Color(0f, 1f, 0.4f, 0.9f);
            Gizmos.DrawWireCube(DropZone.bounds.center, DropZone.bounds.size);
        }
    }
#endif
}
