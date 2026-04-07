using UnityEngine;

/// <summary>
/// 运行时部件实例
/// 持有静态数据(PenPartData) + 实例化的 GameObject
/// </summary>
public class PenPartInstance
{
    public PenPartData Data { get; private set; }
    public GameObject GameObject { get; private set; }

    // 该部件插入的父级 socket
    public PartSocket AttachedSocket { get; private set; }

    public PenPartInstance(PenPartData data, GameObject gameObject)
    {
        Data = data;
        GameObject = gameObject;
    }

    /// <summary>将部件吸附到指定 socket</summary>
    public void AttachTo(PartSocket socket)
    {
        AttachedSocket = socket;
        socket.Attach(this);

        // 对齐到 socket 的位置和朝向
        GameObject.transform.SetParent(socket.transform, false);
        GameObject.transform.localPosition = Vector3.zero;
        GameObject.transform.localRotation = Quaternion.identity;
    }

    /// <summary>从当前 socket 拆下</summary>
    public void Detach()
    {
        if (AttachedSocket != null)
        {
            AttachedSocket.Detach();
            AttachedSocket = null;
        }

        GameObject.transform.SetParent(null);
    }

    public bool IsAttached => AttachedSocket != null;
}
