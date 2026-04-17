using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 阶段切换按钮。挂在 UI Button 上，点击时请求切到 TargetPhase（或回 Battle，若已在 TargetPhase）。
///
/// 被拒（如改装未装好笔杆，GameManager.ChangePhase 返回 false）时播放 RejectFeedback，
/// 反馈绑定在按钮自身，所以"哪个按钮被按，哪个按钮抖"。
/// </summary>
[RequireComponent(typeof(Button))]
public class PhaseButton : MonoBehaviour
{
    [Tooltip("按下时想要进入的阶段；若当前已在该阶段，改为回 Battle")]
    public GamePhase TargetPhase = GamePhase.Shop;

    [Tooltip("被拒时播放的反馈（屏幕抖 / 抖按钮 / 音效等，在 MMFeedbacks 里配）")]
    public MoreMountains.Feedbacks.MMF_Player RejectFeedback;

    private void Awake()
    {
        GetComponent<Button>().onClick.AddListener(OnClick);
    }

    private void OnClick()
    {
        var gm = GameManager.Instance;
        if (gm == null) return;

        GamePhase next = gm.CurrentPhase == TargetPhase ? GamePhase.Battle : TargetPhase;
        bool accepted = gm.ChangePhase(next);
        if (!accepted && RejectFeedback != null) RejectFeedback.PlayFeedbacks();
    }
}
