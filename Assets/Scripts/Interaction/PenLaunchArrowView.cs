using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 发射方向指示 —— 程序化 Mesh 构造的低遮挡弯曲预测线 + 小箭头头。
///
/// <para><b>核心几何决策</b>：mesh holder 放在**场景根**（非 Player 子节点）。
/// 原因：顶点用世界坐标书写，若 holder 挂在 Player 下，Unity 会再过一遍 Player 的
/// localToWorld 矩阵（Player 有 yaw 346° 且位置非零），导致 mesh 被"双重变换"扔到
/// 离笔几米远的地方。LineRenderer 之所以没这问题是因为它有 useWorldSpace flag 能绕过
/// 自身 transform；MeshRenderer 无此开关，唯一干净解是 holder 本身保持 identity 变换。</para>
///
/// <para><b>视觉语言</b>：
///   - 主体：沿玩家按压点预测轨迹排列的宽箭羽，保留最初那种"发射箭头"的清晰方向感
///   - 末端：明确箭头头，远镜头下也能读到发射方向
///   - 颜色：常态浅蓝白，满拉升温到金琥珀，避免旧版厚重红色攻击 UI</para>
///
/// <para><b>密度策略</b>：按固定世界间距采样。预测器仍算真实轨迹，但显示层只取可读前段；
/// 强力/高旋转时提前截断，避免后半段绕圈打结。</para>
///
/// <para><b>深度分层</b>（防 Z-fighting 闪烁）：预测线是瞄准 overlay，整体抬高到笔面上方；
/// 描边层在基础 Y，填充层抬 <see cref="FILL_Y_LIFT"/>，每片鳞片按序号再微抬
/// <see cref="PER_SCALE_LIFT"/>，并用稳定渲染队列避免透明排序争抢。</para>
/// </summary>
[RequireComponent(typeof(PenEntity))]
public class PenLaunchArrowView : MonoBehaviour
{
    [Header("Channel")]
    [SerializeField] private AimPhaseChannelSO _channel;

    [Header("Fill Colors")]
    [Tooltip("常态填充色。浅蓝白半透明桌面投影感，不抢笔本体和碰撞反馈。")]
    public Color fillColor = new Color(0.55f, 0.88f, 1.00f, 0.70f);
    [Tooltip("满拉（OverThreshold）时的填充色。升温到金琥珀，但不使用大块警告红。")]
    public Color fillOverThreshold = new Color(1.00f, 0.76f, 0.28f, 0.82f);

    [Header("Outline")]
    [Tooltip("描边颜色。低 alpha 深青色让投影在桌面上可读，但不会形成厚贴纸边。")]
    public Color outlineColor = new Color(0.03f, 0.10f, 0.14f, 0.42f);
    [Tooltip("每个轨迹片相对其 centroid 外扩的距离（m）。增强桌面可读性，但不回到厚红箭头。")]
    public float outlineOffset = 0.018f;

    [Header("Chevron Body")]
    [Tooltip("每个轨迹片的半宽（m）。比赛视角下要像箭头，不是看不清的小点线。")]
    public float halfWidth = 0.070f;
    [Tooltip("最多绘制多少个预测短段。预测实际点数由轨迹长度 / segmentSpacing 决定。")]
    [Range(4, 48)] public int targetScaleCount = 22;
    [Tooltip("预测短段之间的世界间距（m）。固定间距让力度长度更可信。")]
    [Range(0.08f, 0.6f)] public float segmentSpacing = 0.22f;
    [Tooltip("每段占间距的比例。小于 1 会留呼吸间隔，形成清晰的比赛辅助线。")]
    [Range(0.35f, 1.0f)] public float scaleOverlapFactor = 0.86f;
    [Tooltip("尾部收尖比例。0 接近菱形点列，数值越大越像攻击箭头。")]
    [Range(0f, 0.5f)] public float notchFactor = 0.16f;

    [Header("Arrow Head")]
    [Tooltip("箭头本体长度（m）")]
    public float headLength = 0.38f;
    [Tooltip("箭头半宽（m）。末端保持可读，但不做巨大攻击箭头。")]
    public float headHalfWidth = 0.150f;

    [Header("Readability Clamp")]
    [Tooltip("低力度时最多显示完整轨迹的比例。现在默认几乎完整显示，让玩家读到距离。")]
    [Range(0.35f, 1f)] public float visibleArcRatioAtSoftForce = 1.00f;
    [Tooltip("满力度时最多显示完整轨迹的比例。只在末段可能打结时收掉一点。")]
    [Range(0.30f, 0.98f)] public float visibleArcRatioAtFullForce = 0.86f;
    [Tooltip("低力度时预测线的最大可视长度（m）。")]
    public float maxVisibleArcAtSoftForce = 4.80f;
    [Tooltip("满力度时预测线的最大可视长度（m）。")]
    public float maxVisibleArcAtFullForce = 3.80f;
    [Tooltip("预判线至少显示这么长，避免刚过 deadzone 就只剩一个点。")]
    public float minReadableArc = 1.05f;
    [Tooltip("未启用 Bend Limit 时，低力度允许的累计转向角。超过后截断，避免自交。")]
    [Range(90f, 360f)] public float maxCumulativeTurnAtSoftForce = 280f;
    [Tooltip("未启用 Bend Limit 时，满力度允许的累计转向角。满拖时更早截断。")]
    [Range(90f, 360f)] public float maxCumulativeTurnAtFullForce = 210f;
    [Tooltip("未启用 Bend Limit 时，若后段接近前段到这个距离内，视为即将打结并截断。")]
    public float loopProximity = 0.22f;

    [Header("Power Growth")]
    [Tooltip("力度变化时显示弧长的跟随速度。让同角度加力时稳定延长，不整条线跳变。")]
    public float displayArcFollowSpeed = 24f;
    [Tooltip("新长出来的末端箭羽在这段弧长内渐显（m）。")]
    public float tailFadeInArc = 0.18f;
    [Tooltip("沿轨迹向远端的最低透明度比例。数值越高，同力度变化越稳定。")]
    [Range(0.4f, 1f)] public float farSegmentAlpha = 0.78f;

    [Header("Trajectory Smoothing")]
    [Tooltip("只平滑显示用采样点，不改变物理预测来源。开启后折线会变成清晰弧线。")]
    public bool smoothTrajectoryForDisplay = true;
    [Tooltip("平滑次数。1 已经能消除明显折角，2 更像连续弧线。")]
    [Range(0, 3)] public int trajectorySmoothingPasses = 2;
    [Tooltip("每次平滑削掉拐角的比例。越大越圆，但越偏离原采样。推荐 0.12-0.22。")]
    [Range(0.02f, 0.32f)] public float trajectoryCornerCut = 0.16f;

