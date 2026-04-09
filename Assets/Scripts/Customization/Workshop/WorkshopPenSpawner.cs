using UnityEngine;

/// <summary>
/// 在改装场景抽屉里生成散落的零件
/// SpawnArea 用 BoxCollider 定义区域，WorkshopSlot 范围内不生成
/// 生成时保证整个碰撞体不超出 SpawnArea、不进入 WorkshopSlot
/// </summary>
public class WorkshopPenSpawner : MonoBehaviour
{
    [Header("生成配置")]
    public PenPartData[] AvailableParts;
    public BoxCollider SpawnArea;       // 定义可生成区域的 BoxCollider（可包含改装台）
    public WorkshopSlot WorkshopSlot;  // 改装台，其 BoxCollider 范围内不生成

    [Header("引用")]
    public PenAssembly TargetAssembly;

    public void SpawnParts()
    {
        if (AvailableParts == null) return;

        foreach (var partData in AvailableParts)
        {
            if (partData == null || partData.VisualPrefab == null) continue;

            var rot = Quaternion.Euler(0, Random.Range(0f, 360f), 0);

            // 1. 先在原点创建容器并拟合碰撞体，才能知道实际尺寸
            var container = new GameObject($"LoosePart_{partData.PartID}");
            container.transform.rotation = rot;

            var visual = Instantiate(partData.VisualPrefab, container.transform);
            visual.transform.SetLocalPositionAndRotation(Vector3.zero, Quaternion.identity);
            foreach (var c in visual.GetComponentsInChildren<Collider>())
                c.enabled = false;

            var col = container.AddComponent<BoxCollider>();
            var renderers = visual.GetComponentsInChildren<Renderer>();
            FitBoxCollider(col, renderers);

            // 2. 用碰撞体的世界 AABB 半尺寸寻找完全合法的位置
            Vector3 halfExtents = col.bounds.extents;
            var pos = GetSpawnPosOutsideSlot(halfExtents);
            if (!pos.HasValue) { Destroy(container); continue; }

            // 3. 设置位置并贴地
            container.transform.position = pos.Value;
            if (renderers.Length > 0)
            {
                var wb = renderers[0].bounds;
                foreach (var r in renderers) wb.Encapsulate(r.bounds);
                container.transform.position += Vector3.up * (pos.Value.y - wb.min.y);
            }

            // 4. 添加物理和交互
            var rb = container.AddComponent<Rigidbody>();
            rb.mass = partData.Mass;

            var wp = container.AddComponent<WorkshopPart>();
            wp.PartData = partData;
            wp.TargetAssembly = TargetAssembly;
            wp.Slot = WorkshopSlot;
            wp.DragArea = SpawnArea;
            wp.SetLoose();
        }
    }

    /// <summary>
    /// 在指定位置生成散落零件（供外部调用，如 DisassembleAll 脱落时使用）。
    /// 不做区域校验，由调用方确定位置。
    /// </summary>
    public void SpawnLoosePart(PenPartData data, Vector3 pos, Quaternion rot)
    {
        var container = new GameObject($"LoosePart_{data.PartID}");
        container.transform.SetPositionAndRotation(pos, rot);

        var visual = Instantiate(data.VisualPrefab, container.transform);
        visual.transform.SetLocalPositionAndRotation(Vector3.zero, Quaternion.identity);
        foreach (var c in visual.GetComponentsInChildren<Collider>())
            c.enabled = false;

        var col = container.AddComponent<BoxCollider>();
        var renderers = visual.GetComponentsInChildren<Renderer>();
        FitBoxCollider(col, renderers);

        var rb = container.AddComponent<Rigidbody>();
        rb.mass = data.Mass;

        var wp = container.AddComponent<WorkshopPart>();
        wp.PartData = data;
        wp.TargetAssembly = TargetAssembly;
        wp.Slot = WorkshopSlot;
        wp.DragArea = SpawnArea;
        wp.SetLoose();
    }

