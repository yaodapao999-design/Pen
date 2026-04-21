using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// 独立 PhysicsScene 容器。双笔(玩家 + 敌方)均以 PenAssembly.InitData + BuildBattleView 在镜像场景
/// 完整装配,barrel + 所有子零件各自的 Collider / PhysicsMaterial / Mass 与主场景 1:1 对齐。
///
/// SyncFrom(PenSnapshot) 在每次预测前:
///   1. 比对 BarrelData / AssembledParts 差异 → 变化则重建该笔的装配副本
///   2. 同步玩家笔的动态属性(位姿 / mass / COM / 阻尼)
///   3. 同步敌方笔当前位姿(视为静态,不 ApplyLaunch)
///
/// 初始化失败时 IsReady=false;外部(Simulator / Controller)须退化到 Minimal 披露,
/// 不得用解析近似作为 fallback(GDD §6 / 非目标)。
/// </summary>
public class PhysicsMirrorWorld : MonoBehaviour
{
    [Header("来源(主场景)")]
    [Tooltip("主场景玩家笔。镜像通过 sourcePen.Assembly 读取 BarrelData + AssembledParts 在镜像场景装配")]
    [SerializeField] private PenEntity sourcePen;

    [Tooltip("主场景敌方笔。同样复用 PenAssembly 构建镜像装配;蓄力阶段视为静态(每次 SyncFrom 同步当前位姿)")]
    [SerializeField] private PenEntity sourceEnemyPen;

    [Tooltip("主场景台面 Collider。Mirror 以 AABB 创建等尺寸 BoxCollider 作为副本")]
    [SerializeField] private Collider tableCollider;

    [Tooltip("额外需要进入镜像的物理元素(桌脚 / 墙壁 / 凸起 / 障碍物等)。每个 Collider 的所在 GameObject 会被 Instantiate 到镜像场景,保留 Collider 与 PhysicsMaterial,禁用 MB/Renderer/Rigidbody.useGravity")]
    [SerializeField] private Collider[] extraColliders;

    [Header("独立 Scene")]
    [Tooltip("镜像 Scene 名称(Profiler/Hierarchy 区分用,不影响逻辑)")]
    [SerializeField] private string mirrorSceneName = "TrajectoryMirror";

    private Scene _scene;
    private PhysicsScene _physics;
    private GameObject _root;

    private GameObject _mirrorPenGo;
    private Rigidbody _mirrorPenRb;
    private PenAssembly _mirrorPenAssembly;

    private GameObject _mirrorEnemyGo;
    private Rigidbody _mirrorEnemyRb;
    private PenAssembly _mirrorEnemyAssembly;

    private bool _initializedOnce;
    private bool _failed;

    public bool IsReady => _initializedOnce && !_failed && _mirrorPenRb != null;
    public bool IsFailed => _failed;
    public Rigidbody MirrorPen => _mirrorPenRb;
    public Rigidbody MirrorEnemyPen => _mirrorEnemyRb;
    public PhysicsScene Physics => _physics;
    public TrajectoryCollisionProbe PlayerCollisionProbe =>
        _mirrorPenGo != null ? _mirrorPenGo.GetComponent<TrajectoryCollisionProbe>() : null;

    /// <summary>首次调用时构建镜像;后续直接返回当前状态。失败即永久失败(本实例生命周期内不再重试)。</summary>
    public bool EnsureInitialized()
    {
        if (_initializedOnce) return !_failed;
        _initializedOnce = true;

        try
        {
            if (sourcePen == null)
                throw new System.InvalidOperationException("sourcePen 未赋值");
            if (sourcePen.Assembly == null || sourcePen.Assembly.BarrelData == null)
                throw new System.InvalidOperationException("sourcePen.Assembly 未就绪(PenAssembly.BuildBattleView 尚未完成)");
            if (tableCollider == null)
                throw new System.InvalidOperationException("tableCollider 未赋值");

            _scene = SceneManager.CreateScene(mirrorSceneName, new CreateSceneParameters(LocalPhysicsMode.Physics3D));
            _physics = _scene.GetPhysicsScene();

            _root = new GameObject("Root");
            SceneManager.MoveGameObjectToScene(_root, _scene);

            BuildMirrorTable();
            BuildExtraMirrors();
            BuildAssembledMirror(sourcePen, "MirrorPen", out _mirrorPenGo, out _mirrorPenRb, out _mirrorPenAssembly);

            if (sourceEnemyPen != null && sourceEnemyPen.Assembly != null && sourceEnemyPen.Assembly.BarrelData != null)
            {
                BuildAssembledMirror(sourceEnemyPen, "MirrorEnemyPen", out _mirrorEnemyGo, out _mirrorEnemyRb, out _mirrorEnemyAssembly);
            }
            else if (sourceEnemyPen != null)
            {
                Debug.LogWarning("[PhysicsMirrorWorld] sourceEnemyPen 已赋值但其 PenAssembly 未就绪,敌方镜像跳过构建(将在装配完成后首次 SyncFrom 时补建)");
            }

            AttachPlayerCollisionProbe();
            return true;
        }
        catch (System.Exception ex)
        {
            _failed = true;
            Debug.LogError($"[PhysicsMirrorWorld] 初始化失败,预测将退化为 Minimal: {ex}");
            return false;
        }
    }

