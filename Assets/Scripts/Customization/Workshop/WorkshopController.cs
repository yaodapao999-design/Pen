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
    [Header("改装台")]
    public WorkshopSlot WorkshopSlot;

    [Header("当前笔配置")]
    [Tooltip("玩家笔。初始装配由 GameManager 从 PlayerLoadoutSO 注入；Workshop 只做视图构建和数据读写")]
    public PenAssembly CurrentPen;

    [Header("引用")]
    public WorkshopPenSpawner PenSpawner;

    private CinemachineBrain _brain;
    private bool _inDrawer;
    private Rigidbody _penRb;
    private bool _isTransitioning;

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

    [Header("入场动画")]
    [Tooltip("抽屉打开到位后各零件依次弹入的间隔（秒）。0 = 所有同时")]
    [SerializeField] private float _introStaggerDelay = 0.06f;

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

        // 笔的初始装配（Barrel + Equipped）和战斗视图由 GameManager.ApplyLoadoutOnNewSave 统一处理
    }

    // ─── 输入 ─────────────────────────────────────────────────────────────────

    private void Update()
    {
        if (!_inDrawer || _isTransitioning) return;

        var mouse = Mouse.current;
        if (mouse == null) return;

        var cam = Camera.main;
        if (cam == null) return;

        // 拖拽中：不做悬停高亮 / 不显示 PartInfoPanel——StatDelta 浮窗已经担任拖拽期间的信息来源
        if (HasAnyDragging())
        {
            ClearHover();
            PartInfoPanel.Instance?.Hide();
            return;
        }

        var ray = ScreenHelper.ScreenPointToRay(cam, mouse.position.ReadValue());

        // 射线检测：优先子零件，兜底笔杆
        var target = RaycastBestTarget(ray);

        // ── 悬停高亮（每帧，不限按下） ──
        UpdateHover(target);

        // ── 按下拖拽 ──
        if (mouse.leftButton.wasPressedThisFrame && target != null)
        {
            // 已有零件在"点击切换"拖拽中 → 这一帧把输入让给它走 Clicked→End，不启动新拖拽
            // 否则会出现"旧零件被 HandleMouseUp 随地一放 + 新零件同时被 BeginDrag"的双拖拽
            if (HasAnyDragging()) return;

            ClearHover(); // 拖拽开始时关掉高亮
            target.BeginDrag();
        }
    }

    /// <summary>是否存在任何处于 Dragging 状态的 WorkshopPart（供 Update 避免跨零件双拖拽）</summary>
    private bool HasAnyDragging()
    {
        if (Registry == null) return false;
        foreach (var wp in Registry.All)
            if (wp != null && wp.State == WorkshopPart.PartState.Dragging)
                return true;
        return false;
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

    /// <summary>悬停高亮：脉冲发光 + 内部零件时笔杆变半透明 + 信息面板</summary>
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
                    MaterialAlphaUtility.ApplyAlpha(barrel.gameObject, BarrelTransparentAlpha);
                }
            }
        }

        if (_hoveredPart != null)
        {
            float pulse = Mathf.PingPong(Time.time * HoverPulseSpeed, 1f);
            Color emission = Color.Lerp(HoverColorMin, HoverColorMax, pulse);
            SetEmission(_hoveredPart, emission);

            // 信息面板（Workshop 不显示价格）
            if (_hoveredPart.PartData != null)
                PartInfoPanel.Instance?.Show(_hoveredPart.PartData, showPrice: false);
        }
        else
        {
            PartInfoPanel.Instance?.Hide();
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
            MaterialAlphaUtility.ApplyAlpha(_transparentBarrel.gameObject, 1f);
            _transparentBarrel = null;
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

        // 笔位置所有权属于 BattleStateMachine（SnapshotPens/RestorePens 管理）；
        // 两把笔的 SetActive(false) 由 BattlePhase.Exit 启动的统一倒计时负责，
        // Player 和 Enemy 同步消失。Workshop 不再碰 Player 视觉——
        // 退出 Workshop 时 PenAssembly.BuildBattleView 内部会自动 ClearBattleView 重建。
        CreateWorkshopView();
        if (PenSpawner != null) PenSpawner.SpawnParts();

        // 记录每个 WorkshopPart 的"目标 scale"并预先置 0，抽屉动画期间不可见（仅作为"位置载体"跟着抽屉走）
        var partScales = CapturePartScalesAndHide();

        var frozenParts = FreezeLooseParts();

        Vector3 drawerStartPos = DrawerAnim != null ? DrawerAnim.DrawerTransform.position : Vector3.zero;
        if (DrawerAnim != null)
            yield return AnimateDrawerAndFollow(open: true, frozenParts);
        Vector3 drawerEndPos = DrawerAnim != null ? DrawerAnim.DrawerTransform.position : Vector3.zero;

        // 开门到位：散落零件立即获得惯性初速度开始 settle（全程可见，此刻开始自然下落）
        float inertia = WorkshopConfig.Instance != null ? WorkshopConfig.Instance.DrawerOpenInertia : 0.8f;
        UnfreezeWithInertia(frozenParts, drawerEndPos - drawerStartPos, inertia);

        // 紧接着触发改装台零件的错开弹入（Assembled only，不影响抽屉里正在 settle 的零件）
        TriggerStaggerPopIn(partScales);

        // 抽屉打开后更新锚点（世界坐标已改变）
        var barrel = Registry?.GetAssembledBarrel();
        if (barrel != null)
            barrel.SetSlotAnchor(barrel.transform.position, barrel.transform.rotation);

        _isTransitioning = false;
    }

    /// <summary>快照改装台上（Assembled）各零件的 localScale 并置零。
    /// 抽屉散落的 Loose 零件不动 —— 抽屉滑开过程它们全程可见，由抽屉自身的运动揭示即可。</summary>
    private List<(WorkshopPart wp, Vector3 scale)> CapturePartScalesAndHide()
    {
        var list = new List<(WorkshopPart, Vector3)>();
        if (Registry == null) return list;
        foreach (var wp in Registry.All)
        {
            if (wp == null) continue;
            if (wp.State != WorkshopPart.PartState.Assembled) continue; // Loose 全程可见
            list.Add((wp, wp.transform.localScale));
            wp.transform.localScale = Vector3.zero;
        }
        return list;
    }

    /// <summary>按记录的目标 scale 触发各 WorkshopPart 的入场弹入，按索引错开。</summary>
    private void TriggerStaggerPopIn(List<(WorkshopPart wp, Vector3 scale)> list)
    {
        for (int i = 0; i < list.Count; i++)
        {
            var (wp, scale) = list[i];
            if (wp == null) continue;
            wp.PlayIntroPopIn(delay: i * _introStaggerDelay, homeScale: scale);
        }
    }

    // ─── 退出改装 ─────────────────────────────────────────────────────────────

    private IEnumerator TransitionFromDrawer()
    {
        _isTransitioning = true;
        ClearHover();
        PartInfoPanel.Instance?.Hide();

        var frozenParts = FreezeLooseParts();

        if (DrawerAnim != null)
            yield return AnimateDrawerAndFollow(open: false, frozenParts);

        // 抽屉关闭后：记忆散落件位置 → 写回数据 → 销毁容器 → 重建战斗视图
        if (PenSpawner != null) PenSpawner.MemorizeLoosePoses();
        WriteBackData();
        DestroyAllWorkshopParts();

        SetPenEntityVisible(true);
        // Player 笔位置由 BattlePhase.Enter 的 RestorePens 统一恢复；Workshop 不再强行 SetPosition
        // 避免和 BSM 快照争夺所有权产生可见闪帧
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

        // ① Assembly 数据：已装配到笔杆的零件
        var parts = new List<PenAssembly.PartEntry>();
        foreach (var socket in barrel.GetComponentsInChildren<PartSocket>())
        {
            var child = socket.GetComponentInChildren<WorkshopPart>();
            if (child != null && child != barrel && child.PartData != null)
                parts.Add(new PenAssembly.PartEntry(child.PartData, socket.SocketType));
        }
        CurrentPen.SetData(barrel.PartData, parts);

        // ② Inventory 数据：抽屉里仍然 Loose 的零件
        //   Equipment-as-Container 语义：零件要么在 Inventory 要么在 Assembly，互斥。
        //   不写回会导致：卸下的零件丢失，装上的零件在下次散落时重复出现。
        var inv = GameManager.Instance != null ? GameManager.Instance.Inventory : null;
        if (inv != null)
        {
            inv.Clear();
            foreach (var wp in Registry.All)
            {
                if (wp == null || wp.PartData == null) continue;
                if (wp.State != WorkshopPart.PartState.Loose) continue;
                inv.Add(wp.PartData);
            }
        }
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
    /// 惯性解冻：Dynamic 化 + 沿 motion 方向赋初速度（VelocityChange 与质量无关，表现一致）。
    /// motion 为 0 或 velocity ≤ 0 时退化为普通 UnfreezeAll。
    /// </summary>
    private void UnfreezeWithInertia(List<(Transform t, Vector3 start)> parts, Vector3 motion, float velocity)
    {
        if (motion.sqrMagnitude < 0.0001f || velocity <= 0f)
        {
            UnfreezeAll(parts);
            return;
        }
        Vector3 v = motion.normalized * velocity;
        foreach (var (t, _) in parts)
        {
            if (t == null) continue;
            var rb = t.GetComponent<Rigidbody>();
            if (rb == null) continue;
            rb.isKinematic = false;
            rb.linearVelocity = v;
            rb.angularVelocity = Vector3.zero;
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
