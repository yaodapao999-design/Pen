using UnityEngine;

public class ActionState : IEntityState
{
    private readonly BattleStateMachine stateMachine;
    private readonly BattleContext ctx;

    private float elapsedTime;

    public ActionState(BattleStateMachine stateMachine, BattleContext ctx)
    {
        this.stateMachine = stateMachine;
        this.ctx = ctx;
    }

    public void Enter()
    {
        elapsedTime = 0f;

        ctx.pen.Launch(ctx.LaunchDirection, ctx.LaunchForce, ctx.ContactPointWorld);
        Debug.Log("进入弹射状态");
    }

    public void Update()
    {
        elapsedTime += Time.deltaTime;
        if (elapsedTime < ctx.pen.stopCheckDelay) return;

        // 掉落检测
        bool playerFell = ctx.pen.HasFallen;
        bool enemyFell = ctx.enemyPen.HasFallen;
        if (playerFell || enemyFell)
        {
            stateMachine.ChangeState(new ResultState(stateMachine, ctx, playerFell, enemyFell));
            return;
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
        Debug.Log("离开弹射状态");
    }
}
