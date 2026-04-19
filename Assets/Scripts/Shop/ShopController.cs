using System.Collections.Generic;
using Unity.Cinemachine;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// 商店场景控制器。
///
/// 职责：
///   1. 进入商店时：从 ShopPool 按权重随机抽 SlotCount 个 PenPartData，实例化到 DisplaySlots
///   2. 集中输入分发：每帧 Raycast 检测鼠标按下对 ShopPart 的选择，触发 BeginDrag
///   3. 购买回调：把 PenPartData 加入 PlayerInventory（经 GameManager），销毁展示物
///   4. 退出商店：清理剩余展示物
///
/// 与 Workshop 解耦：不依赖 WorkshopPart / WorkshopController。
/// </summary>
public class ShopController : MonoBehaviour
{
    [Header("商品配置")]
    public ShopPool Pool;
    [Tooltip("一次展示多少件商品（若 Pool 不足则取全部）")]
    public int SlotCount = 3;
    public Transform[] DisplaySlots;

    [Header("抽屉")]
    public ShopDrawer Drawer;

    [Header("根节点（用于整组显隐）")]
    public GameObject ShopRoot;
    [Tooltip("笔记本滑入/滑出动画；留空则走瞬切")]
    public ShopBookAnimator BookAnimator;

    [Header("镜头（由 GameManager 调度，本地不改 Priority）")]
    public CinemachineCamera ShopVCam;

    [Header("价格标签")]
    [Tooltip("价签沿世界 Z 轴相对零件 pivot 的偏移（米）。正值 = 远离相机；负值 = 靠近相机。")]
    [SerializeField] private float _priceTagZOffset = 0.15f;

    [Header("入场动画")]
    [Tooltip("每件商品入场弹出的时间差（秒）。0 = 所有同时；0.08 = 依次错开")]
    [SerializeField] private float _introStaggerDelay = 0.08f;

    [Header("购买反馈（Step C）")]
    [Tooltip("购买时从购买位置喷出的金币数量")]
    [SerializeField] private int _coinFlyCount = 5;
    [Tooltip("金币 UI sprite（用 Unity 内置 Knob 即可，金色染色）")]
    [SerializeField] private Sprite _coinSprite;
    [Tooltip("金币飞入的目标 RectTransform（通常是 CurrencyHUD/CoinIcon 或 CoinText）")]
    [SerializeField] private RectTransform _coinFlightTarget;
    [Tooltip("金币 UI 所在的 Canvas（通常是场景主 Canvas）")]
    [SerializeField] private Canvas _uiCanvas;

    [Header("预购拒绝反馈")]
    [Tooltip("鼠标按下买不起物品时播放——推荐接音效 + 价签/镜头抖。留空不播。")]
    [SerializeField] private MoreMountains.Feedbacks.MMF_Player _rejectFeedback;

    private readonly List<ShopPart> _activeParts = new List<ShopPart>();
    private bool _active;
    private PlayerWallet _subscribedWallet;

    private void Awake()
    {
        if (ShopRoot != null) ShopRoot.SetActive(false);
        if (BookAnimator != null) BookAnimator.SnapToExited();
        // 镜头优先级由 GameManager 统一调度，这里不碰
    }

    private void OnEnable()
    {
        // 延迟到 GameManager.Instance 就绪再订阅；每帧检测直到成功或被 OnDisable 撤销
        StartCoroutine(SubscribeWalletWhenReady());
    }

    private void OnDisable()
    {
        if (_subscribedWallet != null)
        {
            _subscribedWallet.OnChanged -= RefreshAllAffordVisuals;
            _subscribedWallet = null;
        }
    }

    private System.Collections.IEnumerator SubscribeWalletWhenReady()
    {
        while (GameManager.Instance == null || GameManager.Instance.Wallet == null)
            yield return null;
        _subscribedWallet = GameManager.Instance.Wallet;
        _subscribedWallet.OnChanged += RefreshAllAffordVisuals;
    }

    private void RefreshAllAffordVisuals()
    {
        foreach (var sp in _activeParts)
            if (sp != null) sp.RefreshAffordVisual();
    }

    // ─── 入口 ─────────────────────────────────────────────────────────────────

    /// <summary>协程版 Enter：激活根 → 笔记本滑入 → 生成商品。
    /// Player / Enemy 笔的 SetActive(false) 由 BattlePhase.Exit 启动的统一倒计时负责（两把笔同步），
    /// 本协程不再手动关笔，避免时机和 Workshop 不一致。</summary>
    public System.Collections.IEnumerator EnterShopRoutine()
    {
        if (_active) yield break;
        _active = true;
        if (ShopRoot != null) ShopRoot.SetActive(true);
        if (Drawer != null) Drawer.SnapClosed();
        if (BookAnimator != null)
        {
            BookAnimator.SnapToExited();
            yield return BookAnimator.PlayEnter();
        }
        PopulateShop();
    }

    /// <summary>协程版 Exit：清商品 → 播笔记本滑出动画 → 关根。</summary>
    public System.Collections.IEnumerator ExitShopRoutine()
    {
        if (!_active) yield break;
        _active = false;
        PartInfoPanel.Instance?.Hide();
        ClearActiveParts();
        if (Drawer != null) Drawer.Close();
        if (BookAnimator != null)
        {
            yield return BookAnimator.PlayExit();
        }
        if (ShopRoot != null) ShopRoot.SetActive(false);
    }

    // ─── 生成商品 ─────────────────────────────────────────────────────────────