    public void SyncFrom(PenSnapshot snapshot)
    {
        if (!_initializedOnce) EnsureInitialized();
        if (_failed || _mirrorPenRb == null) return;
        if (!snapshot.IsValid) return;

        // 玩家:装配 diff → 重建
        if (sourcePen != null && NeedsRebuildAssembly(_mirrorPenAssembly, sourcePen.Assembly))
        {
            RebuildAssembly(_mirrorPenAssembly, sourcePen);
        }

        // 玩家:动态属性同步
        ApplyPlayerDynamics(_mirrorPenRb, snapshot);

        // 敌方:若 SerializeField 赋了但之前未建(Assembly 当时未就绪),补建
        bool enemyMirrorChanged = false;
        if (sourceEnemyPen != null && _mirrorEnemyAssembly == null &&
            sourceEnemyPen.Assembly != null && sourceEnemyPen.Assembly.BarrelData != null)
        {
            BuildAssembledMirror(sourceEnemyPen, "MirrorEnemyPen", out _mirrorEnemyGo, out _mirrorEnemyRb, out _mirrorEnemyAssembly);
            enemyMirrorChanged = true;
        }

        // 敌方:装配 diff → 重建
        if (sourceEnemyPen != null && _mirrorEnemyAssembly != null && NeedsRebuildAssembly(_mirrorEnemyAssembly, sourceEnemyPen.Assembly))
        {
            RebuildAssembly(_mirrorEnemyAssembly, sourceEnemyPen);
        }

        if (enemyMirrorChanged) AttachPlayerCollisionProbe();

        // 敌方:**完整**动态属性同步(mass/COM/damping/constraints + 位姿 + 速度清零)——
        // 之前只同步位姿会导致镜像敌方 mass/COM 与主场景漂移,碰撞动量预测失真
        if (_mirrorEnemyRb != null && sourceEnemyPen != null && sourceEnemyPen.rb != null)
        {
            var erb = sourceEnemyPen.rb;
            _mirrorEnemyRb.mass = erb.mass;
            _mirrorEnemyRb.centerOfMass = erb.centerOfMass;
            _mirrorEnemyRb.linearDamping = erb.linearDamping;
            _mirrorEnemyRb.angularDamping = erb.angularDamping;
            _mirrorEnemyRb.useGravity = erb.useGravity;
            _mirrorEnemyRb.constraints = erb.constraints;
            _mirrorEnemyRb.position = erb.position;
            _mirrorEnemyRb.rotation = erb.rotation;
            _mirrorEnemyRb.linearVelocity = Vector3.zero;
            _mirrorEnemyRb.angularVelocity = Vector3.zero;
        }

        // rb.position setter 不会自动同步 transform,下一次 Simulate 前碰撞检测可能仍用旧 transform 位置
        // 导致镜像笔疑似"穿过台面"自由落体 → 无摩擦 → 预测距离异常远。显式同步修正
        UnityEngine.Physics.SyncTransforms();
    }

    public void Step(float dt)
    {
        if (!IsReady) return;
        _physics.Simulate(dt);
    }

    // ─── 装配构建 ────────────────────────────────────────────────────────

    private void BuildAssembledMirror(PenEntity source, string name, out GameObject go, out Rigidbody rb, out PenAssembly assembly)
    {
        go = new GameObject(name);
        SceneManager.MoveGameObjectToScene(go, _scene);
        go.transform.SetParent(_root.transform);

        // Rigidbody 先加 —— PenAssembly.Awake 里 GetComponent<Rigidbody> 需要
        rb = go.AddComponent<Rigidbody>();
        rb.useGravity = true;
        rb.linearDamping = 0f;
        rb.angularDamping = 0f;
        rb.automaticInertiaTensor = true;
        rb.automaticCenterOfMass = false;
        // 冲量较大时 Discrete 可能一帧穿透台面(镜像笔直接自由落体 → 无摩擦 → 预测飞很远)
        rb.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;

        assembly = go.AddComponent<PenAssembly>();

        var partsCopy = CloneAssembledParts(source.Assembly);
        assembly.SetData(source.Assembly.BarrelData, partsCopy);
        assembly.BuildBattleView();

        // 禁用镜像笔的所有 Renderer:主相机会渲染所有已加载 Scene 的 Renderer,
        // 镜像笔带有 barrel+零件的 MeshRenderer,会和玩家/敌方笔叠加显示("mirror 没隐藏");
        // 镜像只关心物理,无视觉需求
        DisableAllRenderers(go);
    }

    private void RebuildAssembly(PenAssembly mirrorAssembly, PenEntity source)
    {
        mirrorAssembly.ClearBattleView();
        var partsCopy = CloneAssembledParts(source.Assembly);
        mirrorAssembly.SetData(source.Assembly.BarrelData, partsCopy);
        mirrorAssembly.BuildBattleView();
        DisableAllRenderers(mirrorAssembly.gameObject);
    }

