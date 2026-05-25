using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// 发射轨迹预测 —— 隐藏 PhysicsScene 里短时模拟当前笔。
///
/// <para><b>核心数据来源</b>：
///   - 初速度：和 <see cref="PenEntity.Launch"/> 同一套 maxLaunchVelocity × force × 装配倍率
///   - 偏心旋转：用 AddForceAtPosition 的 r × impulse 估算 yaw 角速度
///   - 质量分布：读取 Rigidbody.mass / centerOfMass / inertiaTensor
///   - 局部摩擦：读取笔和零件 Collider 上的 PhysicsMaterial
/// </para>
///
/// <para>预测只放入当前笔和一块无限桌面，不放敌人、围栏、桌沿，所以它表达的是：
/// "如果这支笔从这里弹出去，玩家按住的这个点大致会怎么走"，而不是完整战斗结算。</para>
/// </summary>
public static class AimTrajectoryPredictor
{
    private const string PREDICTION_SCENE_NAME = "_AimTrajectoryPredictionScene";
    private const float SIMULATION_STEP = 0.02f;
    private const float MAX_SIMULATION_SECONDS = 5.0f;
    private const float MIN_SIMULATION_SECONDS = 0.18f;
    private const float STOP_SPEED = 0.05f;
    private const float SUPPORT_RAY_UP = 1.0f;
    private const float SUPPORT_RAY_DISTANCE = 6.0f;
    private const float FLOOR_THICKNESS = 0.12f;
    private const float FLOOR_SIZE = 80f;

    private const float DEFAULT_DYNAMIC_FRICTION = 0.42f;
    private const float MIN_FRICTION = 0.04f;
    private const float MAX_FRICTION = 2.5f;
    private const float FRICTION_DECEL_SCALE = 1.85f;
    private const float SPIN_DRIFT_METERS_PER_RAD = 0.055f;
    private const float ANGULAR_DECEL_SCALE = 5.5f;
    private const float MAX_ANGULAR_SPEED = 18f;
    private const float MAX_PREDICTION_SECONDS = 1.35f;
    private const float MIN_PREDICTION_SECONDS = 0.12f;
    private const float MIN_PREDICTION_SPEED = 0.05f;

    private static Scene _predictionScene;
    private static PhysicsScene _physicsScene;
    private static bool _warnedSimulationFailure;

    /// <summary>
    /// 生成预测轨迹采样点到 <paramref name="output"/>（预分配，零 GC）。
    /// 轨迹是玩家按压点释放后的运动过程采样，会随装配后的质量、质心、惯性、发射倍率和零件摩擦变化。
    /// </summary>
    public static void Predict(
        PenEntity pen,
        Vector3 contactWorld,
        Vector3 launchDir,
        float force,
        List<Vector3> output,
        int sampleCount = 25)
    {
        output.Clear();

        Rigidbody rb = pen != null ? pen.rb : null;
        if (pen == null || rb == null)
            return;

        if (TryPredictWithPhysicsScene(pen, contactWorld, launchDir, force, output, sampleCount))
            return;

        PredictFallback(pen, contactWorld, launchDir, force, output, sampleCount);
    }

