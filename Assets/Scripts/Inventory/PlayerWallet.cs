using UnityEngine;
using UnityEngine.Serialization;

/// <summary>
/// 玩家钱包（SO 资产）。持有金币数，提供 Add / TrySpend / Set API。
///
/// - OnChanged 事件：每次金币变化时触发，UI（CurrencyHUD）订阅即可自动刷新
/// - 会话快照：和 PlayerInventory 相同模式——Play 期间的变动只活在内存，
///   OnDisable 还原磁盘原值。防止编辑器下 Play 污染 asset
/// </summary>
[CreateAssetMenu(fileName = "PlayerWallet", menuName = "GameData/PlayerWallet")]
public class PlayerWallet : ScriptableObject
{
    [Tooltip("运行时钱包余额 + Session Snapshot 载体。不要在此处设置起始金币——" +
             "GameManager.Start() 会用 PlayerLoadoutSO.InitialCoins 覆盖此值。此字段只用于 Play 模式下实时观察余额。")]
    [FormerlySerializedAs("Coins")]
    [SerializeField] private int _coins;

    public int Coins => _coins;

    /// <summary>金币数变化时触发（花费 / 获得 / Set 都会触发）</summary>
    public event System.Action OnChanged;

    [System.NonSerialized] private int _sessionSnapshot;
    [System.NonSerialized] private bool _snapshotValid;

    private void OnEnable()
    {
        _sessionSnapshot = _coins;
        _snapshotValid = true;
    }

    private void OnDisable()
    {
        if (_snapshotValid) _coins = _sessionSnapshot;
    }

    /// <summary>尝试扣费。不够则返回 false，不做任何改变</summary>
    public bool TrySpend(int amount)
    {
        if (amount < 0 || _coins < amount) return false;
        _coins -= amount;
        OnChanged?.Invoke();
        return true;
    }

    /// <summary>获得金币（正数）</summary>
    public void Add(int amount)
    {
        if (amount <= 0) return;
        _coins += amount;
        OnChanged?.Invoke();
    }

    /// <summary>覆盖金币数（用于新存档初始化）</summary>
    public void Set(int amount)
    {
        _coins = Mathf.Max(0, amount);
        OnChanged?.Invoke();
    }
}
