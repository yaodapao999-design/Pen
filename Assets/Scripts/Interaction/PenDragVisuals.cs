using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 拖拽视觉 —— 低遮挡的输入蓄力带，程序化 mesh（与 <see cref="PenLaunchArrowView"/> 同管线）。
///
/// <para><b>视觉家族统一策略</b>：同 procedural mesh pipeline（场景根 orphan holder，
/// identity transform）、同 URP Particles/Unlit + MaterialPropertyBlock tint、
/// 同 3 横纹 flat shading。拖拽线负责表达"玩家输入"，预测线负责表达"可能输出"。</para>
///
/// <para><b>行业常见层级</b>：输入线比预测轨迹更暖、更近身，但保持半透明和细线宽；
/// 满拉状态只升温到金橙色，不使用粗暴红色警告面。</para>
///
/// <para><b>锥形</b>：contact 端略粗、cursor 端略细，借橡皮筋拉伸直觉表达蓄力。
/// 整体宽度仍按 <see cref="widthByForce"/> 响应力度。</para>
/// </summary>
[RequireComponent(typeof(PenEntity))]
public class PenDragVisuals : MonoBehaviour
{
    [Header("Channel")]
    [SerializeField] private AimPhaseChannelSO _channel;

    [Header("Fill (主绳色)")]
    [Tooltip("常态填充基色。象牙金细丝带，在棕色木桌上清楚但不刺眼。")]
    public Color fillColor = new Color(1.00f, 0.86f, 0.52f, 0.56f);
    [Tooltip("满拉（OverThreshold）时填充色。升温但保持优雅，不做大面积警告红。")]
    public Color fillOverThreshold = new Color(1.00f, 0.92f, 0.62f, 0.68f);
    [Tooltip("颜色插值速度")]
    public float colorLerpSpeed = 14f;

    [Header("Outline (描边)")]
    [Tooltip("描边色。更低 alpha 的暖影，只负责托住形状，不抢视觉。")]
    public Color outlineColor = new Color(0.08f, 0.04f, 0.02f, 0.24f);
    [Tooltip("描边从 centroid 外扩距离（m）。细线化，避免厚贴纸感。")]
    public float outlineOffset = 0.008f;

    [Header("Shape")]
    [Tooltip("contact 端半宽的基准值（m）。实际还会乘以 widthByForce 曲线。")]
    public float baseHalfWidth = 0.034f;
    [Tooltip("cursor 端相对 contact 端的收细比。更明显的收细让它读成轻丝带。")]
    [Range(0.2f, 1.0f)] public float taperFactor = 0.62f;
    [Tooltip("力度→整体宽度乘子曲线。轻拉细、满拉厚。")]
    public AnimationCurve widthByForce = AnimationCurve.EaseInOut(0f, 0.12f, 1f, 0.34f);
    [Tooltip("力度→alpha 曲线。")]
    public AnimationCurve alphaByForce = AnimationCurve.EaseInOut(0f, 0.30f, 1f, 0.64f);

    [Header("Interaction States")]
    [Tooltip("按压点锚点半径（m）。玩家先读到'我抓住了哪里'，再读拖拽力度。")]
    public float contactAnchorRadius = 0.044f;
    [Tooltip("拖拽终点小锚点半径（m）。帮助玩家分辨输入线终点，不和预测线混淆。")]
    public float cursorCapRadius = 0.024f;
    [Tooltip("未达到有效发射力度时的整体透明度倍率。")]
    [Range(0.25f, 0.9f)] public float preArmAlphaMultiplier = 0.62f;
    [Tooltip("未达到有效发射力度时的线宽倍率。")]
    [Range(0.35f, 1f)] public float preArmWidthMultiplier = 0.58f;
    [Tooltip("丝带中心高光宽度倍率。很细，只给一点玻璃纸质感。")]
    [Range(0f, 0.5f)] public float sheenWidthFactor = 0.18f;
    [Tooltip("丝带中心高光透明度倍率。")]
    [Range(0f, 0.8f)] public float sheenAlphaFactor = 0.42f;
    [Tooltip("瞄准时质心提示点半径（m）。很小，只让重笔帽/偏心件在战斗中被看见。")]
    public float comCueRadius = 0.026f;
    [Tooltip("质心偏移超过这个归一化值才显示提示点，避免裸笔也像 debug UI。")]
    [Range(0f, 0.35f)] public float comCueOffsetThreshold = 0.055f;

