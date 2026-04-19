using System.Collections;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// 改装场景零件统一交互容器。
///
/// 体验特性：
///   - 磁吸引导：拖拽接近 socket 时目标位置被吸引偏移
///   - 吸附预览：接近 socket 时在 socket 位置显示半透明预览
///   - 吸附动画：EaseOutCubic 平滑飞入 + 弹跳缩放
///   - 替换弹出：旧零件向拖来反方向弹出
///   - 无效反馈：放不了时抖动回弹而非直接掉落
///   - 音效接口：吸附/弹出/无效时播放音效
/// </summary>
public class WorkshopPart : MonoBehaviour
{
    public enum PartState { Assembled, Dragging, Snapping, Loose }

    [Header("引用")]
    public PenPartData PartData;
    public WorkshopSlot Slot;
    public BoxCollider DragArea;

    public PartState State { get; private set; } = PartState.Loose;
    public bool IsBarrel => PartData != null && PartData.Category == PartType.Barrel;

    // 从全局单例读取配置
    private static WorkshopConfig Cfg => WorkshopConfig.Instance;
    private float SnapDistance => Cfg != null ? Cfg.SnapDistance : 0.15f;
    private float DragLiftHeight => Cfg != null ? Cfg.DragLiftHeight : 0.03f;
    private float DragDamping => Cfg != null ? Cfg.DragDamping : 50f;
    private float SnapDuration => Cfg != null ? Cfg.SnapDuration : 0.12f;
    private float BounceDuration => Cfg != null ? Cfg.BounceDuration : 0.08f;
    private float BounceScale => Cfg != null ? Cfg.BounceScale : 1.05f;
    private float ScatterForce => Cfg != null ? Cfg.ScatterForce : 1.5f;
    private float MagnetStrength => Cfg != null ? Cfg.MagnetStrength : 0.3f;
    private float InvalidShakeDuration => Cfg != null ? Cfg.InvalidShakeDuration : 0.2f;
    private float InvalidShakeIntensity => Cfg != null ? Cfg.InvalidShakeIntensity : 0.02f;
    private AudioClip SnapSound => Cfg != null ? Cfg.SnapSound : null;
    private AudioClip EjectSound => Cfg != null ? Cfg.EjectSound : null;
    private AudioClip InvalidSound => Cfg != null ? Cfg.InvalidSound : null;

    private Rigidbody _rb;
    private Collider _col;
    private Camera _cam;
    private SocketHighlighter _highlighter;
    private AudioSource _audio;
    private bool _isDragging;
    private readonly DragInputState _dragInput = new();
    private Plane _dragPlane;
    private Vector3 _dragOffset;
    private Vector3 _dragTarget;
    private float _originalDrag;
    private float _originalAngularDrag;

    // 磁吸 + 预览 + 替换暗示
    private PartSocket _nearestSocket;
    private GameObject _preview;
    private WorkshopPart _threattenedOccupant;
    private Vector3 _threattenedOriginalScale;

    // 入场弹入
    private bool _introActive;
    private Coroutine _introCo;

    // 拖拽起始位置（笔杆回弹用）
    private Vector3 _dragStartPosition;
    private Quaternion _dragStartRotation;

    // 拖拽中忽略碰撞的目标
    private Collider _ignoredCollider;

    // 笔杆专用
    private Vector3 _slotPosition;
    private Quaternion _slotRotation;
    private bool _slotAnchorSet;

    // ─── 生命周期 ─────────────────────────────────────────────────────────────

    private void Awake()
    {
        _rb = GetComponent<Rigidbody>();
        _col = GetComponent<Collider>();
        _cam = Camera.main;
        _highlighter = FindFirstObjectByType<SocketHighlighter>();
        _originalDrag = _rb.linearDamping;
        _originalAngularDrag = _rb.angularDamping;
    }

