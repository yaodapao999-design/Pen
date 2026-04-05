using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// Editor 工具：一键创建碰撞粒子特效 Prefab（碰撞火花）。
/// 菜单路径：Tools / Pen / Create Collision Particle Prefab
/// </summary>
public static class CollisionParticleSetup
{
    private const string PrefabSavePath   = "Assets/Prefabs/VFX_CollisionSparks.prefab";
    private const string MaterialSavePath = "Assets/Prefabs/Mat_CollisionSparks.mat";

    // URP Particles/Unlit shader 名称（内置于 URP 包）
    private const string URPParticleShader = "Universal Render Pipeline/Particles/Unlit";

    [MenuItem("Tools/Pen/Create Collision Particle Prefab")]
    public static void CreateCollisionParticlePrefab()
    {
        // ── 1. 创建 URP 兼容材质（修复紫色粒子问题） ────────────
        var shader = Shader.Find(URPParticleShader);
        if (shader == null)
        {
            Debug.LogWarning("[CollisionParticleSetup] 未找到 URP Particles/Unlit shader，" +
                             "将使用默认材质（可能显示为紫色）。请确认项目已安装 URP 并正确配置。");
        }

        Material sparkMat;
        var existingMat = AssetDatabase.LoadAssetAtPath<Material>(MaterialSavePath);
        if (existingMat != null)
        {
            sparkMat = existingMat;
        }
        else
        {
            sparkMat = shader != null
                ? new Material(shader)
                : new Material(Shader.Find("Particles/Standard Unlit"));

            // 开启 Alpha Blending（粒子淡出需要透明度）
            sparkMat.SetFloat("_Surface", 1f);         // 0=Opaque, 1=Transparent
            sparkMat.SetFloat("_Blend",   0f);         // Alpha blending
            sparkMat.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
            sparkMat.SetFloat("_DstBlend", (float)BlendMode.OneMinusSrcAlpha);
            sparkMat.SetFloat("_ZWrite",  0f);
            sparkMat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            sparkMat.renderQueue = (int)RenderQueue.Transparent;

            // 自发光白色，让颜色完全由粒子 Color over Lifetime 驱动
            sparkMat.SetColor("_BaseColor", Color.white);

            AssetDatabase.CreateAsset(sparkMat, MaterialSavePath);
        }

        // ── 2. 创建根 GameObject ──────────────────────────────────
        var go = new GameObject("VFX_CollisionSparks");

        // ── 3. 获取并配置 ParticleSystem ─────────────────────────
        var ps       = go.AddComponent<ParticleSystem>();
        var psRenderer = go.GetComponent<ParticleSystemRenderer>();

        // —— Main Module ——
        var main = ps.main;
        main.duration        = 0.3f;
        main.loop            = false;
        main.startLifetime   = new ParticleSystem.MinMaxCurve(0.2f, 0.5f);
        main.startSpeed      = new ParticleSystem.MinMaxCurve(2f, 8f);
        main.startSize       = new ParticleSystem.MinMaxCurve(0.03f, 0.12f);
        main.startColor      = new ParticleSystem.MinMaxGradient(
            new Color(1f, 0.85f, 0.2f, 1f),   // 亮黄
            new Color(1f, 0.4f,  0.1f, 1f));   // 橙红
        main.gravityModifier = new ParticleSystem.MinMaxCurve(0.5f, 1.2f);
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        // MMF_ParticlesInstantiation 使用对象池，粒子结束后必须 Disable 而非 Destroy，
        // 否则第二次从池中取出时引用已失效，抛 MissingReferenceException。
        main.stopAction      = ParticleSystemStopAction.Disable;
        main.maxParticles    = 40;

        // —— Emission Module ——
        var emission = ps.emission;
        emission.enabled = true;
        emission.SetBursts(new[] { new ParticleSystem.Burst(0f, 15, 30) });
        emission.rateOverTime = 0f; // 只有爆发式发射

        // —— Shape Module ——
        var shape = ps.shape;
        shape.enabled         = true;
        shape.shapeType       = ParticleSystemShapeType.Sphere;
        shape.radius          = 0.05f;
        shape.radiusThickness = 0f;

        // —— Velocity over Lifetime ——
        var vol = ps.velocityOverLifetime;
        vol.enabled       = true;
        vol.speedModifier = new ParticleSystem.MinMaxCurve(
            1f,
            AnimationCurve.EaseInOut(0f, 1f, 1f, 0f)); // 逐渐减速

        // —— Size over Lifetime ——
        var sol = ps.sizeOverLifetime;
        sol.enabled = true;
        sol.size    = new ParticleSystem.MinMaxCurve(
            1f,
            AnimationCurve.Linear(0f, 1f, 1f, 0f));    // 线性缩小至消失

        // —— Color over Lifetime ——
        var col = ps.colorOverLifetime;
        col.enabled = true;
        var gradient = new Gradient();
        gradient.SetKeys(
            new[]
            {
                new GradientColorKey(new Color(1f, 0.9f, 0.3f), 0f),
                new GradientColorKey(new Color(1f, 0.3f, 0.1f), 0.6f),
                new GradientColorKey(new Color(0.3f, 0.3f, 0.3f), 1f)
            },
            new[]
            {
                new GradientAlphaKey(1f, 0f),
                new GradientAlphaKey(0.8f, 0.5f),
                new GradientAlphaKey(0f, 1f)
            }
        );
        col.color = new ParticleSystem.MinMaxGradient(gradient);

        // —— Collision Module（粒子与物理碰撞） ——
        var collision = ps.collision;
        collision.enabled      = true;
        collision.type         = ParticleSystemCollisionType.World;
        collision.mode         = ParticleSystemCollisionMode.Collision3D;
        collision.bounce       = new ParticleSystem.MinMaxCurve(0.3f, 0.6f);
        collision.dampen       = new ParticleSystem.MinMaxCurve(0.2f);
        collision.lifetimeLoss = new ParticleSystem.MinMaxCurve(0.1f);

        // —— Renderer（赋 URP 材质，解决紫色问题） ——
        psRenderer.renderMode       = ParticleSystemRenderMode.Billboard;
        psRenderer.sharedMaterial   = sparkMat;
        psRenderer.sortingLayerName = "Default";
        psRenderer.sortingOrder     = 10;

        // ── 4. 停止自动播放（由 Feel MMF_ParticlesInstantiation 驱动） ──
        ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

        // ── 5. 保存为 Prefab ──────────────────────────────────────
        bool prefabSuccess;
        PrefabUtility.SaveAsPrefabAsset(go, PrefabSavePath, out prefabSuccess);
        Object.DestroyImmediate(go);
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        if (prefabSuccess)
        {
            Debug.Log($"[CollisionParticleSetup] 粒子 Prefab 已创建：{PrefabSavePath}");
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabSavePath);
            Selection.activeObject = prefab;
            EditorGUIUtility.PingObject(prefab);
        }
        else
        {
            Debug.LogError("[CollisionParticleSetup] 粒子 Prefab 创建失败，请检查路径是否存在。");
        }
    }

    /// <summary>
    /// 验证菜单项：确保 Prefabs 目录存在。
    /// </summary>
    [MenuItem("Tools/Pen/Create Collision Particle Prefab", true)]
    private static bool ValidateCreateCollisionParticlePrefab()
    {
        return AssetDatabase.IsValidFolder("Assets/Prefabs");
    }
}