    [Header("3-Tier Flat Shading")]
    [Range(0f, 1f)] public float highlightValue = 1.00f;
    [Range(0f, 1f)] public float midValue = 0.70f;
    [Range(0f, 1f)] public float shadowValue = 0.45f;

    [Header("Render")]
    [Tooltip("相对接触点 Y 抬高（m），防和桌面 z-fight")]
    public float yOffset = 0.003f;

    [Header("Audio")]
    [Tooltip("发条卷绕持续音")]
    public AudioSource windingLoop;
    public AnimationCurve windingPitchCurve = AnimationCurve.Linear(0f, 0.8f, 1f, 1.4f);
    public AnimationCurve windingVolumeCurve = AnimationCurve.Linear(0f, 0.3f, 1f, 1f);
    [Tooltip("释放 one-shot 音")]
    public AudioSource releaseClick;

    // ─── 运行时 ─────────────────────────────────────────────────────────────

    private static readonly int BaseColorID = Shader.PropertyToID("_BaseColor");
    private static readonly int ColorID = Shader.PropertyToID("_Color");

    /// <summary>层间深度偏移：描边在基础 Y，填充抬升 FILL_Y_LIFT。防 Z-fighting。</summary>
    private const float FILL_Y_LIFT = 0.0008f;
    private const float SHEEN_Y_LIFT = 0.0006f;
    private const int ANCHOR_SEGMENTS = 8;

    private PenEntity _pen;
    private GameObject _holder;
    private MeshFilter _meshFilter;
    private MeshRenderer _meshRenderer;
    private Mesh _mesh;
    private Material _outlineMat;
    private Material _fillMat;
    private MaterialPropertyBlock _mpb;
    private Color _currentFill;

    private readonly List<Vector3> _verts = new List<Vector3>(64);
    private readonly List<Color> _colors = new List<Color>(64);
    private readonly List<int> _outlineTris = new List<int>(16);
    private readonly List<int> _fillTris = new List<int>(64);

    // ─── 生命周期 ──────────────────────────────────────────────────────────

    private void Awake()
    {
        _pen = GetComponent<PenEntity>();
        ApplyAimStyleDefaults();
        _mpb = new MaterialPropertyBlock();
        _currentFill = fillColor;
        CleanupLegacyChildren();
        BuildSceneRootHolder();
    }

    private void OnEnable()
    {
        // 防御性重建（同箭头：scene reload / domain reload / 异常销毁兜底）
        // 先把可能残留但引用已失效的老 holder 清掉再建，避免泄漏。
        if (_holder == null || _mesh == null || _outlineMat == null || _fillMat == null)
        {
            DestroyHolderImmediate();
            BuildSceneRootHolder();
        }

        if (_channel != null)
        {
            _channel.OnBegan += HandleBegan;
            _channel.OnUpdated += HandleUpdated;
            _channel.OnReleased += HandleReleased;
            _channel.OnCancelled += HandleCancelled;
        }
    }

    private void DestroyHolderImmediate()
    {
        if (_mesh != null) { Destroy(_mesh); _mesh = null; }
        if (_outlineMat != null) { Destroy(_outlineMat); _outlineMat = null; }
        if (_fillMat != null) { Destroy(_fillMat); _fillMat = null; }
        if (_holder != null)
        {
            if (Application.isPlaying) Destroy(_holder);
            else DestroyImmediate(_holder);
            _holder = null;
        }
    }

    private void OnDisable()
    {
        if (_channel != null)
        {
            _channel.OnBegan -= HandleBegan;
            _channel.OnUpdated -= HandleUpdated;
            _channel.OnReleased -= HandleReleased;
            _channel.OnCancelled -= HandleCancelled;
        }
        ForceEnd();
    }

    private void OnDestroy()
    {
        if (_holder != null)
        {
            if (Application.isPlaying) Destroy(_holder);
            else DestroyImmediate(_holder);
        }
        if (_mesh != null) Destroy(_mesh);
        if (_outlineMat != null) Destroy(_outlineMat);
        if (_fillMat != null) Destroy(_fillMat);
    }

    // ─── 事件处理 ──────────────────────────────────────────────────────────

