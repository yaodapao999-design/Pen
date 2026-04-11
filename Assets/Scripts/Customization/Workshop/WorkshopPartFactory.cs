using UnityEngine;

/// <summary>
/// WorkshopPart 容器的唯一创建入口。
/// 统一：创建 GameObject → 实例化视觉 → 拟合碰撞体 → 挂载 Rigidbody + WorkshopPart。
/// </summary>
public static class WorkshopPartFactory
{
    public static WorkshopPart Create(
        PenPartData data,
        Vector3 position,
        Quaternion rotation,
        WorkshopSlot slot,
        BoxCollider dragArea)
    {
        var container = new GameObject($"WP_{data.PartID}");
        container.transform.SetPositionAndRotation(position, rotation);

        // 视觉
        GameObject visual = null;
        if (data.VisualPrefab != null)
        {
            visual = Object.Instantiate(data.VisualPrefab, container.transform);
            visual.transform.SetLocalPositionAndRotation(Vector3.zero, Quaternion.identity);

            // 禁用视觉自带碰撞体，射线只命中容器的 BoxCollider
            foreach (var c in visual.GetComponentsInChildren<Collider>())
                c.enabled = false;
        }

        // 碰撞体（从 Renderer 包围盒拟合）
        var col = container.AddComponent<BoxCollider>();
        FitBoxCollider(col, visual);

        // 刚体（默认阻尼防止滑飞）
        var rb = container.AddComponent<Rigidbody>();
        rb.mass = data.Mass;
        rb.linearDamping = 2f;
        rb.angularDamping = 1f;

        // 交互脚本
        var wp = container.AddComponent<WorkshopPart>();
        wp.PartData = data;
        wp.Slot = slot;
        wp.DragArea = dragArea;

        return wp;
    }

    public static void FitBoxCollider(BoxCollider col, GameObject visual)
    {
        if (visual == null) { col.size = Vector3.one * 0.08f; return; }

        var renderers = visual.GetComponentsInChildren<Renderer>();
        if (renderers.Length == 0) { col.size = Vector3.one * 0.08f; return; }

        var savedRot = col.transform.rotation;
        col.transform.rotation = Quaternion.identity;
        var lb = renderers[0].bounds;
        foreach (var r in renderers) lb.Encapsulate(r.bounds);
        col.center = col.transform.InverseTransformPoint(lb.center);
        col.size = lb.size;
        col.transform.rotation = savedRot;
    }
}
