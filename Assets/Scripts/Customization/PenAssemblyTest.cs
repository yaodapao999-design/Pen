using UnityEngine;

/// <summary>
/// 临时测试脚本：验证 PenAssembly 物理聚合是否正常
/// 测试完成后删除此文件
/// </summary>
public class PenAssemblyTest : MonoBehaviour
{
    [Header("拖入要测试的部件数据")]
    public PenPartData[] testParts;
    public PartSocket[] testSockets;

    private PenAssembly _assembly;

    private void Start()
    {
        _assembly = GetComponent<PenAssembly>();
        if (_assembly == null)
        {
            Debug.LogError("PenAssemblyTest: 找不到 PenAssembly 组件");
            return;
        }

        StartCoroutine(RunTest());
    }

    private System.Collections.IEnumerator RunTest()
    {
        // 等一帧确保 PenAssembly.Start() 已执行完
        yield return null;

        LogPhysicsState("初始状态");

        // 依次装配所有测试部件
        for (int i = 0; i < testParts.Length && i < testSockets.Length; i++)
        {
            bool success = _assembly.AddPart(testParts[i], testSockets[i]);
            Debug.Log($"装配 [{testParts[i].DisplayName}] 到 [{testSockets[i].SocketType}]: {(success ? "成功" : "失败")}");
            LogPhysicsState($"装配 {testParts[i].DisplayName} 后");
        }
    }

    private void LogPhysicsState(string label)
    {
        var rb = GetComponent<Rigidbody>();
        Debug.Log($"[{label}] mass={rb.mass:F3}, centerOfMass={rb.centerOfMass}, launchMultiplier={_assembly.GetLaunchMultiplier():F3}");
    }
}
