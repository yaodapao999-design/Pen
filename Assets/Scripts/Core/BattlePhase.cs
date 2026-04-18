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
            // 顺序关键：
            //   1) RestorePens 在 inactive 下只动 transform（无 warning）
            //   2) SetActive 激活：物理引擎自动从 transform 取位置，避免第一帧闪在残留位置
            //   3) ResetPensPhysics 在 active 下清速度、启重力
            _bsm.RestorePens(2f);
            _bsm.SetPensActive(true);
            _bsm.ResetPensPhysics();
        }
        yield break;
    }

    public IEnumerator Exit()
    {
        // 只快照位置 + 暂停战斗逻辑 + 启动统一的 2s 隐藏倒计时
        // 倒计时由 BSM.HidePensAfterDelay 管，Workshop 和 Shop 两条路径时机对齐；
        // 期间任何 SetPensActive(true) 都会自动取消倒计时（防止回战斗后被打脸）。
        if (_bsm != null)
        {
            _bsm.SnapshotPens();
            _bsm.SetBattlePaused(true);
            _bsm.HidePensAfterDelay();
        }
        yield break;
    }
}
