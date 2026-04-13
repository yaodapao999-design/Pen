using System.Collections;
using UnityEngine;

/// <summary>
/// 商店笔记本的入/退场动画：沿本地 Z 轴滑入/滑出。
///
/// 用法：挂在 ShopBook 根物体上，ShopController 的 Enter/Exit 通过协程等待它完成。
/// 本组件只管视觉；显隐由 ShopController.ShopRoot 控制。
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(Rigidbody))]
public class ShopBookAnimator : MonoBehaviour
{
    [Header("位置（本地坐标）")]
    [Tooltip("入场终点 Z：Enter 结束时的本地 Z")]
    [SerializeField] private float _enteredZ = 0f;
    [Tooltip("退场终点 Z：Exit 结束时/入场起始的本地 Z")]
    [SerializeField] private float _exitedZ = 8f;

    [Header("时长")]
    [SerializeField] private float _enterDuration = 0.35f;
    [SerializeField] private float _exitDuration = 0.3f;

    [Header("曲线")]
    [Tooltip("入场缓动曲线（推荐 EaseOutCubic：快→慢）")]
    [SerializeField] private AnimationCurve _enterCurve = new AnimationCurve(
        new Keyframe(0f, 0f, 0f, 2f), new Keyframe(1f, 1f, 0f, 0f));
    [Tooltip("退场缓动曲线（推荐 EaseInCubic：慢→快）")]
    [SerializeField] private AnimationCurve _exitCurve = new AnimationCurve(
        new Keyframe(0f, 0f, 0f, 0f), new Keyframe(1f, 1f, 2f, 0f));

    private Rigidbody _rb;

    private void Awake()
    {
        _rb = GetComponent<Rigidbody>();
        _rb.isKinematic = true;
        _rb.useGravity = false;
        _rb.interpolation = RigidbodyInterpolation.Interpolate;
        _rb.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;
    }

    /// <summary>瞬间归位到退场位置（进入前调用，避免闪现在入场位）</summary>
    public void SnapToExited()
    {
        SnapToLocalZ(_exitedZ);
    }

    /// <summary>瞬间归位到入场位置</summary>
    public void SnapToEntered()
    {
        SnapToLocalZ(_enteredZ);
    }

    public IEnumerator PlayEnter()
    {
        yield return PlayTween(_exitedZ, _enteredZ, _enterDuration, _enterCurve);
    }

    public IEnumerator PlayExit()
    {
        yield return PlayTween(GetLocalZ(), _exitedZ, _exitDuration, _exitCurve);
    }

    /// <summary>
    /// 物理驱动的 Z 轴 Tween：用 Rigidbody.MovePosition 在 FixedUpdate 步长推进，
    /// 这样 Kinematic RB 能正确推开路径上的 Dynamic 刚体（把桌上的笔撞飞）。
    /// </summary>
    private IEnumerator PlayTween(float fromZ, float toZ, float duration, AnimationCurve curve)
    {
        SnapToLocalZ(fromZ);
        float t = 0f;
        float d = Mathf.Max(0.01f, duration);
        var wait = new WaitForFixedUpdate();
        while (t < 1f)
        {
            t += Time.fixedDeltaTime / d;
            float k = curve != null ? curve.Evaluate(Mathf.Clamp01(t)) : Mathf.Clamp01(t);
            float z = Mathf.LerpUnclamped(fromZ, toZ, k);
            MoveToLocalZ(z);
            yield return wait;
        }
        MoveToLocalZ(toZ);
    }

    private float GetLocalZ() => transform.localPosition.z;

    private Vector3 LocalToWorld(Vector3 local)
    {
        return transform.parent != null ? transform.parent.TransformPoint(local) : local;
    }

    private void MoveToLocalZ(float z)
    {
        var local = transform.localPosition;
        local.z = z;
        _rb.MovePosition(LocalToWorld(local));
    }

    private void SnapToLocalZ(float z)
    {
        var local = transform.localPosition;
        local.z = z;
        transform.localPosition = local;
        _rb.position = transform.position;
    }
}