    private void OnEnable() => WorkshopPartRegistry.Instance?.Register(this);
    private void OnDisable() => WorkshopPartRegistry.Instance?.Unregister(this);

    // ─── 公共 API ─────────────────────────────────────────────────────────────

    public void SetSlotAnchor(Vector3 position, Quaternion rotation)
    {
        _slotPosition = position;
        _slotRotation = rotation;
        _slotAnchorSet = true;
    }

    public void SetAssembled(bool isRoot = false)
    {
        State = PartState.Assembled;
        _rb.isKinematic = true;
        _rb.Sleep();
        // 子零件设为 trigger：不参与物理碰撞但仍能被 Raycast 命中，可直接拖拽
        if (!isRoot && _col != null)
            _col.isTrigger = true;
        WorkshopPartRegistry.Instance?.NotifyStateChanged(this);
    }

    public void SetLoose()
    {
        State = PartState.Loose;
        transform.SetParent(null);
        _rb.isKinematic = false;
        _rb.useGravity = true;
        _rb.linearDamping = _originalDrag;
        _rb.angularDamping = _originalAngularDrag;
        if (_col != null)
        {
            _col.enabled = true;
            _col.isTrigger = false;
        }
        WorkshopPartRegistry.Instance?.NotifyStateChanged(this);
    }

    /// <summary>弹出：若有 DragArea（= 抽屉散落区），沿抛物线动画飞入抽屉中心附近；
    /// 否则回退到基于 awayFrom 的随机方向物理冲量。</summary>
    public void Eject(Vector3 awayFrom)
    {
        if (DragArea != null)
        {
            StartCoroutine(AnimateEjectToArea());
            return;
        }

        SetLoose();
        Vector3 dir = (transform.position - awayFrom).normalized;
        if (dir.sqrMagnitude < 0.01f)
            dir = new Vector3(Random.Range(-1f, 1f), 0.5f, Random.Range(-1f, 1f)).normalized;
        dir.y = Mathf.Max(dir.y, 0.3f);
        _rb.AddForce(dir * ScatterForce, ForceMode.Impulse);
        PlaySound(EjectSound);
    }

    /// <summary>抛物线抛入抽屉：起点 = 当前位置，终点 = 抽屉中心 + 随机小偏移，
    /// apex 抬 0.35m。动画结束后交给物理自然 settle。</summary>
    private IEnumerator AnimateEjectToArea()
    {
        SetLoose();
        _rb.isKinematic = true; // 动画期间禁用物理
        _rb.linearVelocity = Vector3.zero;
        _rb.angularVelocity = Vector3.zero;

        Vector3 start = transform.position;
        Vector3 center = DragArea.bounds.center;
        Vector3 target = center + new Vector3(
            Random.Range(-0.1f, 0.1f), 0f, Random.Range(-0.1f, 0.1f));
        Vector3 apex = Vector3.Lerp(start, target, 0.5f) + Vector3.up * 0.35f;

        const float duration = 0.55f;
        float tumbleAxisX = Random.Range(180f, 540f);
        float tumbleAxisZ = Random.Range(-360f, 360f);

        float t = 0f;
        while (t < 1f)
        {
            t += Time.deltaTime / duration;
            float s = Mathf.Clamp01(t);
            // Quadratic bezier
            Vector3 a = Vector3.Lerp(start, apex, s);
            Vector3 b = Vector3.Lerp(apex, target, s);
            transform.position = Vector3.Lerp(a, b, s);
            transform.Rotate(tumbleAxisX * Time.deltaTime, 0f, tumbleAxisZ * Time.deltaTime, Space.World);
            yield return null;
        }
        transform.position = target;
        _rb.isKinematic = false;
        _rb.linearVelocity = Vector3.zero;
        _rb.angularVelocity = Vector3.zero;
        PlaySound(EjectSound);
    }

    // ─── 拖拽 ─────────────────────────────────────────────────────────────────

