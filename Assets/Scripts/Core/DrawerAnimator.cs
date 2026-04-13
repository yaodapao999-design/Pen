using System.Collections;
using UnityEngine;

/// <summary>
/// 通用抽屉动画：Z 轴开/关 + 瞬时归位。
/// 挂在抽屉模型上，作为 ShopDrawer / WorkshopController 共享的单一动画源。
///
/// 设计原则：只管 Z 轴 localPosition 的动画；不管落点感应、不管零件跟随。
/// 使用方：
///   - ShopDrawer：组合 DrawerAnimator + DropZone 感应
///   - WorkshopController：调用 Open/Close，然后在自己的协程里按世界位移带动零件
/// </summary>
public class DrawerAnimator : MonoBehaviour
{
    [Header("抽屉模型")]
    public Transform DrawerTransform;

    [Tooltip("打开时沿本地 Z 的位移量")]
    public float OpenOffset;

    [Tooltip("开/关动画时长（秒）")]
    public float AnimDuration = 0.5f;

    private float _closedZ;
    private Coroutine _co;
    private bool _isOpen;

    public bool IsOpen => _isOpen;
    public bool IsAnimating => _co != null;
    public float ClosedZ => _closedZ;
    public float OpenZ => _closedZ + OpenOffset;

    private void Awake()
    {
        if (DrawerTransform != null)
            _closedZ = DrawerTransform.localPosition.z;
    }

    public void Open() => StartAnim(OpenZ, true);
    public void Close() => StartAnim(_closedZ, false);

    /// <summary>无动画瞬间归位到关闭状态</summary>
    public void SnapClosed()
    {
        if (_co != null) { StopCoroutine(_co); _co = null; }
        if (DrawerTransform == null) return;
        var lp = DrawerTransform.localPosition;
        lp.z = _closedZ;
        DrawerTransform.localPosition = lp;
        _isOpen = false;
    }

    private void StartAnim(float targetZ, bool willBeOpen)
    {
        if (DrawerTransform == null) return;
        _isOpen = willBeOpen;
        if (_co != null) StopCoroutine(_co);
        _co = StartCoroutine(Animate(targetZ));
    }

    private IEnumerator Animate(float targetZ)
    {
        float startZ = DrawerTransform.localPosition.z;
        float t = 0f;
        while (t < 1f)
        {
            t += Time.deltaTime / Mathf.Max(0.01f, AnimDuration);
            float s = Mathf.SmoothStep(0f, 1f, t);
            var lp = DrawerTransform.localPosition;
            lp.z = Mathf.Lerp(startZ, targetZ, s);
            DrawerTransform.localPosition = lp;
            yield return null;
        }
        _co = null;
    }
}
