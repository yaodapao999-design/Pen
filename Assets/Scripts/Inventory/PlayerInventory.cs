using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 玩家持久化库存（ScriptableObject 资产）。
/// 存放玩家已拥有的笔配件；商店购买后写入，进入 Workshop 前注入 WorkshopPenSpawner。
///
/// 运行时快照模式：Enable 时备份磁盘原始内容，Disable 时还原。
/// 目的：Editor 下 SO 字段的改动默认会写回 asset，导致停止运行后购买结果残留；
/// 快照/还原确保运行期改动是"会话内"的。未来接存档时，从存档文件装入 OwnedParts，然后在 OnDisable 里改写磁盘即可。
/// </summary>
[CreateAssetMenu(fileName = "PlayerInventory", menuName = "GameData/PlayerInventory")]
public class PlayerInventory : ScriptableObject
{
    [Tooltip("运行时已拥有的全部零件 + Session Snapshot 载体。不要在此处设置起始仓库——" +
             "GameManager.Start() 会用 PlayerLoadoutSO.InitialInventory 覆盖此列表。此字段只用于 Play 模式下观察当前库存。")]
    public List<PenPartData> OwnedParts = new List<PenPartData>();

    [System.NonSerialized] private List<PenPartData> _sessionSnapshot;

    private void OnEnable()
    {
        // 备份"磁盘原始值"作为会话基线；运行期的任何 Add/Remove 都只活在内存里
        _sessionSnapshot = new List<PenPartData>(OwnedParts);
    }

    private void OnDisable()
    {
        // 域重载 / 退出播放 时把列表还原为基线，避免污染 asset
        if (_sessionSnapshot == null) return;
        OwnedParts.Clear();
        OwnedParts.AddRange(_sessionSnapshot);
    }

    public void Add(PenPartData part)
    {
        if (part == null) return;
        OwnedParts.Add(part);
    }

    public bool Remove(PenPartData part)
    {
        return part != null && OwnedParts.Remove(part);
    }

    public IReadOnlyList<PenPartData> GetAll() => OwnedParts;

    public void Clear() => OwnedParts.Clear();
}
