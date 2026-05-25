using UnityEngine;

public enum BattleResult
{
    PlayerWin,
    EnemyWin,
    Draw
}

/// <summary>
/// 结算状态：任一笔掉出桌（HasFallen=true）触发，AUTO_RESTART_DELAY 秒后自动重生回 Inspector 锚点 + 回 IdleState。
/// 走 RespawnAtInitial 而不是 RestorePens：snapshot 抓到的位置可能就是导致掉落的不稳定处
/// （例如桌面 collider gap），用它会形成"重生→再次掉落→再次结算"的死循环。
/// Inspector 锚点是设计意图安全位置。
/// </summary>
public class ResultState : IEntityState
{
    private const float AUTO_RESTART_DELAY = 2f;
    private const float RESPAWN_LIFT = 1f;

    private readonly BattleStateMachine stateMachine;
    private readonly BattleContext ctx;

    private float _elapsed;
    private BattleResult _result;

    public ResultState(BattleStateMachine stateMachine, BattleContext ctx)
    {
        this.stateMachine = stateMachine;
        this.ctx = ctx;
    }

    public void Enter()
    {
        _elapsed = 0f;
        _result = ResolveResult();
        Debug.Log($"[ResultState] {_result}，{AUTO_RESTART_DELAY}s 后自动重生");
    }

    public void Update()
    {
        _elapsed += Time.deltaTime;

        BattleResult currentResult = ResolveResult();
        if (currentResult != _result)
        {
            _result = currentResult;
            Debug.Log($"[ResultState] 结算更新为 {_result}");
        }

        if (_elapsed < AUTO_RESTART_DELAY) return;

        stateMachine.RespawnAtInitial(RESPAWN_LIFT);
        stateMachine.ResetPensPhysics();
        stateMachine.ChangeState(new IdleState(stateMachine, ctx));
    }

    private BattleResult ResolveResult()
    {
        bool playerFallen = ctx.PlayerHasFallen;
        bool enemyFallen = ctx.EnemyHasFallen;

        if (playerFallen && enemyFallen) return BattleResult.Draw;
        if (enemyFallen) return BattleResult.PlayerWin;
        if (playerFallen) return BattleResult.EnemyWin;
        return BattleResult.Draw;
    }

    public void Exit() { }
}
