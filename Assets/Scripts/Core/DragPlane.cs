using UnityEngine;

/// <summary>
/// 拖拽平面/锚点的统一构造工具。
///
/// 所有"鼠标射线 → 世界平面"的拖拽模块都应通过这里构造，
/// 保证公式一致，避免非 90° 俯视相机下的视差偏移（光标和物体错位）。
///
/// 约定：
///   - 平面法线 = Vector3.up（XZ 水平平面）
///   - 锚点 Y = 用户点击到的网格表面点 Y + liftHeight
///   - offset = (pivot + lift) - anchor，使得 target = ray.GetPoint(plane) + offset
///     后，pivot 跟随鼠标的同时"点击点"始终贴在光标下
/// </summary>
public static class DragPlane
{
    /// <summary>
    /// 基于 collider 射线命中点构造拖拽平面 + 偏移。
    /// </summary>
    /// <param name="collider">被拖拽物的 collider（用于定位真实点击点）</param>
    /// <param name="pivotWorld">拖拽物 pivot 的世界坐标（通常 transform.position）</param>
    /// <param name="cursorRay">鼠标对应的相机射线</param>
    /// <param name="liftHeight">拖拽时抬升高度（IdleState 传 0 表示不抬）</param>
    /// <param name="plane">输出：拖拽平面（法线 up，高度 = 命中点 Y + lift）</param>
    /// <param name="offset">输出：target = ray.GetPoint(plane) + offset</param>
    /// <param name="anchor">输出：抬升后的锚点世界坐标（可用作初始 target）</param>
    /// <returns>true = collider 命中；false = 未命中，退化用 pivot 做锚</returns>
    public static bool TryBuild(
        Collider collider, Vector3 pivotWorld, Ray cursorRay, float liftHeight,
        out Plane plane, out Vector3 offset, out Vector3 anchor)
    {
        if (collider != null && collider.Raycast(cursorRay, out RaycastHit hit, 1000f))
        {
            anchor = hit.point + Vector3.up * liftHeight;
            plane = new Plane(Vector3.up, anchor);
            offset = (pivotWorld + Vector3.up * liftHeight) - anchor;
            return true;
        }

        // 退化：collider 没命中 → 用 pivot 抬升做锚，offset 为零
        anchor = pivotWorld + Vector3.up * liftHeight;
        plane = new Plane(Vector3.up, anchor);
        offset = Vector3.zero;
        return false;
    }
}
