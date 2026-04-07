using UnityEngine;

/// <summary>
/// 连接点组件，挂在部件预制体上
/// 定义该连接点的类型、位置和当前是否被占用
/// </summary>
public class PartSocket : MonoBehaviour
{
    [Header("连接点配置")]
    public SocketType SocketType;

    // 当前插入此 socket 的部件实例（null = 空闲）
    public PenPartInstance OccupiedBy { get; private set; }

    public bool IsOccupied => OccupiedBy != null;

    public void Attach(PenPartInstance part)
    {
        OccupiedBy = part;
    }

    public void Detach()
    {
        OccupiedBy = null;
    }

    /// <summary>检查某个部件是否可以插入此 socket</summary>
    public bool CanAccept(PenPartData partData)
    {
        return !IsOccupied && partData.PlugsInto == SocketType;
    }

#if UNITY_EDITOR
    // 编辑器下可视化连接点位置
    private void OnDrawGizmos()
    {
        Gizmos.color = IsOccupied ? Color.red : Color.green;
        Gizmos.DrawWireSphere(transform.position, 0.05f);
        Gizmos.DrawLine(transform.position, transform.position + transform.forward * 0.1f);
    }
#endif
}
