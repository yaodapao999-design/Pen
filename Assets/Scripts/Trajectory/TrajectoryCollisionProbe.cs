using UnityEngine;

/// <summary>
/// 挂在镜像玩家笔 Rigidbody 的 GameObject 上。监听 `OnCollisionEnter`,
/// 当 `collision.rigidbody == watchedRigidbody`(镜像敌方笔 rb) 时记录首次碰撞时刻。
/// Simulator 在步进循环中读取 `HasCollided`,首次发现时记录 CollisionTime 与玩家位姿,
/// 并在每次 Simulate 开始时调用 `ResetProbe()` 清状态。
/// 注意:方法名 `ResetProbe` 而非 `Reset`,避免与 Unity 内置 `MonoBehaviour.Reset`(Inspector 按钮) 冲突。
/// </summary>
public class TrajectoryCollisionProbe : MonoBehaviour
{
    [HideInInspector] public Rigidbody watchedRigidbody;

    public bool HasCollided { get; private set; }

    public void ResetProbe()
    {
        HasCollided = false;
    }

    private void OnCollisionEnter(Collision collision)
    {
        if (HasCollided) return;
        if (watchedRigidbody == null) return;
        if (collision.rigidbody == watchedRigidbody)
            HasCollided = true;
    }
}