    private static bool TryPredictWithPhysicsScene(
        PenEntity pen,
        Vector3 contactWorld,
        Vector3 launchDir,
        float force,
        List<Vector3> output,
        int sampleCount)
    {
        if (!Application.isPlaying)
            return false;

        Rigidbody sourceRb = pen.rb;
        if (sourceRb == null)
            return false;

        Vector3 fwd = launchDir;
        fwd.y = 0f;
        if (fwd.sqrMagnitude < 1e-6f)
            return false;
        fwd.Normalize();

        float launchSpeed = Mathf.Max(0f, pen.EstimateLaunchVelocity(force));
        if (launchSpeed <= MIN_PREDICTION_SPEED)
        {
            output.Add(contactWorld);
            return true;
        }

        GameObject scratchRoot = null;
        try
        {
            EnsurePredictionScene();
            if (!_physicsScene.IsValid())
                return false;

            Physics.SyncTransforms();
            scratchRoot = new GameObject("_AimPredictionScratch");
            scratchRoot.hideFlags = HideFlags.HideAndDontSave;
            SceneManager.MoveGameObjectToScene(scratchRoot, _predictionScene);

            FindSupportSurface(pen, out float surfaceY, out PhysicsMaterial surfaceMaterial);
            CreatePredictionFloor(scratchRoot.transform, pen.transform.position, surfaceY, surfaceMaterial);

            Rigidbody ghostRb = CreateGhostPen(pen, scratchRoot.transform);
            if (ghostRb == null)
                return false;

            Vector3 trackedPointLocal = sourceRb.transform.InverseTransformPoint(contactWorld);
            Vector3 effectiveContact = pen.GetEffectiveLaunchContactPoint(contactWorld);
            Vector3 impulse = fwd * (launchSpeed * Mathf.Max(ghostRb.mass, 1e-4f));
            ghostRb.AddForceAtPosition(impulse, effectiveContact, ForceMode.Impulse);

            output.Add(GetTrackedPointWorld(ghostRb, trackedPointLocal, contactWorld.y));

            int maxFrames = Mathf.CeilToInt(MAX_SIMULATION_SECONDS / SIMULATION_STEP);
            int minFrames = Mathf.CeilToInt(MIN_SIMULATION_SECONDS / SIMULATION_STEP);
            int sampleEvery = Mathf.Max(1, maxFrames / Mathf.Max(4, sampleCount));

            for (int frame = 1; frame <= maxFrames; frame++)
            {
                _physicsScene.Simulate(SIMULATION_STEP);
                StabilizeGhostOnTable(ghostRb);

                bool shouldSample = frame % sampleEvery == 0 || frame == maxFrames;
                if (shouldSample)
                    output.Add(GetTrackedPointWorld(ghostRb, trackedPointLocal, contactWorld.y));

                if (frame >= minFrames &&
                    ghostRb.linearVelocity.sqrMagnitude <= STOP_SPEED * STOP_SPEED)
                {
                    if (!shouldSample)
                        output.Add(GetTrackedPointWorld(ghostRb, trackedPointLocal, contactWorld.y));
                    break;
                }
            }

            return output.Count >= 2;
        }
        catch (System.Exception ex)
        {
            if (!_warnedSimulationFailure)
            {
                Debug.LogWarning("[AimTrajectoryPredictor] 隐藏物理预测失败，已退回轻量公式。原因：" + ex.Message);
                _warnedSimulationFailure = true;
            }
            output.Clear();
            return false;
        }
        finally
        {
            if (scratchRoot != null)
                Object.DestroyImmediate(scratchRoot);
        }
    }

    private static void EnsurePredictionScene()
    {
        if (_predictionScene.IsValid() && _physicsScene.IsValid())
            return;

        var parameters = new CreateSceneParameters(LocalPhysicsMode.Physics3D);
        _predictionScene = SceneManager.CreateScene(PREDICTION_SCENE_NAME, parameters);
        _physicsScene = _predictionScene.GetPhysicsScene();
    }

    private static Vector3 GetTrackedPointWorld(Rigidbody rb, Vector3 trackedPointLocal, float visualY)
    {
        Vector3 point = rb.transform.TransformPoint(trackedPointLocal);
        point.y = visualY;
        return point;
    }

    private static void CreatePredictionFloor(Transform parent, Vector3 penPosition, float surfaceY, PhysicsMaterial material)
    {
        var floor = new GameObject("PredictionFloor");
        floor.hideFlags = HideFlags.HideAndDontSave;
        SceneManager.MoveGameObjectToScene(floor, _predictionScene);
        floor.transform.SetParent(parent, false);
        floor.transform.position = new Vector3(penPosition.x, surfaceY - FLOOR_THICKNESS * 0.5f, penPosition.z);

        var box = floor.AddComponent<BoxCollider>();
        box.size = new Vector3(FLOOR_SIZE, FLOOR_THICKNESS, FLOOR_SIZE);
        if (material != null)
            box.sharedMaterial = material;
    }