    private void Update()
    {
        if (!_isDragging) return;
        if (_cam == null) return;
        var mouse = Mouse.current;
        if (mouse == null) return;

        // 无论按住还是点击切换模式，拖拽中每帧都让目标跟随鼠标
        var ray = ScreenHelper.ScreenPointToRay(_cam, mouse.position.ReadValue());
        if (_dragPlane.Raycast(ray, out float enter))
        {
            Vector3 target = ray.GetPoint(enter) + _dragOffset;
            if (DragArea != null)
            {
                var b = DragArea.bounds;
                target.x = Mathf.Clamp(target.x, b.min.x, b.max.x);
                target.z = Mathf.Clamp(target.z, b.min.z, b.max.z);
            }

            // 磁吸引导：非笔杆零件接近 socket 时目标被吸引偏移
            if (!IsBarrel)
            {
                var socket = FindNearestSocket();
                UpdatePreview(socket);
                if (socket != null)
                {
                    float dist = Vector3.Distance(
                        _col.ClosestPoint(socket.transform.position),
                        socket.transform.position);
                    if (dist < SnapDistance)
                    {
                        float pull = (1f - dist / SnapDistance) * MagnetStrength;
                        target = Vector3.Lerp(target, socket.transform.position, pull);
                    }
                }
            }

            _dragTarget = target;
        }

        if (IsBarrel)
            _highlighter?.UpdateBarrelNearest(transform.position, SnapDistance);
        else
            _highlighter?.UpdateNearest(_col, SnapDistance);

        // 统一的 hold/click/cancel 手势处理
        var evt = _dragInput.Poll(
            mouse.leftButton.wasPressedThisFrame,
            mouse.leftButton.wasReleasedThisFrame,
            mouse.rightButton.wasPressedThisFrame);
        if (evt == DragInputState.Event.End) HandleMouseUp();
        else if (evt == DragInputState.Event.Cancel) CancelDrag();
    }

    private void FixedUpdate()
    {
        if (_isDragging)
            _rb.MovePosition(_dragTarget);
    }

    // ─── 入场 PopIn ────────────────────────────────────────────────────────────

    /// <summary>Workshop 打开时的弹入动画：scale 0 → 1，EaseOutBack 回冲。</summary>
    public void PlayIntroPopIn(float delay, Vector3 homeScale)
    {
        _introActive = true;
        transform.localScale = Vector3.zero;
        if (_introCo != null) StopCoroutine(_introCo);
        _introCo = StartCoroutine(IntroRoutine(delay, homeScale));
    }

    private IEnumerator IntroRoutine(float delay, Vector3 homeScale)
    {
        if (delay > 0f) yield return new WaitForSeconds(delay);
        const float duration = 0.38f;
        float t = 0f;
        while (t < 1f)
        {
            t += Time.deltaTime / duration;
            float s = EaseOutBackLocal(Mathf.Clamp01(t), 0.30f);
            transform.localScale = homeScale * Mathf.Max(0f, s);
            yield return null;
        }
        transform.localScale = homeScale;
        _introActive = false;
        _introCo = null;
    }

    private static float EaseOutBackLocal(float x, float overshoot)
    {
        float c1 = 1.70158f * (overshoot / 0.25f);
        float c3 = c1 + 1f;
        float u = x - 1f;
        return 1f + c3 * u * u * u + c1 * u * u;
    }