    private void HandleBegan(Vector3 contactPoint)
    {
        if (_meshRenderer == null) return;
        _meshRenderer.enabled = true;
        _currentFill = fillColor;
        if (_mesh != null) { _mesh.Clear(); _mesh.subMeshCount = 2; }
        ApplyFillColor();
        if (windingLoop != null && windingLoop.clip != null)
        {
            windingLoop.loop = true;
            windingLoop.pitch = windingPitchCurve.Evaluate(0f);
            windingLoop.volume = windingVolumeCurve.Evaluate(0f);
            if (!windingLoop.isPlaying) windingLoop.Play();
        }
    }

    private void HandleUpdated(AimSample s)
    {
        if (_meshRenderer == null) return;
        RebuildMesh(s);

        Color target = s.OverThreshold ? fillOverThreshold : fillColor;
        _currentFill = Color.Lerp(_currentFill, target, Time.deltaTime * colorLerpSpeed);
        // alpha 随力度
        Color c = _currentFill;
        c.a *= alphaByForce.Evaluate(s.Force) * (s.IsArmed ? 1f : preArmAlphaMultiplier);
        ApplyFillColor(c);

        if (windingLoop != null && windingLoop.isPlaying)
        {
            windingLoop.pitch = windingPitchCurve.Evaluate(s.Force);
            windingLoop.volume = windingVolumeCurve.Evaluate(s.Force);
        }
    }

    private void HandleReleased(float _)
    {
        ForceEnd();
        if (releaseClick != null && releaseClick.clip != null)
            releaseClick.PlayOneShot(releaseClick.clip);
    }

    private void HandleCancelled() => ForceEnd();

    private void ForceEnd()
    {
        if (_meshRenderer != null) _meshRenderer.enabled = false;
        if (_mesh != null) { _mesh.Clear(); _mesh.subMeshCount = 2; }
        if (windingLoop != null && windingLoop.isPlaying) windingLoop.Stop();
    }

    private void ApplyFillColor()
    {
        Color c = _currentFill;
        c.a *= alphaByForce.Evaluate(0f);
        ApplyFillColor(c);
    }

    private void ApplyFillColor(Color finalColor)
    {
        // submesh index 1 = fill (outline keeps static color)
        _meshRenderer.GetPropertyBlock(_mpb, 1);
        _mpb.SetColor(BaseColorID, finalColor);
        _mpb.SetColor(ColorID, finalColor);
        _meshRenderer.SetPropertyBlock(_mpb, 1);
    }

    // ─── Mesh 构造 ─────────────────────────────────────────────────────────

