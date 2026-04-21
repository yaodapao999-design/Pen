/// <summary>
/// 披露分级:决定把多少镜像仿真结果披露给玩家。底层仿真不变,切换级别不触发重仿真。
/// 枚举值显式指定,避免新增值时破坏 Inspector 已保存的序列化整数。
/// </summary>
public enum DisclosureLevel
{
    /// <summary>D0:仅操作反馈(施力点/拖拽向量/力度条),不披露任何轨迹结果。</summary>
    Minimal = 0,
    /// <summary>D1:披露初段方向与玩家 ghost 终点位姿,隐藏中段折线。</summary>
    Trend = 1,
    /// <summary>D2:披露完整轨迹(玩家 + 敌方)+ 双方 ghost pen,预测到所有笔静止。</summary>
    Full = 2,
    /// <summary>D1.5:包含所有指示(方向带 + 折线 + 玩家 ghost),但披露到玩家与敌方首次碰撞为止;未碰撞时退化为 Full 玩家侧(不显示敌方 ghost)。</summary>
    OnCollide = 3
}
