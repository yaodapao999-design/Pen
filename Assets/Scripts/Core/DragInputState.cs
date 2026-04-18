using UnityEngine;

/// <summary>
/// 可复用的拖拽输入状态机，支持三种手势并存：
///   1. 按住拖拽：按下开始，松开结束
///   2. 点击切换：快速点击一次开始（零件跟随鼠标），再点击一次结束
///   3. 右键取消：拖拽过程中右键立即取消（走反悔/回弹路径）
///
/// 拖拽对象（WorkshopPart / ShopPart / IdleState 等）持有一个实例，
/// Update 里调 Poll() 判断事件；BeginDrag 时调 NotifyBegin()，结束或取消路径里调 ForceEnd()。
/// </summary>
public class DragInputState
{
    private enum Mode { Idle, Holding, Clicked }

    private Mode _mode = Mode.Idle;
    private float _pressStartTime;

    [Tooltip("按住少于此时长且松手视为'点击切换'，零件将保持跟随鼠标；长于此时长视为'按住拖拽'，松手即结束")]
    public float HoldThreshold = 0.18f;

    public enum Event { None, Begin, End, Cancel }

    public bool IsDragging => _mode != Mode.Idle;

    /// <summary>由拖拽对象在收到外部 BeginDrag 时通知，记录起始时间用于 hold/click 判断</summary>
    public void NotifyBegin()
    {
        _mode = Mode.Holding;
        _pressStartTime = Time.time;
    }

    /// <summary>强制退出拖拽状态（外部中断 / 阶段切换 / HandleMouseUp）</summary>
    public void ForceEnd()
    {
        _mode = Mode.Idle;
    }

    /// <summary>每帧调一次。返回 End 表示正常结束，返回 Cancel 表示用户右键取消</summary>
    public Event Poll(bool mousePressedThisFrame, bool mouseReleasedThisFrame, bool cancelThisFrame = false)
    {
        if (_mode == Mode.Idle) return Event.None;

        // 外部取消（右键）优先于其他判断
        if (cancelThisFrame)
        {
            _mode = Mode.Idle;
            return Event.Cancel;
        }

        // 按住模式：松手时长 ≥ HoldThreshold 视为"按住拖拽"，结束
        // 否则视为"快速点击"，转入点击切换模式等待下次点击
        if (mouseReleasedThisFrame && _mode == Mode.Holding)
        {
            float held = Time.time - _pressStartTime;
            if (held >= HoldThreshold)
            {
                _mode = Mode.Idle;
                return Event.End;
            }
            _mode = Mode.Clicked;
            return Event.None;
        }

        // 点击切换模式：下一次按下即结束
        if (mousePressedThisFrame && _mode == Mode.Clicked)
        {
            _mode = Mode.Idle;
            return Event.End;
        }

        return Event.None;
    }
}