    public void BeginDrag()
    {
        if (State == PartState.Snapping) return;
        if (_isDragging) return; // 防重入（点击切换模式下再点到自己）
        if (_introActive) return; // 入场中不允许拖拽

        _dragInput.NotifyBegin();

        _dragStartPosition = transform.position;
        _dragStartRotation = transform.rotation;

        // 用 DragPlane 工具统一构造拖拽平面：锚点用实际点击网格点 + 抬升高度
        // 这样 45°/70° 俯视相机下光标和物体不会错位
        var ray = ScreenHelper.ScreenPointToRay(_cam, Mouse.current.position.ReadValue());
        DragPlane.TryBuild(_col, transform.position, ray, DragLiftHeight,
            out _dragPlane, out _dragOffset, out _);

        if (State == PartState.Assembled)
            transform.SetParent(null);

        if (_col != null)
        {
            _col.enabled = true;
            _col.isTrigger = false;
        }

        // 拖拽中忽略笔杆碰撞，防止靠近时抖动
        IgnoreBarrelCollision(true);

        _rb.isKinematic = false;
        _rb.useGravity = false;
        _rb.linearDamping = DragDamping;
        _rb.angularDamping = DragDamping;
        _rb.interpolation = RigidbodyInterpolation.Interpolate;
        _rb.collisionDetectionMode = CollisionDetectionMode.Continuous;
        _rb.constraints = RigidbodyConstraints.FreezeRotation;
        _rb.linearVelocity = Vector3.zero;
        _rb.angularVelocity = Vector3.zero;
        _dragTarget = transform.position;

        _isDragging = true;
        State = PartState.Dragging;
        WorkshopPartRegistry.Instance?.NotifyStateChanged(this);

        if (IsBarrel)
        {
            Vector3 pos = _slotAnchorSet ? _slotPosition
                : WorkshopSlotCalculator.GetSlotFloorCenter(Slot);
            // 检查改装台笔杆是否有子零件 → 有则红色（不可替换），无则绿色（可替换）
            var existing = WorkshopPartRegistry.Instance?.GetAssembledBarrel();
            bool blocked = false;
            if (existing != null)
            {
                foreach (var child in existing.GetComponentsInChildren<WorkshopPart>())
                {
                    if (child != existing && child.State == PartState.Assembled)
                    { blocked = true; break; }
                }
            }
            _highlighter?.ShowBarrelSlot(pos, blocked);
        }
        else
        {
            _highlighter?.ShowCompatible(PartData);
        }
    }

    /// <summary>右键取消：走"回到起始位置"路径，不尝试吸附/装配/脱离</summary>
    private void CancelDrag()
    {
        _isDragging = false;
        _dragInput.ForceEnd();
        _highlighter?.HideAll();
        ClearPreviewAndThreat();
        IgnoreBarrelCollision(false);
        _rb.useGravity = false;
        StartCoroutine(AnimateReturnToStart());
    }

    private void HandleMouseUp()
    {
        _isDragging = false;
        _dragInput.ForceEnd();
        _highlighter?.HideAll();
        ClearPreviewAndThreat();
        IgnoreBarrelCollision(false);

        _rb.useGravity = true;
        _rb.linearDamping = _originalDrag;
        _rb.angularDamping = _originalAngularDrag;
        _rb.interpolation = RigidbodyInterpolation.None;
        _rb.collisionDetectionMode = CollisionDetectionMode.Discrete;
        _rb.constraints = RigidbodyConstraints.None;

        bool inSlot = Slot != null && Slot.OverlapsXZ(_col.bounds);

        if (IsBarrel)
            HandleBarrelRelease(inSlot);
        else
            HandlePartRelease(inSlot);
    }

    // ─── 笔杆松手 ────────────────────────────────────────────────────────────

    private void HandleBarrelRelease(bool inSlot)
    {
        if (inSlot)
        {
            var existing = WorkshopPartRegistry.Instance?.GetAssembledBarrel();
            if (existing != null && existing != this)
            {
                // 检查旧笔杆上是否有子零件
                bool hasChildren = false;
                foreach (var child in existing.GetComponentsInChildren<WorkshopPart>())
                {
                    if (child != existing && child.State == PartState.Assembled)
                    { hasChildren = true; break; }
                }

                if (hasChildren)
                {
                    // 有子零件 → 拒绝替换，回弹
                    StartCoroutine(AnimateReturnToStart());
                    return;
                }

                // 无子零件 → 替换，旧笔杆移到新笔杆起始位置
                StartCoroutine(AnimateSwapOut(existing, _dragStartPosition, _dragStartRotation));
            }

            StartCoroutine(AnimateBarrelSnap());
        }
        else
        {
            foreach (var child in GetComponentsInChildren<WorkshopPart>())
            {
                if (child != this && child.State == PartState.Assembled)
                    child.Eject(transform.position);
            }
            State = PartState.Loose;
            WorkshopPartRegistry.Instance?.NotifyStateChanged(this);
        }
    }

