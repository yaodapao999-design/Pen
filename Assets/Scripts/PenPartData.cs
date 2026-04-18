using UnityEngine;

/// <summary>
/// 笔部件的静态数据定义（ScriptableObject）
/// 只存数据，不含任何逻辑
/// </summary>
[CreateAssetMenu(fileName = "NewPenPart", menuName = "GameData/PenPart")]
public class PenPartData : ScriptableObject
{
    [Header("--- 基础身份 ---")]
    public string PartID;
    public string DisplayName;
    public PartType Category;

    [Header("--- 表现 ---")]
    public GameObject VisualPrefab;  // 3D模型预制体（含Collider）
    public Sprite Icon;

    [Header("--- Socket 连接点 ---")]
    // 这个部件插入父级的哪个 socket（Barrel 不需要设置）
    public SocketType PlugsInto;

    [Header("--- 物理：质量与质心 ---")]
    // 该部件自身质量（kg），Unity 会根据所有子 Collider 位置自动计算质心
    public float Mass = 0.1f;

    [Header("--- 物理：摩擦 ---")]
    // 该零件 Collider 的接触材质。Unity 复合刚体在接触时自动选"接触点那个 Collider 的材质"，
    // 所以哪一端触地就用哪一端的摩擦，不需要再区分"全局 / 局部"。留空则用 Unity 默认材质。
    public PhysicsMaterial PhysicsMaterial;

    [Header("--- 战斗属性 ---")]
    // 弹射力倍率（1.0 = 无加成）
    public float LaunchPowerMultiplier = 1.0f;

    [Header("--- 经济 ---")]
    public int BuyPrice;

    [Header("--- 效果 ---")]
    // 该零件提供的效果列表，每个效果是一个 PenPartEffect SO
    // 添加新功能只需创建新 Effect SO 并拖进来
    public PenPartEffect[] Effects;
}