    [Header("Bend Limit")]
    [Tooltip("限制显示轨迹的累计弯曲角度。S 形左右弯也会累计消耗同一额度，超过上限后不继续卷成死结。")]
    public bool limitDisplayedBend = true;
    [Tooltip("显示轨迹最多允许的总弯曲角度。左弯 60 度再右弯 60 度会算作 120 度。")]
    [Range(45f, 220f)] public float maxDisplayedBendDegrees = 180f;
    [Tooltip("每一小段最多允许改变的角度。数值越低越像连续曲线，避免单个点出现 90 度折角。")]
    [Range(4f, 35f)] public float maxDisplayedTurnPerSegmentDegrees = 12f;

    [Header("Temporal Lerp")]
    [Tooltip("用线性插值平滑每帧预测线形状变化。只影响显示层，仍保留 low poly 分段箭羽。")]
    public bool lerpTrajectoryChanges = true;
    [Tooltip("预测线形状跟随新预测的速度。越大越跟手，越小越稳。")]
    public float trajectoryLerpSpeed = 18f;
    [Tooltip("按压点跳变超过该距离时直接重置插值，避免换点拖拽留下旧线残影。")]
    public float trajectoryLerpResetDistance = 0.28f;
    [Tooltip("起始方向突变超过该角度时直接重置插值，避免跨过笔身后旧弧线拖尾。")]
    [Range(35f, 120f)] public float trajectoryLerpResetAngle = 75f;
    [Tooltip("显示层先按固定弧长重采样，再做插值，避免物理预测点分布变化导致箭头游动。")]
    public bool resampleTrajectoryForStability = true;
    [Tooltip("显示层固定采样数量。数值越高曲线越稳定，过高会增加一点 mesh 计算。")]
    [Range(12, 64)] public int stableTrajectorySampleCount = 32;

    [Header("Full Pull Stability")]
    [Tooltip("力度超过该比例时进入满拉稳定模式，压住箭头末端和长度的小抖动。")]
    [Range(0.85f, 1f)] public float fullPullStabilityThreshold = 0.96f;
    [Tooltip("满拉时预测轨迹形状跟随速度。低于普通速度，减少拉满后的细碎跳动。")]
    public float fullPullTrajectoryLerpSpeed = 4f;
    [Tooltip("满拉时预测点变化小于该距离就保持上一帧形状，只平移到当前拖拽点。")]
    public float fullPullShapeDeadbandDistance = 0.10f;
    [Tooltip("满拉时起始方向变化小于该角度就保持上一帧形状。")]
    [Range(0f, 12f)] public float fullPullDirectionDeadbandDegrees = 6f;
    [Tooltip("满拉时轨迹总长变化小于该值就保持上一帧形状，避免箭羽数量来回跳。")]
    public float fullPullArcLengthDeadband = 0.16f;
    [Tooltip("满拉时可视弧长变化小于该值就保持不动，避免尾部箭羽反复出现/消失。")]
    public float fullPullArcDeadband = 0.16f;
    [Tooltip("满拉时可视弧长跟随速度。")]
    public float fullPullArcFollowSpeed = 4f;
    [Tooltip("满拉时箭头头部方向的跟随速度。")]
    public float fullPullHeadTangentFollowSpeed = 6f;
    [Tooltip("满拉时箭头头部方向变化小于该角度就完全不动，避免箭尖细抖。")]
    [Range(0f, 12f)] public float fullPullHeadTangentDeadbandDegrees = 5f;

    [Header("3-Tier Flat Shading")]
    [Tooltip("上纹（Highlight）亮度乘子。和低多边形+像素相机语言一致：硬色断，无插值。")]
    [Range(0f, 1f)] public float highlightValue = 1.00f;
    [Tooltip("中纹（Mid）亮度乘子。介于高光和阴影之间的基色。")]
    [Range(0f, 1f)] public float midValue = 0.86f;
    [Tooltip("下纹（Shadow）亮度乘子。")]
    [Range(0f, 1f)] public float shadowValue = 0.72f;

    [Header("Render")]
    [Tooltip("箭头起点距接触点的间距（m）。0 表示线严格从玩家按住的那个点长出。")]
    public float startOffset = 0f;
    [Tooltip("相对接触点 Y 的抬高（m）。预测线作为 overlay 浮在笔面上方，避免和笔/桌面 z-fighting。")]
    public float yOffset = 0.045f;
    [Tooltip("填充色插值速度（每秒接近目标的系数）")]
    public float colorLerpSpeed = 14f;
    [Tooltip("力度→预测轨迹整体 alpha。低力弱提示，高力更清晰。")]
    public AnimationCurve alphaByForce = AnimationCurve.EaseInOut(0f, 0.56f, 1f, 0.92f);

    // ─── 运行时 ────────────────────────────────────────────────────────────

    private static readonly int BaseColorID = Shader.PropertyToID("_BaseColor");
    private static readonly int ColorID = Shader.PropertyToID("_Color");
    private static readonly int ZTestID = Shader.PropertyToID("_ZTest");
    private static readonly int CullID = Shader.PropertyToID("_Cull");

    private PenEntity _pen;
    private GameObject _holder;
    private MeshFilter _meshFilter;
    private MeshRenderer _meshRenderer;
    private Mesh _mesh;
    private Material _outlineMat;
    private Material _fillMat;
    private MaterialPropertyBlock _mpb;
    private Color _currentFill;

    private readonly List<Vector3> _verts = new List<Vector3>(512);
    private readonly List<Color> _colors = new List<Color>(512);
    private readonly List<int> _outlineTris = new List<int>(256);
    private readonly List<int> _fillTris = new List<int>(256);
    /// <summary>缓存折线累计弧长（每帧 rebuild 时刷新），用于按弧长采样位置和切向。</summary>
    private readonly List<float> _arcLengths = new List<float>(32);
    private readonly AimTrajectoryDisplayFilter _trajectoryFilter = new AimTrajectoryDisplayFilter();
    private bool _hasSmoothedDisplayArc;
    private float _smoothedDisplayArc;
    private Vector3 _lastAimDir;
    private bool _hasSmoothedHeadTangent;
    private Vector3 _smoothedHeadTangent;

    // ─── 生命周期 ──────────────────────────────────────────────────────────

    private void Awake()
    {
        _pen = GetComponent<PenEntity>();
        ApplyAimStyleDefaults();
        _mpb = new MaterialPropertyBlock();
        _currentFill = fillColor;
        CleanupLegacyChild();
        BuildSceneRootHolder();
    }

