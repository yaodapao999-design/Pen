using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 在改装场景抽屉里生成散落的零件。
/// SpawnArea 用 BoxCollider 定义区域，WorkshopSlot 范围内不生成。
/// 生成时保证整个碰撞体不超出 SpawnArea、不进入 WorkshopSlot。
/// </summary>
public class WorkshopPenSpawner : MonoBehaviour
{
    [Header("生成配置")]
    public BoxCollider SpawnArea;
    public WorkshopSlot WorkshopSlot;

    /// <summary>
    /// 当前要生成的散落零件列表。仅运行时存在，数据源是 PlayerInventory —
    /// GameManager 在进入 Workshop 前调 SetAvailableParts(Inventory.GetAll()) 注入，
    /// 商店购买的零件在下次打开抽屉时自动随仓库同步出现。
    /// </summary>
    [System.NonSerialized]
    public List<PenPartData> AvailableParts = new List<PenPartData>();

    /// <summary>记忆上次退出改装时散落零件的位姿，按 PenPartData 分组（允许重复）；下次打开抽屉从队列取用</summary>
    private readonly Dictionary<PenPartData, Queue<(Vector3 pos, Quaternion rot)>> _memorizedPoses = new();

    /// <summary>由 GameManager 在进入 Workshop 前注入玩家库存（覆盖当前列表）</summary>
    public void SetAvailableParts(System.Collections.Generic.IEnumerable<PenPartData> parts)
    {
        AvailableParts.Clear();
        if (parts == null) return;
        foreach (var p in parts)
            if (p != null) AvailableParts.Add(p);
    }

    /// <summary>
    /// 首次或库存新增的零件随机散布；已记忆位置的用原位，实现"第一次散落、之后原位"的视觉记忆。
    /// </summary>
    public void SpawnParts()
    {
        if (AvailableParts == null) return;

        // 拷贝一份记忆，每 spawn 一件消耗一条匹配记录，避免重复使用
        var working = new Dictionary<PenPartData, Queue<(Vector3, Quaternion)>>();
        foreach (var kv in _memorizedPoses)
            working[kv.Key] = new Queue<(Vector3, Quaternion)>(kv.Value);

        foreach (var partData in AvailableParts)
        {
            if (partData == null || partData.VisualPrefab == null) continue;

            bool tryMemory = working.TryGetValue(partData, out var memQueue) && memQueue.Count > 0;

            bool useMemory = false;
            Vector3 memoryPos = default;
            Quaternion memoryRot = default;
            if (tryMemory)
            {
                var next = memQueue.Peek();
                if (IsPoseInsideSpawnArea(next.Item1))
                {
                    useMemory = true;
                    memoryPos = next.Item1;
                    memoryRot = next.Item2;
                    memQueue.Dequeue();
                }
                else
                {
                    memQueue.Dequeue(); // 无效记忆丢弃
                }
            }

            Quaternion rot = useMemory
                ? memoryRot
                : Quaternion.Euler(0, Random.Range(0f, 360f), 0);

            var wp = WorkshopPartFactory.Create(
                partData, Vector3.zero, rot,
                WorkshopSlot, SpawnArea);

            Vector3 placement;
            if (useMemory)
            {
                placement = memoryPos;
            }
            else
            {
                var col = wp.GetComponent<BoxCollider>();
                var random = GetSpawnPosOutsideSlot(col.bounds.extents);
                if (!random.HasValue) { Destroy(wp.gameObject); continue; }
                placement = random.Value;
            }

            wp.transform.position = placement;

            if (!useMemory)
            {
                // 首次散落才贴地（记忆位置已在合理高度）
                var renderers = wp.GetComponentsInChildren<Renderer>();
                if (renderers.Length > 0)
                {
                    var wb = renderers[0].bounds;
                    foreach (var r in renderers) wb.Encapsulate(r.bounds);
                    wp.transform.position += Vector3.up * (placement.y - wb.min.y);
                }
            }

            wp.SetLoose();
        }
    }

    /// <summary>退出改装前调用：快照当前所有 Loose 散落零件的位姿，下次打开复原。</summary>
    public void MemorizeLoosePoses()
    {
        _memorizedPoses.Clear();
        var registry = WorkshopPartRegistry.Instance;
        if (registry == null) return;
        foreach (var wp in registry.All)
        {
            if (wp == null || wp.PartData == null) continue;
            if (wp.State != WorkshopPart.PartState.Loose) continue;
            if (!_memorizedPoses.TryGetValue(wp.PartData, out var q))
            {
                q = new Queue<(Vector3, Quaternion)>();
                _memorizedPoses[wp.PartData] = q;
            }
            q.Enqueue((wp.transform.position, wp.transform.rotation));
        }
    }

    /// <summary>记忆位置合法性：防止零件曾滚出抽屉 / 在 Y 异常处被记住</summary>
    private bool IsPoseInsideSpawnArea(Vector3 pos)
    {
        if (SpawnArea == null) return true;
        var b = SpawnArea.bounds;
        return pos.x >= b.min.x && pos.x <= b.max.x
            && pos.z >= b.min.z && pos.z <= b.max.z
            && pos.y >= b.min.y - 0.5f && pos.y <= b.max.y + 1f;
    }

    /// <summary>在指定位置生成散落零件（不做区域校验，由调用方确定位置）</summary>
    public void SpawnLoosePart(PenPartData data, Vector3 pos, Quaternion rot)
    {
        var wp = WorkshopPartFactory.Create(data, pos, rot, WorkshopSlot, SpawnArea);
        wp.SetLoose();
    }

    public void ClearParts()
    {
        if (WorkshopPartRegistry.Instance == null) return;
        var all = new List<WorkshopPart>(WorkshopPartRegistry.Instance.All);
        foreach (var p in all)
            Destroy(p.gameObject);
    }

    // ─── 位置计算 ─────────────────────────────────────────────────────────────

    private Vector3? GetSpawnPosOutsideSlot(Vector3 halfExtents)
    {
        if (SpawnArea == null) return null;

        var bounds = SpawnArea.bounds;
        var slotCol = WorkshopSlot != null ? WorkshopSlot.GetComponent<BoxCollider>() : null;

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
                Random.Range(minZ, maxZ));

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

        Debug.LogWarning("WorkshopPenSpawner: 无法找到改装区外的合法生成位置");
        return null;
    }

#if UNITY_EDITOR
    private void OnDrawGizmos()
    {
        if (SpawnArea != null)
        {
            Gizmos.color = new Color(0f, 0.5f, 1f, 0.15f);
            Gizmos.DrawCube(SpawnArea.bounds.center, SpawnArea.bounds.size);
            Gizmos.color = new Color(0f, 0.5f, 1f, 0.8f);
            Gizmos.DrawWireCube(SpawnArea.bounds.center, SpawnArea.bounds.size);
        }

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
