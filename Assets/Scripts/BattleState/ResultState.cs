using UnityEngine;

/// <summary>
/// 结算状态：根据谁还在桌上判定胜负，触发结果事件
/// </summary>
public class ResultState : IEntityState
{
    private readonly BattleStateMachine stateMachine;
    private readonly BattleContext ctx;
    private readonly bool playerFell;
    private readonly bool enemyFell;

    public ResultState(BattleStateMachine stateMachine, BattleContext ctx, bool playerFell, bool enemyFell)
    {
        this.stateMachine = stateMachine;
        this.ctx = ctx;
        this.playerFell = playerFell;
        this.enemyFell = enemyFell;
    }

    public void Enter()
    {
        BattleResult result;
        if (playerFell && enemyFell)
            result = BattleResult.Draw;
        else if (enemyFell)
            result = BattleResult.PlayerWin;
        else
            result = BattleResult.EnemyWin;

        Debug.Log($"[ResultState] 对局结果: {result}");
    }

    public void Update() { }

    public void Exit()
    {
        Debug.Log("[ResultState] 离开结算状态");
    }
}