    private static Rigidbody CreateGhostPen(PenEntity pen, Transform parent)
    {
        Rigidbody sourceRb = pen.rb;
        var ghost = new GameObject("PredictionPen");
        ghost.hideFlags = HideFlags.HideAndDontSave;
        SceneManager.MoveGameObjectToScene(ghost, _predictionScene);
        ghost.transform.SetParent(parent, false);
        ghost.transform.SetPositionAndRotation(pen.transform.position, pen.transform.rotation);
        ghost.transform.localScale = Vector3.one;

        Rigidbody rb = ghost.AddComponent<Rigidbody>();
        rb.mass = sourceRb.mass;
        rb.centerOfMass = sourceRb.centerOfMass;
        rb.inertiaTensor = sourceRb.inertiaTensor;
        rb.inertiaTensorRotation = sourceRb.inertiaTensorRotation;
        rb.automaticCenterOfMass = false;
        rb.automaticInertiaTensor = false;
        rb.constraints = sourceRb.constraints;
        rb.useGravity = sourceRb.useGravity;
        rb.isKinematic = false;
        rb.interpolation = RigidbodyInterpolation.None;
        rb.collisionDetectionMode = CollisionDetectionMode.Discrete;
        rb.linearDamping = sourceRb.linearDamping;
        rb.angularDamping = sourceRb.angularDamping;
        rb.linearVelocity = Vector3.zero;
        rb.angularVelocity = Vector3.zero;
        rb.maxAngularVelocity = sourceRb.maxAngularVelocity;

        int copied = 0;
        Collider[] colliders = pen.GetComponentsInChildren<Collider>();
        foreach (Collider source in colliders)
        {
            if (source == null || !source.enabled || source.isTrigger)
                continue;

            if (TryCopyCollider(source, ghost.transform))
                copied++;
        }

        return copied > 0 ? rb : null;
    }

    private static void StabilizeGhostOnTable(Rigidbody rb)
    {
        Quaternion cur = rb.rotation;
        Vector3 eul = cur.eulerAngles;
        rb.MoveRotation(Quaternion.Euler(0f, eul.y, 0f));

        Vector3 angular = rb.angularVelocity;
        rb.angularVelocity = new Vector3(0f, angular.y, 0f);
    }

    private static bool TryCopyCollider(Collider source, Transform ghostRoot)
    {
        var go = new GameObject(source.name + "_PredictionCollider");
        go.hideFlags = HideFlags.HideAndDontSave;
        SceneManager.MoveGameObjectToScene(go, _predictionScene);
        go.transform.SetPositionAndRotation(source.transform.position, source.transform.rotation);
        go.transform.localScale = source.transform.lossyScale;
        go.transform.SetParent(ghostRoot, true);
        go.layer = 0;

        Collider copy = null;
        if (source is CapsuleCollider capsule)
        {
            var dst = go.AddComponent<CapsuleCollider>();
            dst.center = capsule.center;
            dst.radius = capsule.radius;
            dst.height = capsule.height;
            dst.direction = capsule.direction;
            copy = dst;
        }
        else if (source is BoxCollider box)
        {
            var dst = go.AddComponent<BoxCollider>();
            dst.center = box.center;
            dst.size = box.size;
            copy = dst;
        }
        else if (source is SphereCollider sphere)
        {
            var dst = go.AddComponent<SphereCollider>();
            dst.center = sphere.center;
            dst.radius = sphere.radius;
            copy = dst;
        }
        else if (source is MeshCollider mesh && mesh.sharedMesh != null && mesh.convex)
        {
            var dst = go.AddComponent<MeshCollider>();
            dst.sharedMesh = mesh.sharedMesh;
            dst.convex = true;
            copy = dst;
        }

        if (copy == null)
        {
            Object.DestroyImmediate(go);
            return false;
        }

        copy.isTrigger = false;
        copy.sharedMaterial = source.sharedMaterial;
        return true;
    }

    private static void FindSupportSurface(PenEntity pen, out float surfaceY, out PhysicsMaterial material)
    {
        surfaceY = EstimateBottomY(pen);
        material = null;
        Vector3 origin = pen.rb.worldCenterOfMass + Vector3.up * SUPPORT_RAY_UP;
        RaycastHit[] hits = Physics.RaycastAll(origin, Vector3.down, SUPPORT_RAY_DISTANCE, ~0, QueryTriggerInteraction.Ignore);

        float bestDistance = float.MaxValue;
        bool found = false;
        foreach (RaycastHit hit in hits)
        {
            if (hit.collider == null) continue;
            if (hit.collider.GetComponentInParent<PenEntity>() == pen) continue;
            if (hit.distance >= bestDistance) continue;

            bestDistance = hit.distance;
            surfaceY = hit.point.y;
            material = hit.collider.sharedMaterial;
            found = true;
        }

        if (found)
            return;
    }

