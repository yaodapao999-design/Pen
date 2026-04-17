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

    [Header("场景引用")]
    public BattleStateMachine BattleSM;
    public WorkshopController WorkshopCtrl;
    public WorkshopPenSpawner WorkshopSpawner;
    public ShopController ShopCtrl;

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
