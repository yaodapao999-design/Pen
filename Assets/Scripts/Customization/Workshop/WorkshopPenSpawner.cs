using UnityEngine;

/// <summary>
/// 在改装场景抽屉里生成散落的零件
/// 挂在场景管理器上，指定生成区域和可用零件列表
/// </summary>
public class WorkshopPenSpawner : MonoBehaviour
{
    [Header("生成配置")]
    public PenPartData[] AvailableParts;   // 本局可用的零件
    public Transform SpawnArea;            // 抽屉区域中心
    public Vector2 SpawnExtents = new(0.3f, 0.15f);  // 随机散落范围 (x, z)

    [Header("引用")]
    public PenAssembly TargetAssembly;

    /// <summary>生成本局零件，随机散落在抽屉里</summary>
    public void SpawnParts()
    {
        if (AvailableParts == null) return;

        foreach (var partData in AvailableParts)
        {
            if (partData == null || partData.VisualPrefab == null) continue;

            var pos = SpawnArea.position + new Vector3(
                Random.Range(-SpawnExtents.x, SpawnExtents.x),
                0f,
                Random.Range(-SpawnExtents.y, SpawnExtents.y)
            );

            // 生成一个容器，不直接用 VisualPrefab 避免影响笔的 Compound Collider
            var container = new GameObject($"DraggablePart_{partData.PartID}");
            container.transform.position = pos;

            // 视觉子物体
            var visual = Instantiate(partData.VisualPrefab, container.transform);
            visual.transform.localPosition = Vector3.zero;
            visual.transform.localRotation = Quaternion.identity;

            // 确保容器有 Collider 供 OnMouseDown 检测
            if (container.GetComponent<Collider>() == null)
            {
                var col = container.AddComponent<BoxCollider>();
                col.size = Vector3.one * 0.1f;
            }

            var draggable = container.AddComponent<DraggablePart>();
            draggable.PartData = partData;
            draggable.TargetAssembly = TargetAssembly;
        }
    }

    /// <summary>清除所有散落零件（改装完成后调用）</summary>
    public void ClearParts()
    {
        var all = FindObjectsByType<DraggablePart>(FindObjectsSortMode.None);
        foreach (var d in all)
            Destroy(d.gameObject);
    }
}