    private static float EstimateBottomY(PenEntity pen)
    {
        float minY = float.PositiveInfinity;
        Collider[] colliders = pen.GetComponentsInChildren<Collider>();
        foreach (Collider col in colliders)
        {
            if (col == null || !col.enabled || col.isTrigger) continue;
            minY = Mathf.Min(minY, col.bounds.min.y);
        }

        return (!float.IsInfinity(minY) && !float.IsNaN(minY))
            ? minY - 0.002f
            : pen.transform.position.y - 0.05f;
    }

    private static void PredictFallback(
        PenEntity pen,
        Vector3 contactWorld,
        Vector3 launchDir,
        float force,
        List<Vector3> output,
        int sampleCount)
    {
        Rigidbody rb = pen.rb;
        Vector3 fwd = launchDir;
        fwd.y = 0f;
        if (fwd.sqrMagnitude < 1e-6f)
        {
            output.Add(contactWorld);
            return;
        }
        fwd.Normalize();

        float initialSpeed = Mathf.Max(0f, pen.EstimateLaunchVelocity(force));
        Vector3 comWorld = rb.worldCenterOfMass;
        output.Add(contactWorld);

        if (initialSpeed <= MIN_PREDICTION_SPEED)
            return;

        float mass = Mathf.Max(rb.mass, 1e-4f);
        Vector3 impulse = fwd * (initialSpeed * mass);
        Vector3 effectiveContact = pen.GetEffectiveLaunchContactPoint(contactWorld);
        Vector3 r = effectiveContact - comWorld;
        float angularImpulseY = Vector3.Dot(Vector3.Cross(r, impulse), Vector3.up);
        float inertiaY = EstimateInertiaAroundWorldAxis(rb, Vector3.up);
        float angularVelocityY = Mathf.Clamp(angularImpulseY / inertiaY, -MAX_ANGULAR_SPEED, MAX_ANGULAR_SPEED);

        float friction = EstimateDynamicFriction(pen, contactWorld);
        float gravity = Mathf.Max(0.1f, Mathf.Abs(Physics.gravity.y));
        float linearDecel = Mathf.Max(0.05f, gravity * friction * FRICTION_DECEL_SCALE);
        float angularDecel = Mathf.Max(0.05f, gravity * friction * ANGULAR_DECEL_SCALE);
        float stopTime = initialSpeed / linearDecel;
        float horizon = Mathf.Clamp(stopTime, MIN_PREDICTION_SECONDS, MAX_PREDICTION_SECONDS);
        int steps = Mathf.Max(4, sampleCount);
        float dt = horizon / steps;

        Vector3 right = Vector3.Cross(Vector3.up, fwd).normalized;
        Vector3 pos = comWorld;
        Vector3 trackedOffset = contactWorld - comWorld;
        float trackedY = contactWorld.y;
        float speed = initialSpeed;
        float omegaY = angularVelocityY;
        float yawRadians = 0f;

        for (int i = 1; i <= steps; i++)
        {
            float speed01 = Mathf.Clamp01(speed / initialSpeed);
            float lateralSpeed = omegaY * SPIN_DRIFT_METERS_PER_RAD * speed01;
            Vector3 velocity = fwd * speed + right * lateralSpeed;

            pos += velocity * dt;
            pos.y = comWorld.y;
            yawRadians += omegaY * dt;

            Vector3 tracked = pos + Quaternion.AngleAxis(yawRadians * Mathf.Rad2Deg, Vector3.up) * trackedOffset;
            tracked.y = trackedY;
            output.Add(tracked);

            speed = Mathf.Max(0f, speed - linearDecel * dt);
            omegaY = Mathf.MoveTowards(omegaY, 0f, angularDecel * dt);

            if (speed <= MIN_PREDICTION_SPEED && Mathf.Abs(omegaY) <= 0.05f)
                break;
        }
    }

