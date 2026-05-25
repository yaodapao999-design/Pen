using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;

public class BattleStateMachine : MonoBehaviour
{
    [SerializeField] private PenEntity pen;
    [SerializeField] private PenEntity enemyPen;

    [Header("瞄准事件通道")]
    [Tooltip("玩家瞄准阶段事件广播 SO。IdleState 在 Begin/Update/Release/Cancel 各点发事件，\n" +
             "视觉层（PenDragVisuals、PenLaunchArrowView、PenAimThresholdFX、PenPressFeedback、PenHoverPresenter）订阅。\n" +
             "资产路径建议：Assets/ScriptableObjects/Channels/AimPhaseChannel.asset")]
    [SerializeField] private AimPhaseChannelSO _aimChannel;

    [Header("战斗阶段镜头（GameManager 调度）")]
    [SerializeField] private Unity.Cinemachine.CinemachineCamera battleCamera;

    [Header("战斗阶段根节点（非战斗阶段整棵隐藏）")]
    [Tooltip("战斗专属物体（战斗 UI 等）放这下面。注意：BattleStateMachine 自己不能放在这里面，否则失活后无法重新激活")]
    [SerializeField] private GameObject battleRoot;

    [Header("退出战斗时的延迟隐藏")]
    [Tooltip("退出战斗（进 Workshop/Shop）时，玩家笔的 SetActive(false) 延迟秒数")]
    [SerializeField] private float _hidePensDelay = 2f;

    public Unity.Cinemachine.CinemachineCamera BattleCamera => battleCamera;
    public GameObject BattleRoot => battleRoot;
    /// <summary>玩家笔实体（供 GameManager 等外层做 Loadout 注入、数据查询）</summary>
    public PenEntity Pen => pen;
    /// <summary>敌方测试笔。为空时 BattleUpdate 可在运行时复制玩家笔作为临时 enemy。</summary>
    public PenEntity EnemyPen => enemyPen;
    /// <summary>当前是否处于 Idle 状态（用于阶段切换前置校验）</summary>
    public bool IsIdle => currentState is IdleState;
    /// <summary>
    /// 战斗逻辑是否被外层暂停（阶段切换期间为 true）。
    /// 悬停视觉等"只在可交互时工作"的组件应在 IsIdle && !IsPaused 时生效。
    /// </summary>
    public bool IsPaused => _paused;

    /// <summary>
    /// 强制重建 IdleState 并重新 Enter —— 用于 Workshop/Shop 返回 Battle 时
    /// 清空所有 IdleState 内部缓存（interactor / dragInput / isDragging 等），
    /// 防御跨阶段任何组件被重建但 FSM 内部引用没刷新的 edge case。
    /// 幂等：若当前非 IdleState 不做事。
    /// </summary>
    public void RestartIdleState()
    {
        if (ctx == null) return;
        if (currentState is IdleState)
        {
            ChangeState(new IdleState(this, ctx));
        }
    }

    private IEntityState currentState;
    private BattleContext ctx;
    private bool _paused;

    private bool _hasSnapshot;
    private Vector3 _penSnapPos;
    private Quaternion _penSnapRot;
    private bool _hasEnemySnapshot;
    private Vector3 _enemySnapPos;
    private Quaternion _enemySnapRot;

    // Inspector 初始位置：作为"已知安全的 respawn 锚点"——
    // _penSnapPos 来自上一次 SnapshotPens，可能落在桌面 gap 等不稳定处；
    // _penInitialPos 是 Awake 即固化的设计意图位置，ResultState auto-restart 走这里更稳。
    private Vector3 _penInitialPos;
    private Quaternion _penInitialRot;
    private Vector3 _enemyInitialPos;
    private Quaternion _enemyInitialRot;

    private Coroutine _hidePensCo;
    private bool _battleUpdateFallbackChecked;
    private bool _focusCameraPensRegistered;

    private void Awake()
    {
        CacheInitialPose(pen, out _penInitialPos, out _penInitialRot);
        CacheInitialPose(enemyPen, out _enemyInitialPos, out _enemyInitialRot);

        // 预置 scale=0：场景加载首帧笔不可见，等 Start 里 PlayPensIntroPopIn 把它们弹回
        // 避免"进游戏瞬间看到全尺寸笔 → 然后被我们缩小 → 再 pop-in"的闪烁
        SetPenScaleZero(pen);
        SetPenScaleZero(enemyPen);
    }