    /// <summary>
    /// 构造按压锚点 + tapered 3 横纹 quad + 拖拽终点锚点。
    /// 未越过 deadzone 时仍显示轻量输入预备态，但预测线不出现，松手也不会发射。
    /// </summary>
    private void RebuildMesh(AimSample s)
    {
        _verts.Clear(); _colors.Clear(); _outlineTris.Clear(); _fillTris.Clear();

        Vector3 contact = s.ContactPointWorld;
        Vector3 cursor = s.DragEndWorld;
        Vector3 delta = cursor - contact; delta.y = 0f;
        float length = delta.magnitude;
        Vector3 fwd = length > 0.01f ? delta / length : Vector3.forward;
        Vector3 right = Vector3.Cross(Vector3.up, fwd).normalized;

        Vector3 lift = Vector3.up * yOffset;
        Vector3 liftFill = Vector3.up * (yOffset + FILL_Y_LIFT);
        float armT = s.MinLaunchForce > 0.001f ? Mathf.Clamp01(s.Force / s.MinLaunchForce) : 1f;
        float stateWidthMul = s.IsArmed ? 1f : Mathf.Lerp(0.35f, preArmWidthMultiplier, armT);
        float stateAlpha = s.IsArmed ? 1f : Mathf.Lerp(0.62f, 0.92f, armT);
        AddAnchor(contact + lift, contact + liftFill, fwd, right,
                  contactAnchorRadius * Mathf.Lerp(0.78f, 1.08f, armT),
                  stateAlpha);
        AddCenterOfMassCue(s, contact.y, fwd, right, stateAlpha);

        if (length < 0.01f || baseHalfWidth < 1e-4f)
        {
            CommitMesh();
            return;
        }

        float widthMul = Mathf.Max(0f, widthByForce.Evaluate(s.Force)) * stateWidthMul;
        float halfW_contact = baseHalfWidth * widthMul;
        float halfW_cursor  = halfW_contact * taperFactor;

        // 3 横纹分界：outer 半宽 → inner 半宽 = 外侧 1/3 边界
        float innerC = halfW_contact / 3f;
        float innerK = halfW_cursor / 3f;

        Vector3 A = contact + lift;   // contact 端中心（outline 层）
        Vector3 B = cursor + lift;    // cursor 端中心
        Vector3 Af = contact + liftFill;
        Vector3 Bf = cursor + liftFill;

        Color cHi  = new Color(highlightValue, highlightValue, highlightValue, stateAlpha);
        Color cMid = new Color(midValue,       midValue,       midValue,       stateAlpha);
        Color cLow = new Color(shadowValue,    shadowValue,    shadowValue,    stateAlpha);

        // ── Outline quad（外扩）─────────────────────────────────────────────
        // 四角从 centroid ((A+B)/2) 外扩 outlineOffset
        Vector3 centroid = (A + B) * 0.5f;
        Vector3 ol_tL = A + right * halfW_contact;
        Vector3 ol_tR = B + right * halfW_cursor;
        Vector3 ol_bR = B - right * halfW_cursor;
        Vector3 ol_bL = A - right * halfW_contact;
        ol_tL += SafeDir(ol_tL - centroid) * outlineOffset;
        ol_tR += SafeDir(ol_tR - centroid) * outlineOffset;
        ol_bR += SafeDir(ol_bR - centroid) * outlineOffset;
        ol_bL += SafeDir(ol_bL - centroid) * outlineOffset;

        int olStart = _verts.Count;
        _verts.Add(ol_tL); _verts.Add(ol_tR); _verts.Add(ol_bR); _verts.Add(ol_bL);
        _colors.Add(Color.white); _colors.Add(Color.white);
        _colors.Add(Color.white); _colors.Add(Color.white);
        // CCW from +Y: tL → bL → bR → tR
        _outlineTris.Add(olStart + 0); _outlineTris.Add(olStart + 3); _outlineTris.Add(olStart + 2);
        _outlineTris.Add(olStart + 0); _outlineTris.Add(olStart + 2); _outlineTris.Add(olStart + 1);

        // ── Fill：3 横纹，每纹独立顶点硬色断 ─────────────────────────────────
        // Top strip
        AddStripQuad(Af + right * halfW_contact, Bf + right * halfW_cursor,
                     Bf + right * innerK,         Af + right * innerC, cHi);
        // Middle strip
        AddStripQuad(Af + right * innerC,        Bf + right * innerK,
                     Bf - right * innerK,         Af - right * innerC, cMid);
        // Bottom strip
        AddStripQuad(Af - right * innerC,        Bf - right * innerK,
                     Bf - right * halfW_cursor,   Af - right * halfW_contact, cLow);

        if (sheenWidthFactor > 0.001f && sheenAlphaFactor > 0.001f)
        {
            float sheenC = halfW_contact * sheenWidthFactor;
            float sheenK = halfW_cursor * sheenWidthFactor;
            Vector3 sheenLift = Vector3.up * SHEEN_Y_LIFT;
            Color cSheen = new Color(1f, 1f, 1f, stateAlpha * sheenAlphaFactor);
            AddStripQuad(Af + sheenLift + right * sheenC, Bf + sheenLift + right * sheenK,
                         Bf + sheenLift - right * sheenK, Af + sheenLift - right * sheenC, cSheen);
        }

        AddAnchor(cursor + lift, cursor + liftFill, fwd, right,
                  cursorCapRadius * Mathf.Lerp(0.82f, 1.22f, Mathf.Clamp01(s.Force)),
                  stateAlpha * 0.86f);

        CommitMesh();
    }

    private void AddCenterOfMassCue(AimSample s, float contactY, Vector3 fwd, Vector3 right, float stateAlpha)
    {
        float offset01 = Mathf.Abs(s.ComOffsetNormalized);
        if (!s.IsArmed || offset01 < comCueOffsetThreshold || comCueRadius <= 0.001f)
            return;

        Vector3 cue = s.CenterOfMassWorld;
        cue.y = contactY;
        float alpha = Mathf.Clamp01(Mathf.Lerp(0.22f, 0.72f, offset01) * stateAlpha);
        float radius = comCueRadius * Mathf.Lerp(0.86f, 1.22f, s.Mass01);
        Vector3 lift = Vector3.up * (yOffset + FILL_Y_LIFT + SHEEN_Y_LIFT);

        AppendDisc(cue + Vector3.up * yOffset, fwd, right, radius + outlineOffset * 0.65f,
                   alpha * 0.42f, _outlineTris);
        AppendDiamond(cue + lift, fwd, right, radius, alpha, _fillTris);
    }

