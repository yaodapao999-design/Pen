using UnityEngine;

public class BattleStateMachine : MonoBehaviour
{
    [SerializeField] private PenEntity pen;
    [SerializeField] private PenEntity enemyPen;

    private IEntityState currentState;
    private BattleContext ctx;
    private FallOffPredictor predictor;

    public FallOffPredictor Predictor => predictor;

    private void Start()
    {
        ctx = new BattleContext(pen);
        ctx.enemyPen = enemyPen;

        // 初始化预判器
        predictor = GetComponent<FallOffPredictor>();
        if (predictor != null)
            predictor.Init(pen, enemyPen);

        ChangeState(new IdleState(this, ctx));
    }

    public void ChangeState(IEntityState newState)
    {
        currentState?.Exit();
        currentState = newState;
        currentState.Enter();
    }

    private void Update()
    {
        currentState?.Update();
    }
}