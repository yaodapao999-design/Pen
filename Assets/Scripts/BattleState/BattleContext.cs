using UnityEngine;

public class BattleContext
{
    public PenEntity pen { get; private set; }
    public PenEntity enemyPen { get; set; }

    public Vector3 LaunchDirection { get; set; }
    public float LaunchForce { get; set; }
    /// <summary>
    /// 玩家点击时射线在笔 Collider 上的世界坐标命中点。
    /// ActionState.Enter 里直接把它作为 AddForceAtPosition 的 position 参数——
    /// 扭矩相对 rb.worldCenterOfMass 由 Unity 自动算，无需中转成 [-1, 1] 的 offset（那会被胶囊末端夹死）。
    /// </summary>
    public Vector3 ContactPointWorld { get; set; }

    public BattleContext(PenEntity pen)
    {
        this.pen = pen;
    }
}

public enum BattleResult
{
    PlayerWin,
    EnemyWin,
    Draw
}
    
