using UnityEngine;

/// <summary>
/// 拖拽拾取器 —— "比笔大一圈"的命中判定 + 表面吸附。
///
/// 问题：笔真实半径 0.04-0.12m，屏幕上一小条，精准点击手感差。
/// 直接用原生 Raycast 命中 Collider 失败率高，尤其像素双相机下玩家感知的"笔"比实际判定粗一点。
///
/// 解法：SphereCast 替代 Raycast：
///   从相机沿点击方向发射一个半径 hitRadius 的球体，首先碰到笔层级任意 Collider 的表面点
///   即为命中。命中点 hit.point 已经是笔真实表面上的一点（PhysX 内部计算出 sphere 最先触碰
///   pen Collider 的几何点），天然符合"法线吸附"——沿点击方向最接近笔的那个表面点。
///
/// Gizmo 里绘制 hitRadius 扩张的笔包围框，一眼看到判定范围。
/// </summary>
[RequireComponent(typeof(PenEntity))]
public class PenDragInteractor : MonoBehaviour
{
    [Tooltip("命中判定范围 —— 在笔实际几何外扩多少米。0.15-0.3 典型：\n" +
             "0.15 = 笔稍胖一圈，精准玩家；0.3 = 相当容易点到，容错高。\n" +
             "本值直接作为 SphereCast 的球半径。")]
    public float hitRadius = 0.25f;

    [Tooltip("SphereCast 的最大距离。不用调，足够从相机到桌面即可。")]
    public float maxCastDistance = 50f;

    [Tooltip("Tier-2 屏幕空间容错半径（占屏幕短边的比例）。0 = 关闭 fallback；典型 0.04-0.06。\n" +
             "1080p 短边 1080 × 0.05 = 54 像素 ≈ 半个手指宽，是 Stardew/Hades 默认的 'generous hit' 范围。\n" +
             "笔细长，行业最佳实践：精确 SphereCast 没命中时，按光标到笔屏幕投影线段的距离做软命中。")]
    [Range(0f, 0.2f)]
    public float screenFallbackRatio = 0.05f;

    private PenEntity _pen;

    private void Awake()
    {
        _pen = GetComponent<PenEntity>();
    }

    /// <summary>
    /// 两层命中判定。Tier 1 走精确 SphereCast；不中则走 Tier 2 屏幕空间容错（光标到笔屏幕
    /// 投影线段的距离 &lt; 屏幕短边 × screenFallbackRatio 即视为命中）。
    /// 命中 → contactPointWorld 为笔表面最接近的世界点；返回 true。
    /// 未命中返回 false。
    /// <para>
    /// 工业标准模式 "Generous Hit Detection / Soft Aim Lock"——Stardew Valley 钓竿、
    /// Hades 投掷瞄准、Cult of the Lamb 抓崇拜者使用同一套，解决"小/细物体精确点击难"的问题。
    /// </para>
    /// </summary>
    public bool TryHit(Camera cam, Vector2 screenPos, Ray ray, out Vector3 contactPointWorld)
    {
        if (TrySphereCast(ray, out contactPointWorld)) return true;
        return TryScreenSpaceFallback(cam, screenPos, out contactPointWorld);
    }

    /// <summary>
    /// Tier 1：精确 SphereCast。半径 hitRadius 在 3D 空间扩张笔的胶囊壳。
    /// 在笔正侧对镜头时屏幕投影宽 ≈ 50px（1080p, Y=7 俯视），玩家精确点击的舒适区。
    /// </summary>
    private bool TrySphereCast(Ray ray, out Vector3 contactPointWorld)
    {
        contactPointWorld = Vector3.zero;
        RaycastHit[] hits = Physics.SphereCastAll(ray.origin, hitRadius, ray.direction, maxCastDistance);
        if (hits == null || hits.Length == 0) return false;

        RaycastHit best = default; float bestDist = float.MaxValue; bool found = false;
        foreach (var h in hits)
        {
            var onPen = h.collider.GetComponentInParent<PenEntity>();
            if (onPen != _pen) continue;
            if (h.distance < bestDist) { bestDist = h.distance; best = h; found = true; }
        }
        if (!found) return false;

        // SphereCast 边界情况：射线起点已在球内 → hit.distance=0 且 hit.point=Vector3.zero。
        // 此时用 collider.ClosestPoint 把射线原点投到笔表面作为 fallback。
        if (best.distance <= 0.0001f && best.point == Vector3.zero)
            best.point = best.collider.ClosestPoint(ray.origin);

        contactPointWorld = best.point;
        return true;
    }

