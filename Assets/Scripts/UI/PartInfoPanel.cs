using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 零件信息悬停面板（共享，Workshop 和 Shop 都用）。
///
/// 职责：按 PenPartData 展示 Icon/名称/类别/属性/效果/价格。
///   - Workshop 端：悬停 WorkshopPart 时调 Show(data, showPrice=false)
///   - Shop 端：悬停 ShopPart 时调 Show(data, showPrice=true)
///   - 任意上下文：鼠标移开调 Hide()
///
/// 单例挂在一个持久 UI Canvas 上。控制器通过 Instance 直接调用。
/// </summary>
public class PartInfoPanel : MonoBehaviour
{
    public static PartInfoPanel Instance { get; private set; }

    [Header("UI 引用")]
    [SerializeField] private CanvasGroup _group;
    [SerializeField] private RectTransform _panel;
    [SerializeField] private Image _iconImage;
    [SerializeField] private TMP_Text _nameText;
    [SerializeField] private TMP_Text _categoryText;
    [SerializeField] private TMP_Text _statsText;
    [SerializeField] private TMP_Text _effectsText;
    [SerializeField] private GameObject _priceGroup;
    [SerializeField] private TMP_Text _priceText;
    [SerializeField] private Image _rarityStrip; // 顶部彩条，按 category/rarity 上色

    [Header("交互")]
    [Tooltip("面板与鼠标的像素间距（X/Y 各一个值；四象限自动反向避开鼠标）")]
    [SerializeField] private Vector2 _edgeGap = new Vector2(20, 20);
    [Tooltip("淡入淡出时长（秒）")]
    [SerializeField] private float _fadeTime = 0.12f;

    private PenPartData _currentData;
    private bool _visible;
    private float _fadeVel;
    private Camera _uiCam;

    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;