    private void CommitMesh()
    {
        _mesh.Clear();
        _mesh.subMeshCount = 2;
        _mesh.SetVertices(_verts);
        _mesh.SetColors(_colors);
        _mesh.SetTriangles(_outlineTris, 0);
        _mesh.SetTriangles(_fillTris, 1);
        _mesh.RecalculateBounds();
    }

    private void AddAnchor(Vector3 outlineCenter, Vector3 fillCenter,
                           Vector3 fwd, Vector3 right, float radius, float alpha)
    {
        radius = Mathf.Max(0.001f, radius);

        AppendDisc(outlineCenter, fwd, right, radius + outlineOffset,
                   Mathf.Clamp01(alpha * 0.72f), _outlineTris);
        AppendDisc(fillCenter, fwd, right, radius, Mathf.Clamp01(alpha), _fillTris);
    }

    private void AppendDisc(Vector3 center, Vector3 fwd, Vector3 right, float radius, float alpha, List<int> tris)
    {
        int start = _verts.Count;
        _verts.Add(center);
        _colors.Add(new Color(1f, 1f, 1f, alpha));

        for (int i = 0; i < ANCHOR_SEGMENTS; i++)
        {
            float a = (Mathf.PI * 2f * i) / ANCHOR_SEGMENTS;
            Vector3 dir = fwd * Mathf.Cos(a) + right * Mathf.Sin(a);
            _verts.Add(center + dir * radius);
            _colors.Add(new Color(1f, 1f, 1f, alpha));
        }

        for (int i = 0; i < ANCHOR_SEGMENTS; i++)
        {
            int a = start + 1 + i;
            int b = start + 1 + ((i + 1) % ANCHOR_SEGMENTS);
            tris.Add(start);
            tris.Add(a);
            tris.Add(b);
        }
    }

    private void AppendDiamond(Vector3 center, Vector3 fwd, Vector3 right, float radius, float alpha, List<int> tris)
    {
        int start = _verts.Count;
        _verts.Add(center + fwd * radius);
        _verts.Add(center + right * radius * 0.72f);
        _verts.Add(center - fwd * radius);
        _verts.Add(center - right * radius * 0.72f);
        Color color = new Color(1f, 1f, 1f, Mathf.Clamp01(alpha));
        _colors.Add(color);
        _colors.Add(color);
        _colors.Add(color);
        _colors.Add(color);
        tris.Add(start + 0);
        tris.Add(start + 3);
        tris.Add(start + 2);
        tris.Add(start + 0);
        tris.Add(start + 2);
        tris.Add(start + 1);
    }

    /// <summary>
    /// 为一条横纹加 4 顶点 + 2 三角，单色（flat shading）。
    /// 顶点顺序：topLeft (contact 外侧), topRight (cursor 外侧), botRight (cursor 内侧), botLeft (contact 内侧)。
    /// CCW 绕序 from +Y: topLeft → botLeft → botRight → topRight。
    /// </summary>
    private void AddStripQuad(Vector3 topLeft, Vector3 topRight, Vector3 botRight, Vector3 botLeft, Color color)
    {
        int start = _verts.Count;
        _verts.Add(topLeft); _verts.Add(topRight); _verts.Add(botRight); _verts.Add(botLeft);
        _colors.Add(color); _colors.Add(color); _colors.Add(color); _colors.Add(color);
        _fillTris.Add(start + 0); _fillTris.Add(start + 3); _fillTris.Add(start + 2);
        _fillTris.Add(start + 0); _fillTris.Add(start + 2); _fillTris.Add(start + 1);
    }

    private static Vector3 SafeDir(Vector3 v)
    {
        float m = v.magnitude;
        return m > 1e-6f ? v / m : Vector3.zero;
    }

