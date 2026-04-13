using UnityEngine;

/// <summary>
/// 玩家钱包（占位，本阶段不接线）。
/// 预留给后续货币系统：商店购买时调用 TrySpend，战斗奖励调用 Add。
/// </summary>
[CreateAssetMenu(fileName = "PlayerWallet", menuName = "GameData/PlayerWallet")]
public class PlayerWallet : ScriptableObject
{
    [Tooltip("当前金币数")]
    public int Coins;

    public bool TrySpend(int amount)
    {
        if (amount < 0) return false;
        if (Coins < amount) return false;
        Coins -= amount;
        return true;
    }

    public void Add(int amount)
    {
        if (amount <= 0) return;
        Coins += amount;
    }
}
