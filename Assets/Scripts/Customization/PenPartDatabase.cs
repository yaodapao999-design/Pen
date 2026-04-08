using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 全局部件数据库，通过 PartID 查找 PenPartData
/// 在 Project 里创建一个实例，拖入所有 PenPartData
/// </summary>
[CreateAssetMenu(fileName = "PenPartDatabase", menuName = "GameData/PenPartDatabase")]
public class PenPartDatabase : ScriptableObject
{
    public List<PenPartData> AllParts = new();

    private Dictionary<string, PenPartData> _lookup;

    private void OnEnable()
    {
        BuildLookup();
    }

    private void BuildLookup()
    {
        _lookup = new Dictionary<string, PenPartData>(AllParts.Count);
        foreach (var part in AllParts)
        {
            if (part == null) continue;
            if (!_lookup.TryAdd(part.PartID, part))
                Debug.LogWarning($"PenPartDatabase: 重复的 PartID '{part.PartID}'");
        }
    }

    public PenPartData GetByID(string partID)
    {
        if (_lookup == null) BuildLookup();
        _lookup.TryGetValue(partID, out var result);
        return result;
    }
}