    private void Start()
    {
        ctx = new BattleContext(pen, enemyPen);
        ctx.AimChannel = _aimChannel;
        if (_aimChannel == null)
            Debug.LogWarning("[BattleStateMachine] AimPhaseChannel 未配置，瞄准视觉层将不会收到事件。" +
                             "请在 Inspector 里拖入 Assets/ScriptableObjects/Channels/AimPhaseChannel.asset");
        ConfigurePlayerFeedbackChannels();
        // 启动预快照：保证首次 RestorePens 有合法位置，即使从未进出过 Battle
        SnapshotPens();
        ChangeState(new IdleState(this, ctx));
        // 首次进入战斗场景：笔 pop-in 入场
        PlayPensIntroPopIn();
    }

    /// <summary>笔弹入（scale 0 → 1 EaseOutBack）。
    /// 外层调用时机：Start 首次亮相 / BattlePhase.Enter 从 Shop/Workshop 回战斗时。</summary>
    public void PlayPensIntroPopIn()
    {
        if (pen != null) IntroPopIn.PlayOn(this, pen.transform, 0f, Vector3.one);
        if (enemyPen != null) IntroPopIn.PlayOn(this, enemyPen.transform, 0f, Vector3.one);
    }

    public void ChangeState(IEntityState newState)
    {
        currentState?.Exit();
        currentState = newState;
        currentState.Enter();
    }

    private void Update()
    {
        EnsureBattleUpdateEnemyFallback();
        EnsureFocusCameraPensRegistered();

        if (_paused) return;

        // 全局掉落 watchdog：任何状态都覆盖。修复 ActionState 在笔从桌边滑出途中
        // 因瞬时低速被 IsStopped 误判 → 转 IdleState → 笔继续掉穿世界、状态机不再监管
        // 的死角。把"出局判定"从 state 局部责任提升为 SM 级不变量。
        if (ctx != null && currentState is not ResultState && ctx.AnyPenFallen)
        {
            ChangeState(new ResultState(this, ctx));
            return;
        }

        currentState?.Update();
    }

    /// <summary>
    /// 把笔传送回 Inspector 设计意图位置（Awake 抓的初始 transform）。
    /// 用于 ResultState 自动重生——_penSnapPos 可能落在桌面 gap 等不稳定处，
    /// 这里走 Awake 锚点更稳定，且与 RestorePens 不同，不依赖之前是否快照过。
    /// </summary>
    public void RespawnAtInitial(float liftY = 0f)
    {
        if (pen != null)
            pen.transform.SetPositionAndRotation(_penInitialPos + Vector3.up * liftY, _penInitialRot);
        if (enemyPen != null)
            enemyPen.transform.SetPositionAndRotation(_enemyInitialPos + Vector3.up * liftY, _enemyInitialRot);
        Physics.SyncTransforms();
    }

    private static void CacheInitialPose(PenEntity entity, out Vector3 position, out Quaternion rotation)
    {
        position = entity != null ? entity.transform.position : Vector3.zero;
        rotation = entity != null ? entity.transform.rotation : Quaternion.identity;
    }

    private static void SetPenScaleZero(PenEntity entity)
    {
        if (entity != null) entity.transform.localScale = Vector3.zero;
    }

    private void EnsureBattleUpdateEnemyFallback()
    {
        if (_battleUpdateFallbackChecked) return;
        _battleUpdateFallbackChecked = true;

        if (enemyPen != null || pen == null || !IsBattleUpdateScene()) return;

        enemyPen = CreateBattleUpdateEnemyClone();
        if (enemyPen == null) return;

        CacheInitialPose(enemyPen, out _enemyInitialPos, out _enemyInitialRot);
        if (ctx != null) ctx.EnemyPen = enemyPen;
        SnapshotPens();
        IntroPopIn.PlayOn(this, enemyPen.transform, 0f, Vector3.one);
        _focusCameraPensRegistered = false;

        Debug.Log("[BattleStateMachine] BattleUpdate 未配置 enemyPen，已运行时复制玩家笔创建测试 enemy。");
    }

