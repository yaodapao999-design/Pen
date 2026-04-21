#if UNITY_EDITOR
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Prediction.unity 场景一键配置器。
///
/// 目的:让预测系统的 TrajectoryPreview 根节点、子对象、组件引用全部由代码生成,
/// 避免手工在 Inspector 拖拽的易错环节。与 editor-setup.md 的配置步骤语义一致。
///
/// 触发:
///   - 自动:`[InitializeOnLoad]` + `EditorSceneManager.sceneOpened`,打开 Prediction.unity 时若尚未配置则自动配置
///   - 手动:菜单 `Tools/Trajectory/Setup Prediction Scene`(强制重建)
///
/// 自动触发仅在场景路径严格等于 `Assets/Scenes/Prediction.unity` 时生效;其他战斗场景需手动走菜单。
/// </summary>
[InitializeOnLoad]
public static class PredictionSceneSetup
{
    private const string TargetScenePath = "Assets/Scenes/Prediction.unity";
    private const string ConfigAssetDir = "Assets/ScriptableObjects/Trajectory";
    private const string ConfigAssetPath = ConfigAssetDir + "/MirrorSimulationConfig_Default.asset";
    private const string RootName = "TrajectoryPreview";
    private const string DirectionBandName = "DirectionBand";
    private const string FullPathName = "FullPath";

    static PredictionSceneSetup()
    {
        EditorSceneManager.sceneOpened -= OnSceneOpened;
        EditorSceneManager.sceneOpened += OnSceneOpened;
        // 兜底:域重载后若目标场景已是激活场景,sceneOpened 不会再次触发 —— 用 delayCall 补一次检查
        EditorApplication.delayCall += OnDelayedStartup;
    }

    private static void OnDelayedStartup()
    {
        EditorApplication.delayCall -= OnDelayedStartup;
        var scene = EditorSceneManager.GetActiveScene();
        if (scene.IsValid() && scene.path == TargetScenePath)
            EnsureSetup(scene, force: false);
    }

    [MenuItem("Tools/Trajectory/Setup Prediction Scene")]
    public static void SetupFromMenu()
    {
        var scene = EditorSceneManager.GetActiveScene();
        if (!scene.IsValid() || scene.path != TargetScenePath)
        {
            EditorUtility.DisplayDialog("Trajectory Setup",
                $"请先打开 {TargetScenePath} 作为激活场景再执行。\n当前激活场景:{scene.path}", "OK");
            return;
        }
        EnsureSetup(scene, force: true);
    }

    private static void OnSceneOpened(Scene scene, OpenSceneMode mode)
    {
        if (scene.path != TargetScenePath) return;
        EnsureSetup(scene, force: false);
    }

    private static void EnsureSetup(Scene scene, bool force)
    {
        var existing = FindRootInScene(scene);
        if (existing != null && !force) return;

        var sm = Object.FindAnyObjectByType<BattleStateMachine>();
        if (sm == null)
        {
            Debug.LogWarning("[PredictionSceneSetup] 场景中未找到 BattleStateMachine,跳过配置");
            return;
        }

        var pen = sm.Pen;
        if (pen == null)
        {
            Debug.LogWarning("[PredictionSceneSetup] BattleStateMachine.Pen 未赋值,跳过配置");
            return;
        }

        var tableCollider = FindTableCollider(pen, sm.transform);
        if (tableCollider == null)
            Debug.LogWarning("[PredictionSceneSetup] 自动查找台面 Collider 失败,已挂组件但 tableCollider 字段需手工填写");

        var config = LoadOrCreateConfig();

        if (existing != null)
        {
            Undo.DestroyObjectImmediate(existing);
            existing = null;
        }

        var root = new GameObject(RootName);
        Undo.RegisterCreatedObjectUndo(root, "Setup TrajectoryPreview");
        if (root.scene != scene)
            SceneManager.MoveGameObjectToScene(root, scene);

        var directionBand = CreateLineChild(root.transform, DirectionBandName, 2);
        var fullPath = CreateLineChild(root.transform, FullPathName, 0);

        var mirror = root.AddComponent<PhysicsMirrorWorld>();
        var presenter = root.AddComponent<TrajectoryPreviewPresenter>();
        var controller = root.AddComponent<TrajectoryPreviewController>();

        AssignField(mirror, "sourcePen", pen);
        if (sm.EnemyPen != null) AssignField(mirror, "sourceEnemyPen", sm.EnemyPen);
        if (tableCollider != null) AssignField(mirror, "tableCollider", tableCollider);

        AssignField(presenter, "directionBandRenderer", directionBand.GetComponent<LineRenderer>());
        AssignField(presenter, "sourcePenForGhost", pen);
        if (sm.EnemyPen != null) AssignField(presenter, "sourceEnemyPenForGhost", sm.EnemyPen);
        AssignField(presenter, "fullPathRenderer", fullPath.GetComponent<LineRenderer>());

        AssignField(controller, "battleStateMachine", sm);
        AssignField(controller, "mirrorWorld", mirror);
        AssignField(controller, "simulationConfig", config);
        AssignField(controller, "presenter", presenter);

        EditorSceneManager.MarkSceneDirty(scene);
        Debug.Log($"[PredictionSceneSetup] TrajectoryPreview 已配置。sourcePen={pen.name}, sourceEnemyPen={(sm.EnemyPen != null ? sm.EnemyPen.name : "<无>")}, tableCollider={(tableCollider != null ? tableCollider.name : "<需手工>")}。按 Ctrl+S 保存场景。");
    }

