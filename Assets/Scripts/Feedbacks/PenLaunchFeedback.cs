using System.Collections.Generic;
using MoreMountains.Feedbacks;
using UnityEngine;

/// <summary>
/// 发射瞬间的手感反馈 —— 和 <see cref="PenCollisionFeedback"/> 对称。
///
/// 订阅 <see cref="PenEntity.OnLaunched"/>，用 MMF_Player 播设计师配置的反馈链：
/// 典型配置：Camera FOV punch / Freeze frame 1-2 帧 / 镜头推近 / AudioSource "twang" /
/// 释放粒子。强度 = 发射 force（0-1），传给 MMF_Player.FeedbacksIntensity 让所有反馈
/// 按比例缩放。
///
/// 组件独立是故意的 (SRP)：PenEntity 只管物理入口，反馈层吃 event 不耦合。
/// </summary>
[RequireComponent(typeof(PenEntity))]
public class PenLaunchFeedback : MonoBehaviour
{
    [Tooltip("发射时播放的 MMF_Player。留空不触发反馈，逻辑不崩。\n" +
             "建议配置：Camera FOV Punch、MMF_FreezeFrame (1-2 帧)、AudioSource (twang/release)、\n" +
             "ParticlesInstantiation (可选的释放火花)。所有反馈的 intensity 会按 force 自动缩放。")]
    [SerializeField] private MMF_Player launchFeedbacks;

    [Tooltip("低于此 force 不触发反馈（避免轻点取消或微拉也响）。默认 0.2。")]
    [SerializeField] [Range(0f, 1f)] private float minTriggerForce = 0.2f;

    [Tooltip("即便 launchFeedbacks 未配置，也默认调用 FocusCameraController FOV 轻击，让发射瞬间至少有镜头反应。\n" +
             "配置了 MMF 后可关掉走 MMF 管道。")]
    [SerializeField] private bool fallbackCameraPunch = true;

    [Tooltip("fallbackCameraPunch 的强度系数。发射是主动动作，相机反馈强度比碰撞弱（0.4-0.6 典型）。")]
    [SerializeField] [Range(0f, 1f)] private float cameraFallbackScale = 0.5f;

    [Header("发射后的可读性反馈")]
    [Tooltip("玩家瞄准事件通道。BattleStateMachine 会在运行时注入；Inspector 留空也能工作。")]
    [SerializeField] private AimPhaseChannelSO _aimChannel;
    [Tooltip("发射后显示短暂的实际轨迹/预判对照/原因提示。")]
    [SerializeField] private bool drawReadabilityFeedback = true;
    [Tooltip("实际轨迹最多记录多久（秒）。")]
    [SerializeField] private float actualTrailDuration = 1.15f;
    [Tooltip("上一帧预判线作为淡淡对照保留多久（秒）。")]
    [SerializeField] private float predictionGhostDuration = 0.72f;
    [Tooltip("所有发射后提示的淡出时间（秒）。")]
    [SerializeField] private float fadeDuration = 0.46f;
    [Tooltip("实际轨迹采样间距（m）。太小会像 debug 线，太大又读不出弯曲。")]
    [SerializeField] private float trailSampleSpacing = 0.055f;
    [Tooltip("实际轨迹线宽（m）。")]
    [SerializeField] private float actualTrailWidth = 0.032f;
    [Tooltip("上一帧预判对照线宽（m）。")]
    [SerializeField] private float predictionGhostWidth = 0.018f;
    [SerializeField] private Color actualTrailColor = new Color(1.00f, 0.83f, 0.45f, 0.72f);
    [SerializeField] private Color predictionGhostColor = new Color(0.58f, 0.90f, 1.00f, 0.34f);
    [SerializeField] private Color spinCueColor = new Color(1.00f, 0.55f, 0.18f, 0.70f);
    [SerializeField] private Color frictionCueColor = new Color(0.96f, 0.78f, 0.46f, 0.58f);

    private static readonly int BaseColorID = Shader.PropertyToID("_BaseColor");
    private static readonly int ColorID = Shader.PropertyToID("_Color");

