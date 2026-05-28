using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 一次瞄准采样的快照。AimPhaseChannel.Updated 的载荷。
/// 所有订阅者（线、箭头、震动）消费的公共数据契约：
///   ContactPointWorld   — 玩家按下时笔表面的命中点（拖拽线起点）
///   DragEndWorld        — 当前拖拽在空间中的终点（拖拽线终点）
///   PenPositionWorld    — 笔 transform.position（备用锚点）
///   LaunchDirection     — 单位向量，笔将沿此方向离手
///   Force               — [0, 1] 当前拖拽进度
///   IsArmed             — 是否已经越过最小有效力度，松手会真正发射
///   MinLaunchForce      — 本次瞄准的最小有效力度阈值
///   OverThreshold       — Force 是否跨过满拉阈值
///   PredictedTrajectory — 物理预测的按压点运动过程轨迹采样（世界），可 null 表示未预测。
///                         由 AimTrajectoryPredictor 填充；不是最终位置，也不是 COM 轨迹。
///   ComOffsetNormalized / Friction01 / PredictionCurve01
///                       — 当前装配在战斗里的可读物理特征，供 UI 和发射反馈表达"为什么会这样走"。
/// </summary>
public readonly struct AimSample
{
    public readonly Vector3 ContactPointWorld;
    public readonly Vector3 EffectiveContactPointWorld;
    public readonly Vector3 DragEndWorld;
    public readonly Vector3 PenPositionWorld;
    public readonly Vector3 CenterOfMassWorld;
    public readonly Vector3 LaunchDirection;
    public readonly float Force;
    public readonly bool IsArmed;
    public readonly float MinLaunchForce;
    public readonly float DragDistanceWorld;
    public readonly float MaxDragDistanceWorld;
    public readonly bool OverThreshold;
    public readonly IReadOnlyList<Vector3> PredictedTrajectory;
    public readonly float ComOffsetNormalized;
    public readonly float ContactOffsetNormalized;
    public readonly float Spin01;
    public readonly float EstimatedFriction;
    public readonly float Friction01;
    public readonly float MassKg;
    public readonly float Mass01;
    public readonly float LaunchSpeed;
    public readonly float PredictionArcLength;
    public readonly float PredictionCurve01;

    public AimSample(Vector3 contactPoint, Vector3 dragEnd, Vector3 penPos,
                     Vector3 launchDir, float force, bool overThreshold,
                     IReadOnlyList<Vector3> predictedTrajectory = null,
                     bool isArmed = true,
                     float minLaunchForce = 0f,
                     float dragDistanceWorld = -1f,
                     float maxDragDistanceWorld = 0f,
                     Vector3 effectiveContactPoint = default,
                     Vector3 centerOfMassWorld = default,
                     float comOffsetNormalized = 0f,
                     float contactOffsetNormalized = 0f,
                     float spin01 = 0f,
                     float estimatedFriction = AimReadabilityMetrics.DefaultDynamicFriction,
                     float friction01 = 0f,
                     float massKg = 0f,
                     float mass01 = 0f,
                     float launchSpeed = 0f,
                     float predictionArcLength = 0f,
                     float predictionCurve01 = 0f)
    {
        ContactPointWorld = contactPoint;
        EffectiveContactPointWorld = effectiveContactPoint == default ? contactPoint : effectiveContactPoint;
        DragEndWorld = dragEnd;
        PenPositionWorld = penPos;
        CenterOfMassWorld = centerOfMassWorld == default ? penPos : centerOfMassWorld;
        LaunchDirection = launchDir;
        Force = force;
        IsArmed = isArmed;
        MinLaunchForce = Mathf.Clamp01(minLaunchForce);
        if (dragDistanceWorld >= 0f)
        {
            DragDistanceWorld = dragDistanceWorld;
        }
        else
        {
            Vector3 delta = dragEnd - contactPoint;
            delta.y = 0f;
            DragDistanceWorld = delta.magnitude;
        }
        MaxDragDistanceWorld = Mathf.Max(0f, maxDragDistanceWorld);
        OverThreshold = overThreshold;
        PredictedTrajectory = predictedTrajectory;
        ComOffsetNormalized = Mathf.Clamp(comOffsetNormalized, -1.5f, 1.5f);
        ContactOffsetNormalized = Mathf.Clamp(contactOffsetNormalized, -1.5f, 1.5f);
        Spin01 = Mathf.Clamp01(spin01);
        EstimatedFriction = Mathf.Max(0f, estimatedFriction);
        Friction01 = Mathf.Clamp01(friction01);
        MassKg = Mathf.Max(0f, massKg);
        Mass01 = Mathf.Clamp01(mass01);
        LaunchSpeed = Mathf.Max(0f, launchSpeed);
        PredictionArcLength = Mathf.Max(0f, predictionArcLength);
        PredictionCurve01 = Mathf.Clamp01(predictionCurve01);
    }
}

public static class AimReadabilityMetrics
{
    public const float DefaultDynamicFriction = PenLaunchPhysics.DefaultDynamicFriction;

