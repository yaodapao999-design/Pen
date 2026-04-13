using System.Collections;
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

    private Vector3 _homePosition;
    private Quaternion _homeRotation;

    private bool _isDragging;
    private bool _purchased;
    private bool _landingPlayed;
    private Plane _dragPlane;
    private Vector3 _dragOffset;
    private Vector3 _dragTarget;
    private Coroutine _returnCo;

    public bool IsDragging => _isDragging;

    private void Awake()
    {
        _rb = GetComponent<Rigidbody>();
        if (_rb == null) _rb = gameObject.AddComponent<Rigidbody>();
        _rb.isKinematic = true;
        _rb.useGravity = false;
        _rb.interpolation = RigidbodyInterpolation.Interpolate;
    }

    /// <summary>由 ShopController 在实例化后调用</summary>
    public void Init(PenPartData data, Vector3 homePos, Quaternion homeRot,
                     ShopDrawer drawer,
                     System.Action<ShopPart> onPurchased,
                     System.Action<ShopPart> onLanded = null)
    {
        PartData = data;
        _homePosition = homePos;
        _homeRotation = homeRot;
        _drawer = drawer;
        _onPurchased = onPurchased;
        _onLanded = onLanded;
        _cam = Camera.main;
    }

    public void BeginDrag()
    {
        if (_isDragging) return;
        if (_cam == null) _cam = Camera.main;
        if (_cam == null) return;

        if (_returnCo != null) { StopCoroutine(_returnCo); _returnCo = null; }

        _isDragging = true;
        _dragPlane = new Plane(Vector3.up, transform.position);

        var mouse = Mouse.current;
        if (mouse != null)
        {
            var ray = _cam.ScreenPointToRay(mouse.position.ReadValue());
            _dragOffset = _dragPlane.Raycast(ray, out float enter)
                ? transform.position - ray.GetPoint(enter)
                : Vector3.zero;
        }
        _dragTarget = transform.position;

        // 切换到拖拽物理模式：非运动学、无重力、高阻尼
        _rb.isKinematic = false;
        _rb.useGravity = false;
        _rb.linearDamping = DragDamping;
        _rb.angularDamping = DragDamping;
        _rb.constraints = RigidbodyConstraints.FreezeRotation;
        _rb.linearVelocity = Vector3.zero;
        _rb.angularVelocity = Vector3.zero;
    }

    private void Update()
    {
        if (!_isDragging)
        {
            if (!_purchased && _returnCo == null && _displayRotationSpeed != 0f)
                transform.Rotate(Vector3.up, _displayRotationSpeed * Time.deltaTime, Space.World);
            return;
        }
        var mouse = Mouse.current;
        if (mouse == null || _cam == null) return;

        if (mouse.leftButton.isPressed)
        {
            var ray = _cam.ScreenPointToRay(mouse.position.ReadValue());
            if (_dragPlane.Raycast(ray, out float enter))
            {
                Vector3 target = ray.GetPoint(enter) + _dragOffset;
                float hover = HoverAmplitude > 0f
                    ? Mathf.Sin(Time.time * 2f * Mathf.PI * HoverFrequency) * HoverAmplitude
                    : 0f;
                target.y += DragLiftHeight + hover;
                _dragTarget = target;
            }

            if (_drawer != null) _drawer.TryAutoOpenClose(transform.position);
        }

        if (mouse.leftButton.wasReleasedThisFrame)
            HandleMouseUp();
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

        if (_drawer != null && _drawer.IsInDropZone(transform.position))
        {
            _purchased = true;
            _onPurchased?.Invoke(this);
            // 轻抬一下给磁吸留下落时间，然后直接切物理自由落体；XZ 方向由 FixedUpdate 磁吸拉向 DropAnchor
            transform.position += Vector3.up * PurchaseLift;
            EnablePhysicsFall();
            StartCoroutine(LandingTimeoutGuard());
            // 销毁在真正落地后才启动（见 OnCollisionEnter / LandingTimeoutGuard 尾部）
            return;
        }

        // 反悔：切回运动学 + EaseOutBack 弹回
        _rb.linearVelocity = Vector3.zero;
        _rb.angularVelocity = Vector3.zero;
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