    private PenEntity _pen;
    private Rigidbody _rb;
    private Material _readabilityMaterial;
    private GameObject _feedbackRoot;
    private LineRenderer _actualTrail;
    private LineRenderer _predictionGhost;
    private LineRenderer _causeCue;
    private readonly List<Vector3> _actualPoints = new List<Vector3>(64);
    private AimSample _lastAim;
    private bool _hasLastAim;
    private bool _aimSubscribed;
    private bool _feedbackActive;
    private bool _recordingActual;
    private float _feedbackAge;
    private Vector3 _trackedPointLocal;
    private float _feedbackY;

    private void Awake()
    {
        _pen = GetComponent<PenEntity>();
        _rb = _pen != null ? _pen.rb : GetComponent<Rigidbody>();
    }

    private void OnEnable()
    {
        if (_pen != null) _pen.OnLaunched += HandleLaunch;
        SubscribeAimChannel();
    }

    private void OnDisable()
    {
        if (_pen != null) _pen.OnLaunched -= HandleLaunch;
        UnsubscribeAimChannel();
        HideReadabilityFeedback();
    }

    private void OnDestroy()
    {
        if (_feedbackRoot != null)
            Destroy(_feedbackRoot);
        if (_readabilityMaterial != null)
            Destroy(_readabilityMaterial);
    }

    public void Configure(AimPhaseChannelSO aimChannel)
    {
        if (_aimChannel == aimChannel)
            return;

        bool shouldResubscribe = isActiveAndEnabled;
        if (shouldResubscribe)
            UnsubscribeAimChannel();

        _aimChannel = aimChannel;

        if (shouldResubscribe)
            SubscribeAimChannel();
    }

    private void LateUpdate()
    {
        if (!_feedbackActive)
            return;

        _feedbackAge += Time.deltaTime;

        if (_recordingActual)
            SampleActualTrail();

        float fadeStart = Mathf.Max(0.05f, actualTrailDuration);
        float alpha = _feedbackAge <= fadeStart
            ? 1f
            : 1f - ((_feedbackAge - fadeStart) / Mathf.Max(0.05f, fadeDuration));
        alpha = Mathf.Clamp01(alpha);

        float predictionAlpha = _feedbackAge <= predictionGhostDuration
            ? 1f
            : 1f - ((_feedbackAge - predictionGhostDuration) / Mathf.Max(0.05f, fadeDuration));
        predictionAlpha = Mathf.Clamp01(predictionAlpha);

        ApplyLineColor(_actualTrail, actualTrailColor, alpha);
        ApplyLineColor(_predictionGhost, predictionGhostColor, predictionAlpha);
        ApplyLineColor(_causeCue, GetCauseColor(), alpha);

        if (_feedbackAge >= actualTrailDuration || IsPenSlow())
            _recordingActual = false;

        if (alpha <= 0.001f && predictionAlpha <= 0.001f)
            HideReadabilityFeedback();
    }

    private void HandleLaunch(float force)
    {
        if (force < minTriggerForce) return;
        if (launchFeedbacks != null)
        {
            launchFeedbacks.FeedbacksIntensity = force;
            launchFeedbacks.PlayFeedbacks(transform.position);
        }
        if (fallbackCameraPunch)
            FocusCameraController.Instance?.FocusOn(transform.position, force * cameraFallbackScale);

        if (drawReadabilityFeedback)
            BeginReadabilityFeedback();
    }

    private void SubscribeAimChannel()
    {
        if (_aimSubscribed || _aimChannel == null)
            return;

        _aimChannel.OnUpdated += HandleAimUpdated;
        _aimChannel.OnCancelled += HandleAimCancelled;
        _aimSubscribed = true;
    }

    private void UnsubscribeAimChannel()
    {
        if (!_aimSubscribed || _aimChannel == null)
            return;

        _aimChannel.OnUpdated -= HandleAimUpdated;
        _aimChannel.OnCancelled -= HandleAimCancelled;
        _aimSubscribed = false;
    }

    private void HandleAimUpdated(AimSample sample)
    {
        _lastAim = sample;
        _hasLastAim = true;
    }

    private void HandleAimCancelled()
    {
        _hasLastAim = false;
    }

