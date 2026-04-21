using UnityEngine;

/// <summary>
/// 预测输入契约。交由 TrajectorySimulator 在物理镜像中步进。
/// Direction/Force/ContactPointWorld 与 PenEntity.Launch 的参数语义一致;PenSnapshot 是本次预测所依据的物理真值。
/// </summary>
public readonly struct LaunchInput
{
    public readonly Vector3 Direction;
    public readonly float Force;
    public readonly Vector3 ContactPointWorld;
    public readonly PenSnapshot Pen;

    public LaunchInput(Vector3 direction, float force, Vector3 contactPointWorld, PenSnapshot pen)
    {
        Direction = direction;
        Force = force;
        ContactPointWorld = contactPointWorld;
        Pen = pen;
    }

    public bool IsValid => Pen.IsValid && Force > 0f && Direction.sqrMagnitude > 1e-6f;
}

/// <summary>
/// 运行时从 PenEntity 抽取的只读物理快照。**只含动态属性**:位姿、mass、COM、阻尼、弹射参数、停机/掉落阈值,
/// 以及 ApplyLaunch 力臂钳制所需的 CapsuleHalfHeight。
/// **装配结构**(barrel + 零件列表)不走 Snapshot,由 PhysicsMirrorWorld 在 SyncFrom 时直接读取主场景 PenAssembly
/// 并按差异重建镜像副本——这样镜像笔在几何/材质/质量/COM 上都是 PenAssembly.BuildBattleView 的 1:1 等价。
/// </summary>
public readonly struct PenSnapshot
{
    public readonly bool IsValid;

    public readonly Vector3 Position;
    public readonly Quaternion Rotation;

    public readonly float Mass;
    public readonly Vector3 LocalCenterOfMass;
    public readonly float LinearDamping;
    public readonly float AngularDamping;
    public readonly bool UseGravity;

    public readonly float MaxLaunchImpulse;
    public readonly float LaunchMultiplier;
    public readonly float SpinResponseFactor;

    public readonly float CapsuleHalfHeight;

    public readonly RigidbodyConstraints Constraints;

    public readonly float StopVelocityThreshold;
    public readonly float StopAngularThreshold;
    public readonly float FallYThreshold;

    private PenSnapshot(
        Vector3 position, Quaternion rotation,
        float mass, Vector3 localCom, float linearDamping, float angularDamping, bool useGravity,
        float maxLaunchImpulse, float launchMultiplier, float spinResponseFactor,
        float capsuleHalfHeight,
        RigidbodyConstraints constraints,
        float stopVelocity, float stopAngular, float fallY)
    {
        IsValid = true;
        Position = position;
        Rotation = rotation;
        Mass = mass;
        LocalCenterOfMass = localCom;
        LinearDamping = linearDamping;
        AngularDamping = angularDamping;
        UseGravity = useGravity;
        MaxLaunchImpulse = maxLaunchImpulse;
        LaunchMultiplier = launchMultiplier;
        SpinResponseFactor = spinResponseFactor;
        CapsuleHalfHeight = capsuleHalfHeight;
        Constraints = constraints;
        StopVelocityThreshold = stopVelocity;
        StopAngularThreshold = stopAngular;
        FallYThreshold = fallY;
    }

    public static PenSnapshot Empty => default;

    public static PenSnapshot From(PenEntity pen)
    {
        if (pen == null || pen.rb == null) return Empty;
        var rb = pen.rb;
        var cap = pen.penCollider;
        float halfHeight = cap != null ? cap.height * 0.5f : 0f;
        float launchMultiplier = pen.Assembly != null ? pen.Assembly.GetLaunchMultiplier() : 1f;

        return new PenSnapshot(
            rb.position, rb.rotation,
            rb.mass, rb.centerOfMass, rb.linearDamping, rb.angularDamping, rb.useGravity,
            pen.maxLaunchImpulse, launchMultiplier, pen.spinResponseFactor,
            halfHeight,
            rb.constraints,
            pen.stopVelocityThreshold, pen.stopAngularThreshold, pen.fallYThreshold);
    }
}