    public void ClearParts()
    {
        var all = FindObjectsByType<WorkshopPart>(FindObjectsSortMode.None);
        foreach (var p in all)
            Destroy(p.gameObject);
    }

    // ─── 工具方法 ──────────────────────────────────────────────────────────────

    /// <summary>
    /// 寻找让整个碰撞体不超出 SpawnArea 且不进入 WorkshopSlot 的位置。
    /// halfExtents 是零件碰撞体的世界 AABB 半尺寸。
    /// </summary>
    private Vector3? GetSpawnPosOutsideSlot(Vector3 halfExtents)
    {
        if (SpawnArea == null) return null;

        var bounds = SpawnArea.bounds;
        var slotCol = WorkshopSlot != null ? WorkshopSlot.GetComponent<BoxCollider>() : null;

        // 收缩有效范围，让碰撞体边缘也落在 SpawnArea 内
        float minX = bounds.min.x + halfExtents.x;
        float maxX = bounds.max.x - halfExtents.x;
        float minZ = bounds.min.z + halfExtents.z;
        float maxZ = bounds.max.z - halfExtents.z;

        if (minX >= maxX || minZ >= maxZ)
        {
            Debug.LogWarning("WorkshopPenSpawner: 零件过大，无法完整放入 SpawnArea");
            return null;
        }

        for (int i = 0; i < 30; i++)
        {
            var pos = new Vector3(
                Random.Range(minX, maxX),
                bounds.min.y,
                Random.Range(minZ, maxZ)
            );

            // 以碰撞体边缘判断是否与改装区重叠（AABB vs AABB）
            if (slotCol != null)
            {
                var sb = slotCol.bounds;
                bool overlaps = pos.x + halfExtents.x > sb.min.x
                             && pos.x - halfExtents.x < sb.max.x
                             && pos.z + halfExtents.z > sb.min.z
                             && pos.z - halfExtents.z < sb.max.z;
                if (overlaps) continue;
            }

            return pos;
        }

        Debug.LogWarning("WorkshopPenSpawner: 无法找到改装区外的合法生成位置，请检查 SpawnArea 和 WorkshopSlot 的大小");
        return null;
    }

    /// <summary>
    /// 暂时置零旋转，从 Renderer 包围盒精确拟合 BoxCollider。
    /// </summary>
    private static void FitBoxCollider(BoxCollider col, Renderer[] renderers)
    {
        if (renderers.Length == 0) { col.size = Vector3.one * 0.08f; return; }

        var savedRot = col.transform.rotation;
        col.transform.rotation = Quaternion.identity;
        var lb = renderers[0].bounds;
        foreach (var r in renderers) lb.Encapsulate(r.bounds);
        col.center = col.transform.InverseTransformPoint(lb.center);
        col.size = lb.size;
        col.transform.rotation = savedRot;
    }

#if UNITY_EDITOR
    private void OnDrawGizmos()
    {
        // 生成区域（蓝色）
        if (SpawnArea != null)
        {
            Gizmos.color = new Color(0f, 0.5f, 1f, 0.15f);
            Gizmos.DrawCube(SpawnArea.bounds.center, SpawnArea.bounds.size);
            Gizmos.color = new Color(0f, 0.5f, 1f, 0.8f);
            Gizmos.DrawWireCube(SpawnArea.bounds.center, SpawnArea.bounds.size);
        }

        // 排除区域（红色）
        if (WorkshopSlot != null)
        {
            var slotCol = WorkshopSlot.GetComponent<BoxCollider>();
            if (slotCol != null)
            {
                Gizmos.color = new Color(1f, 0.2f, 0.2f, 0.15f);
                Gizmos.DrawCube(slotCol.bounds.center, slotCol.bounds.size);
                Gizmos.color = new Color(1f, 0.2f, 0.2f, 0.8f);
                Gizmos.DrawWireCube(slotCol.bounds.center, slotCol.bounds.size);
            }
        }
    }
#endif
}
