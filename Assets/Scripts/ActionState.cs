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
        ExecuteLaunch();
    }

    private void ExecuteLaunch()
    {
        var rb = ctx.pen.rb;
        rb.linearVelocity = Vector3.zero;
        rb.angularVelocity = Vector3.zero;

        Vector3 penAxis = ctx.pen.GetPenAxis();
        float halfHeight = (ctx.pen.penCollider.height / 2f) - ctx.pen.penCollider.radius;
        Vector3 forcePosition = rb.worldCenterOfMass + penAxis * (ctx.ContactOffset * halfHeight);

        Vector3 force = ctx.LaunchDirection * (ctx.pen.baseForce * ctx.LaunchForce);
        rb.AddForceAtPosition(force, forcePosition, ForceMode.Impulse);

        Debug.Log($"施力点偏移: {ctx.ContactOffset:F3} | 力: {force.magnitude:F2} | 作用点: {forcePosition}");
    }

    public void Update()
    {
        elapsedTime += Time.deltaTime;
        if (elapsedTime < ctx.pen.stopCheckDelay) return;

        var rb = ctx.pen.rb;
        bool hasStopped = rb.linearVelocity.magnitude < ctx.pen.stopVelocityThreshold &&
                          rb.angularVelocity.magnitude < ctx.pen.stopAngularThreshold;

        if (hasStopped)
        {
            rb.linearVelocity = Vector3.zero;
            rb.angularVelocity = Vector3.zero;
            Debug.Log("笔停止运动，回到等待状态");
            stateMachine.ChangeState(new IdleState(stateMachine, ctx));
        }
    }

    public void Exit() => Debug.Log("离开弹射状态");
}