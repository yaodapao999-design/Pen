using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 商店商品池（ScriptableObject 资产）。
/// 配置候选笔配件及其刷新权重，运行时按权重无放回抽样。
/// 未来可支持多池子（章节/难度差异），ShopController 切换引用即可。
/// </summary>
[CreateAssetMenu(fileName = "ShopPool", menuName = "GameData/ShopPool")]
public class ShopPool : ScriptableObject
{
    [Serializable]
    public class Entry
    {
        public PenPartData Part;
        [Tooltip("权重，越大越易出现")]
        public float Weight = 1f;
    }

    public List<Entry> Entries = new List<Entry>();

    /// <summary>按权重无放回抽样 count 个；池子不足时返回全部</summary>
    public List<PenPartData> RollRandom(int count)
    {
        var result = new List<PenPartData>();
        if (Entries == null || Entries.Count == 0 || count <= 0) return result;

        var pool = new List<Entry>(Entries.Count);
        foreach (var e in Entries)
            if (e != null && e.Part != null && e.Weight > 0f) pool.Add(e);

        count = Mathf.Min(count, pool.Count);
        for (int i = 0; i < count; i++)
        {
            float total = 0f;
            foreach (var e in pool) total += e.Weight;

            float r = UnityEngine.Random.value * total;
            float acc = 0f;
            int picked = 0;
            for (int j = 0; j < pool.Count; j++)
            {
                acc += pool[j].Weight;
                if (r <= acc) { picked = j; break; }
            }

            result.Add(pool[picked].Part);
            pool.RemoveAt(picked);
        }
        return result;
    }
}