    private void EnsureFocusCameraPensRegistered()
    {
        if (_focusCameraPensRegistered) return;

        var focusCameraController = FocusCameraController.Instance;
        if (focusCameraController == null) return;

        if (pen != null) focusCameraController.RegisterPen(pen.transform);
        if (enemyPen != null) focusCameraController.RegisterPen(enemyPen.transform);
        _focusCameraPensRegistered = true;
    }

    private void ConfigurePlayerFeedbackChannels()
    {
        if (pen == null)
            return;

        PenLaunchFeedback launchFeedback = pen.GetComponent<PenLaunchFeedback>();
        if (launchFeedback != null)
            launchFeedback.Configure(_aimChannel);
    }

    private static bool IsBattleUpdateScene()
    {
        Scene activeScene = SceneManager.GetActiveScene();
        return activeScene.path == "Assets/Scenes/BattleUpdate.unity";
    }

    private PenEntity CreateBattleUpdateEnemyClone()
    {
        Vector3 enemyPosition = GetMirroredEnemyPosition();
        Quaternion enemyRotation = Quaternion.Euler(0f, pen.transform.eulerAngles.y + 180f, 0f);
        GameObject clone = Instantiate(pen.gameObject, enemyPosition, enemyRotation);
        clone.name = $"{pen.name}_EnemyTest";

        DisablePlayerOnlyComponentsOnEnemy(clone);
        PenEntity entity = clone.GetComponent<PenEntity>();
        if (entity == null)
        {
            Destroy(clone);
            return null;
        }

        SetPenScaleZero(entity);
        StopPenVelocity(entity);
        return entity;
    }

    private Vector3 GetMirroredEnemyPosition()
    {
        Vector3 playerPos = pen.transform.position;
        Vector3 center = Vector3.zero;
        Collider tableCollider = null;

        GameObject table = GameObject.Find("Table");
        if (table != null) tableCollider = table.GetComponent<Collider>();
        if (tableCollider != null) center = tableCollider.bounds.center;

        Vector3 enemyPos = new Vector3(
            center.x * 2f - playerPos.x,
            playerPos.y,
            center.z * 2f - playerPos.z);

        if (tableCollider != null)
        {
            Bounds bounds = tableCollider.bounds;
            const float inset = 0.8f;
            enemyPos.x = Mathf.Clamp(enemyPos.x, bounds.min.x + inset, bounds.max.x - inset);
            enemyPos.z = Mathf.Clamp(enemyPos.z, bounds.min.z + inset, bounds.max.z - inset);
        }

        Vector3 flatDelta = enemyPos - playerPos;
        flatDelta.y = 0f;
        if (flatDelta.sqrMagnitude < 1f)
            enemyPos = playerPos + Vector3.right * 2f;

        return enemyPos;
    }

    private static void DisablePlayerOnlyComponentsOnEnemy(GameObject clone)
    {
        foreach (MonoBehaviour component in clone.GetComponentsInChildren<MonoBehaviour>(true))
        {
            if (component == null) continue;
            if (IsPlayerOnlyEnemyCloneComponent(component.GetType().Name))
                component.enabled = false;
        }
    }

    private static bool IsPlayerOnlyEnemyCloneComponent(string typeName)
    {
        return typeName == "PenDragInteractor" ||
               typeName == "PenDragVisuals" ||
               typeName == "PenLaunchArrowView" ||
               typeName == "PenHoverPresenter" ||
               typeName == "PenHoverVisuals" ||
               typeName == "PenPressFeedback" ||
               typeName == "PenAimThresholdFX" ||
               typeName == "PenLaunchFeedback";
    }

    // ─── 场景层：供 BattlePhase 调用 ──────────────────────────────────────────

    public void ShowBattleScene()
    {
        _paused = false;
        if (battleRoot != null) battleRoot.SetActive(true);
    }

    public void HideBattleScene()
    {
        _paused = true;
        if (battleRoot != null) battleRoot.SetActive(false);
    }

    /// <summary>只暂停/恢复战斗逻辑，不动场景根节点和笔；用于"保留场景视觉、但逻辑冻结"的过渡期。</summary>
    public void SetBattlePaused(bool paused)
    {
        _paused = paused;
    }

    // ─── 笔位置快照/恢复（供阶段切换视觉衔接）─────────────────────────────────

