using System.Collections;

/// <summary>
/// 游戏阶段接口（外层 FSM）。
///
/// 生命周期：
///   1. CanExit() — 同步前置校验；false 则本次切换被拒绝
///   2. OnExitRejected() — 拒绝时的 UI 反馈（抖按钮、播音效等）
///   3. Exit() — 协程，播退出动画
///   4. Enter() — 协程，播进入动画
///
/// 校验与动画必须分离：
///   CanExit 是"能不能退"的规则，错了立刻告诉玩家
///   Exit 是"既然能退，播动画"，动画完成就一定切换成功
/// </summary>
public interface IGamePhase
{
    bool CanExit();
    void OnExitRejected();
    IEnumerator Enter();
    IEnumerator Exit();

    /// <summary>
    /// 本阶段的 Cinemachine 镜头（可为 null，表示用默认镜头）。
    /// 镜头优先级由 GameManager 统一调度，Phase/Controller 不再手动改 Priority。
    /// </summary>
    Unity.Cinemachine.CinemachineCamera Camera { get; }
}