    private static void DisableAllRenderers(GameObject root)
    {
        if (root == null) return;
        var renderers = root.GetComponentsInChildren<Renderer>(true);
        for (int i = 0; i < renderers.Length; i++)
            if (renderers[i] != null) renderers[i].enabled = false;
    }

    private static List<PenAssembly.PartEntry> CloneAssembledParts(PenAssembly source)
    {
        var list = new List<PenAssembly.PartEntry>(source.AssembledParts.Count);
        for (int i = 0; i < source.AssembledParts.Count; i++)
            list.Add(source.AssembledParts[i]);
        return list;
    }

    private static bool NeedsRebuildAssembly(PenAssembly mirror, PenAssembly source)
    {
        if (mirror == null || source == null) return true;
        if (mirror.BarrelData != source.BarrelData) return true;
        if (mirror.AssembledParts.Count != source.AssembledParts.Count) return true;
        for (int i = 0; i < mirror.AssembledParts.Count; i++)
        {
            var a = mirror.AssembledParts[i];
            var b = source.AssembledParts[i];
            if (a.Data != b.Data) return true;
            if (a.Socket != b.Socket) return true;
        }
        return false;
    }

    private static void ApplyPlayerDynamics(Rigidbody rb, PenSnapshot snap)
    {
        rb.mass = snap.Mass;
        rb.centerOfMass = snap.LocalCenterOfMass;
        rb.linearDamping = snap.LinearDamping;
        rb.angularDamping = snap.AngularDamping;
        rb.useGravity = snap.UseGravity;
        rb.constraints = snap.Constraints;
        rb.position = snap.Position;
        rb.rotation = snap.Rotation;
        rb.linearVelocity = Vector3.zero;
        rb.angularVelocity = Vector3.zero;
    }

    private void AttachPlayerCollisionProbe()
    {
        if (_mirrorPenGo == null) return;
        var probe = _mirrorPenGo.GetComponent<TrajectoryCollisionProbe>();
        if (probe == null) probe = _mirrorPenGo.AddComponent<TrajectoryCollisionProbe>();
        probe.watchedRigidbody = _mirrorEnemyRb;
    }

    private void BuildMirrorTable()
    {
        var bounds = tableCollider.bounds;
        var go = new GameObject("MirrorTable");
        SceneManager.MoveGameObjectToScene(go, _scene);
        go.transform.SetParent(_root.transform);
        go.transform.position = bounds.center;
        go.transform.rotation = Quaternion.identity;

        var box = go.AddComponent<BoxCollider>();
        box.size = bounds.size;
        box.sharedMaterial = tableCollider.sharedMaterial;
    }

    private void BuildExtraMirrors()
    {
        if (extraColliders == null) return;
        for (int i = 0; i < extraColliders.Length; i++)
        {
            if (extraColliders[i] != null) BuildExtraMirror(extraColliders[i]);
        }
    }

    private void BuildExtraMirror(Collider source)
    {
        var clone = Instantiate(source.gameObject);
        clone.name = $"MirrorExtra({source.gameObject.name})";

        // 保留世界位姿:先 Move 到镜像 Scene 再挂到 Root 下
        clone.transform.position = source.transform.position;
        clone.transform.rotation = source.transform.rotation;
        clone.transform.localScale = source.transform.lossyScale;

        SceneManager.MoveGameObjectToScene(clone, _scene);
        clone.transform.SetParent(_root.transform, worldPositionStays: true);

        // 禁用所有 MonoBehaviour:避免脚本副作用(反馈/AI/UI 等在镜像 Scene 跑)
        var mbs = clone.GetComponentsInChildren<MonoBehaviour>(true);
        for (int i = 0; i < mbs.Length; i++)
            if (mbs[i] != null) mbs[i].enabled = false;

        // Rigidbody 若有(一般静态物体无 rb,桌脚/障碍偶尔有 kinematic rb):转为 kinematic 并停运动
        var rbs = clone.GetComponentsInChildren<Rigidbody>(true);
        for (int i = 0; i < rbs.Length; i++)
        {
            if (rbs[i] == null) continue;
            rbs[i].isKinematic = true;
            rbs[i].useGravity = false;
            rbs[i].linearVelocity = Vector3.zero;
            rbs[i].angularVelocity = Vector3.zero;
            // 保留 detectCollisions=true(默认),确保参与镜像碰撞
        }

        // 禁用 Renderer:镜像不需要渲染,节省 GPU
        foreach (var r in clone.GetComponentsInChildren<Renderer>(true))
            r.enabled = false;

        // Collider 保持 enabled(物理仍有效)——Instantiate 默认保留
    }

    private void OnDestroy()
    {
        if (_initializedOnce && !_failed && _scene.IsValid() && _scene.isLoaded)
        {
            SceneManager.UnloadSceneAsync(_scene);
        }
    }
}
