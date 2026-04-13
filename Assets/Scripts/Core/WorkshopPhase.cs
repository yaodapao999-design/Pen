using System.Collections;

/// <summary>
/// 改装阶段。
///
/// 退出约束：必须装好笔杆。没笔杆时 CanExit 返回 false，OnExitRejected 播放拒绝反馈。
/// 这个约束对所有目标阶段都生效（想去战斗或商店都不行）。
/// </summary>
public class WorkshopPhase : IGamePhase
{
    private readonly WorkshopController _ctrl;
    private readonly System.Action _beforeEnter;

    public WorkshopPhase(WorkshopController ctrl, System.Action beforeEnter)
    {
        _ctrl = ctrl;
        _beforeEnter = beforeEnter;
    }

    public bool CanExit()
    {
        if (_ctrl == null) return true;
        var registry = WorkshopPartRegistry.Instance;
        return registry != null && registry.HasAssembledBarrel();
    }

    public void OnExitRejected()
    {
        // 反馈由发起请求的 PhaseButton 本地播放，这里无需额外动作
    }

    public Unity.Cinemachine.CinemachineCamera Camera => _ctrl != null ? _ctrl.DrawerVCam : null;

    public IEnumerator Enter()
    {
        _beforeEnter?.Invoke();
        if (_ctrl != null) yield return _ctrl.EnterWorkshopRoutine();
    }

    public IEnumerator Exit()
    {
        if (_ctrl != null) yield return _ctrl.FinishWorkshopRoutine();
    }
}
