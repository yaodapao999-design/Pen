using UnityEngine;

[CreateAssetMenu(fileName = "NewPenPart", menuName = "GameData/PenPart")]
public class PenPartData : ScriptableObject
{
[Header("--- 基础身份 ---")]
    public string PartID;           
    public string DisplayName;      
    public PartType Category;       // 决定它能装在哪个固定槽位

    [Header("--- 表现与生成 ---")]
    public GameObject VisualPrefab; // 包含模型和 Collider
    public Sprite Icon;             

    [Header("--- 核心物理性能 ---")]
    // 质量：【防守属性】
    // 越重，根据动量守恒定理 (P=mv)，对手越难把你撞出位移。
    public float Mass;              

    // 弹射加成：【进攻属性】
    // 模拟不同零件对弹射手感的加成（例如橡胶圈更好发力）。
    public float LaunchPowerMultiplier = 1.0f; 

    // 物理材质：【交互属性】
    // 包含 Friction (抓地力) 和 Bounciness (碰撞反弹力)。
    // 橡胶圈：高摩擦力，防止自己滑出桌面。
    // 金属头：高反弹力，把别人弹出更远。

    [Header("--- 经济属性 ---")]
    public int BuyPrice;
}