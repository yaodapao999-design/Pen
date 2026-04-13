using System.Collections;

/// <summary>
/// 商店阶段：无退出约束，Enter/Exit 委托给 ShopController。
/// </summary>
public class ShopPhase : IGamePhase
{
    private readonly ShopController _ctrl;

    public ShopPhase(ShopController ctrl) { _ctrl = ctrl; }

    public bool CanExit() => true;
    public void OnExitRejected() { }
    public Unity.Cinemachine.CinemachineCamera Camera => _ctrl != null ? _ctrl.ShopVCam : null;

    public IEnumerator Enter()
    {
        if (_ctrl != null) yield return _ctrl.EnterShopRoutine();
    }

    public IEnumerator Exit()
    {
        if (_ctrl != null) yield return _ctrl.ExitShopRoutine();
    }
}
