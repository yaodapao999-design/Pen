using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 悬停视觉：<c>_BaseColor</c> 加亮 tint + 呼吸 scale。
///
/// <para>为什么不用 <c>_EmissionColor</c>：URP Lit 的发光通道需要材质级 <c>_EMISSION</c> keyword
/// 才能在 shader 里实际亮起来，而 MaterialPropertyBlock 不能启用 keyword；若去改 sharedMaterial
/// 会污染 asset、影响其他 pen 实例。因此走 <c>_BaseColor</c> 附加 tint——任何 URP Lit /
/// Simple Lit / 多数 toon shader 都吃 <c>_BaseColor</c> 的 MPB 覆盖，无 keyword 依赖。</para>
///
/// <para>呼吸 scale 作用在笔根 transform，Sin 扰动 1.0 ↔ (1 + breathAmplitude)；默认
/// amplitude=0.04、freq=2Hz，肉眼可辨的"在呼吸"感。</para>
///
/// <para>tint 策略：加法式（<c>finalColor = original + hoverTint × intensity</c>），不需要知道
/// 原色的乘法缩放基准。shader 会自然把颜色钳在合法范围内。</para>
///
/// <para>SRP：本组件只消费 <see cref="PenHoverPresenter.HoverChanged"/>，不读输入、不做命中。</para>
/// </summary>
[RequireComponent(typeof(PenHoverPresenter))]
public class PenHoverVisuals : MonoBehaviour
{
    [Header("颜色 Tint（加法）")]
    [Tooltip("悬停时叠加到 _BaseColor 上的颜色。默认暖偏黄，让笔看起来被烛光照到。\n" +
             "加法式：final = original + tint × intensity。")]
    [SerializeField] private Color _hoverTint = new Color(0.35f, 0.28f, 0.15f, 0f);
    [Tooltip("强度变化的插值速度（每秒接近目标的系数）")]
    [SerializeField] private float _lerpSpeed = 12f;

    [Header("呼吸 Scale")]
    [Tooltip("呼吸幅度（相对 _homeScale 的比例）。0.04 = ±4%，肉眼清晰。")]
    [SerializeField] private float _breathAmplitude = 0.04f;
    [Tooltip("呼吸频率（Hz）")]
    [SerializeField] private float _breathFrequency = 2f;
    [Tooltip("笔正常静态的 localScale。默认 Vector3.one，与 IntroPopIn 一致。")]
    [SerializeField] private Vector3 _homeScale = Vector3.one;

    private static readonly int BaseColorID = Shader.PropertyToID("_BaseColor");
    private static readonly int ColorID = Shader.PropertyToID("_Color"); // legacy fallback

    private PenHoverPresenter _presenter;
    private readonly List<Renderer> _renderers = new();
    /// <summary>每个 renderer 首次被发现时记录它的 shared _BaseColor，作为 tint 的基准。</summary>
    private readonly Dictionary<Renderer, Color> _originalBaseColor = new();
    private MaterialPropertyBlock _mpb;
    private float _currentIntensity;
    private bool _hovered;

    private void Awake()
    {
        _presenter = GetComponent<PenHoverPresenter>();
        _mpb = new MaterialPropertyBlock();
    }

    private void OnEnable()
    {
        if (_presenter != null) _presenter.HoverChanged += OnHoverChanged;
    }

    private void OnDisable()
    {
        if (_presenter != null) _presenter.HoverChanged -= OnHoverChanged;
        _currentIntensity = 0f;
        RestoreAllBaseColors();
        _originalBaseColor.Clear();
        if (transform != null) transform.localScale = _homeScale;
    }

    private void OnHoverChanged(bool hovered) => _hovered = hovered;

    private void Update()
    {
        float target = _hovered ? 1f : 0f;
        _currentIntensity = Mathf.MoveTowards(_currentIntensity, target, Time.deltaTime * _lerpSpeed);
        ApplyTint(_currentIntensity);

        if (_breathAmplitude > 0f)
        {
            float breath = _breathAmplitude * _currentIntensity;
            float offset = Mathf.Sin(Time.time * _breathFrequency * Mathf.PI * 2f) * breath;
            transform.localScale = _homeScale * (1f + offset);
        }
    }

    private void ApplyTint(float t)
    {
        _renderers.Clear();
        GetComponentsInChildren<Renderer>(true, _renderers);
        for (int i = 0; i < _renderers.Count; i++)
        {
            var r = _renderers[i];
            if (r == null) continue;
            if (r is LineRenderer || r is ParticleSystemRenderer) continue;

            // 首次见到这个 renderer，记录它 sharedMaterial 上的 _BaseColor（或 _Color）
            if (!_originalBaseColor.TryGetValue(r, out Color orig))
            {
                orig = ReadBaseColor(r);
                _originalBaseColor[r] = orig;
            }

            Color final = orig + _hoverTint * t;
            r.GetPropertyBlock(_mpb);
            _mpb.SetColor(BaseColorID, final);
            _mpb.SetColor(ColorID, final); // 兼容非 URP 材质
            r.SetPropertyBlock(_mpb);
        }
    }

    private void RestoreAllBaseColors()
    {
        _renderers.Clear();
        GetComponentsInChildren<Renderer>(true, _renderers);
        for (int i = 0; i < _renderers.Count; i++)
        {
            var r = _renderers[i];
            if (r == null) continue;
            if (r is LineRenderer || r is ParticleSystemRenderer) continue;
            if (!_originalBaseColor.TryGetValue(r, out Color orig)) continue;
            r.GetPropertyBlock(_mpb);
            _mpb.SetColor(BaseColorID, orig);
            _mpb.SetColor(ColorID, orig);
            r.SetPropertyBlock(_mpb);
        }
    }

    private static Color ReadBaseColor(Renderer r)
    {
        var mat = r.sharedMaterial;
        if (mat == null) return Color.white;
        if (mat.HasProperty(BaseColorID)) return mat.GetColor(BaseColorID);
        if (mat.HasProperty(ColorID)) return mat.GetColor(ColorID);
        return Color.white;
    }
}
