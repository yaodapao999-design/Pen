using UnityEngine;

/// <summary>
/// 结算状态：根据谁还在桌上判定胜负，触发结果事件
/// </summary>
public class ResultState : IEntityState
{
    private readonly BattleStateMachine stateMachine;
    private readonly BattleContext ctx;

    public ResultState(BattleStateMachine stateMachine, BattleContext ctx)
    {
        this.stateMachine = stateMachine;
        this.ctx = ctx;
    }

    public void Enter()
    {
        bool playerFell = ctx.pen.FallOff != null && ctx.pen.FallOff.HasFallen;
        bool enemyFell = ctx.enemyPen.FallOff != null && ctx.enemyPen.FallOff.HasFallen;

        BattleResult result;
        if (playerFell && enemyFell)
            result = BattleResult.Draw;
        else if (enemyFell)
            result = BattleResult.PlayerWin;
        else if (playerFell)
            result = BattleResult.EnemyWin;
        else
            result = BattleResult.Draw; // 都没掉，平局（回合结束无人出局）

        Debug.Log($"[ResultState] 对局结果: {result}");
    }

    public void Update() { }

    public void Exit()
    {
        // 重置掉落检测器，为下一局做准备
        ctx.pen.FallOff?.ResetDetector();
        ctx.enemyPen.FallOff?.ResetDetector();
        ctx.LastPredictResult = null;

        Debug.Log("[ResultState] 离开结算状态");
    }
}