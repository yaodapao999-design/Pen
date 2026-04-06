using Unity.Cinemachine;
using UnityEditor;
using UnityEngine;

/// <summary>
/// 编辑器工具：一键在场景中创建聚焦镜头运动系统所需的所有 GameObject 和组件。
///
/// 菜单入口：Tools / Pen / Setup Focus Camera
///
/// 创建内容：
///   FocusCameraSystem (GameObject)
///     ├── [Component] FocusCameraController     ← 运动逻辑（支持任意数量笔）
///     ├── [Component] CinemachineTargetGroup    ← 动态目标组
///     └── FocusVirtualCamera (子 GameObject)
///         ├── [Component] CinemachineCamera           ← 聚焦虚拟摄像机
///         └── [Component] CinemachinePositionComposer ← 只控制位移/距离，不影响旋转
///
/// 俯视角维持方式：
///   CinemachinePositionComposer 只调整摄像机世界位置，不改变旋转。
///   FocusVirtualCamera 在创建时被设置为俯视旋转（Pitch = TopDownPitch），
///   游戏运行期间该旋转将保持不变，从而始终维持俯视角。
/// </summary>
public static class FocusCameraSetup
{
    private const string MenuPath = "Tools/Pen/Setup Focus Camera";

    /// <summary>俯视 X 轴旋转角（60° = 斜俯视，90° = 正俯视）</summary>
    private const float TopDownPitch = 60f;

    [MenuItem(MenuPath)]
    public static void SetupFocusCamera()
    {
        // ── 防重复创建 ──────────────────────────────────────────────
        if (Object.FindFirstObjectByType<FocusCameraController>() != null)
        {
            EditorUtility.DisplayDialog(
                "已存在",
                "场景中已有 FocusCameraController，跳过创建。\n请直接在 Inspector 的 Initial Pens 列表中添加参战笔。",
                "OK");
            return;
        }

        // ── 1. 创建根节点 ────────────────────────────────────────────
        var root = new GameObject("FocusCameraSystem");
        Undo.RegisterCreatedObjectUndo(root, "Create FocusCameraSystem");

        // ── 2. 添加 CinemachineTargetGroup ──────────────────────────
        var targetGroup = root.AddComponent<CinemachineTargetGroup>();

        // ── 3. 创建 CinemachineCamera 子节点并设置俯视旋转 ──────────
        var vcamGO = new GameObject("FocusVirtualCamera");
        vcamGO.transform.SetParent(root.transform);
        // 设置俯视旋转：CinemachinePositionComposer 不会修改旋转，该值将在整个游戏中保持不变
        vcamGO.transform.rotation = Quaternion.Euler(TopDownPitch, 0f, 0f);
        Undo.RegisterCreatedObjectUndo(vcamGO, "Create FocusVirtualCamera");

        // ── 4. 添加 CinemachineCamera 并绑定 TargetGroup ─────────────
        var vcam = vcamGO.AddComponent<CinemachineCamera>();
        vcam.Follow = targetGroup.transform;
        vcam.Priority = 15;

        // ── 5. 添加 CinemachinePositionComposer（仅控制距离/位置）──────
        //    该组件不会修改旋转，俯视角由步骤3中的初始旋转决定
        var composer = vcamGO.AddComponent<CinemachinePositionComposer>();
        composer.CameraDistance = 8f;

        // ── 6. 添加 FocusCameraController 并关联引用 ─────────────────
        var controller = root.AddComponent<FocusCameraController>();

        var so = new SerializedObject(controller);
        so.FindProperty("targetGroup").objectReferenceValue = targetGroup;
        so.FindProperty("focusCamera").objectReferenceValue = vcam;
        so.ApplyModifiedProperties();

        // ── 7. 尝试自动添加 Main Camera 上的 CinemachineBrain ────────
        var mainCam = Camera.main;
        if (mainCam != null && mainCam.GetComponent<CinemachineBrain>() == null)
        {
            Undo.AddComponent<CinemachineBrain>(mainCam.gameObject);
            Debug.Log("[FocusCameraSetup] 已在 Main Camera 上自动添加 CinemachineBrain。");
        }

        // ── 8. 选中根节点，提示配置 ──────────────────────────────────
        Selection.activeGameObject = root;
        EditorUtility.DisplayDialog(
            "✅ 聚焦镜头系统已创建",
            "请在 FocusCameraSystem 的 FocusCameraController Inspector 中：\n\n" +
            "  • 将所有参战笔的 Transform 拖入 Initial Pens 列表（支持任意数量）\n" +
            "  • 根据场景尺寸调整 Max Distance / Min FOV / Max FOV\n\n" +
            "俯视角已自动锁定（FocusVirtualCamera 初始 Pitch = " + TopDownPitch + "°）。\n" +
            "笔被淘汰时调用 FocusCameraController.Instance.RemovePen(pen.transform) 即可剔除。",
            "OK");

        Debug.Log("[FocusCameraSetup] FocusCameraSystem 创建完毕。");
    }

    [MenuItem(MenuPath, true)]
    private static bool ValidateSetupFocusCamera()
    {
        return UnityEngine.SceneManagement.SceneManager.GetActiveScene().IsValid();
    }
}
