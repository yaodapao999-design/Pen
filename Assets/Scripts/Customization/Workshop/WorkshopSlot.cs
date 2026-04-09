using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 改装台触发器
/// - 仅在 workshop 激活期间响应触发事件（通过 Activate/Deactivate 控制）
/// - 笔杆完全离开改装区时，所有装配件脱落变 Loose
/// - Contains()：供 WorkshopPart 判断松手时是否在改装区内
/// </summary>
[RequireComponent(typeof(BoxCollider))]
public class WorkshopSlot : MonoBehaviour
{
    [Header("引用")]
    public PenAssembly TargetAssembly;
    public BoxCollider DragArea;

    private BoxCollider _col;
    private bool _isActive;

    private void Awake()
    {
        _col = GetComponent<BoxCollider>();
        _col.isTrigger = true;
    }

    /// <summary>进入改装模式时由 WorkshopController 调用，启用触发器响应</summary>
    public void Activate() => _isActive = true;

    /// <summary>离开改装模式时由 WorkshopController 调用，关闭触发器响应，防止战斗中误触发</summary>
    public void Deactivate() => _isActive = false;

    /// <summary>判断世界坐标点是否在改装区内</summary>
    public bool Contains(Vector3 worldPos)
    {
        return _col.bounds.Contains(worldPos);
    }

    private void OnTriggerExit(Collider other)
    {
        if (!_isActive) return;
        if (TargetAssembly == null) return;
        if (other.gameObject != TargetAssembly.gameObject &&
            !other.transform.IsChildOf(TargetAssembly.transform)) return;

        DisassembleAll();
    }

    private void DisassembleAll()
    {
        if (TargetAssembly == null) return;

        // 先销毁所有处于 Assembled 状态的 WorkshopPart 容器，避免残留孤立可交互件
        var wps = FindObjectsByType<WorkshopPart>(FindObjectsSortMode.None);
        foreach (var wp in wps)
        {
            if (wp.State == WorkshopPart.PartState.Assembled)
                Destroy(wp.gameObject);
        }

        // 将装配列表中的所有零件变为 Loose 散落件
        var parts = new List<PenPartInstance>(TargetAssembly.Parts);
        foreach (var part in parts)
        {
            // 使用 socket 位置（内部 GO 可能已被隐藏，但位置与 socket 一致）
            var pos = part.AttachedSocket != null
                ? part.AttachedSocket.transform.position
                : part.GameObject.transform.position;
            var rot = part.AttachedSocket != null
                ? part.AttachedSocket.transform.rotation
                : part.GameObject.transform.rotation;

            TargetAssembly.RemovePart(part);
            SpawnLoosePart(part.Data, pos, rot);
        }
    }

    private void SpawnLoosePart(PenPartData data, Vector3 pos, Quaternion rot)
    {
        if (data.VisualPrefab == null) return;

        var container = new GameObject($"LoosePart_{data.PartID}");
        container.transform.SetPositionAndRotation(pos, rot);

        var visual = Instantiate(data.VisualPrefab, container.transform);
        visual.transform.SetLocalPositionAndRotation(Vector3.zero, Quaternion.identity);
        foreach (var c in visual.GetComponentsInChildren<Collider>())
            c.enabled = false;

        var col = container.AddComponent<BoxCollider>();
        var renderers = visual.GetComponentsInChildren<Renderer>();
        if (renderers.Length > 0)
        {
            var savedRot = container.transform.rotation;
            container.transform.rotation = Quaternion.identity;
            var lb = renderers[0].bounds;
            foreach (var r in renderers) lb.Encapsulate(r.bounds);
            col.center = container.transform.InverseTransformPoint(lb.center);
            col.size = lb.size;
            container.transform.rotation = savedRot;
        }
        else
        {
            col.size = Vector3.one * 0.08f;
        }

        var rb = container.AddComponent<Rigidbody>();
        rb.mass = data.Mass;

        var wp = container.AddComponent<WorkshopPart>();
        wp.PartData = data;
        wp.TargetAssembly = TargetAssembly;
        wp.Slot = this;
        wp.DragArea = DragArea;
        wp.SetLoose();
    }

#if UNITY_EDITOR
    private void OnDrawGizmos()
    {
        var col = GetComponent<BoxCollider>();
        if (col == null) return;
        Gizmos.matrix = transform.localToWorldMatrix;
        Gizmos.color = new Color(0f, 1f, 0.5f, 0.15f);
        Gizmos.DrawCube(col.center, col.size);
        Gizmos.color = new Color(0f, 1f, 0.5f, 0.8f);
        Gizmos.DrawWireCube(col.center, col.size);
    }
#endif
}
