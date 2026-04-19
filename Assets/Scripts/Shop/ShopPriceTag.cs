using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 商店价格标签——钉在货架位的"纸质价签"。
///
/// 视觉：奶油底板 + 顶部金色细条 + 掉落阴影 + 金"$" + 深棕数字 + 随机轻倾斜（手放置感）。
/// 买不起：底板去饱和灰、数字红。
///
/// 行为：创建时一次性钉在 slot 世界位置，之后完全静止——拿起零件 / 反悔回弹 / 失败 shake 期间价签都不动。
/// 仅购买成交时 FadeOutAndDestroy。LateUpdate 只负责 billboard（相机若移动价签仍正面读）。
///
/// 与 ShopPart 解耦：不是它的子节点，parent 是传入的 Transform 容器。
/// </summary>
public class ShopPriceTag : MonoBehaviour
{
    // 配色常量——集中在顶部，调颜色只改这里
    private static readonly Color _bgAfford   = new Color(0.96f, 0.88f, 0.72f); // 奶油纸
    private static readonly Color _rimAfford  = new Color(0.78f, 0.55f, 0.20f); // 金色细条
    private static readonly Color _shadowCol  = new Color(0f,    0f,    0f,    0.35f);
    private static readonly Color _bgDeny     = new Color(0.68f, 0.64f, 0.58f); // 去饱和灰
    private static readonly Color _rimDeny    = new Color(0.55f, 0.30f, 0.25f); // 暗红条

    // 文本颜色（rich text hex）
    private const string _dollarAfford = "#D4A842"; // 金色 $
    private const string _numAfford    = "#3B2D1F"; // 深棕墨水
    private const string _dollarDeny   = "#A33838"; // 红 $
    private const string _numDeny      = "#A33838"; // 红数字

    private Image _bg;
    private Image _rim;
    private TMP_Text _text;
    private CanvasGroup _group;

    private Camera _cam;
    private float _tiltZ;  // 手放置轻倾斜
    private int _price;
    private bool _isAffordable = true;
    private Coroutine _fadeCo;
    private Coroutine _flashCo;
    private Vector3 _baseScale = Vector3.one;

    /// <summary>工厂：程序式构建完整 UI 树，挂到 parent 下（parent 只作组织容器，不影响世界位置）。</summary>
    public static ShopPriceTag Create(Transform parent, Transform trackedPart, Vector3 worldOffset, int price, Camera cam)
    {
        var go = new GameObject("PriceTag");
        go.transform.SetParent(parent, worldPositionStays: false);

        var canvas = go.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.WorldSpace;
        canvas.sortingOrder = 100;

        var rt = go.GetComponent<RectTransform>();
        rt.localScale = Vector3.one * 0.008f; // 1 UI px ≈ 0.008 世界单位

        int digits = Mathf.Max(1, Mathf.Abs(price).ToString().Length);
        rt.sizeDelta = new Vector2(64 + digits * 22, 54);

        // 阴影：偏移 + 暗色矩形
        var shadowRt = BuildLayer(go.transform, "Shadow");
        shadowRt.anchorMin = Vector2.zero; shadowRt.anchorMax = Vector2.one;
        shadowRt.offsetMin = new Vector2(4, -6); shadowRt.offsetMax = new Vector2(6, -2);
        shadowRt.GetComponent<Image>().color = _shadowCol;

        // 主底板
        var bgRt = BuildLayer(go.transform, "Background");
        bgRt.anchorMin = Vector2.zero; bgRt.anchorMax = Vector2.one;
        bgRt.offsetMin = Vector2.zero; bgRt.offsetMax = Vector2.zero;
        var bgImg = bgRt.GetComponent<Image>();
        bgImg.color = _bgAfford;

        // 顶部金色细条
        var rimRt = BuildLayer(go.transform, "TopRim");
        rimRt.anchorMin = new Vector2(0, 1); rimRt.anchorMax = new Vector2(1, 1);
        rimRt.pivot = new Vector2(0.5f, 1); rimRt.anchoredPosition = Vector2.zero;
        rimRt.sizeDelta = new Vector2(0, 3);
        var rimImg = rimRt.GetComponent<Image>();
        rimImg.color = _rimAfford;

        // 文字
        var txtGo = new GameObject("Text");
        txtGo.transform.SetParent(go.transform, false);
        var txtRt = txtGo.AddComponent<RectTransform>();
        txtRt.anchorMin = Vector2.zero; txtRt.anchorMax = Vector2.one;
        txtRt.offsetMin = new Vector2(6, 0); txtRt.offsetMax = new Vector2(-6, 0);
        var tmp = txtGo.AddComponent<TextMeshProUGUI>();
        tmp.fontSize = 30;
        tmp.fontStyle = FontStyles.Bold;
        tmp.alignment = TextAlignmentOptions.Center;
        tmp.textWrappingMode = TextWrappingModes.NoWrap;
        tmp.raycastTarget = false;
        tmp.text = $"<color={_dollarAfford}>$</color><color={_numAfford}>{price}</color>";

        var group = go.AddComponent<CanvasGroup>();

        var tag = go.AddComponent<ShopPriceTag>();
        tag._bg = bgImg; tag._rim = rimImg; tag._text = tmp; tag._group = group;
        tag._cam = cam;
        tag._price = price;
        tag._tiltZ = Random.Range(-4f, 4f);
        tag._baseScale = rt.localScale; // 供 PlayRejectFlash 脉冲后复位

        // 钉死在 slot 世界位置：trackedPart 在 Init 时就是 slot.position，快照一次即可，之后不再跟随
        if (trackedPart != null)
            go.transform.position = trackedPart.position + worldOffset;

        return tag;
    }

