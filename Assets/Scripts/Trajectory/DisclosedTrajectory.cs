using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 披露过滤器按 DisclosureLevel 裁剪后的可渲染数据。
/// Presenter 只消费此结构,不再触碰 TrajectorySample / 物理。
/// 双方落点用 ghost pen 表达:`GhostPen*` = 玩家 ghost;`EnemyGhost*` = 敌方 ghost(Full 级别独享)。
/// </summary>
public sealed class DisclosedTrajectory
{
    public DisclosureLevel Level;

    public bool ShowDirectionBand;
    public Vector3 DirectionBandStart;
    public Vector3 DirectionBandEnd;

    public bool ShowGhostPen;
    public Vector3 GhostPenPosition;
    public Quaternion GhostPenRotation;

    public bool ShowEnemyGhost;
    public Vector3 EnemyGhostPosition;
    public Quaternion EnemyGhostRotation;

    public bool ShowFullPath;
    public List<Vector3> PathPoints;

    public bool ShowPose;
    public List<Quaternion> PathRotations;

    public DisclosedTrajectory()
    {
        PathPoints = new List<Vector3>(64);
        PathRotations = new List<Quaternion>(64);
    }

    public void Reset(DisclosureLevel level)
    {
        Level = level;
        ShowDirectionBand = false;
        DirectionBandStart = default;
        DirectionBandEnd = default;
        ShowGhostPen = false;
        GhostPenPosition = default;
        GhostPenRotation = Quaternion.identity;
        ShowEnemyGhost = false;
        EnemyGhostPosition = default;
        EnemyGhostRotation = Quaternion.identity;
        ShowFullPath = false;
        ShowPose = false;
        PathPoints.Clear();
        PathRotations.Clear();
    }
}
