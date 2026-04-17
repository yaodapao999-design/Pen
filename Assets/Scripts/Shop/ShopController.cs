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

    [Header("战斗笔联动（可选）")]
    [Tooltip("书到位后会把这两把笔隐藏（由 Battle 快照位置，重进时会从记忆位置上方落回）；留空则无联动")]
    public BattleStateMachine BattleSM;

    [Header("镜头（由 GameManager 调度，本地不改 Priority）")]
    public CinemachineCamera ShopVCam;

    private readonly List<ShopPart> _activeParts = new List<ShopPart>();
    private bool _active;

    private void Awake()
    {
        if (ShopRoot != null) ShopRoot.SetActive(false);
        if (BookAnimator != null) BookAnimator.SnapToExited();
        // 镜头优先级由 GameManager 统一调度，这里不碰
    }

    // ─── 入口 ─────────────────────────────────────────────────────────────────

    /// <summary>协程版 Enter：激活根 → 笔记本滑入（物理撞飞桌上笔）→ 书到位后清笔 → 生成商品。</summary>
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
        // 书到位 → 笔已被物理撞飞到桌边外，此时清场
        if (BattleSM != null) BattleSM.SetPensActive(false);
        PopulateShop();
    }

    /// <summary>协程版 Exit：清商品 → 播笔记本滑出动画 → 关根。</summary>
    public System.Collections.IEnumerator ExitShopRoutine()
    {
        if (!_active) yield break;
        _active = false;
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
            sp.Init(data, slot.position, slot.rotation, Drawer, OnPartPurchased, OnPartLanded);
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

        // 若已有拖拽中的 ShopPart，让其自行处理（它自己监听鼠标）
        foreach (var sp in _activeParts)
            if (sp != null && sp.IsDragging) return;

        var mouse = Mouse.current;
        var cam = Camera.main;
        if (mouse == null || cam == null) return;

        if (!mouse.leftButton.wasPressedThisFrame) return;

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
        if (best != null) best.BeginDrag();
    }

    // ─── 购买回调 ─────────────────────────────────────────────────────────────

    private void OnPartPurchased(ShopPart sp)
    {
        if (sp == null || sp.PartData == null) return;

        if (GameManager.Instance != null)
            GameManager.Instance.OnPartPurchased(sp.PartData);
        else
            Debug.LogWarning("[Shop] GameManager 未初始化，购买未持久化");

        // 不立即销毁 sp：让物理下落 + 静置后 ShopPart 自己销毁
        // 抽屉关闭时机由 OnPartLanded 决定（等零件真正落到抽屉里再关，避免关门穿模）
        _activeParts.Remove(sp);
    }

    private void OnPartLanded(ShopPart sp)
    {
        if (Drawer != null) Drawer.Close();
    }
}
