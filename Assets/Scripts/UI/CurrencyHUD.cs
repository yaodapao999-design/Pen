using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 金币 HUD：订阅 PlayerWallet.OnChanged，值变化时刷新文字 + 缩放 punch + 颜色闪烁。
///
/// 挂在顶层 Canvas 下的一个 UI 节点上（同级于 3 个场景按钮和 PartInfoPanel）。
/// </summary>
public class CurrencyHUD : MonoBehaviour
{
    [Header("数据源")]
    [SerializeField] private PlayerWallet _wallet;

    [Header("UI 引用")]
    [SerializeField] private TMP_Text _coinText;
    [SerializeField] private RectTransform _textTransform;

    [Header("反馈动画")]
    [Tooltip("值变化时的缩放冲击强度（1 = 不冲击，1.3 = 放大 30%）")]
    [SerializeField] private float _punchScale = 1.25f;
    [Tooltip("冲击持续时间（秒）")]
    [SerializeField] private float _punchDuration = 0.28f;
    [Tooltip("获得金币时的文本颜色（一闪）")]
    [SerializeField] private Color _gainColor = new Color(0.4f, 1f, 0.5f);
    [Tooltip("花费金币时的文本颜色（一闪）")]
    [SerializeField] private Color _spendColor = new Color(1f, 0.5f, 0.4f);
    [Tooltip("平常显示的文本颜色（金色）")]
    [SerializeField] private Color _idleColor = new Color(1f, 0.84f, 0.3f);

    private int _lastValue = -1;
    private Coroutine _animCo;

    private void OnEnable()
    {
        if (_wallet == null)
        {
            Debug.LogWarning("[CurrencyHUD] Wallet 未配置，将不显示任何数据");
            return;
        }
        _wallet.OnChanged += OnWalletChanged;
        if (_coinText != null) _coinText.color = _idleColor;
        OnWalletChanged(); // 初次填充
    }

    private void OnDisable()
    {
        if (_wallet != null) _wallet.OnChanged -= OnWalletChanged;
    }

    private void OnWalletChanged()
    {
        if (_wallet == null || _coinText == null) return;
        int newVal = _wallet.Coins;

        _coinText.text = newVal.ToString();

        // 初次（_lastValue = -1 是标记未初始化）不播 punch
        if (_lastValue >= 0 && newVal != _lastValue)
        {
            Color flash = newVal > _lastValue ? _gainColor : _spendColor;
            if (_animCo != null) StopCoroutine(_animCo);
            _animCo = StartCoroutine(PunchAndFlash(flash));
        }
        _lastValue = newVal;
    }

    private IEnumerator PunchAndFlash(Color flash)
    {
        float t = 0f;
        Vector3 baseScale = Vector3.one;
        while (t < _punchDuration)
        {
            t += Time.unscaledDeltaTime;
            float k = t / _punchDuration;
            // 先冲上去 → 收回；用 sin 曲线柔和一点
            float scaleK = Mathf.Sin(k * Mathf.PI);
            float scale = Mathf.Lerp(1f, _punchScale, scaleK);
            if (_textTransform != null) _textTransform.localScale = baseScale * scale;
            if (_coinText != null) _coinText.color = Color.Lerp(flash, _idleColor, k);
            yield return null;
        }
        if (_textTransform != null) _textTransform.localScale = baseScale;
        if (_coinText != null) _coinText.color = _idleColor;
        _animCo = null;
    }
}
