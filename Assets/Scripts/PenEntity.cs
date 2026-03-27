using UnityEngine;

public class PenEntity : MonoBehaviour
{
    [Header("弹射参数")]
    public float baseForce = 10f;
    public float stopVelocityThreshold = 0.05f;
    public float stopAngularThreshold = 0.05f;
    public float stopCheckDelay = 0.5f;
    public float maxDragDistance = 2.0f;

    public Rigidbody rb { get; private set; }
    public CapsuleCollider penCollider { get; private set; }

    private void Awake()
    {
        rb = GetComponent<Rigidbody>();
        penCollider = GetComponent<CapsuleCollider>();
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