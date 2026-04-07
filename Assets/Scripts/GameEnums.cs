public enum PartType
{
    Barrel,     // 笔杆 (核心)
    Cap,        // 笔帽
    Tip,        // 笔头
    Refill,     // 笔芯
    Accessory   // 附件 (橡胶圈等)
}

// 连接点类型：决定哪些部件可以互相拼接
public enum SocketType
{
    None,            // 无（Barrel 等根部件使用）
    BarrelFront,     // 笔杆前端（接笔头/笔芯）
    BarrelRear,      // 笔杆后端（接笔帽）
    BarrelBody,      // 笔杆身（接附件，如橡胶圈）
    TipConnector,    // 笔头连接端
    CapConnector,    // 笔帽连接端
    RefillConnector, // 笔芯连接端
    AccessorySlot,   // 附件槽
}