    /// <summary>
    /// 旧签名保留给外部调试/临时调用。它只知道 COM 和接触点，无法体现完整零件物理。
    /// 战斗瞄准请优先调用 <see cref="Predict(PenEntity, Vector3, Vector3, float, List{Vector3}, int)"/>。
    /// </summary>
    /// <param name="comWorld">动态质心世界坐标（= rb.worldCenterOfMass）</param>
    /// <param name="contactWorld">玩家点击在笔表面的世界坐标</param>
    /// <param name="launchDir">发射方向单位向量</param>
    /// <param name="force">力度 [0,1]</param>
    /// <param name="distance">轨迹总长度（米）= force × maxDragDistance</param>
    /// <param name="curvatureScale">弯曲灵敏度乘子，单位 ≈ 米每米偏移。典型 4~8。</param>
    /// <param name="output">预分配 List；调用前会被 Clear</param>
    /// <param name="sampleCount">采样点数（末点含）</param>
    public static void Predict(
        Vector3 comWorld,
        Vector3 contactWorld,
        Vector3 launchDir,
        float force,
        float distance,
        float curvatureScale,
        List<Vector3> output,
        int sampleCount = 25)
    {
        output.Clear();
        if (distance <= 1e-4f || launchDir.sqrMagnitude < 1e-6f)
        {
            output.Add(comWorld);
            return;
        }

        // launchDir 投到 XZ 平面 + 归一化
        Vector3 fwd = launchDir; fwd.y = 0f;
        if (fwd.sqrMagnitude < 1e-6f) { output.Add(comWorld); return; }
        fwd.Normalize();

        Vector3 side = Vector3.Cross(fwd, Vector3.up).normalized;

        // 点击点相对 COM 的偏移，投到 side 轴上（signed）
        Vector3 r = contactWorld - comWorld; r.y = 0f;
        float offsetSigned = Vector3.Dot(r, side);

        // 末端侧向总偏移 —— 负号：点击一侧 COM 漂向相反侧（离心旋转+非对称摩擦的净效应），
        // 和真实笔的直觉一致（click right → curve left）。
        float bendAmount = -offsetSigned * force * curvatureScale;

        float com_y = comWorld.y;
        for (int i = 0; i <= sampleCount; i++)
        {
            float t = i / (float)sampleCount;
            Vector3 straight = comWorld + fwd * (distance * t);
            // t² 插值：t=0 完全沿 fwd 切向，t=1 侧向偏移 bendAmount
            Vector3 p = straight + side * (t * t * bendAmount);
            p.y = com_y;
            output.Add(p);
        }
    }

    private static float EstimateInertiaAroundWorldAxis(Rigidbody rb, Vector3 worldAxis)
    {
        Vector3 axis = worldAxis.sqrMagnitude > 1e-6f ? worldAxis.normalized : Vector3.up;
        Quaternion principalRotation = rb.rotation * rb.inertiaTensorRotation;
        Vector3 localAxis = Quaternion.Inverse(principalRotation) * axis;
        Vector3 inertia = rb.inertiaTensor;

        float value =
            inertia.x * localAxis.x * localAxis.x +
            inertia.y * localAxis.y * localAxis.y +
            inertia.z * localAxis.z * localAxis.z;

        return Mathf.Max(value, 1e-4f);
    }

    private static float EstimateDynamicFriction(PenEntity pen, Vector3 contactWorld)
    {
        Collider[] colliders = pen.GetComponentsInChildren<Collider>();
        if (colliders == null || colliders.Length == 0)
            return DEFAULT_DYNAMIC_FRICTION;

        float weightedFriction = 0f;
        float totalWeight = 0f;

        foreach (Collider col in colliders)
        {
            if (col == null || col.isTrigger) continue;

            float friction = GetColliderDynamicFriction(col);
            Bounds b = col.bounds;
            Vector3 size = b.size;
            float volumeWeight = Mathf.Max(0.001f, Mathf.Sqrt(Mathf.Max(size.x * size.y * size.z, 0.0001f)));
            float contactDistance = Vector3.Distance(col.ClosestPoint(contactWorld), contactWorld);
            float localWeight = 1f / Mathf.Max(0.12f, contactDistance + 0.12f);
            float weight = volumeWeight * localWeight;

            weightedFriction += friction * weight;
            totalWeight += weight;
        }

        if (totalWeight <= 1e-5f)
            return DEFAULT_DYNAMIC_FRICTION;

        return Mathf.Clamp(weightedFriction / totalWeight, MIN_FRICTION, MAX_FRICTION);
    }

    private static float GetColliderDynamicFriction(Collider col)
    {
        PhysicsMaterial mat = col.sharedMaterial;

        if (mat == null)
            return DEFAULT_DYNAMIC_FRICTION;

        return Mathf.Clamp(mat.dynamicFriction, MIN_FRICTION, MAX_FRICTION);
    }
}
