using UnityEngine;

/// <summary>
/// 出界判定的纯逻辑工具类，无状态，无 MonoBehaviour
/// FallOffDetector 和 FallOffPredictor 共用此逻辑
/// </summary>
public static class FallOffCheck
{
    /// <summary>
    /// 判断刚体重心是否在桌面上方
    /// </summary>
    public static bool IsCenterAboveTable(Vector3 centerOfMass, float rayDistance, LayerMask tableLayer, PhysicsScene? physicsScene = null)
    {
        if (physicsScene.HasValue)
            return physicsScene.Value.Raycast(centerOfMass, Vector3.down, rayDistance, tableLayer);
        else
            return Physics.Raycast(centerOfMass, Vector3.down, rayDistance, tableLayer);
    }

    /// <summary>
    /// 判断是否正在下落
    /// </summary>
    public static bool IsFalling(Vector3 velocity, float threshold)
    {
        return velocity.y < threshold;
    }

    /// <summary>
    /// 判断是否低于初始高度
    /// </summary>
    public static bool IsBelowInitial(float currentY, float initialY, float margin = 0.3f)
    {
        return currentY < initialY - margin;
    }

    /// <summary>
    /// 综合出界判定：重心不在桌上 + 正在下落 + 低于初始高度
    /// </summary>
    public static bool CheckFallOff(Vector3 centerOfMass, Vector3 velocity, float initialY,
        float rayDistance, float fallThreshold, LayerMask tableLayer,
        PhysicsScene? physicsScene = null)
    {
        return !IsCenterAboveTable(centerOfMass, rayDistance, tableLayer, physicsScene)
            && IsFalling(velocity, fallThreshold)
            && IsBelowInitial(centerOfMass.y, initialY);
    }
}