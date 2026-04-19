using System.Collections;
using UnityEngine;

/// <summary>
/// 按 GamePhase 控制 UI 可见性的通用组件。
///
/// 挂在任意带 CanvasGroup 的 UI 节点上，Inspector 勾选"在哪些阶段显示"。
/// 订阅 GameManager.OnPhaseChanged，切换时淡入/淡出 CanvasGroup。
///
/// 为什么用 CanvasGroup 而不是 SetActive：
///   SetActive(false) 会触发 OnDisable → 退订事件 → 再也收不到回推。
///   CanvasGroup.alpha = 0 + blocksRaycasts = false 等效隐藏，组件仍活着。
/// </summary>
[RequireComponent(typeof(CanvasGroup))]
public class PhaseVisibility : MonoBehaviour
{
    [Tooltip("只在列表中的阶段显示；空列表 = 永不显示")]
    [SerializeField] private GamePhase[] _visibleIn = new[] { GamePhase.Shop, GamePhase.Workshop };

    [Tooltip("淡入淡出时长（秒）。0 = 瞬切")]
    [SerializeField] private float _fadeDuration = 0.25f;

    private CanvasGroup _group;
    private Coroutine _fadeCo;
    private Coroutine _subscribeCo;
    private GameManager _subscribed;

    private void Awake()
    {
        _group = GetComponent<CanvasGroup>();
    }

    private void OnEnable()
    {
        // 默认先隐藏（避免 Battle 起步闪一下金币），等 GameManager 就绪后根据实际阶段切
        _group.alpha = 0f;
        _group.interactable = false;
        _group.blocksRaycasts = false;

        _subscribeCo = StartCoroutine(SubscribeWhenReady());
    }

    private void OnDisable()
    {
        if (_subscribeCo != null) { StopCoroutine(_subscribeCo); _subscribeCo = null; }
        if (_fadeCo != null) { StopCoroutine(_fadeCo); _fadeCo = null; }
        if (_subscribed != null)
        {
            _subscribed.OnPhaseChanged -= OnPhaseChanged;
            _subscribed = null;
        }
    }

    private IEnumerator SubscribeWhenReady()
    {
        while (GameManager.Instance == null) yield return null;
        _subscribed = GameManager.Instance;
        _subscribed.OnPhaseChanged += OnPhaseChanged;
        ApplyImmediate(_subscribed.CurrentPhase);
        _subscribeCo = null;
    }

    private void OnPhaseChanged(GamePhase phase)
    {
        bool target = ShouldShow(phase);
        if (_fadeCo != null) StopCoroutine(_fadeCo);
        _fadeCo = StartCoroutine(Fade(target ? 1f : 0f));
    }

    /// <summary>首帧和事件外的瞬切（跳过淡入）</summary>
    private void ApplyImmediate(GamePhase phase)
    {
        float a = ShouldShow(phase) ? 1f : 0f;
        _group.alpha = a;
        _group.interactable = a > 0.5f;
        _group.blocksRaycasts = a > 0.5f;
    }

    private bool ShouldShow(GamePhase phase)
    {
        if (_visibleIn == null) return false;
        for (int i = 0; i < _visibleIn.Length; i++)
            if (_visibleIn[i] == phase) return true;
        return false;
    }

    private IEnumerator Fade(float target)
    {
        float from = _group.alpha;
        if (Mathf.Approximately(from, target) || _fadeDuration <= 0f)
        {
            _group.alpha = target;
            _group.interactable = target > 0.5f;
            _group.blocksRaycasts = target > 0.5f;
            _fadeCo = null;
            yield break;
        }

        float t = 0f;
        _group.blocksRaycasts = target > 0.5f; // 交互状态立即切，避免淡入中途被穿透/卡点
        _group.interactable = target > 0.5f;
        while (t < _fadeDuration)
        {
            t += Time.unscaledDeltaTime;
            _group.alpha = Mathf.Lerp(from, target, t / _fadeDuration);
            yield return null;
        }
        _group.alpha = target;
        _fadeCo = null;
    }
}
