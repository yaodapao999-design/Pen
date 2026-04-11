using System.Collections;
using UnityEngine;

/// <summary>
/// RocketEffect 的运行时状态组件。
/// 由 RocketEffect.OnLaunch 按需创建，管理推力协程。
/// </summary>
public class RocketEffectRunner : MonoBehaviour
{
    private Coroutine _thrustCoroutine;

    public void StartThrust(Vector3 direction, float force, float duration, float liftRatio, Rigidbody rb)
    {
        if (_thrustCoroutine != null)
            StopCoroutine(_thrustCoroutine);
        _thrustCoroutine = StartCoroutine(ThrustRoutine(direction, force, duration, liftRatio, rb));
    }

    private IEnumerator ThrustRoutine(Vector3 direction, float force, float duration, float liftRatio, Rigidbody rb)
    {
        float elapsed = 0f;
        while (elapsed < duration)
        {
            elapsed += Time.fixedDeltaTime;
            Vector3 thrustDir = (direction + Vector3.up * liftRatio).normalized;
            rb.AddForce(thrustDir * force, ForceMode.Force);
            yield return new WaitForFixedUpdate();
        }
        _thrustCoroutine = null;
    }
}
