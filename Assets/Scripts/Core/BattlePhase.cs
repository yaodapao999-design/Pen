using System.Collections;

/// <summary>
/// 战斗阶段：显隐委托给 BattleStateMachine（由它管自己的场景物体列表）。
/// 战斗内部的 Idle/Action/Result 继续由 BattleStateMachine 管。
/// 无退出约束。
/// </summary>
public class BattlePhase : IGamePhase
{
    private readonly BattleStateMachine _bsm;

    public BattlePhase(BattleStateMachine bsm) { _bsm = bsm; }

    public bool CanExit() => _bsm == null || _bsm.IsIdle;
    public void OnExitRejected() { }
    public Unity.Cinemachine.CinemachineCamera Camera => _bsm != null ? _bsm.BattleCamera : null;

    public IEnumerator Enter()
    {
        if (_bsm != null)
        {
            _bsm.SetBattlePaused(false);
            _bsm.SetPensActive(true);
            _bsm.RestorePens(2f);
        }
        yield break;
    }

    public IEnumerator Exit()
    {
        // 只快照位置 + 暂停战斗逻辑；笔保持激活，交给 ShopController 让 ShopBook 物理撞飞
        // 书到位后由 ShopController 调 SetPensActive(false) 完成清场
        if (_bsm != null)
        {
            _bsm.SnapshotPens();
            _bsm.SetBattlePaused(true);
        }
        yield break;
    }
}