    private void PopulateShop()
    {
        ClearActiveParts();

        if (Pool == null || DisplaySlots == null || DisplaySlots.Length == 0) return;

        var picks = Pool.RollRandom(Mathf.Min(SlotCount, DisplaySlots.Length));
        for (int i = 0; i < picks.Count && i < DisplaySlots.Length; i++)
        {
            var data = picks[i];
            var slot = DisplaySlots[i];
            if (data == null || data.VisualPrefab == null || slot == null) continue;

            var go = Instantiate(data.VisualPrefab, slot.position, slot.rotation, slot);

            // 确保有 Collider 以供射线检测（VisualPrefab 自带）
            if (go.GetComponentInChildren<Collider>() == null)
            {
                Debug.LogWarning($"[Shop] {data.DisplayName} 的 VisualPrefab 缺少 Collider，无法拖拽");
                Destroy(go);
                continue;
            }

            // ShopPart 挂在实例化根物体上，移动时整个模型一起走；
            // Collider 可以在子物体，Raycast 命中后 GetComponentInParent<ShopPart>() 能找到
            var sp = go.AddComponent<ShopPart>();
            sp.Init(data, slot.position, slot.rotation, Drawer,
                    priceTagParent: transform,
                    priceTagOffset: new Vector3(0f, 0.15f, _priceTagZOffset),
                    OnPartPurchased, OnPartLanded,
                    affordCheck: GameManager.Instance != null ? (System.Func<PenPartData, bool>)GameManager.Instance.CanAfford : null,
                    onReject: OnPartRejected);
            sp.PlayIntroPopIn(delay: _activeParts.Count * _introStaggerDelay);
            _activeParts.Add(sp);
        }
    }

    private void ClearActiveParts()
    {
        foreach (var sp in _activeParts)
            if (sp != null) Destroy(sp.gameObject);
        _activeParts.Clear();
    }

    // ─── 输入 ─────────────────────────────────────────────────────────────────

    private void Update()
    {
        if (!_active) return;

        var mouse = Mouse.current;
        var cam = Camera.main;
        if (mouse == null || cam == null) return;

        // 若已有拖拽中的 ShopPart，hover 面板隐藏，其他逻辑也跳过
        foreach (var sp in _activeParts)
        {
            if (sp != null && sp.IsDragging)
            {
                PartInfoPanel.Instance?.Hide();
                return;
            }
        }

        // 每帧 raycast 最近的 ShopPart；用于 hover 信息面板 + 按下时开始拖拽
        var ray = ScreenHelper.ScreenPointToRay(cam, mouse.position.ReadValue());
        ShopPart best = null;
        float bestDist = float.MaxValue;
        foreach (var hit in Physics.RaycastAll(ray))
        {
            var sp = hit.collider.GetComponentInParent<ShopPart>();
            if (sp == null) continue;
            if (!_activeParts.Contains(sp)) continue;
            if (hit.distance < bestDist) { bestDist = hit.distance; best = sp; }
        }

        // hover 信息面板（Shop 场景显示价格）
        if (best != null && best.PartData != null)
            PartInfoPanel.Instance?.Show(best.PartData, showPrice: true);
        else
            PartInfoPanel.Instance?.Hide();

        // 通知每个 ShopPart 自己当前是否被 hover（驱动 idle 浮动反馈）
        foreach (var sp in _activeParts)
            if (sp != null) sp.SetHover(sp == best);

        // 按下开始拖拽
        if (mouse.leftButton.wasPressedThisFrame && best != null)
            best.BeginDrag();
    }

    // ─── 购买回调 ─────────────────────────────────────────────────────────────

    private void OnPartPurchased(ShopPart sp)
    {
        if (sp == null || sp.PartData == null) return;

        // 先从 active 列表摘除：后续 Wallet.OnChanged 刷新全体负担视觉时跳过它，
        // 避免"刚买的零件掉落途中被判定买不起而染红"的穿帮
        _activeParts.Remove(sp);

        // 购买反馈：从零件当前位置喷出金币飞入 HUD + 浮动 "-$X" 红字
        Vector3 purchasePos = sp.transform.position;
        int price = sp.PartData.BuyPrice;
        var cam = Camera.main;
        if (price > 0 && cam != null)
        {
            ShopFloatingText.Spawn(
                parent: transform,
                worldPos: purchasePos + Vector3.up * 0.1f,
                text: $"-${price}",
                color: new Color(1f, 0.35f, 0.35f),
                cam: cam);

            // 花钱语义：金币从钱包（HUD）飞出 → 沉入商品位置
            if (_uiCanvas != null && _coinFlightTarget != null)
            {
                Vector2 startScreen = _coinFlightTarget.position; // Overlay Canvas 下 rect.position = 屏幕像素
                Vector2 endScreen   = cam.WorldToScreenPoint(purchasePos);
                ShopCoinFlight.Spawn(_coinFlyCount, startScreen, endScreen, _uiCanvas, _coinSprite);
            }
        }

        if (GameManager.Instance != null)
            GameManager.Instance.OnPartPurchased(sp.PartData);
        else
            Debug.LogWarning("[Shop] GameManager 未初始化，购买未持久化");

        // 不立即销毁 sp：让物理下落 + 静置后 ShopPart 自己销毁
        // 抽屉关闭时机由 OnPartLanded 决定（等零件真正落到抽屉里再关，避免关门穿模）
    }

    private void OnPartLanded(ShopPart sp)
    {
        if (Drawer != null) Drawer.Close();
    }

    /// <summary>由 ShopPart 在鼠标按下但钱不够时回调。ShopPart 已自管零件 shake 和价签红脉冲，
    /// 此处只负责场景级反馈（拒绝音效 / 镜头抖 / 红屏闪，由 MMF_Player 配置）。</summary>
    private void OnPartRejected(ShopPart _)
    {
        if (_rejectFeedback != null) _rejectFeedback.PlayFeedbacks();
    }
}
