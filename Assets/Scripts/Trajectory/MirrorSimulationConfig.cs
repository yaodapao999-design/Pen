using UnityEngine;

/// <summary>
/// 镜像步进参数。由 TrajectoryPreviewController 持有并传入 Simulator。
/// 提供为 ScriptableObject,便于在 Editor 中快速 A/B 不同参数集。
/// </summary>
[CreateAssetMenu(fileName = "MirrorSimulationConfig", menuName = "GameData/Trajectory/MirrorSimulationConfig")]
public class MirrorSimulationConfig : ScriptableObject
{
    [Tooltip("镜像 PhysicsScene 单步时长(秒)。默认与主场景 Time.fixedDeltaTime 对齐,避免与实机差异过大")]
    public float FixedStep = 0.02f;

    [Tooltip("最大步数上限(硬截断)。过大 → 单次预测卡帧;过小 → 未走完就 Timeout。")]
    [Range(1, 2000)]
    public int MaxSteps = 600;

    [Tooltip("最大时间上限(秒)。与 MaxSteps 取先触发者")]
    public float MaxTime = 6f;

    [Tooltip("每 N 步采样一次位姿(1 = 每步采样)。更大 = 数据量小、折线更稀")]
    [Range(1, 20)]
    public int SampleStride = 2;

    [Tooltip("必要的静默帧数:连续多帧满足停止阈值才判 Rested,避免单帧噪声误判")]
    [Range(1, 20)]
    public int RestFrameCount = 3;

    [Tooltip("是否在镜像中纳入敌方笔(MVP 默认否,视为静态;启用后 Mirror 需单独构建 enemy 副本)")]
    public bool IncludeEnemyPen = false;
}
