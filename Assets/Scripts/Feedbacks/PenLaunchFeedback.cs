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

    [Tooltip("低于此 force 不显示释放后的可读性轨迹。这个阈值应接近战斗最小有效力度，" +
             "不要和声音/镜头反馈阈值绑定，否则轻弹会发射但没有轨迹线。")]
    [SerializeField] [Range(0f, 1f)] private float minReadabilityForce = 0.06f;

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
    [Tooltip("释放后第一段轨迹的最小采样间距（m）。比常规间距小，避免轻弹/短滑时线条只有一个点而不可见。")]
    [SerializeField] private float firstTrailSampleSpacing = 0.018f;
    [Tooltip("真实轨迹还没积累到第二个采样点前，先沿发射方向画一小段起笔，保证释放瞬间可见。")]
    [SerializeField] private float initialTrailStubLength = 0.040f;
    [Tooltip("实际轨迹线宽（m）。")]
    [SerializeField] private float actualTrailWidth = 0.032f;
    [Tooltip("上一帧预判对照线宽（m）。")]
    [SerializeField] private float predictionGhostWidth = 0.018f;
    [Tooltip("发射后在 Console 打印预判和实际轨迹误差，方便调试预判线可信度。")]
    [SerializeField] private bool logPredictionAccuracy = true;
    [Tooltip("末端误差低于这个距离时认为预判足够可信。")]
    [SerializeField] private float trustedEndError = 0.28f;
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
    private bool _accuracyReported;
    private float _feedbackAge;
    private float _peakAngularVelocity;
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
        {
            SampleActualTrail();
            if (_rb != null)
                _peakAngularVelocity = Mathf.Max(_peakAngularVelocity, _rb.angularVelocity.magnitude);
        }

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

        if (_recordingActual && (_feedbackAge >= actualTrailDuration || IsPenSlow()))
        {
            _recordingActual = false;
            ReportPredictionAccuracy();
        }

        if (alpha <= 0.001f && predictionAlpha <= 0.001f)
            HideReadabilityFeedback();
    }

    private void HandleLaunch(float force)
    {
        bool shouldPlayImpactFeedback = force >= minTriggerForce;
        if (shouldPlayImpactFeedback && launchFeedbacks != null)
        {
            launchFeedbacks.FeedbacksIntensity = force;
            launchFeedbacks.PlayFeedbacks(transform.position);
        }
        if (shouldPlayImpactFeedback && fallbackCameraPunch)
            FocusCameraController.Instance?.FocusOn(transform.position, force * cameraFallbackScale);

        if (drawReadabilityFeedback && force >= minReadabilityForce)
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
        if (_rb == null)
            return;

        EnsureReadabilityObjects();
        _feedbackActive = true;
        _recordingActual = true;
        _accuracyReported = false;
        _feedbackAge = 0f;
        _peakAngularVelocity = _rb != null ? _rb.angularVelocity.magnitude : 0f;
        _actualPoints.Clear();
        Vector3 contactPoint = GetLaunchContactPointForFeedback();
        _trackedPointLocal = transform.InverseTransformPoint(contactPoint);
        _feedbackY = contactPoint.y + 0.010f;

        Vector3 first = GetActualVisualPoint();
        _actualPoints.Add(first);
        ApplyActualTrailPositions();
        DrawPredictionGhost();
        DrawCauseCue();
        ApplyLineColor(_actualTrail, actualTrailColor, 1f);
        ApplyLineColor(_predictionGhost, predictionGhostColor, 1f);
        ApplyLineColor(_causeCue, GetCauseColor(), 1f);
    }

    private void SampleActualTrail()
    {
        Vector3 point = GetActualVisualPoint();
        float requiredSpacing = _actualPoints.Count <= 1
            ? Mathf.Max(0.001f, firstTrailSampleSpacing)
            : Mathf.Max(0.001f, trailSampleSpacing);

        if (_actualPoints.Count == 0 ||
            Vector3.Distance(Flatten(_actualPoints[_actualPoints.Count - 1]), Flatten(point)) >= requiredSpacing)
        {
            _actualPoints.Add(point);
            ApplyActualTrailPositions();
        }
    }

    private Vector3 GetLaunchContactPointForFeedback()
    {
        if (_pen != null && _pen.LastLaunchSnapshot.IsValid)
            return _pen.LastLaunchSnapshot.ContactPointWorld;

        if (_hasLastAim)
            return _lastAim.ContactPointWorld;

        return transform.position;
    }

    private Vector3 GetActualVisualPoint()
    {
        Vector3 p = transform.TransformPoint(_trackedPointLocal);
        p.y = _feedbackY;
        return p;
    }

    private void DrawPredictionGhost()
    {
        if (!_hasLastAim)
        {
            _predictionGhost.positionCount = 0;
            return;
        }

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

    private void ReportPredictionAccuracy()
    {
        if (_accuracyReported || !_hasLastAim)
            return;
        _accuracyReported = true;

        LaunchPredictionAccuracy accuracy = ComputePredictionAccuracy();
        if (!logPredictionAccuracy)
            return;

        string trust = accuracy.EndError <= Mathf.Max(0.01f, trustedEndError) ? "可信" : "偏差大";
        Debug.Log(
            $"[LaunchAccuracy] {trust} | " +
            $"pred={accuracy.PredictedDistance:F2}m actual={accuracy.ActualDistance:F2}m " +
            $"endErr={accuracy.EndError:F2}m avgErr={accuracy.AverageError:F2}m " +
            $"spinPeak={_peakAngularVelocity:F2}rad/s friction={_lastAim.Friction01:F2} force={_lastAim.Force:F2}",
            this);
    }

    private LaunchPredictionAccuracy ComputePredictionAccuracy()
    {
        var predicted = _lastAim.PredictedTrajectory;
        float predictedDistance = MeasureArc(predicted);
        float actualDistance = MeasureArc(_actualPoints);
        float endError = 0f;
        float averageError = 0f;

        if (predicted != null && predicted.Count >= 2 && _actualPoints.Count >= 2)
        {
            Vector3 predictionOffset = _lastAim.ContactPointWorld - predicted[0];
            predictionOffset.y = 0f;
            Vector3 predictedEnd = predicted[predicted.Count - 1] + predictionOffset;
            predictedEnd.y = 0f;
            Vector3 actualEnd = _actualPoints[_actualPoints.Count - 1];
            actualEnd.y = 0f;
            endError = Vector3.Distance(predictedEnd, actualEnd);
            averageError = MeasureAverageError(predicted, _actualPoints, predictionOffset);
        }

        return new LaunchPredictionAccuracy(predictedDistance, actualDistance, endError, averageError);
    }

    private static float MeasureAverageError(IReadOnlyList<Vector3> predicted, IReadOnlyList<Vector3> actual, Vector3 predictionOffset)
    {
        if (predicted == null || actual == null || predicted.Count < 2 || actual.Count < 2)
            return 0f;

        float actualArc = MeasureArc(actual);
        float predictedArc = MeasureArc(predicted);
        if (actualArc <= 1e-5f || predictedArc <= 1e-5f)
            return 0f;

        const int SAMPLE_COUNT = 8;
        float total = 0f;
        for (int i = 0; i < SAMPLE_COUNT; i++)
        {
            float t = i / (float)(SAMPLE_COUNT - 1);
            Vector3 a = SampleByArc01(actual, actualArc, t);
            Vector3 p = SampleByArc01(predicted, predictedArc, t) + predictionOffset;
            a.y = 0f;
            p.y = 0f;
            total += Vector3.Distance(a, p);
        }

        return total / SAMPLE_COUNT;
    }

    private static float MeasureArc(IReadOnlyList<Vector3> points)
    {
        if (points == null || points.Count < 2)
            return 0f;

        float total = 0f;
        for (int i = 1; i < points.Count; i++)
        {
            Vector3 a = points[i - 1]; a.y = 0f;
            Vector3 b = points[i]; b.y = 0f;
            total += Vector3.Distance(a, b);
        }
        return total;
    }

    private static Vector3 SampleByArc01(IReadOnlyList<Vector3> points, float totalArc, float t)
    {
        if (points == null || points.Count == 0)
            return Vector3.zero;
        if (points.Count == 1 || totalArc <= 1e-5f)
            return points[0];

        float target = Mathf.Clamp01(t) * totalArc;
        float walked = 0f;
        for (int i = 1; i < points.Count; i++)
        {
            Vector3 a = points[i - 1]; a.y = 0f;
            Vector3 b = points[i]; b.y = 0f;
            float segment = Vector3.Distance(a, b);
            if (segment <= 1e-5f)
                continue;

            if (walked + segment >= target)
            {
                float localT = (target - walked) / segment;
                return Vector3.Lerp(points[i - 1], points[i], localT);
            }

            walked += segment;
        }

        return points[points.Count - 1];
    }

    private void DrawCauseCue()
    {
        if (_causeCue == null)
            return;

        if (!_hasLastAim)
        {
            _causeCue.positionCount = 0;
            return;
        }

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

    private void ApplyActualTrailPositions()
    {
        if (_actualTrail == null)
            return;

        if (_actualPoints.Count == 1 && initialTrailStubLength > 0f)
        {
            Vector3 direction = GetInitialLaunchDirection();
            _actualTrail.positionCount = 2;
            _actualTrail.SetPosition(0, _actualPoints[0]);
            _actualTrail.SetPosition(1, _actualPoints[0] + direction * initialTrailStubLength);
            return;
        }

        ApplyPositions(_actualTrail, _actualPoints);
    }

    private Vector3 GetInitialLaunchDirection()
    {
        Vector3 direction = Vector3.zero;
        if (_pen != null && _pen.LastLaunchSnapshot.IsValid)
            direction = _pen.LastLaunchSnapshot.Direction;
        else if (_hasLastAim)
            direction = _lastAim.LaunchDirection;
        else if (_rb != null)
            direction = _rb.linearVelocity;

        direction.y = 0f;
        if (direction.sqrMagnitude > 1e-6f)
            return direction.normalized;

        Vector3 fallback = transform.forward;
        fallback.y = 0f;
        return fallback.sqrMagnitude > 1e-6f ? fallback.normalized : Vector3.forward;
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

    private readonly struct LaunchPredictionAccuracy
    {
        public readonly float PredictedDistance;
        public readonly float ActualDistance;
        public readonly float EndError;
        public readonly float AverageError;

        public LaunchPredictionAccuracy(float predictedDistance, float actualDistance, float endError, float averageError)
        {
            PredictedDistance = predictedDistance;
            ActualDistance = actualDistance;
            EndError = endError;
            AverageError = averageError;
        }
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
