using UnityEngine;

public class ActionState : IEntityState
{
    /// <summary>
    /// "笔已经基本停下"超时窗口：若 linearVelocity 持续低于 stopVelocityThreshold 达此秒数，
    /// 即视为停下，强制 Stop() 清零并回 IdleState——不再等 angularVelocity 单独低于阈值。
    /// 修复 collider gap jitter 导致笔卡桌面 settle 后留下永远清不掉的微小角速度，
    /// IsStopped 永远 false、ActionState 卡死、玩家"第二次点不到笔"的 bug。
    /// </summary>
    private const float LOW_VEL_TIMEOUT = 1.0f;

    private readonly BattleStateMachine stateMachine;
    private readonly BattleContext ctx;

    private float elapsedTime;
    private float _playerLowVelTimer;
    private float _enemyLowVelTimer;
    private bool _roundStarted;

    public ActionState(BattleStateMachine stateMachine, BattleContext ctx)
    {
        this.stateMachine = stateMachine;
        this.ctx = ctx;
    }

    public void Enter()
    {
        elapsedTime = 0f;
        _playerLowVelTimer = 0f;
        _enemyLowVelTimer = 0f;
        _roundStarted = true;
        NotifyRoundStart();
        ctx.pen.Launch(ctx.LaunchDirection, ctx.LaunchForce, ctx.ContactPointWorld);
        ctx.pen.GetComponent<PenEffectRunner>()?.NotifyLaunch(ctx.LaunchDirection, ctx.LaunchForce);
    }

    public void Update()
    {
        elapsedTime += Time.deltaTime;

        // 掉落检测兜底（BSM watchdog 也覆盖这条路径）
        if (ctx.AnyPenFallen)
        {
            stateMachine.ChangeState(new ResultState(stateMachine, ctx));
            return;
        }

        if (elapsedTime < ctx.StopCheckDelay) return;

        // 走 IsStopped（vel + angVel 双低）或 timeout（vel 持续低）任一即停
        bool playerEffectivelyStopped = IsEffectivelyStopped(ctx.PlayerPen, ref _playerLowVelTimer);
        bool enemyEffectivelyStopped = !ctx.HasEnemy || IsEffectivelyStopped(ctx.EnemyPen, ref _enemyLowVelTimer);

        if (playerEffectivelyStopped && enemyEffectivelyStopped)
        {
            // Stop 显式清零线速度+角速度，避免 IdleState.Enter 时笔身上还有 jitter 残留
            ctx.StopAllPens();
            stateMachine.ChangeState(new IdleState(stateMachine, ctx));
        }
    }

    /// <summary>
    /// linearVelocity 持续 &lt; stopVelocityThreshold 累计 LOW_VEL_TIMEOUT 秒视为停。
    /// 一旦超过阈值则 timer 重置——保证只有真正"卡 settle"的状态才算停。
    /// </summary>
    private static bool IsEffectivelyStopped(PenEntity pen, ref float timer)
    {
        if (pen == null || pen.rb == null) return true;
        return pen.IsStopped() || IsLowLinearVelSustained(pen, ref timer);
    }

    private static bool IsLowLinearVelSustained(PenEntity pen, ref float timer)
    {
        if (pen.rb.linearVelocity.magnitude < pen.stopVelocityThreshold)
        {
            timer += Time.deltaTime;
            return timer >= LOW_VEL_TIMEOUT;
        }
        timer = 0f;
        return false;
    }

    public void Exit()
    {
        if (!_roundStarted) return;
        _roundStarted = false;
        NotifyRoundEnd();
    }

    private void NotifyRoundStart()
    {
        ctx.PlayerPen?.GetComponent<PenEffectRunner>()?.NotifyRoundStart();
        ctx.EnemyPen?.GetComponent<PenEffectRunner>()?.NotifyRoundStart();
    }

    private void NotifyRoundEnd()
    {
        ctx.PlayerPen?.GetComponent<PenEffectRunner>()?.NotifyRoundEnd();
        ctx.EnemyPen?.GetComponent<PenEffectRunner>()?.NotifyRoundEnd();
    }
}
