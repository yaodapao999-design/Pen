using System.Collections;
using System.Collections.Generic;
using Unity.Cinemachine;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// 改装场景总控制器。
///
/// 职责：
///   1. 镜头切换 + 抽屉动画
///   2. 进入改装：ClearBattleView → CreateWorkshopView（从数据创建容器树）
///   3. 退出改装：WriteBackData（从容器树读回数据）→ DestroyWorkshopView → BuildBattleView
///   4. 集中输入分发（一次 RaycastAll → 通知 WorkshopPart）
///
/// 不直接查找 WorkshopPart，通过 WorkshopPartRegistry 获取。
/// 不直接计算改装台位置，通过 WorkshopSlotCalculator 获取。
/// </summary>
public class WorkshopController : MonoBehaviour
{
    [Header("镜头")]
    public CinemachineCamera DrawerVCam;

    [Header("抽屉")]
    public DrawerAnimator DrawerAnim;

    [Header("过渡")]
    [Tooltip("退出 Workshop 时，重建战斗视图后解冻物理前的等待时间，用于等相机混合基本到位")]
    [SerializeField] private float _exitSettleDelay = 0.1f;
    [Tooltip("进入 Workshop 时，从切换开始到隐藏战斗双方笔的延迟：给相机过渡留够时间，避免笔在玩家视野里凭空消失")]
    [SerializeField] private float _hidePensDelay = 2f;

    [Header("改装台")]
    public WorkshopSlot WorkshopSlot;

    [Header("当前笔配置")]
    public PenAssembly CurrentPen;
    public PenPartData CurrentBarrel;
    public PenPartData[] CurrentParts;

    [Header("引用")]
    public WorkshopPenSpawner PenSpawner;
    [Tooltip("战斗状态机，进改装时隐藏双方笔，出改装时由 BattlePhase 重新激活并归位")]
    public BattleStateMachine BattleSM;

    private CinemachineBrain _brain;
    private bool _inDrawer;
    private Rigidbody _penRb;
    private bool _isTransitioning;
    private Vector3 _penOriginalPosition;
    private Quaternion _penOriginalRotation;

    private WorkshopPartRegistry Registry => WorkshopPartRegistry.Instance;

    // 悬停高亮（脉冲发光）
    private WorkshopPart _hoveredPart;
    private WorkshopPart _transparentBarrel; // 悬停内部零件时笔杆变半透明
    private static readonly int EmissionColor = Shader.PropertyToID("_EmissionColor");
    [Header("悬停高亮")]
    public Color HoverColorMin = new(0.05f, 0.05f, 0.05f, 1f);
    public Color HoverColorMax = new(0.3f, 0.3f, 0.3f, 1f);
    public float HoverPulseSpeed = 3f;
    public float BarrelTransparentAlpha = 0.3f;

    // ─── 初始化 ───────────────────────────────────────────────────────────────

    private void Start()
    {
        // 确保 Registry 存在
        if (WorkshopPartRegistry.Instance == null)
        {
            var go = new GameObject("WorkshopPartRegistry");
            go.AddComponent<WorkshopPartRegistry>();
        }

        if (DrawerVCam != null) DrawerVCam.Priority = 0;
        _brain = Camera.main.GetComponent<CinemachineBrain>();
        if (CurrentPen != null) _penRb = CurrentPen.GetComponent<Rigidbody>();

        if (CurrentBarrel != null && CurrentPen != null)
        {
            CurrentPen.InitData(CurrentBarrel, CurrentParts);
            CurrentPen.BuildBattleView();
        }
    }

    // ─── 输入 ─────────────────────────────────────────────────────────────────

    private void Update()
    {
        if (!_inDrawer || _isTransitioning) return;

        var mouse = Mouse.current;
        if (mouse == null) return;

        var cam = Camera.main;
        if (cam == null) return;

        var ray = cam.ScreenPointToRay(mouse.position.ReadValue());

        // 射线检测：优先子零件，兜底笔杆
        var target = RaycastBestTarget(ray);

        // ── 悬停高亮（每帧，不限按下） ──
        UpdateHover(target);

        // ── 按下拖拽 ──
        if (mouse.leftButton.wasPressedThisFrame && target != null)
        {
            ClearHover(); // 拖拽开始时关掉高亮
            target.BeginDrag();
        }
    }