    public static void Build(
        PenEntity pen,
        Vector3 contactPointWorld,
        Vector3 launchDirection,
        float force,
        IReadOnlyList<Vector3> predictedTrajectory,
        out Vector3 effectiveContactPointWorld,
        out Vector3 centerOfMassWorld,
        out float comOffsetNormalized,
        out float contactOffsetNormalized,
        out float spin01,
        out float estimatedFriction,
        out float friction01,
        out float massKg,
        out float mass01,
        out float launchSpeed,
        out float predictionArcLength,
        out float predictionCurve01)
    {
        PenLaunchPhysicsSnapshot launch = PenLaunchPhysics.Build(pen, launchDirection, force, contactPointWorld);
        effectiveContactPointWorld = launch.EffectiveContactPointWorld;
        centerOfMassWorld = launch.CenterOfMassWorld;
        massKg = launch.MassKg;
        mass01 = Mathf.InverseLerp(0.08f, 0.8f, massKg);
        launchSpeed = launch.LaunchVelocity;

        Vector3 axis;
        Vector3 bodyCenter;
        float halfLength;
        GetBodyFrame(pen, out axis, out bodyCenter, out halfLength);

        Vector3 comDelta = centerOfMassWorld - bodyCenter;
        comDelta.y = 0f;
        comOffsetNormalized = Vector3.Dot(comDelta, axis) / Mathf.Max(0.05f, halfLength);

        Vector3 fwd = launchDirection;
        fwd.y = 0f;
        if (fwd.sqrMagnitude < 1e-6f)
            fwd = axis;
        fwd.Normalize();

        Vector3 side = Vector3.Cross(Vector3.up, fwd);
        if (side.sqrMagnitude < 1e-6f)
            side = Vector3.Cross(Vector3.up, axis);
        side.Normalize();

        Vector3 momentArm = effectiveContactPointWorld - centerOfMassWorld;
        momentArm.y = 0f;
        contactOffsetNormalized = Vector3.Dot(momentArm, side) / Mathf.Max(0.05f, halfLength);
        spin01 = Mathf.Clamp01(Mathf.Abs(contactOffsetNormalized));

        estimatedFriction = launch.DynamicFriction;
        friction01 = launch.Friction01;

        predictionArcLength = MeasureArcLength(predictedTrajectory);
        predictionCurve01 = MeasureCurve01(predictedTrajectory);
    }

    private static void GetBodyFrame(PenEntity pen, out Vector3 axis, out Vector3 center, out float halfLength)
    {
        axis = Vector3.right;
        center = pen != null ? pen.transform.position : Vector3.zero;
        halfLength = 0.5f;

        if (pen == null)
            return;

        axis = pen.GetPenAxis();
        axis.y = 0f;
        if (axis.sqrMagnitude < 1e-6f)
            axis = pen.transform.right;
        axis.Normalize();

        CapsuleCollider capsule = pen.penCollider;
        if (capsule != null)
        {
            center = capsule.transform.TransformPoint(capsule.center);
            Vector3 scale = capsule.transform.lossyScale;
            float axisScale = capsule.direction switch
            {
                0 => Mathf.Abs(scale.x),
                1 => Mathf.Abs(scale.y),
                2 => Mathf.Abs(scale.z),
                _ => Mathf.Abs(scale.x)
            };
            halfLength = Mathf.Max(0.05f, capsule.height * axisScale * 0.5f);
            return;
        }

        Bounds? bounds = TryGetPenBounds(pen);
        if (bounds.HasValue)
        {
            Bounds b = bounds.Value;
            center = b.center;
            halfLength = Mathf.Max(0.05f, Mathf.Max(b.extents.x, b.extents.z));
        }
    }

    private static Bounds? TryGetPenBounds(PenEntity pen)
    {
        Collider[] colliders = pen.GetComponentsInChildren<Collider>();
        bool hasBounds = false;
        Bounds bounds = default;

        foreach (Collider col in colliders)
        {
            if (col == null || !col.enabled || col.isTrigger)
                continue;

            if (!hasBounds)
            {
                bounds = col.bounds;
                hasBounds = true;
            }
            else
            {
                bounds.Encapsulate(col.bounds);
            }
        }

        return hasBounds ? bounds : null;
    }

    private static float MeasureArcLength(IReadOnlyList<Vector3> points)
    {
        if (points == null || points.Count < 2)
            return 0f;

        float total = 0f;
        for (int i = 1; i < points.Count; i++)
        {
            Vector3 a = points[i - 1];
            Vector3 b = points[i];
            a.y = 0f;
            b.y = 0f;
            total += Vector3.Distance(a, b);
        }
        return total;
    }

    private static float MeasureCurve01(IReadOnlyList<Vector3> points)
    {
        if (points == null || points.Count < 3)
            return 0f;

        Vector3 previous = Vector3.zero;
        float cumulativeTurn = 0f;
        for (int i = 1; i < points.Count; i++)
        {
            Vector3 dir = points[i] - points[i - 1];
            dir.y = 0f;
            if (dir.sqrMagnitude < 1e-6f)
                continue;
            dir.Normalize();

            if (previous.sqrMagnitude > 1e-6f)
                cumulativeTurn += Mathf.Abs(Vector3.SignedAngle(previous, dir, Vector3.up));

            previous = dir;
        }

        return Mathf.Clamp01(cumulativeTurn / 160f);
    }
}
