using UnityEngine;

public class BattleContext
{
    public PenEntity pen { get; private set; }
    public PenEntity PlayerPen => pen;
    public PenEntity EnemyPen { get; set; }
    public PenEntity enemyPen
    {
        get => EnemyPen;
        set => EnemyPen = value;
    }

    public Vector3 LaunchDirection { get; set; }
    public float LaunchForce { get; set; }
    /// <summary>
    /// 玩家点击时射线在笔 Collider 上的世界坐标命中点。
    /// ActionState.Enter 里直接把它作为 AddForceAtPosition 的 position 参数——
    /// 扭矩相对 rb.worldCenterOfMass 由 Unity 自动算，无需中转成 [-1, 1] 的 offset（那会被胶囊末端夹死）。
    /// </summary>
    public Vector3 ContactPointWorld { get; set; }

    /// <summary>
    /// 玩家瞄准事件通道。IdleState 在拖拽生命周期各点发事件，视觉/反馈层作为订阅者响应。
    /// 由 BattleStateMachine 序列化后在 Start 里注入。
    /// </summary>
    public AimPhaseChannelSO AimChannel { get; set; }

    public bool HasEnemy => EnemyPen != null;
    public bool PlayerHasFallen => pen != null && pen.HasFallen;
    public bool EnemyHasFallen => EnemyPen != null && EnemyPen.HasFallen;
    public bool AnyPenFallen => PlayerHasFallen || EnemyHasFallen;

    public float StopCheckDelay
    {
        get
        {
            float delay = pen != null ? pen.stopCheckDelay : 0f;
            if (EnemyPen != null) delay = Mathf.Max(delay, EnemyPen.stopCheckDelay);
            return delay;
        }
    }

    public BattleContext(PenEntity pen, PenEntity enemyPen = null)
    {
        this.pen = pen;
        EnemyPen = enemyPen;
    }

    public void StopAllPens()
    {
        if (pen != null) pen.Stop();
        if (EnemyPen != null) EnemyPen.Stop();
    }
}