    private void ApplyAimStyleDefaults()
    {
        if (Approximately(fillColor, new Color(0.85f, 0.66f, 0.47f, 1f)) ||
            Approximately(fillColor, new Color(0.96f, 0.74f, 0.32f, 1f)) ||
            Approximately(fillColor, new Color(1.00f, 0.70f, 0.22f, 0.78f)) ||
            Approximately(fillColor, new Color(1.00f, 0.68f, 0.20f, 0.68f)) ||
            Approximately(fillColor, new Color(1.00f, 0.74f, 0.28f, 0.58f)))
            fillColor = new Color(1.00f, 0.86f, 0.52f, 0.56f);
        if (Approximately(fillOverThreshold, new Color(0.94f, 0.66f, 0.57f, 1f)) ||
            Approximately(fillOverThreshold, new Color(1f, 0.54f, 0.18f, 1f)) ||
            Approximately(fillOverThreshold, new Color(1.00f, 0.82f, 0.28f, 0.88f)) ||
            Approximately(fillOverThreshold, new Color(1.00f, 0.78f, 0.24f, 0.78f)) ||
            Approximately(fillOverThreshold, new Color(1.00f, 0.84f, 0.38f, 0.70f)))
            fillOverThreshold = new Color(1.00f, 0.92f, 0.62f, 0.68f);
        if (Approximately(outlineColor, new Color(0.16f, 0f, 0f, 1f)) ||
            Approximately(outlineColor, new Color(0.16f, 0.09f, 0.02f, 1f)) ||
            Approximately(outlineColor, new Color(0.18f, 0.11f, 0.03f, 0.38f)) ||
            Approximately(outlineColor, new Color(0.18f, 0.11f, 0.03f, 0.30f)) ||
            Approximately(outlineColor, new Color(0.15f, 0.10f, 0.04f, 0.22f)))
            outlineColor = new Color(0.08f, 0.04f, 0.02f, 0.24f);

        if (Mathf.Abs(outlineOffset - 0.04f) < 0.001f ||
            Mathf.Abs(outlineOffset - 0.018f) < 0.001f ||
            Mathf.Abs(outlineOffset - 0.010f) < 0.001f)
            outlineOffset = 0.008f;
        if (Mathf.Abs(baseHalfWidth - 0.09f) < 0.001f ||
            Mathf.Abs(baseHalfWidth - 0.075f) < 0.001f ||
            Mathf.Abs(baseHalfWidth - 0.058f) < 0.001f ||
            Mathf.Abs(baseHalfWidth - 0.046f) < 0.001f)
            baseHalfWidth = 0.034f;
        if (Mathf.Abs(taperFactor - 0.4f) < 0.001f ||
            Mathf.Abs(taperFactor - 0.65f) < 0.001f ||
            Mathf.Abs(taperFactor - 0.72f) < 0.001f ||
            Mathf.Abs(taperFactor - 0.78f) < 0.001f)
            taperFactor = 0.62f;
        if (LooksLikeTinyLegacyWidthCurve(widthByForce) || LooksLikePreviousRibbonWidthCurve(widthByForce))
            widthByForce = AnimationCurve.EaseInOut(0f, 0.12f, 1f, 0.34f);
        if (LooksLikePreviousAlphaCurve(alphaByForce))
            alphaByForce = AnimationCurve.EaseInOut(0f, 0.30f, 1f, 0.64f);
        if (Mathf.Abs(contactAnchorRadius - 0.08f) < 0.001f ||
            Mathf.Abs(contactAnchorRadius - 0.055f) < 0.001f ||
            contactAnchorRadius <= 0.001f)
            contactAnchorRadius = 0.044f;
        if (Mathf.Abs(cursorCapRadius - 0.05f) < 0.001f ||
            Mathf.Abs(cursorCapRadius - 0.032f) < 0.001f ||
            cursorCapRadius <= 0.001f)
            cursorCapRadius = 0.024f;
        if (Mathf.Abs(preArmWidthMultiplier - 0.64f) < 0.001f)
            preArmWidthMultiplier = 0.58f;
        if (sheenWidthFactor <= 0.001f)
            sheenWidthFactor = 0.18f;
        if (sheenAlphaFactor <= 0.001f)
            sheenAlphaFactor = 0.42f;
        if (comCueRadius <= 0.001f)
            comCueRadius = 0.026f;
        if (comCueOffsetThreshold <= 0.001f)
            comCueOffsetThreshold = 0.055f;
    }

