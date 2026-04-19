using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// 商店单件商品的拖拽交互。
///
/// 手感对齐 WorkshopPart：
///   - 展示态：Rigidbody 运动学，静止在槽位
///   - 拖拽中：kinematic=false、useGravity=false、高阻尼，FixedUpdate 用 MovePosition 平滑追鼠标
///   - 松手进抽屉：开 gravity，零件自然下落；延迟后销毁并入库
///   - 松手落空：运动学 + EaseOutBack 弹回原位
/// </summary>
[RequireComponent(typeof(Collider))]
public class ShopPart : MonoBehaviour
{
    [Header("数据")]
    public PenPartData PartData;

    [Header("拖拽手感")]
    public float DragLiftHeight = 0.15f;
    [Tooltip("拖拽时 Rigidbody 阻尼（越大越稳）")]
    public float DragDamping = 50f;
    [Tooltip("拖拽时上下呼吸浮动幅度（叠加在 DragLiftHeight 上），0 为无")]
    public float HoverAmplitude = 0.03f;
    [Tooltip("呼吸浮动频率（Hz）")]
    public float HoverFrequency = 1.5f;

    [Header("反悔回弹")]
    public float ReturnDuration = 0.3f;
    [Tooltip("回弹过冲量（EaseOutBack）")]
    public float ReturnOvershoot = 0.25f;

    [Header("展示")]
    [Tooltip("未拖拽时绕 Y 轴自转速度（度/秒），0 为不旋转")]
    [SerializeField] private float _displayRotationSpeed = 20f;

    [Header("价格标签")]
    [Tooltip("买不起时零件本体颜色倍率（越小越灰暗）")]
    [SerializeField] private Color _denyTint = new Color(0.5f, 0.5f, 0.55f, 1f);

    [Header("购买下落")]
    [Tooltip("落入抽屉后多久销毁（让物理静置）")]
    public float FallSettleDuration = 0.8f;
    [Tooltip("购买瞬间 Y 方向小抬升，给磁吸留出下落时间")]
    public float PurchaseLift = 0.2f;
    [Tooltip("磁吸强度：朝 DropAnchor 方向的水平恒定加速度（m/s²），越大越明显")]
    public float MagnetStrength = 3f;
    [Tooltip("水平速度上限（m/s），防止磁吸累加到过快")]
    public float MagnetMaxSpeed = 1.5f;
    [Tooltip("视为'卡住'的水平速度阈值（m/s）：低于此值且仍在安全区外，视为卡边缘")]
    public float StuckSpeedThreshold = 0.2f;
    [Tooltip("卡住时磁吸倍率：克服摩擦力把零件推进抽屉")]
    public float StuckForceMultiplier = 6f;
    [Tooltip("落地超时兜底单轮时长；实际最多 4 轮，正常情况碰撞落地先触发根本不走这里")]
    public float LandingTimeout = 0.8f;
    [Tooltip("落进抽屉第一次碰撞时播放的反馈（声音/抖动等都在 MMFeedbacks 里配）")]
    public MoreMountains.Feedbacks.MMF_Player LandingFeedback;
    [Tooltip("低于此相对速度的落地碰撞不触发反馈（防贴底微振）")]
    public float LandingMinVelocity = 0.5f;

    private Rigidbody _rb;
    private Camera _cam;
    private ShopDrawer _drawer;
    private System.Action<ShopPart> _onPurchased;
    private System.Action<ShopPart> _onLanded;
    private System.Action<ShopPart> _onReject;
    private System.Func<PenPartData, bool> _affordCheck;

    // 价格标签 + 本体染色缓存
    private ShopPriceTag _priceTag;
    private readonly List<Renderer> _tintedRenderers = new List<Renderer>();
    private MaterialPropertyBlock _mpb;
    private bool _currentAffordable = true;

    private Vector3 _homePosition;
    private Quaternion _homeRotation;

    private bool _isDragging;
    private bool _purchased;
    private bool _landingPlayed;
    private Plane _dragPlane;
    private Vector3 _dragOffset;
    private Vector3 _dragTarget;
    private Coroutine _returnCo;
    private Coroutine _introCo;
    private Coroutine _rejectCo;
    private Collider _col;
    private readonly DragInputState _dragInput = new();

    // 入场弹出 + 悬停反馈
    private bool _introActive;
    private bool _isHovered;
    private float _hoverPhase;
    private Vector3 _hoverOffsetVel;       // SmoothDamp 速度缓存
    private Vector3 _currentHoverOffset;   // 当前应用的浮动偏移

