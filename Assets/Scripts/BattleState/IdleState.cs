using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// 等待状态：玩家可拖拽笔来蓄力弹射。
/// <para>
/// 命中判定走 <see cref="PenDragInteractor"/>（比笔大一圈的 SphereCast，表面吸附）；
/// 不挂时 fallback 到原生 RaycastAll。
/// </para>
/// <para>
/// 视听反馈统一通过 <see cref="AimPhaseChannelSO"/> 广播，订阅者（PenDragVisuals /
/// PenLaunchArrowView / PenAimThresholdFX / PenPressFeedback / PenHoverPresenter）
/// 各自响应，IdleState 本身不再持有视觉层引用（SRP: 发布者只发布事件，不知道谁在看）。
/// </para>
/// <para>
/// 坐标转换走 <see cref="ScreenHelper"/>，兼容 3DPixelCamera 双相机。
/// 拖拽平面取接触点高度（不是笔根部），确保玩家拖拽的空间坐标跟点击位置一致。
/// </para>
/// </summary>
public class IdleState : IEntityState
{
    /// <summary>
    /// 满拉阈值 —— LaunchForce ≥ 此值视为"已顶到拖拽上限"，供视觉/震动层判断。
    /// 设在 0.85：留出可感知的"快满了"警示区间（85%→100%），让玩家在真正顶到极限前就
    /// 看到红色反馈；若设得太高（如 0.97），屏幕手感上几乎碰不到，等于阈值形同虚设。
    /// </summary>
    private const float OVER_THRESHOLD = 0.85f;
    /// <summary>
    /// 最小有效发射力度。低于它属于"按住/调整按压点"，松手只取消，不进入 ActionState。
    /// </summary>
    private const float MIN_LAUNCH_FORCE = 0.06f;
    /// <summary>
    /// 世界空间死区，防止鼠标轻微抖动被读成发射。和 MIN_LAUNCH_FORCE 取较大者。
    /// </summary>
    private const float DEAD_ZONE_WORLD = 0.08f;
    /// <summary>
    /// 满拉后进入输入稳定模式。此时鼠标半径被 maxDrag 截断，细小屏幕抖动主要表现为角度噪声。
    /// </summary>
    private const float FULL_PULL_STABILITY_FORCE = 0.96f;
    private const float FULL_PULL_DIRECTION_DEADBAND_DEGREES = 2.0f;
    private const float FULL_PULL_DIRECTION_FOLLOW_DEGREES_PER_SECOND = 120f;
    private const float FULL_PULL_PREDICTION_REUSE_ANGLE_DEGREES = 0.25f;
    private const float FULL_PULL_PREDICTION_REUSE_CONTACT_DISTANCE = 0.025f;

    private readonly BattleStateMachine stateMachine;
    private readonly BattleContext ctx;

    private bool isDragging;
    private Vector3 dragStartWorldPos;
    private Vector3 dragPlaneReference; // 拖拽平面参考点（取自接触点的世界坐标）
    private readonly DragInputState _dragInput = new();

    // 缓存命中判定组件（TryStartDrag 时取一次）
    private PenDragInteractor _interactor;
    private PenCOMVisualizer _comVisualizer;

    /// <summary>
    /// 预测轨迹复用 buffer —— 避免每帧 new List 产生 GC。
    /// 容量只是初始值；预测器会按当前物理轨迹追加需要的采样点。
    /// </summary>
    private readonly List<Vector3> _trajectoryBuffer = new List<Vector3>(32);
    private readonly List<Vector3> _predictionCache = new List<Vector3>(32);
    private bool _hasStableLaunchDirection;
    private Vector3 _stableLaunchDirection;
    private bool _hasPredictionCache;
    private Vector3 _cachedPredictionDirection;
    private Vector3 _cachedPredictionContact;
    private float _cachedPredictionForce;

    public IdleState(BattleStateMachine stateMachine, BattleContext ctx)
    {
        this.stateMachine = stateMachine;
        this.ctx = ctx;
    }

    // ─── 状态生命周期 ──────────────────────────────────────────────────────────

    public void Enter()
    {
        isDragging = false;
        _dragInput.ForceEnd();
        ResetAimStability();
        // 战斗只使用"按住拖拽，松手释放"。点击切换适合改装/商店拖零件，不适合弹笔瞄准。
        _dragInput.HoldThreshold = 0f;
        _interactor = ctx.pen != null ? ctx.pen.GetComponent<PenDragInteractor>() : null;
        _comVisualizer = ctx.pen != null ? ctx.pen.GetComponent<PenCOMVisualizer>() : null;
        _comVisualizer?.EndAimVisualization();
    }

