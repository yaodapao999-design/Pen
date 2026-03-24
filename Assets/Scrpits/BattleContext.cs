using UnityEngine;

public class BattleContext 
{
    // 笔的引用
    public GameObject penObject;
    public CapsuleCollider penCollider;
    public Rigidbody penRigidbody;

    // 拖拽结果数据
    public Vector3 launchDirection;   // 弹射方向
    public float launchForce;         // 弹射力度 (0~1)
    
    // 点击点沿笔长轴相对于重心的偏移
    // 范围 -1 ~ 1，0 = 正中心，1 = 笔头，-1 = 笔尾
    // 这个值会直接影响 ActionState 中施加力矩的大小和方向
    public float contactOffset;

    public BattleContext(GameObject pen)
    {
        penObject = pen;
        penCollider = pen.GetComponent<CapsuleCollider>();
        penRigidbody = pen.GetComponent<Rigidbody>();
    }
}
