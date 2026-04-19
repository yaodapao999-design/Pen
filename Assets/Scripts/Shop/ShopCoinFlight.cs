using System.Collections;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 金币飞行反馈——N 个小金币 UI 沿 Bezier 从起点屏幕位置飞到终点屏幕位置。
///
/// 架构：Screen Space Overlay 上的 Image，位置用屏幕像素坐标。调用方决定方向语义：
///   - 花钱（购买）：start = HUD 钱包位置，end = 商品购买屏幕位置（"钱飞出去"）
///   - 收钱（奖励）：start = 奖励世界位置投屏，end = HUD 钱包位置（"钱飞进来"）
/// Bezier 控制点按起→终向量的垂直 + 向上偏移，产生飘浮弧线。
/// 每个金币独立协程，stagger 延迟、速度/曲率抖动、旋转，避免"一队齐飞"的机械感。
/// 尺寸包络：起点小 → 中段满 → 末端缩，前者像"从钱包弹出"、后者像"被商品吸入"。
/// </summary>
public class ShopCoinFlight : MonoBehaviour
{
    /// <summary>在 Canvas 上生成 count 个金币，从 startScreen 飞到 endScreen（屏幕像素坐标）。</summary>
    public static void Spawn(int count, Vector2 startScreen, Vector2 endScreen,
                              Canvas uiCanvas, Sprite coinSprite)
    {
        if (count <= 0 || uiCanvas == null) return;
        for (int i = 0; i < count; i++)
            SpawnOne(uiCanvas, startScreen, endScreen, i, coinSprite);
    }

    private static void SpawnOne(Canvas canvas, Vector2 startScreen, Vector2 endScreen,
                                  int index, Sprite sprite)
    {
        var go = new GameObject($"FlyingCoin_{index}");
        go.transform.SetParent(canvas.transform, worldPositionStays: false);

        var rt = go.AddComponent<RectTransform>();
        rt.sizeDelta = new Vector2(28f, 28f);
        rt.position = new Vector3(startScreen.x, startScreen.y, 0f);
        rt.localScale = Vector3.one * 0.4f; // 起点小，飞出时放大

        var img = go.AddComponent<Image>();
        if (sprite != null) img.sprite = sprite;
        img.color = new Color(1f, 0.84f, 0.3f, 1f);
        img.raycastTarget = false;
        img.preserveAspect = true;

        var flight = go.AddComponent<ShopCoinFlight>();
        flight.StartCoroutine(flight.FlyRoutine(rt, startScreen, endScreen, index));
    }

    private IEnumerator FlyRoutine(RectTransform rt, Vector2 start, Vector2 end, int index)
    {
        float delay = index * 0.04f;
        if (delay > 0f) yield return new WaitForSeconds(delay);

        float duration = 0.55f + Random.Range(-0.08f, 0.08f);

        // Bezier 控制点：垂直 + 向上随机偏移
        Vector2 dir = end - start;
        Vector2 perp = new Vector2(-dir.y, dir.x).normalized;
        Vector2 control = (start + end) * 0.5f
                        + perp * Random.Range(80f, 200f) * (Random.value < 0.5f ? -1f : 1f)
                        + Vector2.up * Random.Range(60f, 140f);

        float t = 0f;
        float rotSpeed = Random.Range(540f, 900f) * (Random.value < 0.5f ? -1f : 1f);
        while (t < 1f)
        {
            t += Time.deltaTime / duration;
            float s = Mathf.Clamp01(t);
            Vector2 p = Mathf.Pow(1 - s, 2) * start + 2 * (1 - s) * s * control + s * s * end;
            rt.position = new Vector3(p.x, p.y, 0f);
            rt.Rotate(0f, 0f, rotSpeed * Time.deltaTime);

            // 尺寸包络：0~15% 从 0.4 膨胀到 1.0；85~100% 从 1.0 收缩到 0.2
            float scale;
            if (s < 0.15f) scale = Mathf.Lerp(0.4f, 1f, s / 0.15f);
            else if (s > 0.85f) scale = Mathf.Lerp(1f, 0.2f, (s - 0.85f) / 0.15f);
            else scale = 1f;
            rt.localScale = Vector3.one * scale;

            yield return null;
        }
        Destroy(gameObject);
    }
}
