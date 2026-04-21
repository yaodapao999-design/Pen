using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 镜像步进产出的完整采样数据。过滤器(DisclosureFilter)基于此裁剪为披露数据。
/// 持久字段含双笔相关:玩家完整采样点列 + 碰撞瞬间玩家位姿 + 敌方最终位姿。
/// </summary>
public sealed class TrajectorySample
{
    public readonly List<Vector3> Positions;
    public readonly List<Quaternion> Rotations;
    public readonly List<Vector3> Velocities;
    public readonly List<float> Times;

    public Vector3 InitialPosition;
    public Vector3 InitialVelocityHint;
    public Vector3 FinalPosition;
    public Quaternion FinalRotation;
    public float Duration;
    public TrajectoryStopReason StopReason;

    // 玩家与敌方首次碰撞(CollisionOccurred=false 时其他字段无效)
    public bool CollisionOccurred;
    public float CollisionTime;
    public Vector3 CollisionPlayerPosition;
    public Quaternion CollisionPlayerRotation;

    // 敌方笔在预测结束时的最终位姿(HasEnemy=false 时无效)
    public bool HasEnemy;
    public Vector3 FinalEnemyPosition;
    public Quaternion FinalEnemyRotation;

    public TrajectorySample(int expectedCapacity = 64)
    {
        Positions = new List<Vector3>(expectedCapacity);
        Rotations = new List<Quaternion>(expectedCapacity);
        Velocities = new List<Vector3>(expectedCapacity);
        Times = new List<float>(expectedCapacity);
    }

    public void Clear()
    {
        Positions.Clear();
        Rotations.Clear();
        Velocities.Clear();
        Times.Clear();
        InitialPosition = default;
        InitialVelocityHint = default;
        FinalPosition = default;
        FinalRotation = Quaternion.identity;
        Duration = 0f;
        StopReason = TrajectoryStopReason.NotStarted;

        CollisionOccurred = false;
        CollisionTime = -1f;
        CollisionPlayerPosition = default;
        CollisionPlayerRotation = Quaternion.identity;

        HasEnemy = false;
        FinalEnemyPosition = default;
        FinalEnemyRotation = Quaternion.identity;
    }

    public int Count => Positions.Count;
}

public enum TrajectoryStopReason
{
    NotStarted,
    Rested,
    Fallen,
    Timeout,
    Aborted
}
