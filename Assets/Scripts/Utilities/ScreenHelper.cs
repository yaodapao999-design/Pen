using UnityEngine;

/// <summary>
/// 统一的屏幕坐标转换工具。
/// <para>
/// 当项目使用 3DPixelCamera 双相机系统时，<c>Camera.main</c> 渲染到低分辨率 RenderTexture，
/// 而 <c>Mouse.position</c> 返回的是实际屏幕像素坐标。两者坐标空间不一致，
/// 直接调用 <c>Camera.main.ScreenPointToRay(mousePos)</c> 会得到错误的射线。
/// </para>
/// <para>
/// 本类通过先将屏幕坐标归一化为 Viewport 坐标（0~1），再使用 <c>ViewportPointToRay</c>，
/// 统一了两种场景下的坐标转换。当 <c>Camera.main.targetTexture</c> 为 null（无双相机系统）时，
/// 行为与直接调用 <c>ScreenPointToRay</c> 等价。
/// </para>
/// </summary>
public static class ScreenHelper
{
    /// <summary>
    /// 检测当前是否处于双相机渲染系统（Camera.main 渲染到 RenderTexture）。
    /// </summary>
    public static bool IsPixelCameraActive
    {
        get
        {
            var cam = Camera.main;
            return cam != null && cam.targetTexture != null;
        }
    }

    /// <summary>
    /// 将屏幕像素坐标转换为 Viewport 坐标（0~1）。
    /// 始终使用 <c>Screen.width/height</c>（实际显示分辨率），
    /// 而非 <c>Camera.pixelWidth/Height</c>（可能是 RenderTexture 分辨率）。
    /// </summary>
    public static Vector2 ScreenToViewport(Vector2 screenPos)
    {
        return new Vector2(screenPos.x / Screen.width, screenPos.y / Screen.height);
    }

    /// <summary>
    /// 从屏幕坐标生成射线，兼容双相机系统。
    /// <para>
    /// 内部先将屏幕坐标转为 Viewport，再调用 <c>cam.ViewportPointToRay</c>，
    /// 确保无论 <c>Camera.main</c> 的 targetTexture 分辨率如何，射线方向都正确。
    /// </para>
    /// </summary>
    public static Ray ScreenPointToRay(Camera cam, Vector2 screenPos)
    {
        Vector2 vp = ScreenToViewport(screenPos);
        return cam.ViewportPointToRay(new Vector3(vp.x, vp.y, 0f));
    }

    /// <summary>
    /// 将世界坐标投影为屏幕 Viewport 2D 坐标（丢弃深度）。
    /// </summary>
    public static Vector2 WorldToViewport2D(Camera cam, Vector3 worldPos)
    {
        Vector3 vp = cam.WorldToViewportPoint(worldPos);
        return new Vector2(vp.x, vp.y);
    }

    /// <summary>
    /// 将屏幕坐标转换为世界坐标，投影到指定世界位置的深度平面上。
    /// 结果的 Y 轴被拉平到 <paramref name="referenceWorldPos"/> 的高度。
    /// </summary>
    public static Vector3 ScreenToWorldOnPlane(Camera cam, Vector2 screenPos, Vector3 referenceWorldPos)
    {
        Vector2 vp = ScreenToViewport(screenPos);
        float depth = cam.WorldToViewportPoint(referenceWorldPos).z;
        Vector3 worldPos = cam.ViewportToWorldPoint(new Vector3(vp.x, vp.y, depth));
        worldPos.y = referenceWorldPos.y;
        return worldPos;
    }
}