    /// <summary>
    /// 防御式重新获取 interactor —— 防止任何 edge case 让缓存失效（脚本 hot-reload / Workshop
    /// 重建后组件被意外替换等）。正常情况仍然拿到同一个 component 引用，零成本。
    /// </summary>
    private void RefreshInteractorCache()
    {
        if (ctx.pen == null) { _interactor = null; _comVisualizer = null; return; }
        // 仅在失效（null 或被销毁）时重取；Unity 的 == null 会把已销毁对象识别为 null
        if (_interactor == null)
            _interactor = ctx.pen.GetComponent<PenDragInteractor>();
        if (_comVisualizer == null)
            _comVisualizer = ctx.pen.GetComponent<PenCOMVisualizer>();
    }

    public void Update()
    {
        var mouse = Mouse.current;
        if (mouse == null) return;

        if (mouse.leftButton.wasPressedThisFrame)
            TryStartDrag(mouse.position.ReadValue());

        if (isDragging)
        {
            UpdateDrag(mouse.position.ReadValue());

            var evt = _dragInput.Poll(
                mouse.leftButton.wasPressedThisFrame,
                mouse.leftButton.wasReleasedThisFrame,
                mouse.rightButton.wasPressedThisFrame);
            if (evt == DragInputState.Event.End) FinishDrag();
            else if (evt == DragInputState.Event.Cancel) CancelDrag();
        }
    }

    public void Exit()
    {
        // 状态切换中途若残留在拖拽，兜底广播取消（视觉订阅者会收尾）
        if (isDragging) ctx.AimChannel?.RaiseCancelled();
        ResetAimStability();
        _comVisualizer?.EndAimVisualization();
    }

    // ─── 拖拽流程 ──────────────────────────────────────────────────────────────

    private void TryStartDrag(Vector2 screenPos)
    {
        RefreshInteractorCache(); // 防御：每次点击前确保 interactor 引用有效
        if (isDragging) return;
        if (ctx.pen == null)
        {
            Debug.LogWarning("[IdleState] ctx.pen 为 null，无法开始拖拽。");
            return;
        }
        if (ctx.pen.penCollider == null)
        {
            Debug.LogWarning("[IdleState] penCollider 为 null，无法开始拖拽。BuildBattleView 可能没有正确重建 BarrelCollider | " +
                             "pen active=" + ctx.pen.gameObject.activeInHierarchy +
                             " Assembly=" + (ctx.pen.Assembly == null ? "null" : "ok") +
                             " Assembly.BarrelCollider=" + (ctx.pen.Assembly == null ? "?" : (ctx.pen.Assembly.BarrelCollider == null ? "null" : "ok")));
            return;
        }

        Camera cam = Camera.main;
        if (cam == null)
        {
            Debug.LogWarning("[IdleState] Camera.main 为 null，无法开始拖拽。");
            return;
        }
        var ray = ScreenHelper.ScreenPointToRay(cam, screenPos);

        Vector3 contactPoint;
        bool hit = _interactor != null
            ? _interactor.TryHit(cam, screenPos, ray, out contactPoint)
            : LegacyRaycastHit(ray, out contactPoint);

        if (!hit) return;

        ctx.ContactPointWorld = contactPoint;
        dragPlaneReference = contactPoint;
        ResetAimStability();

        isDragging = true;
        _dragInput.NotifyBegin();
        dragStartWorldPos = ScreenHelper.ScreenToWorldOnPlane(cam, screenPos, dragPlaneReference);

        _comVisualizer?.BeginAimVisualization(contactPoint);
        ctx.AimChannel?.RaiseBegan(contactPoint);
    }