    /// <summary>射线检测：优先子零件，兜底笔杆</summary>
    private WorkshopPart RaycastBestTarget(Ray ray)
    {
        WorkshopPart bestPart = null;
        WorkshopPart bestBarrel = null;
        float bestPartDist = float.MaxValue;
        float bestBarrelDist = float.MaxValue;

        foreach (var hit in Physics.RaycastAll(ray))
        {
            var wp = hit.collider.GetComponent<WorkshopPart>();
            if (wp == null || wp.State == WorkshopPart.PartState.Snapping) continue;

            if (wp.IsBarrel)
            {
                if (hit.distance < bestBarrelDist)
                { bestBarrelDist = hit.distance; bestBarrel = wp; }
            }
            else
            {
                if (hit.distance < bestPartDist)
                { bestPartDist = hit.distance; bestPart = wp; }
            }
        }

        return bestPart ?? bestBarrel;
    }

    /// <summary>悬停高亮：脉冲发光 + 内部零件时笔杆变半透明</summary>
    private void UpdateHover(WorkshopPart target)
    {
        if (target != _hoveredPart)
        {
            ClearHover();
            _hoveredPart = target;

            // 悬停到笔芯（内嵌在笔杆内部）→ 只有笔杆变半透明
            if (_hoveredPart != null
                && _hoveredPart.PartData != null
                && _hoveredPart.PartData.Category == PartType.Refill
                && _hoveredPart.State == WorkshopPart.PartState.Assembled)
            {
                var barrel = Registry?.GetAssembledBarrel();
                if (barrel != null)
                {
                    _transparentBarrel = barrel;
                    SetAlpha(barrel, BarrelTransparentAlpha);
                }
            }
        }

        if (_hoveredPart != null)
        {
            float pulse = Mathf.PingPong(Time.time * HoverPulseSpeed, 1f);
            Color emission = Color.Lerp(HoverColorMin, HoverColorMax, pulse);
            SetEmission(_hoveredPart, emission);
        }
    }

    private void ClearHover()
    {
        if (_hoveredPart != null)
        {
            SetEmission(_hoveredPart, Color.black);
            _hoveredPart = null;
        }

        // 恢复笔杆不透明
        if (_transparentBarrel != null)
        {
            SetAlpha(_transparentBarrel, 1f);
            _transparentBarrel = null;
        }
    }

