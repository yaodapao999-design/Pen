using System.Collections;
using System.Collections.Generic;
using Unity.Cinemachine;
using UnityEngine;

/// <summary>
/// 改装场景总控制器
/// 负责镜头切换、抽屉动画、笔的定位，以及 WorkshopPart 的初始化与清理。
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

    [Header("引用")]
    public WorkshopPenSpawner PenSpawner;

    private CinemachineBrain _brain;
    private bool _inDrawer;
    private Rigidbody _penRb;
    private float _drawerClosedZ;
    private bool _isTransitioning;
    private Vector3 _penOriginalPosition;
    private Quaternion _penOriginalRotation;

    private void Start()
    {
        if (DrawerVCam != null) DrawerVCam.Priority = 0;
        _brain = Camera.main.GetComponent<CinemachineBrain>();
        if (CurrentPen != null) _penRb = CurrentPen.GetComponent<Rigidbody>();
        if (DrawerTransform != null) _drawerClosedZ = DrawerTransform.localPosition.z;
        if (WorkshopSlot != null && PenSpawner != null)
            WorkshopSlot.DragArea = PenSpawner.SpawnArea;

        // 测试用：在 Start 里初始化笔的装配，正式版应由战斗系统传入
        if (CurrentBarrel != null && CurrentPen != null)
        {
            CurrentPen.SetBarrel(CurrentBarrel);
            foreach (var part in CurrentParts)
            {
                if (part == null) continue;
                var socket = CurrentPen.GetSocket(part.PlugsInto);
                if (socket != null) CurrentPen.AddPart(part, socket);
            }
        }
    }

    /// <summary>同一个按钮切换进入/退出改装</summary>
    public void ToggleWorkshop()
    {
        if (_isTransitioning) return;
        if (_inDrawer) FinishWorkshop();
        else EnterWorkshop();
    }

    public void EnterWorkshop()
    {
        if (_inDrawer || _isTransitioning) return;
        _inDrawer = true;

        // 冻结笔的物理，防止在改装台上掉落
        if (_penRb != null)
        {
            _penRb.linearVelocity = Vector3.zero;
            _penRb.angularVelocity = Vector3.zero;
            _penRb.isKinematic = true;
        }

        WorkshopSlot?.Activate();
        StartCoroutine(TransitionToDrawer());
    }

    public void FinishWorkshop()
    {
        if (!_inDrawer || _isTransitioning) return;
        StartCoroutine(TransitionFromDrawer());
    }

    // ─── 进入改装协程 ──────────────────────────────────────────────────────────

    private IEnumerator TransitionToDrawer()
    {
        _isTransitioning = true;
        if (DrawerVCam != null) DrawerVCam.Priority = DrawerVCamPriority;

        // 等待相机 blend 完成
        yield return new WaitForSeconds(GetBlendDuration());

        // 记录战斗原位，直接设世界坐标（不动层级）
        if (WorkshopSlot != null && CurrentPen != null)
        {
            _penOriginalPosition = CurrentPen.transform.position;
            _penOriginalRotation = CurrentPen.transform.rotation;

            var slotCol = WorkshopSlot.GetComponent<BoxCollider>();

            // slot 底面中心的世界坐标
            Vector3 slotFloor = WorkshopSlot.transform.TransformPoint(new Vector3(
                slotCol.center.x,
                slotCol.center.y - slotCol.size.y * 0.5f,
                slotCol.center.z));
            CurrentPen.transform.position = slotFloor;

            // 先应用躺平旋转，再量包围盒（旋转影响实际底部高度）
            var capsule = CurrentPen.GetComponentInChildren<CapsuleCollider>();
            CurrentPen.transform.rotation = WorkshopSlot.transform.rotation *
                ((capsule != null && capsule.direction == 1)
                    ? Quaternion.Euler(0, 0, 90)
                    : Quaternion.identity);

            // 根据视觉包围盒底部贴到 slot 底面，无需硬编码半径
            var penRenderers = CurrentPen.GetComponentsInChildren<Renderer>();
            if (penRenderers.Length > 0)
            {
                var wb = penRenderers[0].bounds;
                foreach (var r in penRenderers) wb.Encapsulate(r.bounds);
                CurrentPen.transform.position += Vector3.up * (slotFloor.y - wb.min.y);
            }
        }

        // 先生成所有内容（抽屉关着，玩家看不到），避免零件闪现
        InitAssembledPartsAsWorkshopParts();
        if (PenSpawner != null) PenSpawner.SpawnParts();

        // 收集所有 Loose 零件，冻结物理，防止抽屉运动时将其弹飞
        var looseParts = new List<Transform>();
        foreach (var wp in FindObjectsByType<WorkshopPart>(FindObjectsSortMode.None))
        {
            if (wp.State == WorkshopPart.PartState.Loose)
            {
                var rb = wp.GetComponent<Rigidbody>();
                if (rb != null) rb.isKinematic = true;
                looseParts.Add(wp.transform);
            }
        }

        // 与抽屉完全同步地打开，Loose 零件随抽屉同步位移
        if (DrawerTransform != null)
            yield return MoveDrawer(_drawerClosedZ + DrawerOpenOffset, movePen: true, looseParts);

        // 抽屉打开完毕，释放零件物理（抽屉墙壁自然围住）
        foreach (var t in looseParts)
        {
            if (t == null) continue;
            var rb = t.GetComponent<Rigidbody>();
            if (rb != null) rb.isKinematic = false;
        }

        _isTransitioning = false;
    }

    // ─── 离开改装协程 ──────────────────────────────────────────────────────────

    private IEnumerator TransitionFromDrawer()
    {
        _isTransitioning = true;
        WorkshopSlot?.Deactivate();

        // 冻结所有 Loose 零件，随抽屉同步移回
        var looseParts = new List<Transform>();
        foreach (var wp in FindObjectsByType<WorkshopPart>(FindObjectsSortMode.None))
        {
            if (wp.State == WorkshopPart.PartState.Loose)
            {
                var rb = wp.GetComponent<Rigidbody>();
                if (rb != null) rb.isKinematic = true;
                looseParts.Add(wp.transform);
            }
        }

        // 关闭抽屉，笔和零件通过世界坐标同步移回（不动层级）
        if (DrawerTransform != null)
            yield return MoveDrawer(_drawerClosedZ, movePen: true, looseParts);

        // ── 抽屉已关闭，以下操作玩家不可见 ──

        // 恢复所有已装配零件的内部视觉
        if (CurrentPen != null)
        {
            foreach (var part in CurrentPen.Parts)
            {
                if (part.GameObject != null)
                    part.GameObject.SetActive(true);
            }
        }

        // 销毁所有 WorkshopPart 容器
        if (PenSpawner != null) PenSpawner.ClearParts();

        // 还原笔到战斗原位（同时设 Transform 和 Rigidbody，确保物理引擎同步）
        if (CurrentPen != null)
        {
            CurrentPen.transform.position = _penOriginalPosition;
            CurrentPen.transform.rotation = _penOriginalRotation;
        }
        if (_penRb != null)
        {
            _penRb.position = _penOriginalPosition;
            _penRb.rotation = _penOriginalRotation;
        }

        // 切回战斗镜头
        if (DrawerVCam != null) DrawerVCam.Priority = 0;

        // 等一帧让物理引擎消化位置变化，再等相机 blend 完成
        yield return null;
        yield return new WaitForSeconds(GetBlendDuration());

        if (_penRb != null) _penRb.isKinematic = false;
        if (CurrentPen != null) CurrentPen.RefreshPhysics();

        _inDrawer = false;
        _isTransitioning = false;
    }

    // ─── 已装配零件初始化 ──────────────────────────────────────────────────────

    /// <summary>
    /// 为笔上所有已装配的零件创建可交互的 WorkshopPart 容器。
    /// 同时隐藏 PenAssembly 内部的视觉 GO，改由 WorkshopPart 容器呈现视觉。
    /// </summary>
    private void InitAssembledPartsAsWorkshopParts()
    {
        if (CurrentPen == null || WorkshopSlot == null) return;

        foreach (var part in CurrentPen.Parts)
        {
            if (part.AttachedSocket == null) continue;

            // 隐藏 PenAssembly 内部视觉，workshop 中用独立容器替代
            if (part.GameObject != null)
                part.GameObject.SetActive(false);

            // 创建 WorkshopPart 容器，放置在对应 socket 的世界位置
            var container = new GameObject($"WP_{part.Data.PartID}");
            container.transform.SetPositionAndRotation(
                part.AttachedSocket.transform.position,
                part.AttachedSocket.transform.rotation);

            GameObject visual = null;
            if (part.Data.VisualPrefab != null)
            {
                visual = Instantiate(part.Data.VisualPrefab, container.transform);
                visual.transform.SetLocalPositionAndRotation(Vector3.zero, Quaternion.identity);
                // 禁用 visual 自带的所有碰撞体，确保鼠标射线只命中容器上的 BoxCollider
                foreach (var c in visual.GetComponentsInChildren<Collider>())
                    c.enabled = false;
            }

            var col = container.AddComponent<BoxCollider>();
            FitBoxCollider(col, visual);

            var rb = container.AddComponent<Rigidbody>();
            rb.mass = part.Data.Mass;

            // WorkshopPart 的 Awake 在 AddComponent 时立即执行，_rb/_cam 已就绪
            var wp = container.AddComponent<WorkshopPart>();
            wp.PartData = part.Data;
            wp.TargetAssembly = CurrentPen;
            wp.Slot = WorkshopSlot;
            wp.DragArea = PenSpawner != null ? PenSpawner.SpawnArea : null;
            wp.SetAssembled(CurrentPen, part);
        }
    }

    // ─── 工具方法 ──────────────────────────────────────────────────────────────

    /// <summary>
    /// 暂时置零容器旋转，从 Renderer 包围盒精确拟合 BoxCollider，再还原旋转。
    /// 避免硬编码碰撞体尺寸，适配任意大小的视觉预制体。
    /// </summary>
    private static void FitBoxCollider(BoxCollider col, GameObject visual)
    {
        if (visual == null) { col.size = Vector3.one * 0.08f; return; }

        var renderers = visual.GetComponentsInChildren<Renderer>();
        if (renderers.Length == 0) { col.size = Vector3.one * 0.08f; return; }

        // 置零旋转后量包围盒，得到无旋转失真的本地尺寸
        var savedRot = col.transform.rotation;
        col.transform.rotation = Quaternion.identity;
        var lb = renderers[0].bounds;
        foreach (var r in renderers) lb.Encapsulate(r.bounds);
        col.center = col.transform.InverseTransformPoint(lb.center);
        col.size = lb.size;
        col.transform.rotation = savedRot;
    }

    private IEnumerator MoveDrawer(float targetZ, bool movePen = false, List<Transform> looseParts = null)
    {
        float startZ = DrawerTransform.localPosition.z;
        Vector3 penWorldStart = movePen && CurrentPen != null ? CurrentPen.transform.position : Vector3.zero;
        Vector3 drawerWorldStart = DrawerTransform.position;

        // 记录每个 Loose 零件的初始世界坐标
        Vector3[] looseStarts = null;
        if (looseParts != null)
        {
            looseStarts = new Vector3[looseParts.Count];
            for (int i = 0; i < looseParts.Count; i++)
                looseStarts[i] = looseParts[i] != null ? looseParts[i].position : Vector3.zero;
        }

        float t = 0f;
        while (t < 1f)
        {
            t += Time.deltaTime / DrawerOpenDuration;
            float smooth = Mathf.SmoothStep(0f, 1f, t);
            float z = Mathf.Lerp(startZ, targetZ, smooth);
            DrawerTransform.localPosition = new Vector3(
                DrawerTransform.localPosition.x,
                DrawerTransform.localPosition.y,
                z);

            Vector3 drawerDelta = DrawerTransform.position - drawerWorldStart;

            if (movePen && CurrentPen != null)
                CurrentPen.transform.position = penWorldStart + drawerDelta;

            if (looseParts != null)
                for (int i = 0; i < looseParts.Count; i++)
                    if (looseParts[i] != null)
                        looseParts[i].position = looseStarts[i] + drawerDelta;

            yield return null;
        }
    }

    private float GetBlendDuration()
    {
        if (_brain == null) return 1f;
        return _brain.DefaultBlend.Time;
    }
}