    /// <summary>
    /// 记录笔当前位置。BattleStateMachine 是笔位置的唯一所有者，
    /// 每次 BattlePhase.Exit 都调用一次。由 CanExit=IsIdle 保证调用时笔已静止。
    /// </summary>
    public void SnapshotPens()
    {
        _hasSnapshot = SnapshotPen(pen, out _penSnapPos, out _penSnapRot);
        _hasEnemySnapshot = SnapshotPen(enemyPen, out _enemySnapPos, out _enemySnapRot);
    }

    private static bool SnapshotPen(PenEntity entity, out Vector3 position, out Quaternion rotation)
    {
        position = Vector3.zero;
        rotation = Quaternion.identity;
        if (entity == null) return false;
        StopPenVelocity(entity);
        position = entity.transform.position;
        rotation = entity.transform.rotation;
        return true;
    }

    private static void StopPenVelocity(PenEntity entity)
    {
        if (entity == null) return;
        var rb = entity.GetComponent<Rigidbody>();
        if (rb == null || rb.isKinematic) return;
        rb.linearVelocity = Vector3.zero;
        rb.angularVelocity = Vector3.zero;
    }

    /// <summary>
    /// 把笔传送回记忆位置并抬高 liftY（仅改 transform）。
    /// 在 inactive 下调用安全——不访问 Rigidbody 避免 Unity warning。
    /// 配合 <see cref="ResetPensPhysics"/>：先 Restore → SetActive → Reset，
    /// SetActive 时物理引擎自动从 transform 取位置，然后再清速度和启重力。
    /// </summary>
    public void RestorePens(float liftY)
    {
        if (_hasSnapshot && pen != null)
        {
            Vector3 target = _penSnapPos + Vector3.up * liftY;
            pen.transform.SetPositionAndRotation(target, _penSnapRot);
        }
        if (_hasEnemySnapshot && enemyPen != null)
        {
            Vector3 target = _enemySnapPos + Vector3.up * liftY;
            enemyPen.transform.SetPositionAndRotation(target, _enemySnapRot);
        }
        // 强制物理立即同步，避免下一帧 Interpolate 把上一物理步的位置和新位置之间"补间"出闪烁
        Physics.SyncTransforms();
    }

    /// <summary>在 SetPensActive(true) 之后调用：清速度、启重力（此时 Rigidbody 已激活，无 warning）。</summary>
    public void ResetPensPhysics()
    {
        ResetPenPhysics(pen);
        ResetPenPhysics(enemyPen);
    }

    private static void ResetPenPhysics(PenEntity entity)
    {
        if (entity == null) return;
        var rb = entity.GetComponent<Rigidbody>();
        if (rb == null) return;
        if (rb.isKinematic) rb.isKinematic = false;
        rb.linearVelocity = Vector3.zero;
        rb.angularVelocity = Vector3.zero;
        rb.useGravity = true;
    }

    /// <summary>控制笔的激活态（供"书撞飞笔"结束后清场/回战斗时恢复）。</summary>
    public void SetPensActive(bool active)
    {
        // 重新激活 → 取消还没跑完的隐藏倒计时，避免回战斗后立即被 SetActive(false) 打脸
        if (active && _hidePensCo != null)
        {
            StopCoroutine(_hidePensCo);
            _hidePensCo = null;
        }
        if (pen != null) pen.gameObject.SetActive(active);
        if (enemyPen != null) enemyPen.gameObject.SetActive(active);
    }

    /// <summary>
    /// 启动"延迟 _hidePensDelay 秒后笔 SetActive(false)"的倒计时。
    /// 由 BattlePhase.Exit 统一调用，保证 Workshop / Shop 两条退出路径时机一致。
    /// 期间若 SetPensActive(true) 被调用（例如回战斗），倒计时自动取消。
    /// </summary>
    public void HidePensAfterDelay()
    {
        if (_hidePensCo != null) StopCoroutine(_hidePensCo);
        _hidePensCo = StartCoroutine(HidePensCoroutine(_hidePensDelay));
    }

    private IEnumerator HidePensCoroutine(float delay)
    {
        yield return new WaitForSeconds(delay);
        if (pen != null) pen.gameObject.SetActive(false);
        if (enemyPen != null) enemyPen.gameObject.SetActive(false);
        _hidePensCo = null;
    }
}