    private IEnumerator AnimateBarrelSnap()
    {
        if (!_slotAnchorSet && Slot != null)
            WorkshopSlotCalculator.PlaceBarrelOnSlot(this, Slot);

        State = PartState.Snapping;
        WorkshopPartRegistry.Instance?.NotifyStateChanged(this);
        _rb.isKinematic = true;

        // 平滑飞入
        Vector3 startPos = transform.position;
        Quaternion startRot = transform.rotation;
        for (float t = 0f; t < 1f; t += Time.deltaTime / SnapDuration)
        {
            float ease = EaseOutCubic(Mathf.Clamp01(t));
            transform.position = Vector3.Lerp(startPos, _slotPosition, ease);
            transform.rotation = Quaternion.Slerp(startRot, _slotRotation, ease);
            yield return null;
        }
        transform.SetPositionAndRotation(_slotPosition, _slotRotation);
        _rb.position = _slotPosition;
        _rb.rotation = _slotRotation;

        // 弹跳缩放
        yield return AnimateBounce();

        State = PartState.Assembled;
        if (_col != null) _col.enabled = true;
        WorkshopPartRegistry.Instance?.NotifyStateChanged(this);
        PlaySound(SnapSound);
    }

    // ─── 零件松手 ─────────────────────────────────────────────────────────────

    private void HandlePartRelease(bool inSlot)
    {
        if (inSlot && TrySnapToSocket())
            return;

        // 无效放置：在改装区内但不在 socket 范围 → 抖动反馈
        if (inSlot)
        {
            StartCoroutine(AnimateInvalidShake());
            return;
        }

        State = PartState.Loose;
        transform.SetParent(null);
        WorkshopPartRegistry.Instance?.NotifyStateChanged(this);
    }

    private bool TrySnapToSocket()
    {
        var barrel = WorkshopPartRegistry.Instance?.GetAssembledBarrel();
        if (barrel == null) return false;

        PartSocket best = null;
        float bestDist = SnapDistance;
        WorkshopPart occupant = null;

        foreach (var socket in barrel.GetComponentsInChildren<PartSocket>())
        {
            if (socket.SocketType != PartData.PlugsInto) continue;
            float dist = Vector3.Distance(
                _col.ClosestPoint(socket.transform.position),
                socket.transform.position);
            if (dist >= bestDist) continue;

            bestDist = dist;
            best = socket;
            occupant = socket.GetComponentInChildren<WorkshopPart>();
        }

        if (best == null) return false;

        // 替换：旧零件平滑移到新零件的起始位置（交换位置）
        if (occupant != null && occupant != this)
        {
            Vector3 swapTarget = _dragStartPosition;
            Quaternion swapRot = _dragStartRotation;
            StartCoroutine(AnimateSwapOut(occupant, swapTarget, swapRot));
        }

        StartCoroutine(AnimateSnapToSocket(best));
        return true;
    }

    private IEnumerator AnimateSnapToSocket(PartSocket socket)
    {
        State = PartState.Snapping;
        _rb.isKinematic = true;

        // 平滑飞入
        Vector3 startPos = transform.position;
        Quaternion startRot = transform.rotation;
        Vector3 targetPos = socket.transform.position;
        Quaternion targetRot = socket.transform.rotation;

        for (float t = 0f; t < 1f; t += Time.deltaTime / SnapDuration)
        {
            float ease = EaseOutCubic(Mathf.Clamp01(t));
            transform.position = Vector3.Lerp(startPos, targetPos, ease);
            transform.rotation = Quaternion.Slerp(startRot, targetRot, ease);
            yield return null;
        }

        transform.SetParent(socket.transform);
        transform.localPosition = Vector3.zero;
        transform.localRotation = Quaternion.identity;

        // 弹跳缩放
        yield return AnimateBounce();

        SetAssembled(isRoot: false);
        PlaySound(SnapSound);
    }