    private void BeginReadabilityFeedback()
    {
        if (!_hasLastAim || _rb == null)
            return;

        EnsureReadabilityObjects();
        _feedbackActive = true;
        _recordingActual = true;
        _feedbackAge = 0f;
        _actualPoints.Clear();
        _trackedPointLocal = transform.InverseTransformPoint(_lastAim.ContactPointWorld);
        _feedbackY = _lastAim.ContactPointWorld.y + 0.010f;

        Vector3 first = GetActualVisualPoint();
        _actualPoints.Add(first);
        ApplyPositions(_actualTrail, _actualPoints);
        DrawPredictionGhost();
        DrawCauseCue();
        ApplyLineColor(_actualTrail, actualTrailColor, 1f);
        ApplyLineColor(_predictionGhost, predictionGhostColor, 1f);
        ApplyLineColor(_causeCue, GetCauseColor(), 1f);
    }

    private void SampleActualTrail()
    {
        Vector3 point = GetActualVisualPoint();
        if (_actualPoints.Count == 0 ||
            Vector3.Distance(Flatten(_actualPoints[_actualPoints.Count - 1]), Flatten(point)) >= trailSampleSpacing)
        {
            _actualPoints.Add(point);
            ApplyPositions(_actualTrail, _actualPoints);
        }
    }

    private Vector3 GetActualVisualPoint()
    {
        Vector3 p = transform.TransformPoint(_trackedPointLocal);
        p.y = _feedbackY;
        return p;
    }

    private void DrawPredictionGhost()
    {
        var trajectory = _lastAim.PredictedTrajectory;
        if (trajectory == null || trajectory.Count < 2)
        {
            _predictionGhost.positionCount = 0;
            return;
        }

        int count = Mathf.Min(trajectory.Count, 28);
        _predictionGhost.positionCount = count;
        Vector3 offset = _lastAim.ContactPointWorld - trajectory[0];
        offset.y = 0f;

        for (int i = 0; i < count; i++)
        {
            Vector3 p = trajectory[i] + offset;
            p.y = _feedbackY + 0.004f;
            _predictionGhost.SetPosition(i, p);
        }
    }

    private void DrawCauseCue()
    {
        if (_causeCue == null)
            return;

        if (_lastAim.Spin01 >= 0.18f)
        {
            DrawSpinCue();
            return;
        }

        if (_lastAim.Friction01 >= 0.52f)
        {
            DrawFrictionCue();
            return;
        }

        if (_lastAim.Force <= 0.28f)
        {
            DrawSoftLaunchCue();
            return;
        }

        _causeCue.positionCount = 0;
    }

    private void DrawSpinCue()
    {
        const int steps = 14;
        Vector3 fwd = SafeFlatDirection(_lastAim.LaunchDirection, transform.forward);
        Vector3 side = Vector3.Cross(Vector3.up, fwd).normalized;
        float sign = _lastAim.ContactOffsetNormalized >= 0f ? 1f : -1f;
        float radius = Mathf.Lerp(0.12f, 0.24f, _lastAim.Spin01);
        float sweep = Mathf.Lerp(74f, 190f, _lastAim.Spin01);
        Vector3 center = _lastAim.EffectiveContactPointWorld;
        center.y = _feedbackY + 0.008f;

        _causeCue.positionCount = steps;
        for (int i = 0; i < steps; i++)
        {
            float t = i / (float)(steps - 1);
            float angle = sign * Mathf.Lerp(-16f, sweep, t);
            Vector3 dir = Quaternion.AngleAxis(angle, Vector3.up) * (side * sign);
            _causeCue.SetPosition(i, center + dir * radius);
        }
    }

    private void DrawFrictionCue()
    {
        const int steps = 7;
        Vector3 fwd = SafeFlatDirection(_lastAim.LaunchDirection, transform.forward);
        Vector3 right = Vector3.Cross(Vector3.up, fwd).normalized;
        Vector3 start = _lastAim.ContactPointWorld + fwd * 0.12f;
        start.y = _feedbackY + 0.006f;
        float spacing = Mathf.Lerp(0.045f, 0.075f, _lastAim.Friction01);
        float width = Mathf.Lerp(0.030f, 0.060f, _lastAim.Friction01);

        _causeCue.positionCount = steps;
        for (int i = 0; i < steps; i++)
        {
            float zig = (i % 2 == 0 ? -1f : 1f) * width;
            _causeCue.SetPosition(i, start + fwd * (i * spacing) + right * zig);
        }
    }

