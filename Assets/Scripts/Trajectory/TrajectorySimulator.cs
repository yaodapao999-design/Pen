using UnityEngine;

/// <summary>
/// 在 PhysicsMirrorWorld 中对 LaunchInput 步进并采样 TrajectorySample。
/// 停止判定:**双笔**都满足静止阈值(玩家若掉落直接 Fallen),Full 语义由此落地。
/// 碰撞记录:玩家笔与敌方笔首次碰撞时,记录 `CollisionTime` + 玩家瞬时位姿(供 OnCollide 级别截断)。
/// 内部持有一个 TrajectorySample 作为复用缓冲,调用方在下次 Simulate 之前须消费完。
/// </summary>
public class TrajectorySimulator
{
    private readonly TrajectorySample _buffer;

    public TrajectorySimulator(int expectedCapacity = 128)
    {
        _buffer = new TrajectorySample(expectedCapacity);
    }

    public TrajectorySample Simulate(PhysicsMirrorWorld world, in LaunchInput input, MirrorSimulationConfig cfg)
    {
        _buffer.Clear();

        if (world == null || cfg == null) return _buffer;
        if (!input.IsValid) return _buffer;
        if (!world.EnsureInitialized()) return _buffer;

        world.SyncFrom(input.Pen);
        if (!world.IsReady) return _buffer;

        var rb = world.MirrorPen;
        var enemyRb = world.MirrorEnemyPen;
        var probe = world.PlayerCollisionProbe;
        probe?.ResetProbe();

        _buffer.HasEnemy = enemyRb != null;

        ApplyLaunch(rb, input);

        _buffer.InitialPosition = rb.position;
        _buffer.InitialVelocityHint = rb.linearVelocity;
        PushSample(rb, 0f);

        float dt = Mathf.Max(1e-4f, cfg.FixedStep);
        int maxSteps = Mathf.Max(1, cfg.MaxSteps);
        float maxTime = Mathf.Max(dt, cfg.MaxTime);
        int stride = Mathf.Max(1, cfg.SampleStride);
        int restCount = 0;

        for (int step = 1; step <= maxSteps; step++)
        {
            world.Step(dt);
            float t = step * dt;

            if ((step % stride) == 0)
                PushSample(rb, t);

            // 首次碰撞(玩家笔撞敌方笔)
            if (!_buffer.CollisionOccurred && probe != null && probe.HasCollided)
            {
                _buffer.CollisionOccurred = true;
                _buffer.CollisionTime = t;
                _buffer.CollisionPlayerPosition = rb.position;
                _buffer.CollisionPlayerRotation = rb.rotation;
            }

            // 掉落
            if (rb.position.y < input.Pen.FallYThreshold)
            {
                FinalizeSample(rb, enemyRb, t, TrajectoryStopReason.Fallen);
                return _buffer;
            }

            // 双笔静止判定(Full 语义:等敌方也停下)
            bool playerRested = rb.linearVelocity.magnitude < input.Pen.StopVelocityThreshold &&
                                rb.angularVelocity.magnitude < input.Pen.StopAngularThreshold;
            bool enemyRested = enemyRb == null
                               || (enemyRb.linearVelocity.magnitude < input.Pen.StopVelocityThreshold &&
                                   enemyRb.angularVelocity.magnitude < input.Pen.StopAngularThreshold);
            if (playerRested && enemyRested)
            {
                restCount++;
                if (restCount >= Mathf.Max(1, cfg.RestFrameCount))
                {
                    FinalizeSample(rb, enemyRb, t, TrajectoryStopReason.Rested);
                    return _buffer;
                }
            }
            else restCount = 0;

            if (t >= maxTime)
            {
                FinalizeSample(rb, enemyRb, t, TrajectoryStopReason.Timeout);
                return _buffer;
            }
        }

        FinalizeSample(rb, enemyRb, maxSteps * dt, TrajectoryStopReason.Timeout);
        return _buffer;
    }

    private void FinalizeSample(Rigidbody rb, Rigidbody enemyRb, float t, TrajectoryStopReason reason)
    {
        int last = _buffer.Positions.Count - 1;
        if (last < 0 || _buffer.Positions[last] != rb.position)
            PushSample(rb, t);

        _buffer.Duration = t;
        _buffer.FinalPosition = rb.position;
        _buffer.FinalRotation = rb.rotation;
        _buffer.StopReason = reason;

        if (enemyRb != null)
        {
            _buffer.FinalEnemyPosition = enemyRb.position;
            _buffer.FinalEnemyRotation = enemyRb.rotation;
        }
    }

    private void PushSample(Rigidbody rb, float t)
    {
        _buffer.Positions.Add(rb.position);
        _buffer.Rotations.Add(rb.rotation);
        _buffer.Velocities.Add(rb.linearVelocity);
        _buffer.Times.Add(t);
    }

    // 与 PenEntity.Launch 保持 1:1 的冲量/施力点计算,避免镜像与主场景发射语义漂移。
    // 改动此处务必同步 PenEntity.Launch(反之亦然)。
    private static void ApplyLaunch(Rigidbody rb, in LaunchInput input)
    {
        rb.linearVelocity = Vector3.zero;
        rb.angularVelocity = Vector3.zero;

        Vector3 dir = input.Direction.sqrMagnitude > 1e-6f ? input.Direction.normalized : input.Direction;
        Vector3 impulse = dir * (input.Pen.MaxLaunchImpulse * input.Force * input.Pen.LaunchMultiplier);

        Vector3 comWorld = rb.worldCenterOfMass;
        Vector3 offset = input.ContactPointWorld - comWorld;
        offset.y = 0f;
        offset *= input.Pen.SpinResponseFactor;
        float halfLen = input.Pen.CapsuleHalfHeight;
        if (halfLen > 0f)
            offset = Vector3.ClampMagnitude(offset, halfLen);
        Vector3 effectiveContact = comWorld + offset;

        rb.AddForceAtPosition(impulse, effectiveContact, ForceMode.Impulse);
    }
}
