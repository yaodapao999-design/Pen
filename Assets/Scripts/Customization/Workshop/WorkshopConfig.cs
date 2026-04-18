using UnityEngine;

/// <summary>
/// 改装系统全局配置。
/// 挂在 WorkshopController 同一个 GameObject 上，直接在 Inspector 调参。
/// 通过静态 Instance 访问，不需要手动赋值。
/// </summary>
public class WorkshopConfig : MonoBehaviour
{
    public static WorkshopConfig Instance { get; private set; }

    [Header("吸附")]
    public float SnapDistance = 0.15f;
    public float SnapDuration = 0.12f;
    [Range(0f, 1f)] public float MagnetStrength = 0.3f;

    [Header("拖拽")]
    public float DragLiftHeight = 0.03f;
    public float DragDamping = 50f;

    [Header("动画")]
    public float BounceDuration = 0.08f;
    public float BounceScale = 1.05f;
    public float InvalidShakeDuration = 0.2f;
    public float InvalidShakeIntensity = 0.02f;

    [Header("散落")]
    [Tooltip("拖拽笔杆时子零件被弹出的初速度 (m/s)。ForceMode.VelocityChange，与质量无关：0.3=温柔 0.6=适中 1.0=明显 2.0+=爆射")]
    public float ScatterForce = 0.3f;

    [Header("抽屉开门惯性")]
    [Tooltip("抽屉打开到位瞬间，散落零件沿抽屉运动方向获得的初速度 (m/s)，模拟'抽屉停了零件因惯性继续前冲'。0=关闭效果")]
    public float DrawerOpenInertia = 0.8f;

    [Header("音效")]
    public AudioClip SnapSound;
    public AudioClip EjectSound;
    public AudioClip InvalidSound;

    private void Awake()
    {
        Instance = this;
    }
}
