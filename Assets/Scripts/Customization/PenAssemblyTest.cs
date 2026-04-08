using UnityEngine;
using System.Collections;

/// <summary>
/// 临时测试脚本：验证 PenAssembly 物理聚合和部件装配是否正常
/// 测试完成后删除此文件
/// </summary>
public class PenAssemblyTest : MonoBehaviour
{
    [Header("笔杆 SO")]
    public PenPartData barrelData;

    [Header("要装配的部件 SO（按顺序）")]
    public PenPartData[] partDatas;

    private PenAssembly _assembly;

    private void Start()
    {
        // 清除编辑器预览残留（通过名字前缀识别）
        for (int i = transform.childCount - 1; i >= 0; i--)
        {
            var child = transform.GetChild(i).gameObject;
            if (child.name.StartsWith("[Preview]"))
                DestroyImmediate(child);
        }

        _assembly = GetComponent<PenAssembly>();
        if (_assembly == null)
        {
            Debug.LogError("找不到 PenAssembly 组件");
            return;
        }
        StartCoroutine(RunTest());
    }

    private IEnumerator RunTest()
    {
        yield return null;

        LogPhysicsState("初始状态");

        if (barrelData != null)
        {
            _assembly.SetBarrel(barrelData);
            LogPhysicsState("装配 Barrel 后");
        }

        foreach (var data in partDatas)
        {
            if (data == null) continue;

            var socket = _assembly.GetSocket(data.PlugsInto);
            if (socket == null)
            {
                Debug.LogWarning($"找不到 Socket: {data.PlugsInto}，跳过 {data.DisplayName}");
                continue;
            }

            bool success = _assembly.AddPart(data, socket);
            Debug.Log($"装配 [{data.DisplayName}] 到 [{data.PlugsInto}]: {(success ? "成功" : "失败")}");
            LogPhysicsState($"装配 {data.DisplayName} 后");
        }
    }

    private void LogPhysicsState(string label)
    {
        var rb = GetComponent<Rigidbody>();
        Debug.Log($"[{label}] mass={rb.mass:F3}, centerOfMass={rb.centerOfMass}, launchMultiplier={_assembly.GetLaunchMultiplier():F3}");
    }

#if UNITY_EDITOR
    // 编辑器里拖入 SO 后点按钮预览模型，不需要运行游戏
    [ContextMenu("编辑器预览装配")]
    private void EditorPreview()
    {
        EditorClearPreview();

        if (barrelData != null && barrelData.VisualPrefab != null)
        {
            var barrelGo = UnityEditor.PrefabUtility.InstantiatePrefab(barrelData.VisualPrefab, transform) as GameObject;
            barrelGo.transform.localPosition = Vector3.zero;
            barrelGo.transform.localRotation = Quaternion.identity;
            barrelGo.name = "[Preview] Barrel";
            barrelGo.hideFlags = HideFlags.DontSave;

            foreach (var data in partDatas)
            {
                if (data == null || data.VisualPrefab == null) continue;
                foreach (var socket in barrelGo.GetComponentsInChildren<PartSocket>())
                {
                    if (socket.SocketType == data.PlugsInto)
                    {
                        var partGo = UnityEditor.PrefabUtility.InstantiatePrefab(data.VisualPrefab, socket.transform) as GameObject;
                        partGo.transform.localPosition = Vector3.zero;
                        partGo.transform.localRotation = Quaternion.identity;
                        partGo.name = $"[Preview] {data.DisplayName}";
                        partGo.hideFlags = HideFlags.DontSave;
                        break;
                    }
                }
            }
        }

        UnityEditor.SceneView.RepaintAll();
    }

    [ContextMenu("清除编辑器预览")]
    private void EditorClearPreview()
    {
        for (int i = transform.childCount - 1; i >= 0; i--)
        {
            var child = transform.GetChild(i).gameObject;
            if (child.hideFlags == HideFlags.DontSave)
                DestroyImmediate(child);
        }
        UnityEditor.SceneView.RepaintAll();
    }
#endif
}
