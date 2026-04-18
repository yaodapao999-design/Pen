using System.Collections;
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
    /// <summary>玩家笔实体（供 GameManager 等外层做 Loadout 注入、数据查询）</summary>
    public PenEntity Pen => pen;
    /// <summary>当前是否处于 Idle 状态（用于阶段切换前置校验）</summary>
    public bool IsIdle => currentState is IdleState;

    private IEntityState currentState;
    private BattleContext ctx;
    private bool _paused;

    private bool _hasSnapshot;
    private Vector3 _penSnapPos, _enemyPenSnapPos;
    private Quaternion _penSnapRot, _enemyPenSnapRot;

    private Coroutine _hidePensCo;

    private void Start()
    {
        ctx = new BattleContext(pen);
        ctx.enemyPen = enemyPen;
        // 启动预快照：保证首次 RestorePens 有合法位置，即使从未进出过 Battle
        SnapshotPens();
        ChangeState(new IdleState(this, ctx));
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
        currentState?.Update();
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