    private static void SetAlpha(WorkshopPart wp, float alpha)
    {
        foreach (var r in wp.GetComponentsInChildren<Renderer>())
        {
            foreach (var mat in r.materials)
            {
                var c = mat.color;
                mat.color = new Color(c.r, c.g, c.b, alpha);
                if (alpha < 1f)
                {
                    mat.SetFloat("_Surface", 1);
                    mat.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
                    mat.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
                    mat.renderQueue = 3000;
                }
                else
                {
                    mat.SetFloat("_Surface", 0);
                    mat.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.One);
                    mat.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.Zero);
                    mat.renderQueue = -1;
                }
            }
        }
    }

    private static void SetEmission(WorkshopPart wp, Color color)
    {
        foreach (var r in wp.GetComponentsInChildren<Renderer>())
        {
            foreach (var mat in r.materials)
            {
                mat.EnableKeyword("_EMISSION");
                mat.SetColor(EmissionColor, color);
            }
        }
    }

    // ─── 进入/退出 ────────────────────────────────────────────────────────────

    public void ToggleWorkshop()
    {
        if (_isTransitioning) return;

        if (_inDrawer)
        {
            if (Registry == null || !Registry.HasAssembledBarrel())
            {
                // 拒绝反馈由发起请求的 UI（PhaseButton）播放，这里只负责拦截
                return;
            }
            FinishWorkshop();
        }
        else
        {
            EnterWorkshop();
        }
    }

    public void EnterWorkshop() => StartCoroutine(EnterWorkshopRoutine());
    public void FinishWorkshop() => StartCoroutine(FinishWorkshopRoutine());

    /// <summary>公开协程：供 GameManager 的 WorkshopPhase.Enter yield 等待</summary>
    public IEnumerator EnterWorkshopRoutine()
    {
        if (_inDrawer || _isTransitioning) yield break;
        _inDrawer = true;

        // 防御：上一阶段（如 Shop）可能让抽屉处于非关闭状态，先瞬时归位
        if (DrawerAnim != null) DrawerAnim.SnapClosed();

        if (_penRb != null)
        {
            _penRb.linearVelocity = Vector3.zero;
            _penRb.angularVelocity = Vector3.zero;
            _penRb.isKinematic = true;
        }

        yield return TransitionToDrawer();
    }

    /// <summary>公开协程：供 GameManager 的 WorkshopPhase.Exit yield 等待。
    /// 不做笔杆校验（那个是阶段级的前置约束，由 WorkshopPhase.CanExit 负责）。</summary>
    public IEnumerator FinishWorkshopRoutine()
    {
        if (!_inDrawer || _isTransitioning) yield break;
        yield return TransitionFromDrawer();
    }


    // ─── 进入改装 ─────────────────────────────────────────────────────────────

    private IEnumerator TransitionToDrawer()
    {
        _isTransitioning = true;
        // 镜头优先级已由 GameManager 在 ChangePhase 开始时抬起，此处不再 set
        yield return new WaitForSeconds(GetBlendDuration());

        _penOriginalPosition = CurrentPen.transform.position;
        _penOriginalRotation = CurrentPen.transform.rotation;

        CurrentPen.ClearBattleView();
        SetPenEntityVisible(false);

        // 无书可推，进改装延迟隐藏双方笔；让相机过渡期间玩家仍能看到笔在桌上，不突兀
        // 位置已由 BattlePhase.Exit 快照
        if (BattleSM != null) StartCoroutine(HidePensAfterDelay(_hidePensDelay));

        CreateWorkshopView();
        if (PenSpawner != null) PenSpawner.SpawnParts();

        var frozenParts = FreezeLooseParts();

        if (DrawerAnim != null)
            yield return AnimateDrawerAndFollow(open: true, frozenParts);

        UnfreezeAll(frozenParts);

        // 抽屉打开后更新锚点（世界坐标已改变）
        var barrel = Registry?.GetAssembledBarrel();
        if (barrel != null)
            barrel.SetSlotAnchor(barrel.transform.position, barrel.transform.rotation);

        _isTransitioning = false;
    }

    // ─── 退出改装 ─────────────────────────────────────────────────────────────

    private IEnumerator HidePensAfterDelay(float delay)
    {
        yield return new WaitForSeconds(Mathf.Max(0f, delay));
        if (BattleSM != null) BattleSM.SetPensActive(false);
    }

    private IEnumerator TransitionFromDrawer()
    {
        _isTransitioning = true;

        var frozenParts = FreezeLooseParts();

        if (DrawerAnim != null)
            yield return AnimateDrawerAndFollow(open: false, frozenParts);

        // 抽屉关闭后：写回数据 → 销毁容器 → 重建战斗视图
        WriteBackData();
        DestroyAllWorkshopParts();

        // 在 BuildBattleView 前重新激活笔根节点，否则新建的战斗视图挂在 inactive 父物体下不可见
        if (BattleSM != null) BattleSM.SetPensActive(true);

        SetPenEntityVisible(true);
        CurrentPen.transform.SetPositionAndRotation(_penOriginalPosition, _penOriginalRotation);
        if (_penRb != null)
        {
            _penRb.position = _penOriginalPosition;
            _penRb.rotation = _penOriginalRotation;
        }

        CurrentPen.BuildBattleView();

        // 镜头优先级由 GameManager 调度，这里不改
        yield return null;
        yield return new WaitForSeconds(_exitSettleDelay);

        if (_penRb != null) _penRb.isKinematic = false;

        _inDrawer = false;
        _isTransitioning = false;
    }

    // ─── 视图创建/销毁 ────────────────────────────────────────────────────────

    private void CreateWorkshopView()
    {
        if (CurrentPen == null || CurrentPen.BarrelData == null) return;

        var dragArea = PenSpawner != null ? PenSpawner.SpawnArea : null;
        var floorCenter = WorkshopSlotCalculator.GetSlotFloorCenter(WorkshopSlot);

        // 笔杆
        var barrelWP = WorkshopPartFactory.Create(
            CurrentPen.BarrelData, floorCenter, Quaternion.identity,
            WorkshopSlot, dragArea);

        WorkshopSlotCalculator.PlaceBarrelOnSlot(barrelWP, WorkshopSlot);
        barrelWP.SetAssembled(isRoot: true);

        // 已装配零件
        foreach (var entry in CurrentPen.AssembledParts)
        {
            var socket = FindSocket(barrelWP, entry.Socket);
            if (socket == null)
            {
                Debug.LogWarning($"[Workshop] 笔杆容器上找不到 Socket {entry.Socket}");
                continue;
            }

            var partWP = WorkshopPartFactory.Create(
                entry.Data, socket.transform.position, socket.transform.rotation,
                WorkshopSlot, dragArea);

            partWP.transform.SetParent(socket.transform);
            partWP.transform.localPosition = Vector3.zero;
            partWP.transform.localRotation = Quaternion.identity;
            partWP.SetAssembled();
        }
    }

    private void WriteBackData()
    {
        if (CurrentPen == null) return;

        var barrel = Registry?.GetAssembledBarrel();
        if (barrel == null)
        {
            Debug.LogWarning("[Workshop] 退出时找不到 Assembled 笔杆，保留原数据");
            return;
        }

        var parts = new List<PenAssembly.PartEntry>();
        foreach (var socket in barrel.GetComponentsInChildren<PartSocket>())
        {
            var child = socket.GetComponentInChildren<WorkshopPart>();
            if (child != null && child != barrel && child.PartData != null)
                parts.Add(new PenAssembly.PartEntry(child.PartData, socket.SocketType));
        }

        CurrentPen.SetData(barrel.PartData, parts);
    }

    private void DestroyAllWorkshopParts()
    {
        if (Registry == null) return;
        // 拷贝一份再遍历，避免修改集合
        var all = new List<WorkshopPart>(Registry.All);
        foreach (var wp in all)
            Destroy(wp.gameObject);
    }

    // ─── 工具 ─────────────────────────────────────────────────────────────────

    private static PartSocket FindSocket(WorkshopPart barrelWP, SocketType type)
    {
        foreach (var s in barrelWP.GetComponentsInChildren<PartSocket>())
            if (s.SocketType == type) return s;
        return null;
    }

    private void SetPenEntityVisible(bool visible)
    {
        if (CurrentPen == null) return;
        foreach (var r in CurrentPen.GetComponentsInChildren<Renderer>())
            r.enabled = visible;
        foreach (var c in CurrentPen.GetComponentsInChildren<Collider>())
            c.enabled = visible;
    }

    private List<(Transform t, Vector3 start)> FreezeLooseParts()
    {
        var result = new List<(Transform, Vector3)>();
        if (Registry == null) return result;
        foreach (var wp in Registry.All)
        {
            if (wp.State == WorkshopPart.PartState.Loose)
            {
                var rb = wp.GetComponent<Rigidbody>();
                if (rb != null) rb.isKinematic = true;
                result.Add((wp.transform, wp.transform.position));
            }
        }
        return result;
    }

    private void UnfreezeAll(List<(Transform t, Vector3 start)> parts)
    {
        foreach (var (t, _) in parts)
        {
            if (t == null) continue;
            var rb = t.GetComponent<Rigidbody>();
            if (rb != null)
            {
                rb.isKinematic = false;
                rb.linearVelocity = Vector3.zero;
                rb.angularVelocity = Vector3.zero;
            }
        }
    }

    /// <summary>
    /// 抽屉开/关动画由 DrawerAnimator 驱动；本协程并行跑，按抽屉世界位移带动零件跟随。
    /// </summary>
    private IEnumerator AnimateDrawerAndFollow(bool open, List<(Transform t, Vector3 start)> frozenParts)
    {
        var drawerT = DrawerAnim.DrawerTransform;
        Vector3 drawerWorldStart = drawerT.position;

        // 收集根级 Assembled 容器（子物体通过层级自动跟随）
        var assembledRoots = new List<(Transform t, Vector3 start)>();
        if (Registry != null)
        {
            foreach (var wp in Registry.All)
            {
                if (wp.State == WorkshopPart.PartState.Assembled && wp.transform.parent == null)
                    assembledRoots.Add((wp.transform, wp.transform.position));
            }
        }

        if (open) DrawerAnim.Open(); else DrawerAnim.Close();

        while (DrawerAnim.IsAnimating)
        {
            Vector3 delta = drawerT.position - drawerWorldStart;
            foreach (var (tr, s) in assembledRoots)
                if (tr != null) tr.position = s + delta;
            foreach (var (tr, s) in frozenParts)
                if (tr != null) tr.position = s + delta;
            yield return null;
        }
    }

    private float GetBlendDuration()
    {
        return _brain != null ? _brain.DefaultBlend.Time : 1f;
    }

}