    private static RectTransform BuildLayer(Transform parent, string name)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        var rt = go.AddComponent<RectTransform>();
        var img = go.AddComponent<Image>();
        img.raycastTarget = false;
        return rt;
    }

    // ─── 对外 API ─────────────────────────────────────────────────────────────

    public void SetAffordable(bool affordable)
    {
        _isAffordable = affordable;
        if (_bg  != null) _bg.color  = affordable ? _bgAfford  : _bgDeny;
        if (_rim != null) _rim.color = affordable ? _rimAfford : _rimDeny;
        if (_text != null)
        {
            string d = affordable ? _dollarAfford : _dollarDeny;
            string n = affordable ? _numAfford    : _numDeny;
            _text.text = $"<color={d}>$</color><color={n}>{_price}</color>";
        }
    }

    /// <summary>拒绝购买时的红色脉冲：底板瞬间变亮红 + scale 扩张，然后 0.35s 内回到常态（含之前的 afford/deny 色）。</summary>
    public void PlayRejectFlash()
    {
        if (_flashCo != null) StopCoroutine(_flashCo);
        _flashCo = StartCoroutine(DoRejectFlash());
    }

    private IEnumerator DoRejectFlash()
    {
        const float duration = 0.35f;
        var flashBg  = new Color(1f,   0.35f, 0.30f);
        var flashRim = new Color(0.85f, 0.15f, 0.15f);
        Vector3 peak = _baseScale * 1.25f;

        float t = 0f;
        while (t < duration)
        {
            t += Time.deltaTime;
            float s = Mathf.Clamp01(t / duration);
            // 前半段扩张 + 红到顶，后半段回落
            float k = s < 0.5f ? s / 0.5f : 1f - (s - 0.5f) / 0.5f;

            transform.localScale = Vector3.Lerp(_baseScale, peak, k);
            if (_bg != null)
                _bg.color = Color.Lerp(_isAffordable ? _bgAfford : _bgDeny, flashBg, k);
            if (_rim != null)
                _rim.color = Color.Lerp(_isAffordable ? _rimAfford : _rimDeny, flashRim, k);
            yield return null;
        }
        transform.localScale = _baseScale;
        if (_bg != null)  _bg.color  = _isAffordable ? _bgAfford  : _bgDeny;
        if (_rim != null) _rim.color = _isAffordable ? _rimAfford : _rimDeny;
        _flashCo = null;
    }

    public void FadeOutAndDestroy(float duration = 0.25f)
    {
        if (_fadeCo != null) StopCoroutine(_fadeCo);
        _fadeCo = StartCoroutine(DoFade(duration));
    }

    private IEnumerator DoFade(float d)
    {
        float t = 0f;
        float from = _group != null ? _group.alpha : 1f;
        while (t < d)
        {
            t += Time.deltaTime;
            if (_group != null) _group.alpha = Mathf.Lerp(from, 0f, t / d);
            yield return null;
        }
        Destroy(gameObject);
    }

    // ─── 每帧：仅 billboard（位置已在 Create 时钉死） ──────────────────────────

    private void LateUpdate()
    {
        var cam = _cam != null ? _cam : Camera.main;
        if (cam != null)
        {
            // 面向相机：Canvas 的 -Z 朝相机 → transform.forward 朝 away from camera
            var rot = Quaternion.LookRotation(transform.position - cam.transform.position, cam.transform.up);
            transform.rotation = rot * Quaternion.Euler(0f, 0f, _tiltZ);
        }
    }
}