    private void UpdateDrag(Vector2 screenPos)
    {
        Camera cam = Camera.main;
        // 拖拽中途若相机被换走（Cinemachine 切镜头 / Camera.main tag 被夺）→ 静默退出当前帧
        // 不取消拖拽（下一帧 Camera.main 恢复就继续），避免单帧失镜头让玩家"手感断了"
        if (cam == null) return;
        Vector3 currentWorldPos = ScreenHelper.ScreenToWorldOnPlane(cam, screenPos, dragPlaneReference);
        Vector3 dragVector = currentWorldPos - dragStartWorldPos;
        dragVector.y = 0f;

        float maxDrag = Mathf.Max(0.001f, ctx.pen.maxDragDistance);
        if (dragVector.magnitude > maxDrag)
            dragVector = dragVector.normalized * maxDrag;

        float dragDistance = dragVector.magnitude;
        float rawForce = Mathf.Clamp01(dragDistance / maxDrag);
        float minLaunchForce = GetMinLaunchForce(maxDrag);
        bool isArmed = rawForce >= minLaunchForce && dragVector.sqrMagnitude > 0.0001f;

        Vector3 targetLaunchDirection = isArmed ? -dragVector.normalized : Vector3.zero;
        ctx.LaunchDirection = isArmed ? StabilizeLaunchDirection(targetLaunchDirection, rawForce) : Vector3.zero;
        ctx.LaunchForce = isArmed ? rawForce : 0f;

        Vector3 dragEnd = ctx.ContactPointWorld + dragVector;
        bool overThreshold = isArmed && rawForce >= OVER_THRESHOLD;

        // 物理预测轨迹 —— 基于当前物理参数算出发射后 COM 的 25 帧走向
        PredictTrajectory(isArmed, rawForce);
        _comVisualizer?.UpdateAimVisualization(ctx.ContactPointWorld, ctx.LaunchDirection, rawForce);
        AimReadabilityMetrics.Build(
            pen: ctx.pen,
            contactPointWorld: ctx.ContactPointWorld,
            launchDirection: ctx.LaunchDirection,
            force: rawForce,
            predictedTrajectory: _trajectoryBuffer,
            effectiveContactPointWorld: out Vector3 effectiveContactPoint,
            centerOfMassWorld: out Vector3 centerOfMassWorld,
            comOffsetNormalized: out float comOffsetNormalized,
            contactOffsetNormalized: out float contactOffsetNormalized,
            spin01: out float spin01,
            estimatedFriction: out float estimatedFriction,
            friction01: out float friction01,
            massKg: out float massKg,
            mass01: out float mass01,
            launchSpeed: out float launchSpeed,
            predictionArcLength: out float predictionArcLength,
            predictionCurve01: out float predictionCurve01);

        var sample = new AimSample(
            contactPoint: ctx.ContactPointWorld,
            dragEnd: dragEnd,
            penPos: ctx.pen.transform.position,
            launchDir: ctx.LaunchDirection,
            force: rawForce,
            overThreshold: overThreshold,
            predictedTrajectory: _trajectoryBuffer,
            isArmed: isArmed,
            minLaunchForce: minLaunchForce,
            dragDistanceWorld: dragDistance,
            maxDragDistanceWorld: maxDrag,
            effectiveContactPoint: effectiveContactPoint,
            centerOfMassWorld: centerOfMassWorld,
            comOffsetNormalized: comOffsetNormalized,
            contactOffsetNormalized: contactOffsetNormalized,
            spin01: spin01,
            estimatedFriction: estimatedFriction,
            friction01: friction01,
            massKg: massKg,
            mass01: mass01,
            launchSpeed: launchSpeed,
            predictionArcLength: predictionArcLength,
            predictionCurve01: predictionCurve01);
        ctx.AimChannel?.RaiseUpdated(sample);
    }

    /// <summary>基于当前装配后的 Rigidbody/零件物理数据生成短时 COM 预测。</summary>
    private void PredictTrajectory(bool isArmed, float force)
    {
        var pen = ctx.pen;
        var rb = pen != null ? pen.rb : null;
        if (!isArmed || pen == null || rb == null || ctx.LaunchDirection.sqrMagnitude < 0.0001f)
        {
            _trajectoryBuffer.Clear();
            ClearPredictionCache();
            return;
        }

        if (CanReusePrediction(force))
        {
            CopyTrajectory(_predictionCache, _trajectoryBuffer);
            return;
        }

        AimTrajectoryPredictor.Predict(
            pen: pen,
            contactWorld: ctx.ContactPointWorld,
            launchDir: ctx.LaunchDirection,
            force: force,
            output: _trajectoryBuffer);
        UpdatePredictionCache(force);
    }