    public bool IsDragging => _isDragging;
    public bool IsIntroActive => _introActive;

    private void Awake()
    {
        _rb = GetComponent<Rigidbody>();
        if (_rb == null) _rb = gameObject.AddComponent<Rigidbody>();
        _col = GetComponent<Collider>();
        _rb.isKinematic = true;
        _rb.useGravity = false;
        _rb.interpolation = RigidbodyInterpolation.Interpolate;
    }

    /// <summary>由 ShopController 在实例化后调用。
    /// priceTagParent：价签的组织父节点（不影响世界位置）——ShopController.transform 即可。
    /// priceTagOffset：价签相对零件 pivot 的世界偏移（X 左右 / Y 上下 / Z 桌面纵深）。
    /// onReject：鼠标按下但买不起时触发（用于播放场景级 MMFeedback：拒绝音效 / 镜头抖等）。</summary>
    public void Init(PenPartData data, Vector3 homePos, Quaternion homeRot,
                     ShopDrawer drawer,
                     Transform priceTagParent,
                     Vector3 priceTagOffset,
                     System.Action<ShopPart> onPurchased,
                     System.Action<ShopPart> onLanded = null,
                     System.Func<PenPartData, bool> affordCheck = null,
                     System.Action<ShopPart> onReject = null)
    {
        PartData = data;
        _homePosition = homePos;
        _homeRotation = homeRot;
        _drawer = drawer;
        _onPurchased = onPurchased;
        _onLanded = onLanded;
        _onReject = onReject;
        _affordCheck = affordCheck;
        _cam = Camera.main;

        CachePartRenderers();
        if (priceTagParent != null && data != null)
            _priceTag = ShopPriceTag.Create(priceTagParent, transform, priceTagOffset, data.BuyPrice, _cam);
        RefreshAffordVisual();
    }

    private void OnDestroy()
    {
        // 若未经过购买路径销毁（如 Shop 退出时清场），兜底消掉价签
        if (_priceTag != null) Destroy(_priceTag.gameObject);
    }

    // ─── 本体染色（价签由 ShopPriceTag 自管） ─────────────────────────────────

    private void CachePartRenderers()
    {
        _tintedRenderers.Clear();
        foreach (var r in GetComponentsInChildren<Renderer>(true))
            _tintedRenderers.Add(r);
        if (_mpb == null) _mpb = new MaterialPropertyBlock();
    }

    /// <summary>由 ShopController 在 Wallet 变化时调用，刷新价签颜色 + 本体染色。</summary>
    public void RefreshAffordVisual()
    {
        if (PartData == null) return;
        bool canAfford = _affordCheck == null || _affordCheck(PartData);
        _currentAffordable = canAfford;

        if (_priceTag != null) _priceTag.SetAffordable(canAfford);

        // 本体染色：MaterialPropertyBlock 设 _BaseColor（URP/Built-in 都能识别）
        if (_mpb == null) _mpb = new MaterialPropertyBlock();
        Color tint = canAfford ? Color.white : _denyTint;
        foreach (var r in _tintedRenderers)
        {
            if (r == null) continue;
            r.GetPropertyBlock(_mpb);
            _mpb.SetColor(BaseColorID, tint);
            _mpb.SetColor(ColorID, tint);
            r.SetPropertyBlock(_mpb);
        }
    }

    private static readonly int BaseColorID = Shader.PropertyToID("_BaseColor");
    private static readonly int ColorID = Shader.PropertyToID("_Color");

    // ─── 入场动画 / Hover 反馈 ────────────────────────────────────────────────

    /// <summary>货架弹入动画：localScale 从 0 → 1 带 EaseOutBack 回冲，旋转从随机偏角归位。</summary>
    public void PlayIntroPopIn(float delay)
    {
        transform.localScale = Vector3.zero;
        _introActive = true;
        if (_introCo != null) StopCoroutine(_introCo);
        _introCo = StartCoroutine(IntroRoutine(delay));
    }

    private IEnumerator IntroRoutine(float delay)
    {
        if (delay > 0f) yield return new WaitForSeconds(delay);

        const float duration = 0.4f;
        Quaternion fromRot = _homeRotation * Quaternion.Euler(0f, Random.Range(-25f, 25f), 0f);
        float t = 0f;
        while (t < 1f)
        {
            t += Time.deltaTime / duration;
            float s = EaseOutBack(Mathf.Clamp01(t), 0.35f);
            transform.localScale = Vector3.one * Mathf.Max(0f, s);
            transform.rotation = Quaternion.SlerpUnclamped(fromRot, _homeRotation, s);
            yield return null;
        }
        transform.localScale = Vector3.one;
        transform.rotation = _homeRotation;
        _introActive = false;
        _introCo = null;
    }

