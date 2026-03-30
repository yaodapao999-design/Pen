using UnityEngine;

public class ActionState : IEntityState
{
    private readonly BattleStateMachine stateMachine;
    private readonly BattleContext ctx;

    private float elapsedTime;
    private bool fallDetected;

    public ActionState(BattleStateMachine stateMachine, BattleContext ctx)
    {
        this.stateMachine = stateMachine;
        this.ctx = ctx;
    }

    public void Enter()
    {
        elapsedTime = 0f;
        fallDetected = false;

        ctx.pen.Launch(ctx.LaunchDirection, ctx.LaunchForce, ctx.ContactOffset);

        if (ctx.LastPredictResult != null && ctx.LastPredictResult.isKillShot)
        {
            Time.timeScale = 0.15f;
            Time.fixedDeltaTime = 0.02f * 0.15f;
            Debug.Log("[ActionState] KillShot 慢动作开始");
        }

        Debug.Log("进入弹射状态");
    }

    public void Update()
    {
        elapsedTime += Time.deltaTime;
        if (elapsedTime < ctx.pen.stopCheckDelay) return;

        // 检测所有笔是否掉落
        if (!fallDetected)
        {
            bool playerFell = ctx.pen.FallOff != null && ctx.pen.FallOff.HasFallen;
            bool enemyFell = ctx.enemyPen.FallOff != null && ctx.enemyPen.FallOff.HasFallen;

            if (playerFell || enemyFell)
            {
                fallDetected = true;
                Debug.Log($"[ActionState] 检测到掉落 - 玩家: {playerFell}, 敌人: {enemyFell}");
                stateMachine.ChangeState(new ResultState(stateMachine, ctx));
                return;
            }
        }

        // 所有笔停稳则回到等待状态
        if (ctx.pen.IsStopped() && ctx.enemyPen.IsStopped())
        {
            ctx.pen.Stop();
            ctx.enemyPen.Stop();
            Debug.Log("所有笔停止运动，回到等待状态");
            stateMachine.ChangeState(new IdleState(stateMachine, ctx));
        }
    }

    public void Exit()
    {
        Time.timeScale = 1f;
        Time.fixedDeltaTime = 0.02f;
        Debug.Log("离开弹射状态");
    }
}
