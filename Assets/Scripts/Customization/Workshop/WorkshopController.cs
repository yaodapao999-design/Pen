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
    public int DrawerVCamPriority = 20;

    [Header("抽屉")]
    public Transform DrawerTransform;
    public float DrawerOpenOffset = 0.3f;
    public float DrawerOpenDuration = 0.5f;

    [Header("改装台")]
    public WorkshopSlot WorkshopSlot;

    [Header("当前笔配置")]
    public PenAssembly CurrentPen;
    public PenPartData CurrentBarrel;
    public PenPartData[] CurrentParts;

    [Header("退出按钮")]
    public RectTransform ExitButton;
    public float ShakeIntensity = 10f;
    public float ShakeDuration = 0.4f;

    [Header("引用")]
    public WorkshopPenSpawner PenSpawner;

    private CinemachineBrain _brain;
    private bool _inDrawer;
    private Rigidbody _penRb;
    private float _drawerClosedZ;
    private bool _isTransitioning;
    private bool _isShaking;
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
        if (DrawerTransform != null) _drawerClosedZ = DrawerTransform.localPosition.z;

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

            // 悬停到非笔杆的已装配零件 → 笔杆变半透明，露出内部
            if (_hoveredPart != null && !_hoveredPart.IsBarrel
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
                if (ExitButton != null) StartCoroutine(ShakeButton());
                return;
            }
            FinishWorkshop();
        }
        else
        {
            EnterWorkshop();
        }
    }

    public void EnterWorkshop()
    {
        if (_inDrawer || _isTransitioning) return;
        _inDrawer = true;

        if (_penRb != null)
        {
            _penRb.linearVelocity = Vector3.zero;
            _penRb.angularVelocity = Vector3.zero;
            _penRb.isKinematic = true;
        }

        StartCoroutine(TransitionToDrawer());
    }

    public void FinishWorkshop()
    {
        if (!_inDrawer || _isTransitioning) return;
        StartCoroutine(TransitionFromDrawer());
    }

    // ─── 进入改装 ─────────────────────────────────────────────────────────────

    private IEnumerator TransitionToDrawer()
    {
        _isTransitioning = true;
        if (DrawerVCam != null) DrawerVCam.Priority = DrawerVCamPriority;

        yield return new WaitForSeconds(GetBlendDuration());

        _penOriginalPosition = CurrentPen.transform.position;
        _penOriginalRotation = CurrentPen.transform.rotation;

        CurrentPen.ClearBattleView();
        SetPenEntityVisible(false);

        CreateWorkshopView();
        if (PenSpawner != null) PenSpawner.SpawnParts();

        var frozenParts = FreezeLooseParts();

        if (DrawerTransform != null)
            yield return AnimateDrawer(_drawerClosedZ + DrawerOpenOffset, frozenParts);

        UnfreezeAll(frozenParts);

        // 抽屉打开后更新锚点（世界坐标已改变）
        var barrel = Registry?.GetAssembledBarrel();
        if (barrel != null)
            barrel.SetSlotAnchor(barrel.transform.position, barrel.transform.rotation);

        _isTransitioning = false;
    }

    // ─── 退出改装 ─────────────────────────────────────────────────────────────

    private IEnumerator TransitionFromDrawer()
    {
        _isTransitioning = true;

        var frozenParts = FreezeLooseParts();

        if (DrawerTransform != null)
            yield return AnimateDrawer(_drawerClosedZ, frozenParts);

        // 抽屉关闭后：写回数据 → 销毁容器 → 重建战斗视图
        WriteBackData();
        DestroyAllWorkshopParts();

        SetPenEntityVisible(true);
        CurrentPen.transform.SetPositionAndRotation(_penOriginalPosition, _penOriginalRotation);
        if (_penRb != null)
        {
            _penRb.position = _penOriginalPosition;
            _penRb.rotation = _penOriginalRotation;
        }

        CurrentPen.BuildBattleView();

        if (DrawerVCam != null) DrawerVCam.Priority = 0;

        yield return null;
        yield return new WaitForSeconds(GetBlendDuration());

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

    private IEnumerator AnimateDrawer(float targetZ, List<(Transform t, Vector3 start)> frozenParts)
    {
        float startZ = DrawerTransform.localPosition.z;
        Vector3 drawerWorldStart = DrawerTransform.position;

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

        float t = 0f;
        while (t < 1f)
        {
            t += Time.deltaTime / DrawerOpenDuration;
            float smooth = Mathf.SmoothStep(0f, 1f, t);
            DrawerTransform.localPosition = new Vector3(
                DrawerTransform.localPosition.x,
                DrawerTransform.localPosition.y,
                Mathf.Lerp(startZ, targetZ, smooth));

            Vector3 delta = DrawerTransform.position - drawerWorldStart;

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

    private IEnumerator ShakeButton()
    {
        if (_isShaking) yield break;
        _isShaking = true;

        var origin = ExitButton.anchoredPosition;
        float elapsed = 0f;
        while (elapsed < ShakeDuration)
        {
            elapsed += Time.unscaledDeltaTime;
            ExitButton.anchoredPosition = origin + new Vector2(
                Random.Range(-ShakeIntensity, ShakeIntensity),
                Random.Range(-ShakeIntensity, ShakeIntensity));
            yield return null;
        }
        ExitButton.anchoredPosition = origin;
        _isShaking = false;
    }
}
