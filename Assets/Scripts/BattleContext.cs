using UnityEngine;

public class BattleContext
{
    public PenEntity pen { get; private set; }

    // 回合内运行时数据
    public Vector3 LaunchDirection { get; set; }
    public float LaunchForce { get; set; }      // 0~1
    public float ContactOffset { get; set; }    // -1~1

    public BattleContext(PenEntity pen)
    {
        this.pen = pen;
    }
}