    /// <summary>
    /// Tier 2：屏幕空间软命中。把笔身上每个 Collider（barrel + 装配的零件 cap/tip/refill 等）
    /// 投影成屏幕 2D 线段，取光标到所有线段中最近的距离；&lt; 阈值即视为命中。
    /// <para>
    /// **自适配体积变化**：每次调用都重新枚举 GetComponentsInChildren&lt;Collider&gt;()，
    /// Workshop 装/卸零件后笔的物理形状随时变化，本方法立即跟上——无需缓存、无需事件订阅。
    /// </para>
    /// <para>
    /// **自适配分辨率**：阈值是 Screen 短边的百分比，在 1080p / 4K / 任意 RT 上屏分辨率下
    /// 表现一致（fallback 区域始终占视觉的 ~5%）。
    /// </para>
    /// </summary>
    private bool TryScreenSpaceFallback(Camera cam, Vector2 screenPos, out Vector3 contactPointWorld)
    {
        contactPointWorld = Vector3.zero;
        if (cam == null || screenFallbackRatio <= 0f) return false;

        float threshold = Mathf.Min(Screen.width, Screen.height) * screenFallbackRatio;
        float thresholdSqr = threshold * threshold;

        float bestSqrDist = float.MaxValue;
        Vector3 bestWorldPoint = Vector3.zero;
        bool found = false;

        // 包含 inactive=false 的 Collider；笔装配后每个零件都挂在 socket 子物体里，统统枚举到。
        // 性能：click 是事件型，零件数 < 10，每次调用 < 0.1ms 量级。
        var cols = GetComponentsInChildren<Collider>();
        foreach (var col in cols)
        {
            if (!col.enabled) continue;

            ComputeWorldSegment(col, out Vector3 worldA, out Vector3 worldB);

            // 投影到屏幕；任一端在镜头后方（z <= 0）则跳过此 collider，避免坐标翻转
            Vector3 sa3 = cam.WorldToScreenPoint(worldA);
            Vector3 sb3 = cam.WorldToScreenPoint(worldB);
            if (sa3.z <= 0f || sb3.z <= 0f) continue;

            Vector2 sa = new Vector2(sa3.x, sa3.y);
            Vector2 sb = new Vector2(sb3.x, sb3.y);

            Vector2 closest = ClosestPointOnSegment(sa, sb, screenPos, out float t);
            float sqr = (closest - screenPos).sqrMagnitude;
            if (sqr < thresholdSqr && sqr < bestSqrDist)
            {
                bestSqrDist = sqr;
                bestWorldPoint = Vector3.Lerp(worldA, worldB, t);
                found = true;
            }
        }

        if (found)
        {
            contactPointWorld = bestWorldPoint;
            return true;
        }

        Debug.Log($"[PenDragInteractor/debug] Tier1+Tier2 都未命中 | screenPos={screenPos} threshold={threshold:F1}px");
        return false;
    }

    /// <summary>
    /// 算 Collider 在世界空间的代表线段。CapsuleCollider 走真实两端（笔主体），
    /// 其他类型用 bounds 中心当点（线段长度=0，等价点+threshold半径的圆形容错）。
    /// </summary>
    private static void ComputeWorldSegment(Collider col, out Vector3 a, out Vector3 b)
    {
        if (col is CapsuleCollider cap)
        {
            Vector3 localAxis = cap.direction switch
            {
                0 => Vector3.right,
                1 => Vector3.up,
                2 => Vector3.forward,
                _ => Vector3.right
            };
            // 端点距 center 的距离 = (height/2 - radius)，即 capsule 主体（除去半球帽）的两端
            float halfBody = Mathf.Max(0f, cap.height * 0.5f - cap.radius);
            Vector3 localA = cap.center - localAxis * halfBody;
            Vector3 localB = cap.center + localAxis * halfBody;
            // TransformPoint 自动应用 transform 的 scale/rotation/position，世界尺寸正确
            a = cap.transform.TransformPoint(localA);
            b = cap.transform.TransformPoint(localB);
        }
        else
        {
            a = col.bounds.center;
            b = col.bounds.center;
        }
    }

    /// <summary>2D 线段最近点。线段退化为点时 t=0。</summary>
    private static Vector2 ClosestPointOnSegment(Vector2 a, Vector2 b, Vector2 p, out float t)
    {
        Vector2 ab = b - a;
        float lenSqr = ab.sqrMagnitude;
        if (lenSqr < 0.0001f) { t = 0f; return a; }
        t = Mathf.Clamp01(Vector2.Dot(p - a, ab) / lenSqr);
        return a + ab * t;
    }

#if UNITY_EDITOR
    private void OnDrawGizmos()
    {
        // 沿笔长轴画一条胶囊状命中区示意（简化为笔的 AABB 外扩）
        var cols = GetComponentsInChildren<Collider>();
        if (cols == null || cols.Length == 0) return;
        Bounds b = new Bounds(cols[0].bounds.center, cols[0].bounds.size);
        for (int i = 1; i < cols.Length; i++) b.Encapsulate(cols[i].bounds);
        b.Expand(hitRadius * 2f);
        Gizmos.color = new Color(1f, 0.9f, 0.2f, 0.35f);
        Gizmos.DrawWireCube(b.center, b.size);
    }
#endif
}
