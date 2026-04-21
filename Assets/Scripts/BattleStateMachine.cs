using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class BattleStateMachine : MonoBehaviour
{
    [SerializeField] private PenEntity pen;
    [SerializeField] private PenEntity enemyPen;

    [Header("战斗阶段镜头（GameManager 调度）")]
    [SerializeField] private Unity.Cinemachine.CinemachineCamera battleCamera;

    [Header("战斗阶段根节点（非战斗阶段整棵隐藏）")]
    [Tooltip("战斗专属物体（如 Enemy、战斗 UI）放这下面。注意：BattleStateMachine 自己不能放在这里面，否则失活后无法重新激活")]
    [SerializeField] private GameObject battleRoot;

    [Header("退出战斗时的延迟隐藏")]
    [Tooltip("退出战斗（进 Workshop/Shop）时，Player 和 Enemy 笔的 SetActive(false) 延迟。两把笔同步生效")]
    [SerializeField] private float _hidePensDelay = 2f;

    public Unity.Cinemachine.CinemachineCamera BattleCamera => battleCamera;
    public GameObject BattleRoot => battleRoot;
    /// <summary>玩家笔实体（供 GameManager 等外层做 Loadout 注入、数据查询)</summary>
    public PenEntity Pen => pen;
    /// <summary>敌方笔实体(供预测系统在镜像场景中装配双笔、以静态位姿同步)</summary>
    public PenEntity EnemyPen => enemyPen;
    /// <summary>当前是否处于 Idle 状态（用于阶段切换前置校验）</summary>
    public bool IsIdle => currentState is IdleState;
    /// <summary>当前战斗上下文；Idle 蓄力期间 LaunchDirection/Force/ContactPointWorld 由 IdleState 实时填入,预测系统等外部观察者可读取</summary>
    public BattleContext Ctx => ctx;
    /// <summary>当前是否处于 Idle 子状态且玩家正在拖拽蓄力(供预测系统"只在拖拽时显示"开关使用)</summary>
    public bool IsDragging => (currentState as IdleState)?.IsDragging ?? false;

    private IEntityState currentState;
    private BattleContext ctx;
    private bool _paused;
    private bool _enemyAssemblyEnsured;

    private bool _hasSnapshot;
    private Vector3 _penSnapPos, _enemyPenSnapPos;
    private Quaternion _penSnapRot, _enemyPenSnapRot;

    private Coroutine _hidePensCo;

    [Header("入场动画")]
    [Tooltip("Player 与 Enemy 入场之间的错开时长（秒）")]
    [SerializeField] private float _introStaggerDelay = 0.1f;

    private void Awake()
    {
        // 预置 scale=0：场景加载首帧笔不可见，等 Start 里 PlayPensIntroPopIn 把它们弹回
        // 避免"进游戏瞬间看到全尺寸笔 → 然后被我们缩小 → 再 pop-in"的闪烁
        if (pen != null) pen.transform.localScale = Vector3.zero;
        if (enemyPen != null) enemyPen.transform.localScale = Vector3.zero;
    }

    private void Start()
    {
        ctx = new BattleContext(pen);
        ctx.enemyPen = enemyPen;
        // 启动预快照：保证首次 RestorePens 有合法位置，即使从未进出过 Battle
        SnapshotPens();
        ChangeState(new IdleState(this, ctx));
        // 首次进入战斗场景：两把笔 pop-in 入场
        PlayPensIntroPopIn();
    }

    /// <summary>Player 与 Enemy 笔错开弹入（scale 0 → 1 EaseOutBack）。
    /// 外层调用时机：Start 首次亮相 / BattlePhase.Enter 从 Shop/Workshop 回战斗时。</summary>
    public void PlayPensIntroPopIn()
    {
        if (pen != null) IntroPopIn.PlayOn(this, pen.transform, 0f, Vector3.one);
        if (enemyPen != null) IntroPopIn.PlayOn(this, enemyPen.transform, _introStaggerDelay, Vector3.one);
    }

    public void ChangeState(IEntityState newState)
    {
        currentState?.Exit();
        currentState = newState;
        currentState.Enter();
    }

    private void Update()
    {
        if (_paused) return;
        if (!_enemyAssemblyEnsured) TryEnsureEnemyAssembly();
        currentState?.Update();
    }

    /// <summary>
    /// 敌方笔默认只挂了视觉模型,PenAssembly 未 InitData;这里在玩家装配完成后用同一份 Loadout 补建敌方装配,
    /// 使敌方笔在物理层面也是完整 barrel+零件(与玩家等价),才能进入镜像场景参与碰撞预测。
    /// </summary>
    private void TryEnsureEnemyAssembly()
    {
        if (enemyPen == null || enemyPen.Assembly == null) { _enemyAssemblyEnsured = true; return; }
        if (enemyPen.Assembly.BarrelData != null) { _enemyAssemblyEnsured = true; return; }
        if (pen == null || pen.Assembly == null || pen.Assembly.BarrelData == null) return;

        var parts = new List<PenAssembly.PartEntry>(pen.Assembly.AssembledParts.Count);
        for (int i = 0; i < pen.Assembly.AssembledParts.Count; i++)
            parts.Add(pen.Assembly.AssembledParts[i]);
        enemyPen.Assembly.SetData(pen.Assembly.BarrelData, parts);
        enemyPen.Assembly.BuildBattleView();
        _enemyAssemblyEnsured = true;
        Debug.Log("[BattleStateMachine] 敌方笔用玩家默认装配补建完成(TryEnsureEnemyAssembly)");
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
    /// 记录两把笔当前位置。BattleStateMachine 是 Player/Enemy 笔位置的唯一所有者，
    /// 每次 BattlePhase.Exit 都调用一次。由 CanExit=IsIdle 保证调用时笔已静止。
    /// </summary>
    public void SnapshotPens()
    {
        if (pen != null)
        {
            StopPenVelocity(pen);
            _penSnapPos = pen.transform.position;
            _penSnapRot = pen.transform.rotation;
        }
        if (enemyPen != null)
        {
            StopPenVelocity(enemyPen);
            _enemyPenSnapPos = enemyPen.transform.position;
            _enemyPenSnapRot = enemyPen.transform.rotation;
        }
        _hasSnapshot = pen != null || enemyPen != null;
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
    /// 把两把笔传送回记忆位置并抬高 liftY（仅改 transform）。
    /// 在 inactive 下调用安全——不访问 Rigidbody 避免 Unity warning。
    /// 配合 <see cref="ResetPensPhysics"/>：先 Restore → SetActive → Reset，
    /// SetActive 时物理引擎自动从 transform 取位置，然后再清速度和启重力。
    /// </summary>
    public void RestorePens(float liftY)
    {
        if (!_hasSnapshot) return;
        if (pen != null) RestoreTransformOnly(pen, _penSnapPos, _penSnapRot, liftY);
        if (enemyPen != null) RestoreTransformOnly(enemyPen, _enemyPenSnapPos, _enemyPenSnapRot, liftY);
        // 强制物理立即同步，避免下一帧 Interpolate 把上一物理步的位置和新位置之间"补间"出闪烁
        Physics.SyncTransforms();
    }

    /// <summary>在 SetPensActive(true) 之后调用：清速度、启重力（此时 Rigidbody 已激活，无 warning）。</summary>
    public void ResetPensPhysics()
    {
        if (!_hasSnapshot) return;
        if (pen != null) ResetOnePhysics(pen);
        if (enemyPen != null) ResetOnePhysics(enemyPen);
    }

    private static void RestoreTransformOnly(PenEntity entity, Vector3 pos, Quaternion rot, float liftY)
    {
        Vector3 target = pos + Vector3.up * liftY;
        entity.transform.SetPositionAndRotation(target, rot);
    }

    private static void ResetOnePhysics(PenEntity entity)
    {
        var rb = entity.GetComponent<Rigidbody>();
        if (rb == null) return;
        if (rb.isKinematic) rb.isKinematic = false;
        rb.linearVelocity = Vector3.zero;
        rb.angularVelocity = Vector3.zero;
        rb.useGravity = true;
    }

    /// <summary>控制两把笔的激活态（供"书撞飞笔"结束后清场/回战斗时恢复）。</summary>
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
    /// 启动"延迟 _hidePensDelay 秒后两把笔同步 SetActive(false)"的倒计时。
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