    /// <summary>由 ShopController 每帧按射线结果调用（命中 = true，否则 = false）。</summary>
    public void SetHover(bool hovered)
    {
        if (_isHovered == hovered) return;
        _isHovered = hovered;
    }

    public void BeginDrag()
    {
        if (_isDragging || _introActive) return;
        if (_cam == null) _cam = Camera.main;
        if (_cam == null) return;

        // 预购拒绝：钱不够 → 不进入拖拽，原地 shake + 价签红脉冲 + 通知控制器播全局 MMFeedback
        if (_affordCheck != null && PartData != null && !_affordCheck(PartData))
        {
            if (_rejectCo != null) StopCoroutine(_rejectCo);
            _rejectCo = StartCoroutine(RejectInPlaceShake());
            _priceTag?.PlayRejectFlash();
            _onReject?.Invoke(this);
            return;
        }

        if (_returnCo != null) { StopCoroutine(_returnCo); _returnCo = null; }

        _dragInput.NotifyBegin();
        _isDragging = true;

        // 价签在 Create 时就钉死在 slot 世界位置，零件离场期间价签不动——此处无需额外操作

        // 用 DragPlane 工具统一锚点（点击网格点 + 抬升），消除斜视相机视差
        var mouse = Mouse.current;
        var cursorRay = mouse != null ? ScreenHelper.ScreenPointToRay(_cam, mouse.position.ReadValue()) : default;
        DragPlane.TryBuild(_col, transform.position, cursorRay, DragLiftHeight,
            out _dragPlane, out _dragOffset, out var anchor);
        _dragTarget = anchor;

        // Kinematic 拖拽：MovePosition 对 Kinematic RB 确定性，避免阻尼+Dynamic 导致的抖动
        _rb.isKinematic = true;
        _rb.useGravity = false;
        _rb.linearDamping = 0f;
        _rb.angularDamping = 0f;
        _rb.constraints = RigidbodyConstraints.FreezeRotation;
    }

    private void Update()
    {
        if (!_isDragging)
        {
            if (!_purchased && !_introActive && _returnCo == null && _rejectCo == null)
            {
                if (_displayRotationSpeed != 0f)
                    transform.Rotate(Vector3.up, _displayRotationSpeed * Time.deltaTime, Space.World);

                // Hover 浮动：鼠标悬停时在 home 位置上叠加一个柔和 Y bob + 细微 X/Z 漂移；
                //   取消悬停时偏移 SmoothDamp 回零。kinematic 下直接写 transform.position 不冲突。
                Vector3 targetOff;
                if (_isHovered)
                {
                    _hoverPhase += Time.deltaTime * 4f; // ~0.64Hz
                    float bob = Mathf.Sin(_hoverPhase) * 0.025f + 0.03f; // 悬停时整体抬一点（0.03 基底）+ 呼吸
                    targetOff = new Vector3(0f, bob, 0f);
                }
                else
                {
                    targetOff = Vector3.zero;
                }
                _currentHoverOffset = Vector3.SmoothDamp(
                    _currentHoverOffset, targetOff, ref _hoverOffsetVel, 0.12f);
                transform.position = _homePosition + _currentHoverOffset;
            }
            return;
        }
        var mouse = Mouse.current;
        if (mouse == null || _cam == null) return;

        // 拖拽期间（无论按住还是点击切换）都让目标跟随鼠标
        var ray = ScreenHelper.ScreenPointToRay(_cam, mouse.position.ReadValue());
        if (_dragPlane.Raycast(ray, out float enter))
        {
            Vector3 target = ray.GetPoint(enter) + _dragOffset;
            if (HoverAmplitude > 0f)
                target.y += Mathf.Sin(Time.time * 2f * Mathf.PI * HoverFrequency) * HoverAmplitude;
            _dragTarget = target;
        }

        if (_drawer != null) _drawer.TryAutoOpenClose(transform.position);

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
        {
            _rb.MovePosition(_dragTarget);
            return;
        }

        // 磁吸兜底：仅当零件 XZ 飞出抽屉底投影范围时，给一点指向 DropAnchor 的水平力把它"拉回来"；
        // 正常落在安全区内则纯重力自由落体，不干预手感。
        if (_purchased && !_landingPlayed && _drawer != null && _drawer.DropAnchor != null && !_rb.isKinematic)
        {
            Vector3 pos = _rb.position;
            if (IsOutsideSafeZone(pos, _drawer.InteriorFloor))
            {
                Vector3 anchor = _drawer.DropAnchor.position;
                Vector3 dirXZ = new Vector3(anchor.x - pos.x, 0f, anchor.z - pos.z);
                if (dirXZ.sqrMagnitude > 0.0001f)
                {
                    dirXZ.Normalize();
                    Vector3 vel = _rb.linearVelocity;
                    Vector2 velXZ = new Vector2(vel.x, vel.z);

                    // 卡在安全区外静止 → 磁吸放大，克服摩擦强行推入
                    float strength = velXZ.magnitude < StuckSpeedThreshold
                        ? MagnetStrength * StuckForceMultiplier
                        : MagnetStrength;

                    _rb.AddForce(dirXZ * strength, ForceMode.Acceleration);

                    if (velXZ.magnitude > MagnetMaxSpeed)
                    {
                        velXZ = velXZ.normalized * MagnetMaxSpeed;
                        _rb.linearVelocity = new Vector3(velXZ.x, vel.y, velXZ.y);
                    }
                }
            }
        }
    }