    // ─── 磁吸 + 预览 ─────────────────────────────────────────────────────────

    /// <summary>查找当前最近的兼容 socket</summary>
    private PartSocket FindNearestSocket()
    {
        var barrel = WorkshopPartRegistry.Instance?.GetAssembledBarrel();
        if (barrel == null || PartData == null) return null;

        PartSocket best = null;
        float bestDist = SnapDistance;

        foreach (var socket in barrel.GetComponentsInChildren<PartSocket>())
        {
            if (socket.SocketType != PartData.PlugsInto) continue;
            float dist = Vector3.Distance(
                _col.ClosestPoint(socket.transform.position),
                socket.transform.position);
            if (dist < bestDist)
            {
                bestDist = dist;
                best = socket;
            }
        }

        return best;
    }

    /// <summary>在 socket 位置显示预览 + 暗示旧零件即将被替换</summary>
    private void UpdatePreview(PartSocket socket)
    {
        // socket 没变，不处理
        if (socket == _nearestSocket) return;

        // 清理旧状态
        ClearPreviewAndThreat();
        _nearestSocket = socket;

        if (socket == null || PartData == null) return;

        // 预览影子
        if (PartData.VisualPrefab != null)
        {
            _preview = Instantiate(PartData.VisualPrefab, socket.transform);
            _preview.transform.localPosition = Vector3.zero;
            _preview.transform.localRotation = Quaternion.identity;

            MaterialAlphaUtility.ApplyAlpha(_preview, 0.3f);
            foreach (var c in _preview.GetComponentsInChildren<Collider>())
                c.enabled = false;
        }

        // 替换暗示：旧零件缩小 + 半透明
        var occupant = socket.GetComponentInChildren<WorkshopPart>();
        if (occupant != null && occupant != this)
        {
            _threattenedOccupant = occupant;
            _threattenedOriginalScale = occupant.transform.localScale;
            occupant.transform.localScale = _threattenedOriginalScale * 0.85f;
            MaterialAlphaUtility.ApplyAlpha(occupant.gameObject, 0.5f);
        }
    }

    private void ClearPreviewAndThreat()
    {
        if (_preview != null)
        {
            Destroy(_preview);
            _preview = null;
        }

        // 恢复被暗示替换的旧零件
        if (_threattenedOccupant != null)
        {
            _threattenedOccupant.transform.localScale = _threattenedOriginalScale;
            MaterialAlphaUtility.ApplyAlpha(_threattenedOccupant.gameObject, 1f);
            _threattenedOccupant = null;
        }

        _nearestSocket = null;
    }

    // ─── 动画工具 ─────────────────────────────────────────────────────────────

    /// <summary>吸附完成后的弹跳缩放（视觉反馈"咔嗒到位"）</summary>
    private IEnumerator AnimateBounce()
    {
        Vector3 originalScale = transform.localScale;
        Vector3 bounceUp = originalScale * BounceScale;

        // 放大
        for (float t = 0f; t < 1f; t += Time.deltaTime / (BounceDuration * 0.5f))
        {
            transform.localScale = Vector3.Lerp(originalScale, bounceUp, t);
            yield return null;
        }
        // 缩回
        for (float t = 0f; t < 1f; t += Time.deltaTime / (BounceDuration * 0.5f))
        {
            transform.localScale = Vector3.Lerp(bounceUp, originalScale, t);
            yield return null;
        }
        transform.localScale = originalScale;
    }

