using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 世界空间浮动文字——购买点的 "-$5"、升级提示等短促反馈。
///
/// 行为：在指定世界坐标生成 → 上浮 0.4 米 + 透明度 1→0（0.8 秒）→ 自毁。
/// billboard 朝向相机，读起来始终正。
/// </summary>
public class ShopFloatingText : MonoBehaviour
{
    private TMP_Text _tmp;
    private Camera _cam;

    public static ShopFloatingText Spawn(Transform parent, Vector3 worldPos, string text,
                                          Color color, Camera cam,
                                          float fontSize = 44f, float riseHeight = 0.35f,
                                          float duration = 0.85f)
    {
        var go = new GameObject("FloatingText");
        go.transform.SetParent(parent, worldPositionStays: false);
        go.transform.position = worldPos;

        var canvas = go.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.WorldSpace;
        canvas.sortingOrder = 110;

        var rt = go.GetComponent<RectTransform>();
        rt.localScale = Vector3.one * 0.008f;
        rt.sizeDelta = new Vector2(140f, 60f);

        var txtGo = new GameObject("Text");
        txtGo.transform.SetParent(go.transform, false);
        var txtRt = txtGo.AddComponent<RectTransform>();
        txtRt.anchorMin = Vector2.zero; txtRt.anchorMax = Vector2.one;
        txtRt.offsetMin = Vector2.zero; txtRt.offsetMax = Vector2.zero;

        var tmp = txtGo.AddComponent<TextMeshProUGUI>();
        tmp.text = text;
        tmp.fontSize = fontSize;
        tmp.fontStyle = FontStyles.Bold;
        tmp.alignment = TextAlignmentOptions.Center;
        tmp.color = color;
        tmp.raycastTarget = false;
        tmp.textWrappingMode = TextWrappingModes.NoWrap;

        var shadow = txtGo.AddComponent<Shadow>();
        shadow.effectColor = new Color(0f, 0f, 0f, 0.6f);
        shadow.effectDistance = new Vector2(2f, -2f);

        var ft = go.AddComponent<ShopFloatingText>();
        ft._tmp = tmp;
        ft._cam = cam;
        ft.StartCoroutine(ft.Animate(worldPos, riseHeight, duration));
        return ft;
    }

    private IEnumerator Animate(Vector3 startWorld, float rise, float duration)
    {
        float t = 0f;
        Color baseColor = _tmp.color;
        while (t < 1f)
        {
            t += Time.deltaTime / duration;
            float s = Mathf.Clamp01(t);
            float ease = 1f - (1f - s) * (1f - s); // EaseOutQuad
            transform.position = startWorld + Vector3.up * (rise * ease);

            // 前 70% 保持 alpha=1 体现存在感，后 30% 淡出
            float alpha = s < 0.7f ? 1f : 1f - (s - 0.7f) / 0.3f;
            var c = baseColor; c.a = alpha; _tmp.color = c;

            var cam = _cam != null ? _cam : Camera.main;
            if (cam != null)
                transform.rotation = Quaternion.LookRotation(
                    transform.position - cam.transform.position, cam.transform.up);

            yield return null;
        }
        Destroy(gameObject);
    }
}
