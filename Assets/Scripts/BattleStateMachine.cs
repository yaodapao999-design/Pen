using UnityEngine;

public class BattleStateMachine : MonoBehaviour
{
    [SerializeField] private PenEntity pen;

    private IEntityState currentState;
    private BattleContext ctx;

    private void Start()
    {
        ctx = new BattleContext(pen);
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