    private Vector3 StabilizeLaunchDirection(Vector3 targetDirection, float force)
    {
        targetDirection.y = 0f;
        if (targetDirection.sqrMagnitude <= 1e-6f)
            return Vector3.zero;
        targetDirection.Normalize();

        if (force < FULL_PULL_STABILITY_FORCE)
        {
            _hasStableLaunchDirection = false;
            _stableLaunchDirection = targetDirection;
            return targetDirection;
        }

        if (!_hasStableLaunchDirection || _stableLaunchDirection.sqrMagnitude <= 1e-6f)
        {
            _stableLaunchDirection = targetDirection;
            _hasStableLaunchDirection = true;
            return targetDirection;
        }

        float angle = Vector3.Angle(_stableLaunchDirection, targetDirection);
        if (angle <= FULL_PULL_DIRECTION_DEADBAND_DEGREES)
            return _stableLaunchDirection;

        float maxStep = FULL_PULL_DIRECTION_FOLLOW_DEGREES_PER_SECOND * Time.deltaTime;
        float stepDegrees = Mathf.Min(Mathf.Max(0f, angle - FULL_PULL_DIRECTION_DEADBAND_DEGREES), maxStep);
        _stableLaunchDirection = Vector3.RotateTowards(
            _stableLaunchDirection,
            targetDirection,
            stepDegrees * Mathf.Deg2Rad,
            0f).normalized;
        return _stableLaunchDirection;
    }

    private bool CanReusePrediction(float force)
    {
        if (!_hasPredictionCache || _predictionCache.Count < 2 || force < FULL_PULL_STABILITY_FORCE)
            return false;
        if (Mathf.Abs(force - _cachedPredictionForce) > 0.0025f)
            return false;

        Vector3 contact = ctx.ContactPointWorld;
        Vector3 cachedContact = _cachedPredictionContact;
        contact.y = 0f;
        cachedContact.y = 0f;
        if (Vector3.Distance(contact, cachedContact) > FULL_PULL_PREDICTION_REUSE_CONTACT_DISTANCE)
            return false;

        return Vector3.Angle(_cachedPredictionDirection, ctx.LaunchDirection) <= FULL_PULL_PREDICTION_REUSE_ANGLE_DEGREES;
    }

    private void UpdatePredictionCache(float force)
    {
        CopyTrajectory(_trajectoryBuffer, _predictionCache);
        _cachedPredictionDirection = ctx.LaunchDirection;
        _cachedPredictionContact = ctx.ContactPointWorld;
        _cachedPredictionForce = force;
        _hasPredictionCache = _predictionCache.Count >= 2;
    }

    private void ClearPredictionCache()
    {
        _predictionCache.Clear();
        _hasPredictionCache = false;
    }

    private void ResetAimStability()
    {
        _hasStableLaunchDirection = false;
        _stableLaunchDirection = Vector3.zero;
        ClearPredictionCache();
    }

    private static void CopyTrajectory(IReadOnlyList<Vector3> source, List<Vector3> target)
    {
        target.Clear();
        if (source == null) return;
        for (int i = 0; i < source.Count; i++)
            target.Add(source[i]);
    }

    private static float GetMinLaunchForce(float maxDragDistance)
    {
        return Mathf.Clamp01(Mathf.Max(MIN_LAUNCH_FORCE, DEAD_ZONE_WORLD / Mathf.Max(0.001f, maxDragDistance)));
    }

    private void FinishDrag()
    {
        if (ctx.LaunchForce <= 0f || ctx.LaunchDirection.sqrMagnitude < 0.0001f)
        {
            CancelDrag();
            return;
        }

        isDragging = false;
        _dragInput.ForceEnd();
        ResetAimStability();
        _comVisualizer?.EndAimVisualization();
        ctx.AimChannel?.RaiseReleased(ctx.LaunchForce);

        stateMachine.ChangeState(new ActionState(stateMachine, ctx));
    }

    private void CancelDrag()
    {
        isDragging = false;
        _dragInput.ForceEnd();
        ResetAimStability();
        ctx.LaunchDirection = Vector3.zero;
        ctx.LaunchForce = 0f;
        _comVisualizer?.EndAimVisualization();
        ctx.AimChannel?.RaiseCancelled();
    }

    // ─── Fallback（PenDragInteractor 未挂时走老逻辑） ────────────────────────

    private bool LegacyRaycastHit(Ray ray, out Vector3 contact)
    {
        contact = Vector3.zero;
        float bestDist = float.MaxValue;
        bool found = false;
        foreach (var h in Physics.RaycastAll(ray, 1000f))
        {
            var penOnHit = h.collider.GetComponentInParent<PenEntity>();
            if (penOnHit != ctx.pen) continue;
            if (h.distance < bestDist) { bestDist = h.distance; contact = h.point; found = true; }
        }
        return found;
    }
}