    private static bool IsOutsideSafeZone(Vector3 worldPos, Collider floor)
    {
        // InteriorFloor 未配置则视为"没有安全区"，磁吸不介入
        if (floor == null) return false;
        var b = floor.bounds;
        return worldPos.x < b.min.x || worldPos.x > b.max.x
            || worldPos.z < b.min.z || worldPos.z > b.max.z;
    }

    private void HandleMouseUp()
    {
        _isDragging = false;
        _dragInput.ForceEnd();

        if (_drawer != null && _drawer.IsInDropZone(transform.position))
        {
            // 钱包不够 → 拒绝购买，走"摇头 + 回弹"拒绝反馈
            if (_affordCheck != null && !_affordCheck(PartData))
            {
                _rb.isKinematic = true;
                if (_returnCo != null) StopCoroutine(_returnCo);
                _returnCo = StartCoroutine(ReturnWithShake());
                if (_drawer != null) _drawer.Close();
                return;
            }

            _purchased = true;
            _onPurchased?.Invoke(this);
            transform.position += Vector3.up * PurchaseLift;
            EnablePhysicsFall();
            // 购买成交——价签在货架位淡出
            if (_priceTag != null) _priceTag.FadeOutAndDestroy();
            StartCoroutine(LandingTimeoutGuard());
            return;
        }

        // 反悔：保持 Kinematic 弹回（BeginDrag 已切 Kinematic）
        _rb.isKinematic = true;
        if (_returnCo != null) StopCoroutine(_returnCo);
        _returnCo = StartCoroutine(ReturnHome());
        if (_drawer != null) _drawer.Close();
    }

    /// <summary>预购拒绝：鼠标按下时资金不够——原地抖 0.28s，不离开 home 位</summary>
    private IEnumerator RejectInPlaceShake()
    {
        const float duration = 0.28f;
        const float amplitude = 0.022f;
        float t = 0f;
        while (t < duration)
        {
            t += Time.deltaTime;
            float decay = 1f - t / duration;
            float sx = (Random.value * 2f - 1f) * amplitude * decay;
            float sz = (Random.value * 2f - 1f) * amplitude * decay;
            transform.position = _homePosition + new Vector3(sx, 0f, sz);
            yield return null;
        }
        transform.position = _homePosition;
        _rejectCo = null;
    }

    /// <summary>（旧版兜底，保留）钱不够但已拖到抽屉才检出时：原地红色 shake 半秒 → 回弹到 home 位</summary>
    private IEnumerator ReturnWithShake()
    {
        const float shakeTime = 0.35f;
        const float shakeAmplitude = 0.04f;
        Vector3 basePos = transform.position;
        float t = 0f;
        while (t < shakeTime)
        {
            t += Time.deltaTime;
            float decay = 1f - t / shakeTime;
            float sx = (Random.value * 2f - 1f) * shakeAmplitude * decay;
            float sz = (Random.value * 2f - 1f) * shakeAmplitude * decay;
            transform.position = basePos + new Vector3(sx, 0f, sz);
            yield return null;
        }
        transform.position = basePos;

        // shake 完毕后正常回弹
        yield return ReturnHome();
    }