    private static bool LooksLikeTinyLegacyWidthCurve(AnimationCurve curve)
    {
        if (curve == null || curve.length < 2) return false;
        return curve.keys[0].value <= 0.03f && curve.keys[curve.length - 1].value <= 0.08f;
    }

    private static bool LooksLikePreviousRibbonWidthCurve(AnimationCurve curve)
    {
        if (curve == null || curve.length < 2) return false;
        float first = curve.keys[0].value;
        float last = curve.keys[curve.length - 1].value;
        return (Mathf.Abs(first - 0.22f) < 0.01f && Mathf.Abs(last - 0.62f) < 0.01f) ||
               (Mathf.Abs(first - 0.20f) < 0.01f && Mathf.Abs(last - 0.56f) < 0.01f) ||
               (Mathf.Abs(first - 0.18f) < 0.01f && Mathf.Abs(last - 0.48f) < 0.01f);
    }

    private static bool LooksLikePreviousAlphaCurve(AnimationCurve curve)
    {
        if (curve == null || curve.length < 2) return false;
        float first = curve.keys[0].value;
        float last = curve.keys[curve.length - 1].value;
        return (Mathf.Abs(first - 0.36f) < 0.01f && Mathf.Abs(last - 0.72f) < 0.01f);
    }

    private static bool Approximately(Color a, Color b)
    {
        const float eps = 0.01f;
        return Mathf.Abs(a.r - b.r) < eps &&
               Mathf.Abs(a.g - b.g) < eps &&
               Mathf.Abs(a.b - b.b) < eps &&
               Mathf.Abs(a.a - b.a) < eps;
    }

    // ─── Holder + Material ────────────────────────────────────────────────

    private void CleanupLegacyChildren()
    {
        // 旧双 LineRenderer 实现的子物体，删掉避免两套视觉叠加
        string[] legacyNames = { "DragLine", "DragLineOutline" };
        foreach (var n in legacyNames)
        {
            var old = transform.Find(n);
            if (old == null) continue;
            if (Application.isPlaying) Destroy(old.gameObject);
            else DestroyImmediate(old.gameObject);
        }
    }

    private void BuildSceneRootHolder()
    {
        var go = new GameObject("_DragRope_" + gameObject.name);
        go.hideFlags = HideFlags.DontSave;
        go.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
        go.transform.localScale = Vector3.one;
        _holder = go;

        _meshFilter = go.AddComponent<MeshFilter>();
        _meshRenderer = go.AddComponent<MeshRenderer>();

        _mesh = new Mesh { name = "DragRope_Mesh" };
        _mesh.MarkDynamic();
        _mesh.subMeshCount = 2;
        _meshFilter.sharedMesh = _mesh;

        _outlineMat = CreateUnlitMaterial("DragRope_Outline", outlineColor);
        _fillMat = CreateUnlitMaterial("DragRope_Fill", fillColor);
        _meshRenderer.sharedMaterials = new[] { _outlineMat, _fillMat };
        _meshRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        _meshRenderer.receiveShadows = false;
        _meshRenderer.enabled = false;
    }

    /// <summary>和箭头同 shader（URP Particles/Unlit）——透明混合，降低战斗画面遮挡。</summary>
    private static Material CreateUnlitMaterial(string matName, Color baseColor)
    {
        Shader sh = Shader.Find("Universal Render Pipeline/Particles/Unlit");
        if (sh == null) sh = Shader.Find("Universal Render Pipeline/Unlit");
        if (sh == null) sh = Shader.Find("Unlit/Color");

        var mat = new Material(sh) { name = matName };
        if (mat.HasProperty(BaseColorID)) mat.SetColor(BaseColorID, baseColor);
        if (mat.HasProperty(ColorID)) mat.SetColor(ColorID, baseColor);
        if (mat.HasProperty("_Surface")) mat.SetFloat("_Surface", 1f);
        if (mat.HasProperty("_Blend")) mat.SetFloat("_Blend", 0f);
        if (mat.HasProperty("_ZWrite")) mat.SetFloat("_ZWrite", 0f);
        if (mat.HasProperty("_SrcBlend")) mat.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
        if (mat.HasProperty("_DstBlend")) mat.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
        mat.SetOverrideTag("RenderType", "Transparent");
        mat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
        mat.EnableKeyword("_ALPHABLEND_ON");
        mat.DisableKeyword("_ALPHAPREMULTIPLY_ON");
        mat.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
        return mat;
    }
}
