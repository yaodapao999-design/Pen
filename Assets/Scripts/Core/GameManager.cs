using System.Collections;
using UnityEngine;

/// <summary>
/// 游戏阶段：战斗 / 商店 / 改装。
/// </summary>
public enum GamePhase { Battle, Shop, Workshop }

/// <summary>
/// 外层阶段状态机（单例）。
///
/// 架构：
///   - ChangePhase(next) 是唯一切换入口
///   - 协程顺序：Exit 旧阶段 → 切 CurrentPhase → Enter 新阶段
///   - _transitioning 锁防抖：过渡中再点按钮直接忽略
///
/// 各阶段实现为 IGamePhase：
///   BattlePhase / ShopPhase / WorkshopPhase
///
/// 内层状态（战斗 Idle/Action/Result 等）由各 Controller 自管，外层不掺和。
/// </summary>
public class GameManager : MonoBehaviour
{
    public static GameManager Instance { get; private set; }

    [Header("玩家数据（ScriptableObject 资产）")]
    public PlayerInventory Inventory;
    public PlayerWallet Wallet;
    [Tooltip("新存档的起始配置（笔杆 + 已装配零件 + 散落仓库）。未配置 = 保持场景里现有状态不变")]
    public PlayerLoadoutSO Loadout;

    [Header("场景引用")]
    public BattleStateMachine BattleSM;
    public WorkshopController WorkshopCtrl;
    public WorkshopPenSpawner WorkshopSpawner;
    public ShopController ShopCtrl;

    [Header("切换冷却")]
    [Tooltip("每次阶段切换完成后额外锁定的时长（秒），防止暴力快点 / 加载未完成就再切")]
    [SerializeField] private float _postTransitionCooldown = 2f;

    public GamePhase CurrentPhase { get; private set; } = GamePhase.Battle;
    public bool IsTransitioning { get; private set; }

    private IGamePhase _battle, _shop, _workshop;

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;

        _battle = new BattlePhase(BattleSM);
        _shop = new ShopPhase(ShopCtrl);
        _workshop = new WorkshopPhase(WorkshopCtrl, InjectInventoryToSpawner);

        // 初始化所有 Phase 镜头优先级：默认 Battle 阶段
        if (_shop.Camera != null) _shop.Camera.Priority = INACTIVE_CAM_PRIORITY;
        if (_workshop.Camera != null) _workshop.Camera.Priority = INACTIVE_CAM_PRIORITY;
        if (_battle.Camera != null) _battle.Camera.Priority = ACTIVE_CAM_PRIORITY;
    }

    private void Start()
    {
        // 所有 Awake 已完成（PenAssembly._rb、BattleSM.pen 都就绪），可安全写入 Loadout
        ApplyLoadoutOnNewSave();
    }

    /// <summary>
    /// 把 Loadout 的起始配置写入玩家数据层 + 构建笔的战斗视图。
    /// 当前每次启动都视为"新存档"。接入存档系统后，在开头加一句
    ///     if (SaveService.HasSave()) return;
    /// 即可让加载存档的路径跳过这里。
    /// </summary>
    private void ApplyLoadoutOnNewSave()
    {
        if (Loadout == null)
        {
            Debug.LogWarning("[GameManager] Loadout 未配置，玩家笔将没有初始装备，仓库将为空。请在 Inspector 拖入 PlayerLoadout 资产。");
            return;
        }

        // 1) 仓库：清空后写入起始散落零件（PlayerInventory 的 session-snapshot 机制
        //    保证 OnDisable 会把磁盘还原，不污染 asset）
        if (Inventory != null)
        {
            Inventory.Clear();
            foreach (var p in Loadout.InitialInventory)
                if (p != null) Inventory.Add(p);
        }

        // 2) 玩家笔：写数据层 + 构建战斗视图（Start 时 PenAssembly._rb 已就绪）
        var assembly = BattleSM != null && BattleSM.Pen != null ? BattleSM.Pen.Assembly : null;
        if (assembly != null && Loadout.InitialBarrel != null)
        {
            var equipped = new PenPartData[Loadout.InitialEquipped.Count];
            for (int i = 0; i < equipped.Length; i++) equipped[i] = Loadout.InitialEquipped[i];
            assembly.InitData(Loadout.InitialBarrel, equipped);
            assembly.BuildBattleView();
        }
    }

    // ─── 对外入口 ─────────────────────────────────────────────────────────────

    /// <summary>
    /// 切换到目标阶段（唯一入口）。
    /// 返回：true=已接受并开始过渡；false=被拒（当前阶段 CanExit 返回 false / 过渡中 / 同阶段）。
    /// 调用方（如按钮）可依返回值播放本地拒绝反馈。
    /// </summary>
    public bool ChangePhase(GamePhase next)
    {
        if (IsTransitioning) return false;
        if (next == CurrentPhase) return false;
        var current = GetPhase(CurrentPhase);
        if (!current.CanExit())
        {
            current.OnExitRejected();
            return false;
        }
        StartCoroutine(ChangePhaseRoutine(next));
        return true;
    }

    /// <summary>按钮快捷：在 Shop 则退出（回 Battle），否则进 Shop</summary>
    public bool ToggleShop()
    {
        return ChangePhase(CurrentPhase == GamePhase.Shop ? GamePhase.Battle : GamePhase.Shop);
    }

    /// <summary>按钮快捷：在 Workshop 则退出（回 Battle），否则进 Workshop</summary>
    public bool ToggleWorkshop()
    {
        return ChangePhase(CurrentPhase == GamePhase.Workshop ? GamePhase.Battle : GamePhase.Workshop);
    }

    // ─── 购买入口（由 ShopPart 回调） ──────────────────────────────────────────

    public void OnPartPurchased(PenPartData part)
    {
        if (Inventory == null || part == null) return;
        // 货币本阶段仅占位：若将来启用，先 Wallet.TrySpend(part.BuyPrice) 再 Add
        Inventory.Add(part);
    }

    // ─── 内部 ─────────────────────────────────────────────────────────────────

    private const int ACTIVE_CAM_PRIORITY = 20;
    private const int INACTIVE_CAM_PRIORITY = 0;

    private IEnumerator ChangePhaseRoutine(GamePhase next)
    {
        // CanExit 校验已在 ChangePhase 入口处理，此处假定可退出
        var current = GetPhase(CurrentPhase);
        var incoming = GetPhase(next);

        IsTransitioning = true;

        // 镜头：切换开始瞬间同时抬新压旧，让 Cinemachine 在 Exit 动画期间直接 blend，
        // 不会经过"默认镜头"这个中间态
        if (incoming.Camera != null) incoming.Camera.Priority = ACTIVE_CAM_PRIORITY;
        if (current.Camera != null) current.Camera.Priority = INACTIVE_CAM_PRIORITY;

        yield return current.Exit();
        CurrentPhase = next;
        yield return incoming.Enter();

        if (_postTransitionCooldown > 0f)
            yield return new WaitForSeconds(_postTransitionCooldown);

        IsTransitioning = false;
    }

    private IGamePhase GetPhase(GamePhase p) => p switch
    {
        GamePhase.Battle => _battle,
        GamePhase.Shop => _shop,
        GamePhase.Workshop => _workshop,
        _ => _battle
    };

    private void InjectInventoryToSpawner()
    {
        if (WorkshopSpawner == null || Inventory == null) return;
        WorkshopSpawner.SetAvailableParts(Inventory.GetAll());
    }
}