    private void OnEnable()
    {
        // 防御性重建：holder / mesh / material 若被意外销毁（场景切换 / domain reload / hot-reload），
        // Awake 不会重跑，这里补齐。先清残留避免泄漏，再建。
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
        Hide();
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

    private void HandleBegan(Vector3 _)
    {
        if (_meshRenderer == null) return;
        _meshRenderer.enabled = true;
        _currentFill = fillColor;
        _hasSmoothedDisplayArc = false;
        _hasSmoothedHeadTangent = false;
        ResetTemporalTrajectory();
        if (_mesh != null) { _mesh.Clear(); _mesh.subMeshCount = 2; }
        ApplyFillColor();
    }

    private void HandleUpdated(AimSample s)
    {
        if (_meshRenderer == null) return;
        if (!s.IsArmed)
        {
            ClearMesh();
            ResetTemporalTrajectory();
            return;
        }

        RebuildMesh(s);
        Color target = s.OverThreshold ? fillOverThreshold : fillColor;
        _currentFill = Color.Lerp(_currentFill, target, Time.deltaTime * colorLerpSpeed);
        Color finalColor = _currentFill;
        finalColor.a *= alphaByForce.Evaluate(s.Force);
        finalColor.a *= Mathf.Lerp(1.04f, 0.90f, s.Friction01);
        ApplyFillColor(finalColor);
    }

    private void HandleReleased(float _) => Hide();
    private void HandleCancelled() => Hide();

    private void Hide()
    {
        if (_meshRenderer != null) _meshRenderer.enabled = false;
        _hasSmoothedDisplayArc = false;
        _hasSmoothedHeadTangent = false;
        ResetTemporalTrajectory();
        ClearMesh();
    }

    private void ClearMesh()
    {
        if (_mesh != null) { _mesh.Clear(); _mesh.subMeshCount = 2; }
    }

    private void ResetTemporalTrajectory()
    {
        _trajectoryFilter.Reset();
    }

    // ─── 核心构造 ──────────────────────────────────────────────────────────

    private void ApplyFillColor()
    {
        Color finalColor = _currentFill;
        finalColor.a *= alphaByForce.Evaluate(0f);
        ApplyFillColor(finalColor);
    }

    private void ApplyFillColor(Color finalColor)
    {
        _meshRenderer.GetPropertyBlock(_mpb, 1);
        _mpb.SetColor(BaseColorID, finalColor);
        _mpb.SetColor(ColorID, finalColor);
        _meshRenderer.SetPropertyBlock(_mpb, 1);
    }

    /// <summary>
    /// 沿 <see cref="AimSample.PredictedTrajectory"/> 折线按弧长采样排列鳞片。
    /// <para>每片鳞片取该弧长处的位置 + 局部切向（tangent）作为本地 dir，
    /// 本地 side = Cross(tangent, up)。曲率自然沿折线走。</para>
    /// <para>轨迹点来自玩家按压点本身的刚体预测，不再用 COM 轨迹平移伪装。
    /// 例如按笔头，线显示笔头释放后随平移和 yaw 旋转共同形成的路径。</para>
    /// <para>总弧长 = 预测按压点真正走到停下（V &lt; stopSpeed）的距离，和拖拽力度解耦。
    /// 拖拽线仍表达"我拉了多远"的输入；箭头表达"笔会走多远"的输出。</para>
    /// </summary>
    private void RebuildMesh(AimSample s)
    {
        _verts.Clear();
        _colors.Clear();
        _outlineTris.Clear();
        _fillTris.Clear();

        var rawTrajectory = s.PredictedTrajectory;
        if (rawTrajectory == null || rawTrajectory.Count < 2)
        {
            ClearMesh();
            return;
        }

        IReadOnlyList<Vector3> traj = BuildReadableTrajectory(rawTrajectory, s.Force);
        float totalArc = ComputeArcLengths(traj);
        float displayArc = SmoothDisplayArc(
            ComputeReadableArcLimit(traj, totalArc, s.Force, s.PredictionCurve01),
            s.LaunchDirection,
            s.Force);
        if (displayArc <= startOffset)
        {
            ClearMesh();
            return;
        }

        // 预测器输出的已经是按压点轨迹。这里仍做极小平移校正，确保线严格从鼠标按住处长出。
        Vector3 visualOffset = s.ContactPointWorld - traj[0];
        visualOffset.y = 0f;
        Vector3 offset = visualOffset + Vector3.up * yOffset;

        float availArc = displayArc - startOffset;
        float actualHeadLen = Mathf.Min(headLength, availArc);
        float bodyRoom = Mathf.Max(0f, availArc - actualHeadLen);
        float bodyStartArc = startOffset;
        float headApexArc = displayArc;
        float headBaseArc = headApexArc - actualHeadLen;

        const float MIN_BODY_ROOM = 0.04f;

        int scalesDrawn = 0;
        if (bodyRoom >= MIN_BODY_ROOM && targetScaleCount > 0)
        {
            float step = Mathf.Max(0.04f, segmentSpacing);
            int desiredCount = Mathf.CeilToInt(bodyRoom / step);
            int count = Mathf.Clamp(desiredCount, 1, targetScaleCount);
            float segmentLen = step * scaleOverlapFactor;
            float bodyEndArc = bodyStartArc + bodyRoom;

            for (int i = 0; i < count; i++)
            {
                float sArc = bodyStartArc + i * step;
                if (sArc >= bodyEndArc) break;

                float apexArc = sArc + segmentLen;
                float thisLen = apexArc > bodyEndArc
                    ? Mathf.Max(0f, bodyEndArc - sArc)
                    : segmentLen;
                if (thisLen < MIN_BODY_ROOM * 0.5f) continue;

                Vector3 basePos = SamplePolyline(traj, sArc, out Vector3 tangent) + offset;
                Vector3 side = Vector3.Cross(tangent, Vector3.up).normalized;
                float remainingArc = Mathf.Max(0f, bodyEndArc - sArc);
                float tailReveal = Mathf.Clamp01(remainingArc / Mathf.Max(0.001f, tailFadeInArc));
                float width = halfWidth *
                              Mathf.Lerp(0.92f, 1.0f, Mathf.Clamp01(s.Force)) *
                              Mathf.Lerp(1.04f, 0.92f, s.Friction01) *
                              Mathf.Lerp(1.00f, 1.10f, s.Spin01) *
                              Mathf.Lerp(0.72f, 1f, tailReveal);
                float segmentAlpha = ComputeStableSegmentAlpha(
                    distanceFromStart: sArc - bodyStartArc,
                    force: s.Force,
                    friction01: s.Friction01,
                    tailReveal: tailReveal);
                AddChevronScale(basePos, tangent, side, thisLen, width, notchFactor, scalesDrawn, segmentAlpha);
                scalesDrawn++;
            }
        }

        // Head：apex 在轨迹末端，base 沿切向回退 actualHeadLen
        Vector3 headApexPos = SamplePolyline(traj, headApexArc, out Vector3 headTangent) + offset;
        Vector3 headBasePos = SamplePolyline(traj, headBaseArc, out _) + offset;
        Vector3 averagedHeadTangent = headApexPos - headBasePos;
        averagedHeadTangent.y = 0f;
        headTangent = SmoothHeadTangent(
            averagedHeadTangent.sqrMagnitude > 1e-6f ? averagedHeadTangent : headTangent,
            s.Force);
        Vector3 headSide = Vector3.Cross(headTangent, Vector3.up).normalized;
        float headWidth = headHalfWidth *
                          Mathf.Lerp(0.94f, 1.08f, Mathf.Clamp01(s.Force)) *
                          Mathf.Lerp(1.00f, 0.94f, s.Friction01);
        AddSolidTriangle(headApexPos,
                         headBasePos - headSide * headWidth,
                         headBasePos + headSide * headWidth,
                         scalesDrawn,
                         1f);

        _mesh.Clear();
        _mesh.subMeshCount = 2;
        _mesh.SetVertices(_verts);
        _mesh.SetColors(_colors);
        _mesh.SetTriangles(_outlineTris, 0);
        _mesh.SetTriangles(_fillTris, 1);
        _mesh.RecalculateBounds();
    }

    /// <summary>计算每个采样点到起点的累计弧长（XZ 平面），返回总长。</summary>
    private float ComputeArcLengths(IReadOnlyList<Vector3> pts)
    {
        _arcLengths.Clear();
        if (pts == null || pts.Count == 0) return 0f;
        _arcLengths.Add(0f);
        float total = 0f;
        for (int i = 1; i < pts.Count; i++)
        {
            Vector3 a = pts[i - 1]; a.y = 0f;
            Vector3 b = pts[i]; b.y = 0f;
            total += Vector3.Distance(a, b);
            _arcLengths.Add(total);
        }
        return total;
    }

    private IReadOnlyList<Vector3> BuildReadableTrajectory(IReadOnlyList<Vector3> raw, float force)
    {
        return _trajectoryFilter.Build(raw, force, Time.deltaTime, BuildTrajectoryFilterSettings());
    }

    private AimTrajectoryDisplayFilter.Settings BuildTrajectoryFilterSettings()
    {
        return new AimTrajectoryDisplayFilter.Settings
        {
            Smooth = smoothTrajectoryForDisplay,
            SmoothingPasses = trajectorySmoothingPasses,
            CornerCut = trajectoryCornerCut,

            LimitBend = limitDisplayedBend,
            MaxTotalBendDegrees = maxDisplayedBendDegrees,
            MaxTurnPerSegmentDegrees = maxDisplayedTurnPerSegmentDegrees,

            TemporalLerp = lerpTrajectoryChanges,
            ResampleForStability = resampleTrajectoryForStability,
            StableSampleCount = stableTrajectorySampleCount,
            TrajectoryLerpSpeed = trajectoryLerpSpeed,
            TrajectoryLerpResetDistance = trajectoryLerpResetDistance,
            TrajectoryLerpResetAngle = trajectoryLerpResetAngle,

            FullPullStabilityThreshold = fullPullStabilityThreshold,
            FullPullTrajectoryLerpSpeed = fullPullTrajectoryLerpSpeed,
            FullPullShapeDeadbandDistance = fullPullShapeDeadbandDistance,
            FullPullDirectionDeadbandDegrees = fullPullDirectionDeadbandDegrees,
            FullPullArcLengthDeadband = fullPullArcLengthDeadband
        };
    }

    /// <summary>
    /// 将真实预测轨迹裁成玩家能读懂的一段。强力偏心发射可能让按压点末端绕圈；
    /// Bend Limit 开启时用弯曲上限处理绕圈，只按力度/长度裁剪；关闭时才回退到急转/自交截断。
    /// </summary>
    private float ComputeReadableArcLimit(IReadOnlyList<Vector3> pts, float totalArc, float force, float curve01)
    {
        if (pts == null || pts.Count < 2 || totalArc <= 0f)
            return 0f;

        float f = Mathf.Clamp01(force);
        float c = Mathf.Clamp01(curve01);
        float ratio = Mathf.Lerp(visibleArcRatioAtSoftForce, visibleArcRatioAtFullForce, f);
        float absoluteCap = Mathf.Lerp(maxVisibleArcAtSoftForce, maxVisibleArcAtFullForce, f);
        ratio *= Mathf.Lerp(1.0f, 0.90f, c);
        absoluteCap *= Mathf.Lerp(1.0f, 0.88f, c);
        float ratioCap = totalArc * Mathf.Clamp01(ratio);
        float lengthCap = Mathf.Min(totalArc, Mathf.Min(ratioCap, Mathf.Max(0.05f, absoluteCap)));
        float readableMin = Mathf.Min(totalArc, Mathf.Max(startOffset + 0.08f, minReadableArc));
        float cappedArc = Mathf.Min(totalArc, Mathf.Max(readableMin, lengthCap));
        if (limitDisplayedBend)
            return Mathf.Clamp(cappedArc, Mathf.Min(readableMin, totalArc), totalArc);

        float turnCap = Mathf.Lerp(maxCumulativeTurnAtSoftForce, maxCumulativeTurnAtFullForce, f);
        turnCap *= Mathf.Lerp(1.0f, 0.84f, c);
        float turnLimitedArc = FindTurnOrLoopLimitArc(pts, cappedArc, turnCap, readableMin);
        return Mathf.Clamp(Mathf.Min(cappedArc, turnLimitedArc), Mathf.Min(readableMin, totalArc), totalArc);
    }

    private float SmoothDisplayArc(float targetArc, Vector3 aimDirection, float force)
    {
        Vector3 flatDir = aimDirection;
        flatDir.y = 0f;
        if (flatDir.sqrMagnitude > 1e-6f)
            flatDir.Normalize();

        bool fullPull = IsFullPull(force);
        float aimResetDot = fullPull ? 0.82f : 0.94f;
        bool aimChanged = _lastAimDir.sqrMagnitude > 1e-6f &&
                          flatDir.sqrMagnitude > 1e-6f &&
                          Vector3.Dot(_lastAimDir, flatDir) < aimResetDot;

        if (!_hasSmoothedDisplayArc || aimChanged || Time.deltaTime <= 0f)
        {
            _smoothedDisplayArc = targetArc;
            _hasSmoothedDisplayArc = true;
            _lastAimDir = flatDir;
            return targetArc;
        }

        if (fullPull && Mathf.Abs(targetArc - _smoothedDisplayArc) <= Mathf.Max(0.001f, fullPullArcDeadband))
        {
            _lastAimDir = flatDir;
            return _smoothedDisplayArc;
        }

        float speed = fullPull
            ? Mathf.Max(1f, fullPullArcFollowSpeed)
            : Mathf.Max(1f, displayArcFollowSpeed);
        float follow = 1f - Mathf.Exp(-speed * Time.deltaTime);
        _smoothedDisplayArc = Mathf.Lerp(_smoothedDisplayArc, targetArc, follow);
        _lastAimDir = flatDir;
        return _smoothedDisplayArc;
    }

    private Vector3 SmoothHeadTangent(Vector3 targetTangent, float force)
    {
        targetTangent.y = 0f;
        if (targetTangent.sqrMagnitude <= 1e-6f)
            return _hasSmoothedHeadTangent ? _smoothedHeadTangent : Vector3.forward;
        targetTangent.Normalize();

        if (!IsFullPull(force) || !_hasSmoothedHeadTangent || Time.deltaTime <= 0f)
        {
            _smoothedHeadTangent = targetTangent;
            _hasSmoothedHeadTangent = true;
            return targetTangent;
        }

        if (Vector3.Dot(_smoothedHeadTangent, targetTangent) < 0.35f)
        {
            _smoothedHeadTangent = targetTangent;
            return targetTangent;
        }

        float angle = Vector3.Angle(_smoothedHeadTangent, targetTangent);
        if (angle <= Mathf.Max(0f, fullPullHeadTangentDeadbandDegrees))
            return _smoothedHeadTangent;

        float follow = 1f - Mathf.Exp(-Mathf.Max(1f, fullPullHeadTangentFollowSpeed) * Time.deltaTime);
        _smoothedHeadTangent = Vector3.Slerp(_smoothedHeadTangent, targetTangent, follow).normalized;
        return _smoothedHeadTangent;
    }

    private bool IsFullPull(float force)
    {
        return force >= Mathf.Clamp01(fullPullStabilityThreshold);
    }

    private float ComputeStableSegmentAlpha(float distanceFromStart, float force, float friction01, float tailReveal)
    {
        float distanceFade = Mathf.Lerp(1f, farSegmentAlpha, Mathf.Clamp01(distanceFromStart / 4.2f));
        float powerReadability = Mathf.Lerp(0.90f, 1.04f, Mathf.Clamp01(force));
        float frictionFade = Mathf.Lerp(1.02f, 0.92f, Mathf.Clamp01(friction01));
        float reveal = Mathf.SmoothStep(0.18f, 1f, Mathf.Clamp01(tailReveal));
        return distanceFade * powerReadability * frictionFade * reveal;
    }

    private float FindTurnOrLoopLimitArc(IReadOnlyList<Vector3> pts, float maxArc, float turnCapDegrees, float minArc)
    {
        Vector3 prevDir = Vector3.zero;
        float cumulativeTurn = 0f;

        for (int i = 1; i < pts.Count && i < _arcLengths.Count; i++)
        {
            float arc = _arcLengths[i];
            if (arc > maxArc)
                return maxArc;

            Vector3 dir = pts[i] - pts[i - 1];
            dir.y = 0f;
            if (dir.sqrMagnitude < 1e-6f)
                continue;
            dir.Normalize();

            if (prevDir.sqrMagnitude > 1e-6f)
            {
                float turn = Mathf.Abs(Vector3.SignedAngle(prevDir, dir, Vector3.up));
                cumulativeTurn += turn;
                if (arc >= minArc && (turn >= 118f || cumulativeTurn >= turnCapDegrees))
                    return Mathf.Max(minArc, _arcLengths[Mathf.Max(0, i - 1)]);
            }

            if (arc >= minArc + segmentSpacing)
            {
                for (int j = 0; j <= i - 3 && j < _arcLengths.Count; j++)
                {
                    if (arc - _arcLengths[j] < segmentSpacing * 1.5f)
                        continue;

                    Vector3 a = pts[i];
                    Vector3 b = pts[j];
                    a.y = 0f;
                    b.y = 0f;
                    if (Vector3.Distance(a, b) <= loopProximity)
                        return Mathf.Max(minArc, _arcLengths[Mathf.Max(0, i - 1)]);
                }
            }

            prevDir = dir;
        }

        return maxArc;
    }

    /// <summary>
    /// 在折线上弧长 <paramref name="s"/> 处取位置和局部切向。
    /// 依赖 <see cref="_arcLengths"/> 已经由 ComputeArcLengths 填充。
    /// </summary>
    private Vector3 SamplePolyline(IReadOnlyList<Vector3> pts, float s, out Vector3 tangent)
    {
        int n = pts.Count;
        if (n == 0) { tangent = Vector3.forward; return Vector3.zero; }
        if (n == 1) { tangent = Vector3.forward; return pts[0]; }

        // 线性扫描（N ≤ 26，不值得二分）
        for (int i = 0; i < _arcLengths.Count - 1; i++)
        {
            float a0 = _arcLengths[i];
            float a1 = _arcLengths[i + 1];
            if (s >= a0 && s <= a1)
            {
                float t = a1 > a0 ? (s - a0) / (a1 - a0) : 0f;
                Vector3 p = Vector3.Lerp(pts[i], pts[i + 1], t);
                Vector3 tan = pts[i + 1] - pts[i];
                tan.y = 0f;
                tangent = tan.sqrMagnitude > 1e-6f ? tan.normalized : Vector3.forward;
                return p;
            }
        }

        // s 超出总弧长：取最后一点和最后切向
        Vector3 tailTan = pts[n - 1] - pts[n - 2];
        tailTan.y = 0f;
        tangent = tailTan.sqrMagnitude > 1e-6f ? tailTan.normalized : Vector3.forward;
        return pts[n - 1];
    }

    /// <summary>层间深度偏移，防描边/填充共面 Z-fighting。</summary>
    private const float FILL_Y_LIFT = 0.0040f;
    private const float PER_SCALE_LIFT = 0.00018f;

    /// <summary>
    /// 一片低遮挡预测短线，切成 3 条横纹（flat shading 硬色断）+ 外扩描边。
    /// <para>几何：4 silhouette 顶点（apex / upper / notch / lower）+ 切点 A,B,A',B'（v=±hw/3
    /// 在四条边上的交点）。每条纹的顶点**独立不共用**，给相邻纹保留硬色断不被插值拉平。</para>
    /// <para>纹内颜色：all-white × 纹亮度档 × 段落 alpha。
    /// 材质再乘 _BaseColor → 最终呈现半透明低多边形投影。和项目低多边形+像素相机美术一致。</para>
    /// </summary>
    private void AddChevronScale(Vector3 baseP, Vector3 dir, Vector3 side,
                                 float length, float hw, float notchFrac, int order, float alpha)
    {
        // 旧版是厚重 chevron。这里保留同一 mesh 管线，但形状改成轻量短线段：
        // 尾部收尖、肩部最宽、前端收尖，读作真实位移轨迹，不像攻击按钮。
        Vector3 apex = baseP + dir * length;
        Vector3 shoulder = baseP + dir * (length * 0.52f);
        Vector3 upper = shoulder + side * hw;
        Vector3 lower = shoulder - side * hw;
        Vector3 notch = baseP + dir * (length * Mathf.Clamp(notchFrac, 0f, 0.18f));

        // 横纹切点（v = ±hw/3 在四条边上的位置）
        // A  = apex→upper 边上 v=+hw/3 点
        // B  = upper→notch 边上 v=+hw/3 点
        // A' = apex→lower 边上 v=-hw/3 点
        // B' = lower→notch 边上 v=-hw/3 点
        Vector3 A  = Vector3.Lerp(apex,  upper, 1f / 3f);
        Vector3 B  = Vector3.Lerp(upper, notch, 2f / 3f);
        Vector3 A_ = Vector3.Lerp(apex,  lower, 1f / 3f);
        Vector3 B_ = Vector3.Lerp(lower, notch, 2f / 3f);

        Vector3 centroid = (apex + upper + lower + notch) * 0.25f;
        Vector3 fillLift = Vector3.up * (FILL_Y_LIFT + order * PER_SCALE_LIFT);

        // ── 外扩描边（基础 Y） ───────────────────────────────
        int olStart = _verts.Count;
        _verts.Add(apex  + SafeDir(apex  - centroid) * outlineOffset);
        _verts.Add(upper + SafeDir(upper - centroid) * outlineOffset);
        _verts.Add(notch + SafeDir(notch - centroid) * outlineOffset);
        _verts.Add(lower + SafeDir(lower - centroid) * outlineOffset);
        Color outlineVertex = new Color(1f, 1f, 1f, Mathf.Clamp01(alpha * 0.85f));
        _colors.Add(outlineVertex); _colors.Add(outlineVertex);
        _colors.Add(outlineVertex); _colors.Add(outlineVertex);
        _outlineTris.Add(olStart + 0); _outlineTris.Add(olStart + 2); _outlineTris.Add(olStart + 1);
        _outlineTris.Add(olStart + 0); _outlineTris.Add(olStart + 3); _outlineTris.Add(olStart + 2);

        // ── 填充：3 条横纹，每纹独立顶点 + 单色（flat shading） ──
        Color cHi  = new Color(highlightValue, highlightValue, highlightValue, alpha);
        Color cMid = new Color(midValue,       midValue,       midValue,       alpha);
        Color cLow = new Color(shadowValue,    shadowValue,    shadowValue,    alpha);

        // 上纹：triangle(upper, A, B) — 高光
        int upStart = _verts.Count;
        _verts.Add(upper + fillLift);
        _verts.Add(A + fillLift);
        _verts.Add(B + fillLift);
        _colors.Add(cHi); _colors.Add(cHi); _colors.Add(cHi);
        _fillTris.Add(upStart + 0); _fillTris.Add(upStart + 1); _fillTris.Add(upStart + 2);

        // 中纹：6 顶点（apex, A, B, notch, B', A'）扇形 4 三角 — 中色
        // CCW 边界 apex → A' → B' → notch → B → A → apex，fan from apex
        int midStart = _verts.Count;
        _verts.Add(apex  + fillLift);  // 0
        _verts.Add(A     + fillLift);  // 1
        _verts.Add(B     + fillLift);  // 2
        _verts.Add(notch + fillLift);  // 3
        _verts.Add(B_    + fillLift);  // 4
        _verts.Add(A_    + fillLift);  // 5
        _colors.Add(cMid); _colors.Add(cMid); _colors.Add(cMid);
        _colors.Add(cMid); _colors.Add(cMid); _colors.Add(cMid);
        _fillTris.Add(midStart + 0); _fillTris.Add(midStart + 5); _fillTris.Add(midStart + 4); // apex, A', B'
        _fillTris.Add(midStart + 0); _fillTris.Add(midStart + 4); _fillTris.Add(midStart + 3); // apex, B', notch
        _fillTris.Add(midStart + 0); _fillTris.Add(midStart + 3); _fillTris.Add(midStart + 2); // apex, notch, B
        _fillTris.Add(midStart + 0); _fillTris.Add(midStart + 2); _fillTris.Add(midStart + 1); // apex, B, A

        // 下纹：triangle(lower, B', A') — 阴影
        int loStart = _verts.Count;
        _verts.Add(lower + fillLift);
        _verts.Add(B_    + fillLift);
        _verts.Add(A_    + fillLift);
        _colors.Add(cLow); _colors.Add(cLow); _colors.Add(cLow);
        _fillTris.Add(loStart + 0); _fillTris.Add(loStart + 1); _fillTris.Add(loStart + 2);
    }

    /// <summary>
    /// 末端箭头：实心三角（apex / left=upper / right=lower），同样切 3 条横纹 flat-shaded。
    /// left/right 外部已按 headHalfWidth 定位（left = +side×hhw, right = -side×hhw）。
    /// </summary>
    private void AddSolidTriangle(Vector3 apex, Vector3 left, Vector3 right, int order, float alpha)
    {
        // 横纹切点
        // left/right 的 v = ±headHalfWidth；切纹 v = ±headHalfWidth / 3
        // Ah  = apex→left  边上 v=+hhw/3 点   (t = 1/3)
        // Bh  = left→right 边上 v=+hhw/3 点   (t = 1/3)
        // Ah' = apex→right 边上 v=-hhw/3 点   (t = 1/3)
        // Bh' = left→right 边上 v=-hhw/3 点   (t = 2/3)
        Vector3 Ah  = Vector3.Lerp(apex,  left,  1f / 3f);
        Vector3 Bh  = Vector3.Lerp(left,  right, 1f / 3f);
        Vector3 Ah_ = Vector3.Lerp(apex,  right, 1f / 3f);
        Vector3 Bh_ = Vector3.Lerp(left,  right, 2f / 3f);

        Vector3 centroid = (apex + left + right) / 3f;
        Vector3 fillLift = Vector3.up * (FILL_Y_LIFT + order * PER_SCALE_LIFT);

        // ── 外扩描边（基础 Y） ───────────────────────────────
        int olStart = _verts.Count;
        _verts.Add(apex  + SafeDir(apex  - centroid) * outlineOffset);
        _verts.Add(left  + SafeDir(left  - centroid) * outlineOffset);
        _verts.Add(right + SafeDir(right - centroid) * outlineOffset);
        Color outlineVertex = new Color(1f, 1f, 1f, Mathf.Clamp01(alpha * 0.85f));
        _colors.Add(outlineVertex); _colors.Add(outlineVertex); _colors.Add(outlineVertex);
        // CCW (apex, right, left) → +Y
        _outlineTris.Add(olStart + 0); _outlineTris.Add(olStart + 2); _outlineTris.Add(olStart + 1);

        // ── 填充：3 条横纹 ────────────────────────────────────
        Color cHi  = new Color(highlightValue, highlightValue, highlightValue, alpha);
        Color cMid = new Color(midValue,       midValue,       midValue,       alpha);
        Color cLow = new Color(shadowValue,    shadowValue,    shadowValue,    alpha);

        // 上纹：triangle(left, Ah, Bh) — 高光
        int upStart = _verts.Count;
        _verts.Add(left + fillLift);
        _verts.Add(Ah + fillLift);
        _verts.Add(Bh + fillLift);
        _colors.Add(cHi); _colors.Add(cHi); _colors.Add(cHi);
        _fillTris.Add(upStart + 0); _fillTris.Add(upStart + 1); _fillTris.Add(upStart + 2);

        // 中纹：5 顶点（apex, Ah, Bh, Bh', Ah'）扇形 3 三角 — 中色
        // CCW 边界 apex → Ah' → Bh' → Bh → Ah → apex，fan from apex
        int midStart = _verts.Count;
        _verts.Add(apex + fillLift); // 0
        _verts.Add(Ah   + fillLift); // 1
        _verts.Add(Bh   + fillLift); // 2
        _verts.Add(Bh_  + fillLift); // 3
        _verts.Add(Ah_  + fillLift); // 4
        _colors.Add(cMid); _colors.Add(cMid); _colors.Add(cMid);
        _colors.Add(cMid); _colors.Add(cMid);
        _fillTris.Add(midStart + 0); _fillTris.Add(midStart + 4); _fillTris.Add(midStart + 3); // apex, Ah', Bh'
        _fillTris.Add(midStart + 0); _fillTris.Add(midStart + 3); _fillTris.Add(midStart + 2); // apex, Bh', Bh
        _fillTris.Add(midStart + 0); _fillTris.Add(midStart + 2); _fillTris.Add(midStart + 1); // apex, Bh, Ah

        // 下纹：triangle(right, Bh', Ah') — 阴影
        int loStart = _verts.Count;
        _verts.Add(right + fillLift);
        _verts.Add(Bh_   + fillLift);
        _verts.Add(Ah_   + fillLift);
        _colors.Add(cLow); _colors.Add(cLow); _colors.Add(cLow);
        _fillTris.Add(loStart + 0); _fillTris.Add(loStart + 1); _fillTris.Add(loStart + 2);
    }

    private static Vector3 SafeDir(Vector3 v)
    {
        float m = v.magnitude;
        return m > 1e-6f ? v / m : Vector3.zero;
    }

    private void ApplyAimStyleDefaults()
    {
        if (Approximately(fillColor, new Color(0.95f, 0.22f, 0.18f, 1f)) ||
            Approximately(fillColor, new Color(0.46f, 0.88f, 1f, 1f)))
            fillColor = new Color(0.55f, 0.88f, 1.00f, 0.70f);
        if (Approximately(fillOverThreshold, new Color(1f, 0.05f, 0.05f, 1f)) ||
            Approximately(fillOverThreshold, new Color(1f, 0.62f, 0.22f, 1f)))
            fillOverThreshold = new Color(1.00f, 0.76f, 0.28f, 0.82f);
        if (Approximately(outlineColor, new Color(0.16f, 0f, 0f, 1f)) ||
            Approximately(outlineColor, new Color(0.03f, 0.13f, 0.15f, 1f)) ||
            Approximately(outlineColor, new Color(0.04f, 0.12f, 0.16f, 0.32f)))
            outlineColor = new Color(0.03f, 0.10f, 0.14f, 0.42f);

        if (Mathf.Abs(outlineOffset - 0.06f) < 0.001f ||
            Mathf.Abs(outlineOffset - 0.10f) < 0.001f ||
            Mathf.Abs(outlineOffset - 0.022f) < 0.001f ||
            Mathf.Abs(outlineOffset - 0.010f) < 0.001f)
            outlineOffset = 0.018f;
        if (Mathf.Abs(halfWidth - 0.13f) < 0.001f ||
            Mathf.Abs(halfWidth - 0.060f) < 0.001f ||
            Mathf.Abs(halfWidth - 0.044f) < 0.001f ||
            Mathf.Abs(halfWidth - 0.032f) < 0.001f ||
            Mathf.Abs(halfWidth - 0.026f) < 0.001f)
            halfWidth = 0.070f;
        if (targetScaleCount == 11 || targetScaleCount == 9 || targetScaleCount == 8 ||
            targetScaleCount == 14 || targetScaleCount == 16 || targetScaleCount == 28 || targetScaleCount == 20)
            targetScaleCount = 22;
        if (Mathf.Abs(scaleOverlapFactor - 1.30f) < 0.001f ||
            Mathf.Abs(scaleOverlapFactor - 1.08f) < 0.001f ||
            Mathf.Abs(scaleOverlapFactor - 0.58f) < 0.001f ||
            Mathf.Abs(scaleOverlapFactor - 0.62f) < 0.001f)
            scaleOverlapFactor = 0.86f;
        if (segmentSpacing <= 0.001f ||
            Mathf.Abs(segmentSpacing - 0.24f) < 0.001f ||
            Mathf.Abs(segmentSpacing - 0.25f) < 0.001f)
            segmentSpacing = 0.22f;
        if (Mathf.Abs(notchFactor - 0.28f) < 0.001f ||
            Mathf.Abs(notchFactor - 0.08f) < 0.001f ||
            Mathf.Abs(notchFactor - 0.0f) < 0.001f)
            notchFactor = 0.16f;
        if (Mathf.Abs(headLength - 0.45f) < 0.001f ||
            Mathf.Abs(headLength - 0.32f) < 0.001f ||
            Mathf.Abs(headLength - 0.24f) < 0.001f ||
            Mathf.Abs(headLength - 0.20f) < 0.001f ||
            Mathf.Abs(headLength - 0.17f) < 0.001f)
            headLength = 0.38f;
        if (Mathf.Abs(headHalfWidth - 0.22f) < 0.001f ||
            Mathf.Abs(headHalfWidth - 0.105f) < 0.001f ||
            Mathf.Abs(headHalfWidth - 0.075f) < 0.001f ||
            Mathf.Abs(headHalfWidth - 0.060f) < 0.001f ||
            Mathf.Abs(headHalfWidth - 0.048f) < 0.001f)
            headHalfWidth = 0.150f;
        if (Mathf.Abs(startOffset - 0.20f) < 0.001f ||
            Mathf.Abs(startOffset - 0.08f) < 0.001f ||
            Mathf.Abs(startOffset - 0.06f) < 0.001f)
            startOffset = 0f;
        if (Mathf.Abs(yOffset - 0.003f) < 0.001f || yOffset <= 0.001f)
            yOffset = 0.045f;
        if (Mathf.Abs(midValue - 0.65f) < 0.001f || Mathf.Abs(midValue - 0.78f) < 0.001f)
            midValue = 0.86f;
        if (Mathf.Abs(shadowValue - 0.35f) < 0.001f || Mathf.Abs(shadowValue - 0.58f) < 0.001f)
            shadowValue = 0.72f;
        if (visibleArcRatioAtSoftForce <= 0.001f || Mathf.Abs(visibleArcRatioAtSoftForce - 0.88f) < 0.001f)
            visibleArcRatioAtSoftForce = 1.00f;
        if (visibleArcRatioAtFullForce <= 0.001f ||
            Mathf.Abs(visibleArcRatioAtFullForce - 0.52f) < 0.001f ||
            Mathf.Abs(visibleArcRatioAtFullForce - 0.42f) < 0.001f)
            visibleArcRatioAtFullForce = 0.86f;
        if (maxVisibleArcAtSoftForce <= 0.001f ||
            Mathf.Abs(maxVisibleArcAtSoftForce - 3.20f) < 0.001f ||
            Mathf.Abs(maxVisibleArcAtSoftForce - 2.80f) < 0.001f)
            maxVisibleArcAtSoftForce = 4.80f;
        if (maxVisibleArcAtFullForce <= 0.001f ||
            Mathf.Abs(maxVisibleArcAtFullForce - 2.25f) < 0.001f ||
            Mathf.Abs(maxVisibleArcAtFullForce - 1.65f) < 0.001f)
            maxVisibleArcAtFullForce = 3.80f;
        if (minReadableArc <= 0.001f ||
            Mathf.Abs(minReadableArc - 0.55f) < 0.001f ||
            Mathf.Abs(minReadableArc - 0.78f) < 0.001f)
            minReadableArc = 1.05f;
        if (maxCumulativeTurnAtSoftForce <= 0.001f || Mathf.Abs(maxCumulativeTurnAtSoftForce - 220f) < 0.001f)
            maxCumulativeTurnAtSoftForce = 280f;
        if (maxCumulativeTurnAtFullForce <= 0.001f ||
            Mathf.Abs(maxCumulativeTurnAtFullForce - 135f) < 0.001f ||
            Mathf.Abs(maxCumulativeTurnAtFullForce - 120f) < 0.001f)
            maxCumulativeTurnAtFullForce = 210f;
        if (loopProximity <= 0.001f || Mathf.Abs(loopProximity - 0.18f) < 0.001f)
            loopProximity = 0.22f;
        if (displayArcFollowSpeed <= 0.001f)
            displayArcFollowSpeed = 24f;
        if (tailFadeInArc <= 0.001f)
            tailFadeInArc = 0.18f;
        if (farSegmentAlpha <= 0.001f)
            farSegmentAlpha = 0.78f;
        if (trajectorySmoothingPasses < 0)
            trajectorySmoothingPasses = 2;
        if (trajectoryCornerCut <= 0.001f)
            trajectoryCornerCut = 0.16f;
        if (maxDisplayedBendDegrees <= 0.001f)
            maxDisplayedBendDegrees = 180f;
        if (maxDisplayedTurnPerSegmentDegrees <= 0.001f)
            maxDisplayedTurnPerSegmentDegrees = 12f;
        if (trajectoryLerpSpeed <= 0.001f)
            trajectoryLerpSpeed = 18f;
        if (trajectoryLerpResetDistance <= 0.001f)
            trajectoryLerpResetDistance = 0.28f;
        if (stableTrajectorySampleCount < 12)
            stableTrajectorySampleCount = 32;
        if (fullPullTrajectoryLerpSpeed <= 0.001f)
            fullPullTrajectoryLerpSpeed = 4f;
        if (fullPullShapeDeadbandDistance <= 0.001f)
            fullPullShapeDeadbandDistance = 0.10f;
        if (fullPullDirectionDeadbandDegrees <= 0.001f)
            fullPullDirectionDeadbandDegrees = 6f;
        if (fullPullArcLengthDeadband <= 0.001f)
            fullPullArcLengthDeadband = 0.16f;
        if (fullPullArcDeadband <= 0.001f)
            fullPullArcDeadband = 0.16f;
        if (fullPullArcFollowSpeed <= 0.001f)
            fullPullArcFollowSpeed = 4f;
        if (fullPullHeadTangentFollowSpeed <= 0.001f)
            fullPullHeadTangentFollowSpeed = 6f;
        if (fullPullHeadTangentDeadbandDegrees <= 0.001f)
            fullPullHeadTangentDeadbandDegrees = 5f;
        if (LooksLikeThinAlphaCurve(alphaByForce))
            alphaByForce = AnimationCurve.EaseInOut(0f, 0.56f, 1f, 0.92f);
    }

    private static bool LooksLikeThinAlphaCurve(AnimationCurve curve)
    {
        if (curve == null || curve.length < 2) return false;
        float first = curve.keys[0].value;
        float last = curve.keys[curve.length - 1].value;
        return (Mathf.Abs(first - 0.38f) < 0.01f && Mathf.Abs(last - 0.72f) < 0.01f) ||
               (Mathf.Abs(first - 0.36f) < 0.01f && Mathf.Abs(last - 0.72f) < 0.01f);
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

    private void CleanupLegacyChild()
    {
        var old = transform.Find("LaunchArrow");
        if (old == null) return;
        if (Application.isPlaying) Destroy(old.gameObject);
        else DestroyImmediate(old.gameObject);
    }

    private void BuildSceneRootHolder()
    {
        var go = new GameObject("_LaunchArrowMesh_" + gameObject.name);
        go.hideFlags = HideFlags.DontSave;
        go.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
        go.transform.localScale = Vector3.one;
        _holder = go;

        _meshFilter = go.AddComponent<MeshFilter>();
        _meshRenderer = go.AddComponent<MeshRenderer>();

        _mesh = new Mesh { name = "LaunchArrow_Mesh" };
        _mesh.MarkDynamic();
        _mesh.subMeshCount = 2;
        _meshFilter.sharedMesh = _mesh;

        _outlineMat = CreateUnlitMaterial("LaunchArrow_Outline", outlineColor);
        _fillMat = CreateUnlitMaterial("LaunchArrow_Fill", fillColor);
        _meshRenderer.sharedMaterials = new[] { _outlineMat, _fillMat };
        _meshRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        _meshRenderer.receiveShadows = false;
        _meshRenderer.sortingOrder = 40;
        _meshRenderer.enabled = false;
    }

    /// <summary>
    /// URP Particles/Unlit：轻量 unlit + vertex color 乘 _BaseColor 的标准组合。
    /// 透明混合用于降低遮挡；URP 普通 Unlit 不吃 vertex color，Particles 版本吃。
    /// </summary>
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
        if (mat.HasProperty(ZTestID)) mat.SetFloat(ZTestID, (float)UnityEngine.Rendering.CompareFunction.Always);
        if (mat.HasProperty(CullID)) mat.SetFloat(CullID, (float)UnityEngine.Rendering.CullMode.Off);
        if (mat.HasProperty("_SrcBlend")) mat.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
        if (mat.HasProperty("_DstBlend")) mat.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
        mat.SetOverrideTag("RenderType", "Transparent");
        mat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
        mat.EnableKeyword("_ALPHABLEND_ON");
        mat.DisableKeyword("_ALPHAPREMULTIPLY_ON");
        mat.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent + 80;
        return mat;
    }
}