    private static GameObject FindRootInScene(Scene scene)
    {
        foreach (var go in scene.GetRootGameObjects())
        {
            if (go.name == RootName) return go;
        }
        return null;
    }

    private static MirrorSimulationConfig LoadOrCreateConfig()
    {
        if (!Directory.Exists(ConfigAssetDir))
        {
            Directory.CreateDirectory(ConfigAssetDir);
            AssetDatabase.Refresh();
        }
        var config = AssetDatabase.LoadAssetAtPath<MirrorSimulationConfig>(ConfigAssetPath);
        if (config != null) return config;

        config = ScriptableObject.CreateInstance<MirrorSimulationConfig>();
        AssetDatabase.CreateAsset(config, ConfigAssetPath);
        AssetDatabase.SaveAssets();
        return config;
    }

    // 台面 Collider 查找策略:
    //   1. 名字含 "Table" 且无 Rigidbody 的 Collider
    //   2. 否则取最大水平面积的 BoxCollider 且无 Rigidbody
    private static Collider FindTableCollider(PenEntity pen, Transform smTransform)
    {
        var all = Object.FindObjectsByType<Collider>(FindObjectsSortMode.None);

        foreach (var c in all)
        {
            if (c == null) continue;
            if (c.attachedRigidbody != null) continue;
            if (c.GetComponentInParent<PenEntity>() != null) continue;
            if (c.gameObject.name.IndexOf("Table", System.StringComparison.OrdinalIgnoreCase) >= 0)
                return c;
        }

        Collider best = null;
        float bestArea = 0f;
        foreach (var c in all)
        {
            if (c == null) continue;
            if (c.attachedRigidbody != null) continue;
            if (c.GetComponentInParent<PenEntity>() != null) continue;
            if (!(c is BoxCollider)) continue;
            float area = c.bounds.size.x * c.bounds.size.z;
            if (area > bestArea) { bestArea = area; best = c; }
        }
        return best;
    }

    private static GameObject CreateLineChild(Transform parent, string name, int pointCount)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        var lr = go.AddComponent<LineRenderer>();
        lr.useWorldSpace = true;
        lr.positionCount = pointCount;
        lr.widthMultiplier = 0.03f;
        lr.material = GetDefaultLineMaterial();
        lr.enabled = false;
        return go;
    }

    private static Material _cachedLineMaterial;
    private static Material GetDefaultLineMaterial()
    {
        if (_cachedLineMaterial != null) return _cachedLineMaterial;
        var shader = Shader.Find("Universal Render Pipeline/Unlit")
                     ?? Shader.Find("Unlit/Color")
                     ?? Shader.Find("Sprites/Default");
        _cachedLineMaterial = new Material(shader);
        _cachedLineMaterial.name = "TrajectoryLine(runtime)";
        return _cachedLineMaterial;
    }

    private static void AssignField(Object target, string fieldName, Object value)
    {
        if (target == null) return;
        using var so = new SerializedObject(target);
        var prop = so.FindProperty(fieldName);
        if (prop == null)
        {
            Debug.LogWarning($"[PredictionSceneSetup] 找不到 SerializeField {target.GetType().Name}.{fieldName}");
            return;
        }
        prop.objectReferenceValue = value;
        so.ApplyModifiedPropertiesWithoutUndo();
    }
}
#endif
