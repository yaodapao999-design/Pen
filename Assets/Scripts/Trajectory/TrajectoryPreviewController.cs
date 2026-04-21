using UnityEngine;

/// <summary>
/// 蓄力阶段预测管线编排:节流调用 Simulator → Filter → Presenter。
/// 披露级别切换复用最近一次 TrajectorySample,不触发重仿真。
/// 镜像不可用或非 Idle / 蓄力力度不足时,直接 Hide(等同 Minimal 呈现),
/// 不使用解析近似 fallback(与 GDD §6 / 非目标对齐)。
/// </summary>
public class TrajectoryPreviewController : MonoBehaviour
{
    [Header("依赖引用")]
    [SerializeField] private BattleStateMachine battleStateMachine;
    [SerializeField] private PhysicsMirrorWorld mirrorWorld;
    [SerializeField] private MirrorSimulationConfig simulationConfig;
    [SerializeField] private TrajectoryPreviewPresenter presenter;

    [Header("披露级别")]
    [SerializeField] private DisclosureLevel disclosureLevel = DisclosureLevel.Trend;
    [SerializeField] private TrajectoryDisclosureFilter.FilterConfig filterConfig = TrajectoryDisclosureFilter.FilterConfig.Default;

    [Header("节流与开关")]
    [Tooltip("两次镜像仿真的最小间隔(毫秒)")]
    [SerializeField, Range(0f, 500f)] private float throttleMs = 60f;

    [Tooltip("触发重仿真的输入变化阈值(米/单位):方向、施力点、力度任一超过即允许重仿真")]
    [SerializeField, Min(0f)] private float dragDeltaThreshold = 0.02f;

    [Tooltip("蓄力力度低于此值视为未发力,不做预测")]
    [SerializeField, Range(0f, 1f)] private float minForceToPredict = 0.02f;

    [Tooltip("全局开关:关闭时 Presenter 保持 Hide(等同玩家设置了 Minimal)")]
    [SerializeField] private bool enablePreview = true;

    [Tooltip("勾选时只在玩家正在拖拽蓄力期间显示预测;松手/未开始拖拽时立即隐藏。默认勾选,避免 Idle 初帧因上次 LaunchForce 残留而瞬显预测")]
    [SerializeField] private bool onlyShowWhileDragging = true;

    public DisclosureLevel DisclosureLevel
    {
        get => disclosureLevel;
        set => disclosureLevel = value;
    }

    public bool EnablePreview
    {
        get => enablePreview;
        set => enablePreview = value;
    }

    private readonly TrajectorySimulator _simulator = new TrajectorySimulator();
    private readonly DisclosedTrajectory _disclosedBuffer = new DisclosedTrajectory();

    private TrajectorySample _lastSample;
    private DisclosureLevel _lastRenderedLevel = (DisclosureLevel)(-1);
    private float _lastPredictTime = -999f;
    private Vector3 _lastInputContact;
    private Vector3 _lastInputDir;
    private float _lastInputForce;
    private bool _isPreviewVisible;

    private void OnDisable()
    {
        HideNow();
    }

    private void Update()
    {
        if (!ShouldPredictThisFrame(out BattleContext ctx))
        {
            if (_isPreviewVisible) HideNow();
            return;
        }

        if (!mirrorWorld.EnsureInitialized())
        {
            if (_isPreviewVisible) HideNow();
            return;
        }

        if (disclosureLevel == DisclosureLevel.Minimal)
        {
            if (_isPreviewVisible) HideNow();
            return;
        }

        bool needSimulate = ShouldResimulate(ctx);
        bool needRender = needSimulate || _lastRenderedLevel != disclosureLevel;

        if (needSimulate)
        {
            var snap = PenSnapshot.From(ctx.pen);
            if (!snap.IsValid) { if (_isPreviewVisible) HideNow(); return; }

            var input = new LaunchInput(ctx.LaunchDirection, ctx.LaunchForce, ctx.ContactPointWorld, snap);
            _lastSample = _simulator.Simulate(mirrorWorld, input, simulationConfig);
            _lastPredictTime = Time.unscaledTime;
            _lastInputContact = ctx.ContactPointWorld;
            _lastInputDir = ctx.LaunchDirection;
            _lastInputForce = ctx.LaunchForce;
        }

        if (needRender)
        {
            TrajectoryDisclosureFilter.Filter(_lastSample, disclosureLevel, filterConfig, _disclosedBuffer);
            presenter.Show(_disclosedBuffer);
            _lastRenderedLevel = disclosureLevel;
            _isPreviewVisible = true;
        }
    }

    private bool ShouldPredictThisFrame(out BattleContext ctx)
    {
        ctx = null;
        if (!enablePreview) return false;
        if (battleStateMachine == null || !battleStateMachine.IsIdle) return false;
        if (onlyShowWhileDragging && !battleStateMachine.IsDragging) return false;
        ctx = battleStateMachine.Ctx;
        if (ctx == null || ctx.pen == null) return false;
        if (ctx.LaunchForce < minForceToPredict) return false;
        if (mirrorWorld == null || simulationConfig == null || presenter == null) return false;
        return true;
    }

    private bool ShouldResimulate(BattleContext ctx)
    {
        if (_lastSample == null || _lastSample.StopReason == TrajectoryStopReason.NotStarted) return true;
        if ((Time.unscaledTime - _lastPredictTime) * 1000f < throttleMs) return false;

        float thSq = dragDeltaThreshold * dragDeltaThreshold;
        float dirDeltaSq = (ctx.LaunchDirection - _lastInputDir).sqrMagnitude;
        float contactDeltaSq = (ctx.ContactPointWorld - _lastInputContact).sqrMagnitude;
        float forceDelta = Mathf.Abs(ctx.LaunchForce - _lastInputForce);

        return dirDeltaSq > thSq || contactDeltaSq > thSq || forceDelta > dragDeltaThreshold;
    }

    private void HideNow()
    {
        if (presenter != null) presenter.Hide();
        _lastRenderedLevel = (DisclosureLevel)(-1);
        _isPreviewVisible = false;
    }
}