    private void DrawSoftLaunchCue()
    {
        const int steps = 5;
        Vector3 fwd = SafeFlatDirection(_lastAim.LaunchDirection, transform.forward);
        Vector3 right = Vector3.Cross(Vector3.up, fwd).normalized;
        Vector3 start = _lastAim.ContactPointWorld + fwd * 0.07f;
        start.y = _feedbackY + 0.006f;

        _causeCue.positionCount = steps;
        _causeCue.SetPosition(0, start - right * 0.035f);
        _causeCue.SetPosition(1, start + fwd * 0.045f);
        _causeCue.SetPosition(2, start + right * 0.035f);
        _causeCue.SetPosition(3, start + fwd * 0.090f);
        _causeCue.SetPosition(4, start);
    }

    private Color GetCauseColor()
    {
        if (!_hasLastAim)
            return spinCueColor;
        if (_lastAim.Spin01 >= 0.18f)
            return spinCueColor;
        return frictionCueColor;
    }

    private void EnsureReadabilityObjects()
    {
        if (_feedbackRoot == null)
        {
            _feedbackRoot = new GameObject("_LaunchReadability_" + gameObject.name);
            _feedbackRoot.hideFlags = HideFlags.DontSave;
            _feedbackRoot.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
        }

        if (_readabilityMaterial == null)
            _readabilityMaterial = CreateReadabilityMaterial();

        if (_actualTrail == null)
            _actualTrail = CreateLine("ActualTrail", actualTrailWidth);
        if (_predictionGhost == null)
            _predictionGhost = CreateLine("PredictionGhost", predictionGhostWidth);
        if (_causeCue == null)
            _causeCue = CreateLine("CauseCue", predictionGhostWidth * 1.35f);
    }

    private LineRenderer CreateLine(string lineName, float width)
    {
        var go = new GameObject(lineName);
        go.hideFlags = HideFlags.DontSave;
        go.transform.SetParent(_feedbackRoot.transform, false);

        var line = go.AddComponent<LineRenderer>();
        line.useWorldSpace = true;
        line.sharedMaterial = _readabilityMaterial;
        line.positionCount = 0;
        line.startWidth = width;
        line.endWidth = width;
        line.numCapVertices = 3;
        line.numCornerVertices = 3;
        line.alignment = LineAlignment.View;
        line.textureMode = LineTextureMode.Stretch;
        line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        line.receiveShadows = false;
        return line;
    }

    private void HideReadabilityFeedback()
    {
        _feedbackActive = false;
        _recordingActual = false;
        _actualPoints.Clear();
        if (_actualTrail != null) _actualTrail.positionCount = 0;
        if (_predictionGhost != null) _predictionGhost.positionCount = 0;
        if (_causeCue != null) _causeCue.positionCount = 0;
    }

    private static void ApplyPositions(LineRenderer line, List<Vector3> points)
    {
        if (line == null)
            return;

        line.positionCount = points.Count;
        for (int i = 0; i < points.Count; i++)
            line.SetPosition(i, points[i]);
    }

    private static void ApplyLineColor(LineRenderer line, Color baseColor, float alphaMultiplier)
    {
        if (line == null)
            return;

        Color c = baseColor;
        c.a *= Mathf.Clamp01(alphaMultiplier);
        line.startColor = c;
        line.endColor = c;
    }

    private bool IsPenSlow()
    {
        if (_rb == null || _feedbackAge < 0.18f)
            return false;

        return _rb.linearVelocity.sqrMagnitude <= 0.01f;
    }

    private static Vector3 SafeFlatDirection(Vector3 preferred, Vector3 fallback)
    {
        preferred.y = 0f;
        if (preferred.sqrMagnitude > 1e-6f)
            return preferred.normalized;

        fallback.y = 0f;
        return fallback.sqrMagnitude > 1e-6f ? fallback.normalized : Vector3.forward;
    }

    private static Vector3 Flatten(Vector3 p)
    {
        p.y = 0f;
        return p;
    }

    private static Material CreateReadabilityMaterial()
    {
        Shader shader = Shader.Find("Universal Render Pipeline/Particles/Unlit");
        if (shader == null) shader = Shader.Find("Universal Render Pipeline/Unlit");
        if (shader == null) shader = Shader.Find("Unlit/Color");

        var mat = new Material(shader) { name = "LaunchReadability_Line" };
        Color white = Color.white;
        if (mat.HasProperty(BaseColorID)) mat.SetColor(BaseColorID, white);
        if (mat.HasProperty(ColorID)) mat.SetColor(ColorID, white);
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