    /// <summary>右键取消：强制走反悔路径，EaseBack 回 home 位</summary>
    private void CancelDrag()
    {
        _isDragging = false;
        _dragInput.ForceEnd();
        _rb.isKinematic = true;
        if (_returnCo != null) StopCoroutine(_returnCo);
        _returnCo = StartCoroutine(ReturnHome());
        if (_drawer != null) _drawer.Close();
    }

    /// <summary>反悔回弹：EaseOutBack 曲线</summary>
    private IEnumerator ReturnHome()
    {
        Vector3 from = transform.position;
        Quaternion fromR = transform.rotation;
        float t = 0f;
        while (t < 1f)
        {
            t += Time.deltaTime / Mathf.Max(0.01f, ReturnDuration);
            float s = EaseOutBack(Mathf.Clamp01(t), ReturnOvershoot);
            transform.position = Vector3.LerpUnclamped(from, _homePosition, s);
            transform.rotation = Quaternion.SlerpUnclamped(fromR, _homeRotation, s);
            yield return null;
        }
        transform.position = _homePosition;
        transform.rotation = _homeRotation;
        _returnCo = null;
    }

    private void OnCollisionEnter(Collision collision)
    {
        // 仅购买后第一次有效碰撞触发落地反馈（反悔/拖拽阶段的碰撞忽略）
        if (!_purchased || _landingPlayed) return;
        if (collision.relativeVelocity.magnitude < LandingMinVelocity) return;
        // 必须撞到抽屉内部 layer 才算真·落地（防桌面误判）
        if (_drawer != null)
        {
            int hitLayerMask = 1 << collision.gameObject.layer;
            if ((hitLayerMask & _drawer.InteriorLayers.value) == 0) return;
        }
        HandleLanded(playFeedback: true);
    }

    /// <summary>统一的落地处理：清速度、kinematic、parent 到抽屉、回调、启动销毁。</summary>
    private void HandleLanded(bool playFeedback)
    {
        _landingPlayed = true;
        if (playFeedback && LandingFeedback != null) LandingFeedback.PlayFeedbacks();

        _rb.linearVelocity = Vector3.zero;
        _rb.angularVelocity = Vector3.zero;
        _rb.isKinematic = true;
        if (_drawer != null) transform.SetParent(_drawer.transform, worldPositionStays: true);

        _onLanded?.Invoke(this);
        StartCoroutine(DestroyAfterSettle());
    }

    private IEnumerator DestroyAfterSettle()
    {
        yield return new WaitForSeconds(FallSettleDuration);
        Destroy(gameObject);
    }

    /// <summary>落地超时兜底：只在"已进入安全区但没触发碰撞落地"时强制落地；
    /// 如果还卡在安全区外，说明 stuck boost 还在推，不强制，继续等下一轮。
    /// 总次数封顶，避免极端情况下永远不关抽屉。</summary>
    private IEnumerator LandingTimeoutGuard()
    {
        const int MaxAttempts = 4;
        int attempts = 0;
        while (!_landingPlayed && attempts < MaxAttempts)
        {
            yield return new WaitForSeconds(Mathf.Max(0.1f, LandingTimeout));
            if (_landingPlayed) yield break;
            attempts++;

            bool hasSafeZone = _drawer != null && _drawer.InteriorFloor != null;
            bool insideSafeZone = hasSafeZone && !IsOutsideSafeZone(_rb.position, _drawer.InteriorFloor);

            // 已在安全区（大概率在抽屉底上缓慢静置，OnCollisionEnter 因速度阈值未触发）→ 强制落地
            if (insideSafeZone || !hasSafeZone)
            {
                HandleLanded(playFeedback: false);
                yield break;
            }
            // 否则继续等 stuck boost 把零件推进抽屉
        }
        // 兜底的兜底：最大次数仍卡在外面，强制走流程避免阶段卡死
        if (!_landingPlayed) HandleLanded(playFeedback: false);
    }

    private void EnablePhysicsFall()
    {
        _rb.isKinematic = false;
        _rb.useGravity = true;
        _rb.linearDamping = 0.1f;
        _rb.angularDamping = 0.1f;
        _rb.constraints = RigidbodyConstraints.None;
        _rb.linearVelocity = Vector3.zero;
        _rb.angularVelocity = Vector3.zero;
    }

    private static float EaseOutBack(float x, float overshoot)
    {
        float c1 = 1.70158f * (overshoot / 0.25f);
        float c3 = c1 + 1f;
        float u = x - 1f;
        return 1f + c3 * u * u * u + c1 * u * u;
    }
}
