using System.Collections;
using UnityEngine;

/// <summary>
/// 通用"弹入"动效：localScale 从 0 → homeScale，EaseOutBack 回冲 overshoot。
///
/// 用法：任何 MonoBehaviour 作 host 即可跑协程——
///   <c>IntroPopIn.PlayOn(this, target, delay: 0.1f, homeScale: Vector3.one);</c>
///
/// 特性：
///   - 立即把 target.localScale 置 0（避免首帧闪全尺寸）
///   - delay 期间也保持 scale=0
///   - 目标被销毁时协程自动 early-return，不会报错
/// </summary>
public static class IntroPopIn
{
    public static void PlayOn(MonoBehaviour host, Transform target, float delay, Vector3 homeScale,
                               float duration = 0.4f, float overshoot = 0.30f)
    {
        if (host == null || target == null) return;
        target.localScale = Vector3.zero;
        host.StartCoroutine(Routine(target, delay, homeScale, duration, overshoot));
    }

    private static IEnumerator Routine(Transform t, float delay, Vector3 home, float duration, float overshoot)
    {
        if (delay > 0f) yield return new WaitForSeconds(delay);

        float k = 0f;
        while (k < 1f)
        {
            k += Time.deltaTime / Mathf.Max(0.01f, duration);
            if (t == null) yield break;
            float s = EaseOutBack(Mathf.Clamp01(k), overshoot);
            t.localScale = home * Mathf.Max(0f, s);
            yield return null;
        }
        if (t != null) t.localScale = home;
    }

    private static float EaseOutBack(float x, float overshoot)
    {
        float c1 = 1.70158f * (overshoot / 0.25f);
        float c3 = c1 + 1f;
        float u = x - 1f;
        return 1f + c3 * u * u * u + c1 * u * u;
    }
}
