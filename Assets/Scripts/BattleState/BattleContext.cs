using UnityEngine;

public class BattleContext
{
    public PenEntity pen { get; private set; }
    public PenEntity enemyPen { get; set; }

    public Vector3 LaunchDirection { get; set; }
    public float LaunchForce { get; set; }
    public float ContactOffset { get; set; }

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
    
