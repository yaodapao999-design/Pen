using MoreMountains.Feedbacks;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Editor 工具：一键为场景中的 FeedbackManager 配置碰撞音效 Feedback。
/// 菜单路径：Tools / Pen / Setup Collision Audio Feedback
///
/// 执行效果：
///   1. 在 FeedbackManager 上添加 AudioSource（若不存在）
///   2. 在 MMF_Player 中插入 MMF_AudioSource Feedback
///   3. 配置 Pitch 随机化（±0.1）与强度驱动 Volume
/// </summary>
public static class CollisionAudioSetup
{
    private const string FeedbackManagerName = "FeedbackManager";
    private const string AudioSourceGoName   = "CollisionAudioSource";

    [MenuItem("Tools/Pen/Setup Collision Audio Feedback")]
    public static void SetupCollisionAudioFeedback()
    {
        // ── 1. 找到场景中的 FeedbackManager ──────────────────────────
        var feedbackManagerGo = GameObject.Find(FeedbackManagerName);
        if (feedbackManagerGo == null)
        {
            Debug.LogError($"[CollisionAudioSetup] 场景中未找到名为 \"{FeedbackManagerName}\" 的 GameObject。" +
                           "请先按计划文档三、3.1 节在场景中创建 FeedbackManager 并挂载 MMF_Player。");
            return;
        }

        var mmfPlayer = feedbackManagerGo.GetComponent<MMF_Player>();
        if (mmfPlayer == null)
        {
            Debug.LogError($"[CollisionAudioSetup] \"{FeedbackManagerName}\" 上未找到 MMF_Player 组件。" +
                           "请先添加 MMF_Player 并完成镜头抖动基础配置。");
            return;
        }

        // ── 2. 创建（或复用）专用 AudioSource 子物体 ─────────────────
        //    将 AudioSource 放在子物体上，保持 FeedbackManager 层级整洁。
        var audioSourceGo = FindOrCreateChild(feedbackManagerGo, AudioSourceGoName);
        var audioSource   = EnsureComponent<AudioSource>(audioSourceGo);

        // 配置 AudioSource 默认参数：
        // - PlayOnAwake 关闭，由 MMF_Player 按需触发
        // - SpatialBlend = 0（纯 2D 音效，全局播放）
        // - Volume 由 MMF_AudioSource Feedback 的 RemapVolumeOne 控制
        audioSource.playOnAwake  = false;
        audioSource.spatialBlend = 0f;
        audioSource.volume       = 1f;

        // ── 3. 检查 MMF_Player 中是否已有 MMF_AudioSource ────────────
        bool alreadyExists = false;
        foreach (var fb in mmfPlayer.FeedbacksList)
        {
            if (fb is MMF_AudioSource)
            {
                alreadyExists = true;
                Debug.LogWarning("[CollisionAudioSetup] MMF_Player 中已存在 MMF_AudioSource Feedback，" +
                                 "跳过重复添加。如需重置，请手动删除后重新执行此菜单。");
                break;
            }
        }

        if (!alreadyExists)
        {
            // ── 4. 创建 MMF_AudioSource 实例并添加到 MMF_Player ──────
            var audioFeedback = new MMF_AudioSource();
            mmfPlayer.AddFeedback(audioFeedback);

            // —— 绑定 AudioSource ——
            audioFeedback.TargetAudioSource = audioSource;

            // —— Pitch 随机化（±0.1 防止重复感）——
            // MMF_AudioSource 在播放时取 Random.Range(MinPitch, MaxPitch)
            audioFeedback.MinPitch = 0.9f;
            audioFeedback.MaxPitch = 1.1f;

            // —— Volume 范围（强度已通过 ComputeIntensity 自动缩放）——
            // MMF_AudioSource.CustomPlayFeedback：volume = Random.Range(MinVolume,MaxVolume) * intensityMultiplier
            // 此处设为最大音量 1，实际播放音量由碰撞强度（FeedbacksIntensity）乘以此值。
            audioFeedback.MinVolume = 1f;
            audioFeedback.MaxVolume = 1f;

            Debug.Log("[CollisionAudioSetup] ✅ MMF_AudioSource Feedback 已添加并配置完成！\n" +
                      $"AudioSource 子物体路径：{FeedbackManagerName}/{AudioSourceGoName}\n" +
                      "后续步骤：在 Inspector 中将碰撞 SFX 音频文件赋值到 TargetAudioSource 的 AudioClip 字段。");
        }

        // ── 5. 标记场景已修改 ─────────────────────────────────────────
        EditorUtility.SetDirty(feedbackManagerGo);
        EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());

        // 选中 FeedbackManager 方便在 Inspector 查看
        Selection.activeGameObject = feedbackManagerGo;
        EditorGUIUtility.PingObject(feedbackManagerGo);
    }

    /// <summary>菜单校验：仅在编辑器非播放状态下可用。</summary>
    [MenuItem("Tools/Pen/Setup Collision Audio Feedback", true)]
    private static bool ValidateSetupCollisionAudioFeedback()
    {
        return !EditorApplication.isPlaying;
    }

    // ── 辅助方法 ──────────────────────────────────────────────────────

    /// <summary>在父物体下查找或创建同名子物体。</summary>
    private static GameObject FindOrCreateChild(GameObject parent, string childName)
    {
        var existingTransform = parent.transform.Find(childName);
        if (existingTransform != null)
            return existingTransform.gameObject;

        var child = new GameObject(childName);
        child.transform.SetParent(parent.transform, false);
        return child;
    }

    /// <summary>确保 GameObject 上存在指定组件，不存在则添加。</summary>
    private static T EnsureComponent<T>(GameObject go) where T : Component
    {
        var comp = go.GetComponent<T>();
        return comp != null ? comp : go.AddComponent<T>();
    }
}
