using UnityEngine;
using UnityEngine.InputSystem;

public class IdleState : IEntityState
{
    private readonly BattleStateMachine stateMachine;
    private readonly BattleContext ctx;

    private bool isDragging;
    private Vector3 dragStartWorldPos;

    public IdleState(BattleStateMachine stateMachine, BattleContext ctx)
    {
        this.stateMachine = stateMachine;
        this.ctx = ctx;
    }

    public void Enter()
    {
        isDragging = false;
        Debug.Log("进入了等待状态，现在可以准备弹射了");
    }

    public void Update()
    {
        var mouse = Mouse.current;
        if (mouse == null) return;

        if (mouse.leftButton.wasPressedThisFrame)
            TryStartDrag(mouse.position.ReadValue());

        if (isDragging)
        {
            UpdateDrag(mouse.position.ReadValue());
            if (mouse.leftButton.wasReleasedThisFrame)
                FinishDrag();
        }
    }

    private void TryStartDrag(Vector2 screenPos)
    {
        Ray ray = Camera.main.ScreenPointToRay(screenPos);
        if (!ctx.pen.penCollider.Raycast(ray, out RaycastHit hit, Mathf.Infinity))
            return;

        isDragging = true;
        dragStartWorldPos = GetWorldPositionOnPenPlane(screenPos);

        Vector3 localHitPoint = ctx.pen.transform.InverseTransformPoint(hit.point);
        float rawOffset = ctx.pen.GetOffsetAlongCapsuleAxis(localHitPoint);
        float halfHeight = (ctx.pen.penCollider.height / 2f) - ctx.pen.penCollider.radius;
        ctx.ContactOffset = Mathf.Clamp(rawOffset / halfHeight, -1f, 1f);

        Debug.Log($"点击偏移: {ctx.ContactOffset:F3}（-1=笔尾, 0=中心, 1=笔头）");
    }

    private void UpdateDrag(Vector2 screenPos)
    {
        Vector3 currentWorldPos = GetWorldPositionOnPenPlane(screenPos);
        Vector3 dragVector = currentWorldPos - dragStartWorldPos;

        if (dragVector.magnitude > ctx.pen.maxDragDistance)
            dragVector = dragVector.normalized * ctx.pen.maxDragDistance;

        ctx.LaunchDirection = -dragVector.normalized;
        ctx.LaunchForce = dragVector.magnitude / ctx.pen.maxDragDistance;
    }

    private void FinishDrag()
    {
        isDragging = false;

        PredictResult predictResult = null;
        var predictor = stateMachine.Predictor;
        if (predictor != null)
        {
            predictor.SyncSimScene();
            predictResult = predictor.Predict(ctx);
        }

        ctx.LastPredictResult = predictResult;

        bool isKillShot = predictResult != null && predictResult.isKillShot;
        Debug.Log($"弹射！方向: {ctx.LaunchDirection}, 力度: {ctx.LaunchForce:F2}, " +
                  $"偏移: {ctx.ContactOffset:F3} | 一击必杀: {isKillShot}" +
                  (isKillShot ? $" (击杀帧: {predictResult.killFrame}, 采样点: {predictResult.samples.Count})" : ""));

            stateMachine.ChangeState(new ActionState(stateMachine, ctx));
    }

    private Vector3 GetWorldPositionOnPenPlane(Vector2 screenPos)
    {
        Ray ray = Camera.main.ScreenPointToRay(screenPos);
        Plane penPlane = new Plane(Vector3.up, ctx.pen.transform.position);
        penPlane.Raycast(ray, out float distance);
        return ray.GetPoint(distance);
    }

    public void Exit() => Debug.Log("离开了等待状态");
}