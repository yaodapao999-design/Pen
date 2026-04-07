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
    // 这个部件插入父级的哪个 socket
    public SocketType PlugsInto;
    // 这个部件自身提供哪些 socket 给子部件连接
    public SocketType[] ProvidesSocket;

    [Header("--- 物理：质量与质心 ---")]
    // 该部件自身质量（kg）
    public float Mass = 0.1f;
    // 质心偏移：相对于该部件本地坐标的偏移
    // 例如重笔帽会把整只笔的质心往后拉
    public Vector3 CenterOfMassOffset = Vector3.zero;

    [Header("--- 物理：摩擦 ---")]
    // 是否覆盖全局摩擦（false = 局部摩擦，只影响该部件接触面）
    public bool OverrideGlobalFriction = false;
    public PhysicsMaterial PhysicsMaterial;

    [Header("--- 战斗属性 ---")]
    // 弹射力倍率（1.0 = 无加成）
    public float LaunchPowerMultiplier = 1.0f;

    [Header("--- 经济 ---")]
    public int BuyPrice;

    [Header("--- 特殊效果 ---")]
    // 预留：后续电锯/口香糖等特殊组件挂载自定义效果
    // 在 Inspector 里拖入实现了 IPenPartEffect 的 ScriptableObject
    public PenPartEffectData[] SpecialEffects;
}
