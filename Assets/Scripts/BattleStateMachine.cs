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

    public Unity.Cinemachine.CinemachineCamera BattleCamera => battleCamera;
    public GameObject BattleRoot => battleRoot;
    /// <summary>当前是否处于 Idle 状态（用于阶段切换前置校验）</summary>
    public bool IsIdle => currentState is IdleState;

    private IEntityState currentState;
    private BattleContext ctx;
    private bool _paused;

    private bool _hasSnapshot;
    private Vector3 _penSnapPos, _enemyPenSnapPos;
    private Quaternion _penSnapRot, _enemyPenSnapRot;

    private void Start()
    {
        ctx = new BattleContext(pen);
        ctx.enemyPen = enemyPen;
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

    /// <summary>记录两把笔当前位置，供下次回战斗时原位归还。</summary>
    public void SnapshotPens()
    {
        if (pen != null)
        {
            _penSnapPos = pen.transform.position;
            _penSnapRot = pen.transform.rotation;
        }
        if (enemyPen != null)
        {
            _enemyPenSnapPos = enemyPen.transform.position;
            _enemyPenSnapRot = enemyPen.transform.rotation;
        }
        _hasSnapshot = pen != null || enemyPen != null;
    }

    /// <summary>把两把笔传送回记忆位置并抬高 liftY，速度清零让重力接管自然落下。</summary>
    public void RestorePens(float liftY)
    {
        if (!_hasSnapshot) return;
        if (pen != null) RestoreOne(pen, _penSnapPos, _penSnapRot, liftY);
        if (enemyPen != null) RestoreOne(enemyPen, _enemyPenSnapPos, _enemyPenSnapRot, liftY);
    }

    private static void RestoreOne(PenEntity entity, Vector3 pos, Quaternion rot, float liftY)
    {
        var t = entity.transform;
        t.SetPositionAndRotation(pos + Vector3.up * liftY, rot);
        var rb = entity.GetComponent<Rigidbody>();
        if (rb != null)
        {
            rb.linearVelocity = Vector3.zero;
            rb.angularVelocity = Vector3.zero;
            if (rb.isKinematic) rb.isKinematic = false;
            rb.useGravity = true;
        }
    }

    /// <summary>控制两把笔的激活态（供"书撞飞笔"结束后清场/回战斗时恢复）。</summary>
    public void SetPensActive(bool active)
    {
        if (pen != null) pen.gameObject.SetActive(active);
        if (enemyPen != null) enemyPen.gameObject.SetActive(active);
    }
}
