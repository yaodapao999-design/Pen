using UnityEngine;
using UnityEngine.Events;

/// <summary>
/// 出界检测器：挂在笔上，负责连续帧确认 + 触发事件 + 渲染层切换
/// 判定逻辑委托给 FallOffCheck 静态工具类
/// </summary>
public class FallOffDetector : MonoBehaviour
{
    [Header("判定参数")]
    public LayerMask tableLayer;
    public float raycastDistance = 5f;
    public float fallVelocityThreshold = -0.5f;
    public int requiredFrames = 5;

    [Header("渲染")]
    public string fallingLayerName = "Background";

    [Header("事件")]
    public UnityEvent OnFellOff;

    private Rigidbody rb;
    private Renderer[] penRenderers;
    private float initialY;
    private int offTableFrames;
    private bool hasFallen;

    public bool HasFallen => hasFallen;

    private void Awake()
    {
        rb = GetComponent<Rigidbody>();
        penRenderers = GetComponentsInChildren<Renderer>();
    }

    private void Start()
    {
        initialY = rb.worldCenterOfMass.y;
    }

    private void FixedUpdate()
    {
        if (hasFallen) return;

        if (FallOffCheck.CheckFallOff(
                rb.worldCenterOfMass, rb.linearVelocity, initialY,
                raycastDistance, fallVelocityThreshold, tableLayer))
        {
            offTableFrames++;
            if (offTableFrames >= requiredFrames)
                TriggerFallOff();
        }
        else
        {
            offTableFrames = 0;
        }
    }

    private void TriggerFallOff()
    {
        hasFallen = true;
        foreach (var r in penRenderers)
            r.sortingLayerName = fallingLayerName;
        OnFellOff?.Invoke();
    }

    public void ResetDetector(string normalLayerName = "Pen")
    {
        hasFallen = false;
        offTableFrames = 0;
        foreach (var r in penRenderers)
            r.sortingLayerName = normalLayerName;
    }
}