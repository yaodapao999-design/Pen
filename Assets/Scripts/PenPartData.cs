using UnityEngine;

[CreateAssetMenu(fileName = "NewPenPart", menuName = "GameData/PenPart")]
public class PenPartData : ScriptableObject
{

    [field: SerializeField] 
    public PartType PartType { get; private set; } // 这个零件属于哪个槽（笔帽/橡胶圈/笔尖/笔芯/笔杆）

    [field: SerializeField] 
    public GameObject VisualPrefab { get; private set; } // 3D模型预制体

    [field: SerializeField] 
    public float MassAdd { get; private set; }  //增加多少质量（可为负数）

    [field: SerializeField] 
    public Vector3 CenterOfMassOffset { get; private set; } // 对整只笔质心的影响

    [field: SerializeField] 
    public PhysicsModifier PhysicsModifier { get; private set; } //  摩擦/弹性相关参数

    [field: SerializeField] 
    public string DisplayName { get; private set; }

    [field: SerializeField] 
    public Sprite Icon { get; private set; }

    [field: SerializeField] 
    public int Price { get; private set; }

}

[System.Serializable]
public struct PhysicsModifier
{
    public float globalFriction; // 影响整只笔的摩擦（-1代表不覆盖）
    public float localFrictionAdd; // 局部摩擦增量（橡胶圈用）
    public bool isLocalFriction; // true则只影响该部件碰撞体，false影响全局
    public float bounciness; // 弹性系数（-1代表不覆盖）
}