    /// <summary>无效放置时的抖动反馈</summary>
    private IEnumerator AnimateInvalidShake()
    {
        PlaySound(InvalidSound);
        State = PartState.Snapping; // 暂时不可交互
        _rb.isKinematic = true;

        Vector3 origin = transform.position;
        float elapsed = 0f;
        while (elapsed < InvalidShakeDuration)
        {
            elapsed += Time.deltaTime;
            // 衰减抖动
            float decay = 1f - elapsed / InvalidShakeDuration;
            float x = Random.Range(-1f, 1f) * InvalidShakeIntensity * decay;
            float z = Random.Range(-1f, 1f) * InvalidShakeIntensity * decay;
            transform.position = origin + new Vector3(x, 0, z);
            yield return null;
        }
        transform.position = origin;

        // 抖动结束后变 Loose
        SetLoose();
    }

    /// <summary>替换时旧零件平滑飞到指定位置后变 Loose</summary>
    private IEnumerator AnimateSwapOut(WorkshopPart oldPart, Vector3 targetPos, Quaternion targetRot)
    {
        oldPart.transform.SetParent(null);
        var rb = oldPart.GetComponent<Rigidbody>();
        if (rb != null) rb.isKinematic = true;

        // 临时设为 Snapping 防止被交互
        oldPart.State = PartState.Snapping;

        Vector3 startPos = oldPart.transform.position;
        Quaternion startRot = oldPart.transform.rotation;

        for (float t = 0f; t < 1f; t += Time.deltaTime / SnapDuration)
        {
            float ease = EaseOutCubic(Mathf.Clamp01(t));
            oldPart.transform.position = Vector3.Lerp(startPos, targetPos, ease);
            oldPart.transform.rotation = Quaternion.Slerp(startRot, targetRot, ease);
            yield return null;
        }

        oldPart.transform.SetPositionAndRotation(targetPos, targetRot);
        oldPart.SetLoose();
        PlaySound(EjectSound);
    }

    /// <summary>笔杆被拒绝时平滑飞回拖起位置</summary>
    private IEnumerator AnimateReturnToStart()
    {
        PlaySound(InvalidSound);
        State = PartState.Snapping;
        _rb.isKinematic = true;

        Vector3 startPos = transform.position;
        Quaternion startRot = transform.rotation;

        for (float t = 0f; t < 1f; t += Time.deltaTime / SnapDuration)
        {
            float ease = EaseOutCubic(Mathf.Clamp01(t));
            transform.position = Vector3.Lerp(startPos, _dragStartPosition, ease);
            transform.rotation = Quaternion.Slerp(startRot, _dragStartRotation, ease);
            yield return null;
        }

        transform.SetPositionAndRotation(_dragStartPosition, _dragStartRotation);
        SetLoose();
    }

    /// <summary>拖拽中忽略/恢复与笔杆的碰撞</summary>
    private void IgnoreBarrelCollision(bool ignore)
    {
        if (IsBarrel || _col == null) return;

        var barrel = WorkshopPartRegistry.Instance?.GetAssembledBarrel();
        if (barrel == null) return;

        var barrelCol = barrel.GetComponent<Collider>();
        if (barrelCol == null) return;

        if (ignore)
        {
            _ignoredCollider = barrelCol;
            Physics.IgnoreCollision(_col, barrelCol, true);
        }
        else if (_ignoredCollider != null)
        {
            Physics.IgnoreCollision(_col, _ignoredCollider, false);
            _ignoredCollider = null;
        }
    }

    private static float EaseOutCubic(float t) => 1f - Mathf.Pow(1f - t, 3f);

    private void PlaySound(AudioClip clip)
    {
        if (clip == null) return;
        if (_audio == null)
        {
            _audio = gameObject.AddComponent<AudioSource>();
            _audio.spatialBlend = 1f;
            _audio.playOnAwake = false;
        }
        _audio.PlayOneShot(clip);
    }
}