        if (_group != null) _group.alpha = 0f;
        if (_panel != null) _panel.gameObject.SetActive(false);
    }

    private void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    // ─── 对外 API ─────────────────────────────────────────────────────────────

    /// <summary>显示面板。</summary>
    public void Show(PenPartData data, bool showPrice)
    {
        if (data == null) { Hide(); return; }

        // 数据没变就不重绘
        if (_currentData == data && _visible)
            return;

        _currentData = data;
        Populate(data, showPrice);
        _visible = true;
        if (_panel != null) _panel.gameObject.SetActive(true);
    }

    /// <summary>隐藏面板（淡出）。</summary>
    public void Hide()
    {
        _visible = false;
        _currentData = null;
    }

    // ─── 填充 ─────────────────────────────────────────────────────────────────

    private void Populate(PenPartData data, bool showPrice)
    {
        if (_iconImage != null)
        {
            _iconImage.sprite = data.Icon;
            _iconImage.enabled = data.Icon != null;
        }
        if (_nameText != null) _nameText.text = data.DisplayName;
        if (_categoryText != null) _categoryText.text = CategoryLabel(data.Category);
        if (_statsText != null) _statsText.text = FormatStats(data);
        if (_effectsText != null) _effectsText.text = FormatEffects(data);
        if (_priceGroup != null) _priceGroup.SetActive(showPrice);
        if (showPrice && _priceText != null) _priceText.text = $"${data.BuyPrice}";
        if (_rarityStrip != null) _rarityStrip.color = CategoryColor(data.Category);
    }

    private static string CategoryLabel(PartType t) => t switch
    {
        PartType.Barrel    => "笔杆",
        PartType.Cap       => "笔帽",
        PartType.Tip       => "笔头",
        PartType.Refill    => "笔芯",
        PartType.Accessory => "配件",
        _ => t.ToString()
    };

    private static Color CategoryColor(PartType t) => t switch
    {
        PartType.Barrel    => new Color(0.65f, 0.50f, 0.35f), // 棕
        PartType.Cap       => new Color(0.40f, 0.50f, 0.70f), // 蓝
        PartType.Tip       => new Color(0.80f, 0.30f, 0.30f), // 红
        PartType.Refill    => new Color(0.35f, 0.60f, 0.40f), // 绿
        PartType.Accessory => new Color(0.75f, 0.55f, 0.25f), // 黄
        _ => Color.gray
    };

    private static string FormatStats(PenPartData d)
    {
        var sb = new System.Text.StringBuilder();
        sb.Append("<b>质量</b> ").Append(d.Mass.ToString("0.00"));
        if (!Mathf.Approximately(d.LaunchPowerMultiplier, 1f))
            sb.Append("    <b>发力</b> ×").Append(d.LaunchPowerMultiplier.ToString("0.00"));
        if (d.PhysicsMaterial != null)
        {
            // 展示真实物理参数而不是资产名。摩擦用 Dynamic Friction（动摩擦系数，玩家最能感受的那个）
            sb.Append("\n<b>摩擦</b> ").Append(FrictionLabel(d.PhysicsMaterial.dynamicFriction));
            if (d.PhysicsMaterial.bounciness > 0.01f)
                sb.Append("    <b>弹性</b> ").Append(BounceLabel(d.PhysicsMaterial.bounciness));
        }
        return sb.ToString();
    }

    /// <summary>把摩擦系数转换成"低/中/高/极粘"+ 数值的可读标签</summary>
    private static string FrictionLabel(float f)
    {
        string tier = f < 0.3f ? "低" : f < 0.7f ? "中" : f < 1.2f ? "高" : "极粘";
        return $"{tier} ({f:0.0})";
    }

    /// <summary>把弹性系数转换成 qualitative 标签</summary>
    private static string BounceLabel(float b)
    {
        string tier = b < 0.2f ? "微" : b < 0.5f ? "中" : b < 0.8f ? "强" : "超弹";
        return $"{tier} ({b:0.0})";
    }

    private static string FormatEffects(PenPartData d)
    {
        var sb = new System.Text.StringBuilder();
        if (!string.IsNullOrWhiteSpace(d.Description))
            sb.Append(d.Description);

        if (d.Effects == null || d.Effects.Length == 0) return sb.ToString();

        for (int i = 0; i < d.Effects.Length; i++)
        {
            var e = d.Effects[i];
            if (e == null) continue;
            if (sb.Length > 0) sb.Append('\n');
            string label = !string.IsNullOrWhiteSpace(e.DisplayName) ? e.DisplayName : e.name;
            sb.Append("• ").Append(label);
            if (!string.IsNullOrWhiteSpace(e.Description))
                sb.Append("：").Append(e.Description);
        }
        return sb.ToString();
    }

    // ─── 帧循环：跟随鼠标 + 淡入淡出 ────────────────────────────────────────────

    private void LateUpdate()
    {
        // 位置：按鼠标所在的屏幕四象限动态设 pivot + 位置，永远朝远离鼠标的方向展开
        //   鼠标左上 → 面板右下展开（pivot 左上）
        //   鼠标右上 → 面板左下展开（pivot 右上）
        //   鼠标左下 → 面板右上展开（pivot 左下）
        //   鼠标右下 → 面板左上展开（pivot 右下）
        if (_panel != null && _panel.gameObject.activeSelf)
        {
            Vector2 mouse = UnityEngine.InputSystem.Mouse.current != null
                ? UnityEngine.InputSystem.Mouse.current.position.ReadValue()
                : (Vector2)Input.mousePosition;

            float cx = Screen.width * 0.5f;
            float cy = Screen.height * 0.5f;
            float pivotX = mouse.x < cx ? 0f : 1f;   // 鼠标左半屏 pivot.x=0 面板向右展开
            float pivotY = mouse.y < cy ? 0f : 1f;   // 鼠标下半屏 pivot.y=0 面板向上展开
            _panel.pivot = new Vector2(pivotX, pivotY);

            Vector2 offDir = new Vector2(
                pivotX == 0f ? 1f : -1f,
                pivotY == 0f ? 1f : -1f);
            _panel.position = mouse + Vector2.Scale(offDir, _edgeGap);
        }

        // 淡入淡出
        if (_group != null)
        {
            float target = _visible ? 1f : 0f;
            _group.alpha = Mathf.SmoothDamp(_group.alpha, target, ref _fadeVel, _fadeTime);
            if (!_visible && _group.alpha < 0.02f && _panel != null && _panel.gameObject.activeSelf)
            {
                _group.alpha = 0f;
                _panel.gameObject.SetActive(false);
            }
        }
    }
}
