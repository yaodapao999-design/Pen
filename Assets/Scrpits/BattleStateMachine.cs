using UnityEngine;

public class BattleStateMachine : MonoBehaviour
{
    [SerializeField] private GameObject penObject;

    private IEntityState _currentState;
    private BattleContext _ctx;

    private void Start()
    {
        _ctx = new BattleContext(penObject);
        ChangeState(new IdleState(this, _ctx));
    }

    public void ChangeState(IEntityState newState)
    {
        _currentState?.Exit();
        _currentState = newState;
        _currentState.Enter();
    }

    private void Update()
    {
        _currentState?.Update();
    }
}
