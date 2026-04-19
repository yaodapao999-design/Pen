using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 新存档初始状态的单一事实源。
///
/// 定义新游戏开始时玩家的起始装备：笔杆 + 已装配零件 + 散落仓库。
/// 由 GameManager 在启动时读入并写入 PenAssembly / PlayerInventory。
/// 本 SO 只保存数据，不做应用逻辑（SRP）。
///
/// 未来接入存档系统时：
///   GameManager 只需在应用前判断 SaveService.HasSave() 即可跳过。
/// </summary>
[CreateAssetMenu(fileName = "PlayerLoadout", menuName = "GameData/PlayerLoadout")]
public class PlayerLoadoutSO : ScriptableObject
{
    [Tooltip("起始笔杆（装配在玩家笔上作为根）")]
    [SerializeField] private PenPartData _initialBarrel;

    [Tooltip("起始已装配在笔上的零件（笔头/笔帽/笔芯/配件等）")]
    [SerializeField] private PenPartData[] _initialEquipped;

    [Tooltip("起始散落在仓库里的零件（未装配，下次打开改装抽屉时以散落物形式生成）")]
    [SerializeField] private PenPartData[] _initialInventory;

    [Tooltip("新存档起始金币数——唯一入口。GameManager.Start() 会 Wallet.Set() 此值到 PlayerWallet。" +
             "在 PlayerWallet.asset 里直接改 _coins 没有意义，会被这里覆盖。")]
    [SerializeField] private int _initialCoins = 10;

    public PenPartData InitialBarrel => _initialBarrel;
    public IReadOnlyList<PenPartData> InitialEquipped => _initialEquipped ?? System.Array.Empty<PenPartData>();
    public IReadOnlyList<PenPartData> InitialInventory => _initialInventory ?? System.Array.Empty<PenPartData>();
    public int InitialCoins => _initialCoins;
}
