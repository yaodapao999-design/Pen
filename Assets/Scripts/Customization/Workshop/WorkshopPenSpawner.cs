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
    public PenPartData[] AvailableParts;
    public BoxCollider SpawnArea;
    public WorkshopSlot WorkshopSlot;


    public void SpawnParts()
    {
        if (AvailableParts == null) return;

        foreach (var partData in AvailableParts)
        {
            if (partData == null || partData.VisualPrefab == null) continue;

            var rot = Quaternion.Euler(0, Random.Range(0f, 360f), 0);

            var wp = WorkshopPartFactory.Create(
                partData, Vector3.zero, rot,
                WorkshopSlot, SpawnArea);

            // 用碰撞体世界 AABB 半尺寸寻找合法位置
            var col = wp.GetComponent<BoxCollider>();
            var pos = GetSpawnPosOutsideSlot(col.bounds.extents);
            if (!pos.HasValue) { Destroy(wp.gameObject); continue; }

            // 设置位置并贴地
            wp.transform.position = pos.Value;
            var renderers = wp.GetComponentsInChildren<Renderer>();
            if (renderers.Length > 0)
            {
                var wb = renderers[0].bounds;
                foreach (var r in renderers) wb.Encapsulate(r.bounds);
                wp.transform.position += Vector3.up * (pos.Value.y - wb.min.y);
            }

            wp.SetLoose();
        }
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
