using UnityEngine;

public class PenEntity : MonoBehaviour
{
    [Header("弹射参数")]
    public float baseForce = 10f;
    public float stopVelocityThreshold = 0.05f;
    public float stopAngularThreshold = 0.05f;
    public float stopCheckDelay = 0.5f;
    public float maxDragDistance = 2.0f;

    [Header("掉落检测")]
    public float fallYThreshold = -2f;

    public Rigidbody rb { get; private set; }
    public CapsuleCollider penCollider => Assembly != null ? Assembly.BarrelCollider : null;
    public PenAssembly Assembly { get; private set; }
    public bool HasFallen => rb != null && rb.position.y < fallYThreshold;

    private void Awake()
    {
        rb = GetComponent<Rigidbody>();
        Assembly = GetComponent<PenAssembly>();
    }

    /// <summary>施加弹射冲量，弹射力受装配部件倍率影响</summary>
    public void Launch(Vector3 direction, float force, float contactOffset)
    {
        rb.linearVelocity = Vector3.zero;
        rb.angularVelocity = Vector3.zero;

        float launchMultiplier = Assembly != null ? Assembly.GetLaunchMultiplier() : 1f;

        Vector3 penAxis = GetPenAxis();
        float halfHeight = (penCollider.height / 2f) - penCollider.radius;
        Vector3 forcePosition = rb.worldCenterOfMass + penAxis * (contactOffset * halfHeight);

        rb.AddForceAtPosition(direction * (baseForce * force * launchMultiplier), forcePosition, ForceMode.Impulse);
    }

    /// <summary>是否已停稳</summary>
    public bool IsStopped()
    {
        return rb.linearVelocity.magnitude < stopVelocityThreshold &&
               rb.angularVelocity.magnitude < stopAngularThreshold;
    }

    /// <summary>强制停止</summary>
    public void Stop()
    {
        rb.linearVelocity = Vector3.zero;
        rb.angularVelocity = Vector3.zero;
    }

    public Vector3 GetPenAxis()
    {
        return penCollider.direction switch
        {
            0 => transform.right,
            1 => transform.up,
            2 => transform.forward,
            _ => transform.right
        };
    }

    public float GetOffsetAlongCapsuleAxis(Vector3 localPoint)
    {
        return penCollider.direction switch
        {
            0 => localPoint.x,
            1 => localPoint.y,
            2 => localPoint.z,
            _ => localPoint.y
        };
    }
}