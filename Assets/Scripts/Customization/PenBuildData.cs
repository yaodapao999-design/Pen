using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 一只笔的完整配置存档：只存 PartID，不持有任何 Unity 对象引用
/// 可序列化，用于保存/读取玩家的笔配置
/// </summary>
[Serializable]
public class PenBuildData
{
    public string BarrelID;
    public List<string> PartIDs = new();

    /// <summary>从当前 PenAssembly 状态序列化</summary>
    public static PenBuildData FromAssembly(PenAssembly assembly)
    {
        var data = new PenBuildData();
        data.BarrelID = assembly.CurrentBarrelData != null ? assembly.CurrentBarrelData.PartID : string.Empty;
        foreach (var part in assembly.Parts)
            data.PartIDs.Add(part.Data.PartID);
        return data;
    }

    /// <summary>将存档还原到目标 PenAssembly</summary>
    public void ApplyTo(PenAssembly assembly, PenPartDatabase database)
    {
        var barrel = database.GetByID(BarrelID);
        if (barrel == null)
        {
            Debug.LogError($"PenBuildData: 找不到 BarrelID '{BarrelID}'");
            return;
        }

        assembly.SetBarrel(barrel);

        foreach (var id in PartIDs)
        {
            var partData = database.GetByID(id);
            if (partData == null)
            {
                Debug.LogWarning($"PenBuildData: 找不到 PartID '{id}'，跳过");
                continue;
            }

            var socket = assembly.GetSocket(partData.PlugsInto);
            if (socket == null)
            {
                Debug.LogWarning($"PenBuildData: 没有可用的 Socket '{partData.PlugsInto}' 给部件 '{id}'，跳过");
                continue;
            }

            assembly.AddPart(partData, socket);
        }
    }
}
