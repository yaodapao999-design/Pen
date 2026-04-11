using UnityEngine;

/// <summary>
/// 改装台位置计算工具（唯一实现，消除重复）。
/// 负责：slot 底面中心坐标、笔杆躺平旋转、贴地对齐。
/// </summary>
public static class WorkshopSlotCalculator
{
    /// <summary>改装台 BoxCollider 底面中心的世界坐标</summary>
    public static Vector3 GetSlotFloorCenter(WorkshopSlot slot)
    {
        var col = slot.GetComponent<BoxCollider>();
        return slot.transform.TransformPoint(new Vector3(
            col.center.x,
            col.center.y - col.size.y * 0.5f,
            col.center.z));
    }

    /// <summary>笔杆应该躺平的旋转（根据 CapsuleCollider 方向判断是否需要翻转 90°）</summary>
    public static Quaternion GetBarrelRotation(WorkshopSlot slot, WorkshopPart barrelWP)
    {
        Quaternion slotRot = slot.transform.rotation;
        var capsule = barrelWP.GetComponentInChildren<CapsuleCollider>();
        if (capsule != null && capsule.direction == 1)
            slotRot *= Quaternion.Euler(0, 0, 90);
        return slotRot;
    }

    /// <summary>将容器底部对齐到改装台底面</summary>
    public static void AlignToFloor(WorkshopPart wp, Vector3 floorY)
    {
        var renderers = wp.GetComponentsInChildren<Renderer>();
        if (renderers.Length == 0) return;

        var wb = renderers[0].bounds;
        foreach (var r in renderers) wb.Encapsulate(r.bounds);
        wp.transform.position += Vector3.up * (floorY.y - wb.min.y);
    }

    /// <summary>一次性完成：设置位置 + 旋转 + 贴地 + 保存锚点</summary>
    public static void PlaceBarrelOnSlot(WorkshopPart barrelWP, WorkshopSlot slot)
    {
        Vector3 floorCenter = GetSlotFloorCenter(slot);
        Quaternion rot = GetBarrelRotation(slot, barrelWP);

        barrelWP.transform.SetPositionAndRotation(floorCenter, rot);
        AlignToFloor(barrelWP, floorCenter);
        barrelWP.SetSlotAnchor(barrelWP.transform.position, barrelWP.transform.rotation);
    }
}
