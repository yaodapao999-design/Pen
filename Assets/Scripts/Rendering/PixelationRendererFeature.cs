using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.RenderGraphModule.Util;
using static UnityEngine.Rendering.RenderGraphModule.Util.RenderGraphUtils;

namespace Pen.Rendering
{
    /// <summary>
    /// 像素化后处理 ScriptableRendererFeature（URP 17 / Unity 6 Render Graph 版本）。
    /// 原理：将当前相机颜色 RT 复制到临时纹理，再通过像素化材质（UV Snapping）渲染回相机颜色 RT。
    /// </summary>
    public class PixelationRendererFeature : ScriptableRendererFeature
    {
        // ──────────────────────────────────────────────
        // 序列化设置
        // ──────────────────────────────────────────────
        [System.Serializable]
        public class Settings
        {
            [Tooltip("像素块大小（1 = 不缩放，4 = 四倍像素化）")]
            public int pixelSize = 4;

            [Tooltip("注入时序，默认在后处理之后")]
            public RenderPassEvent renderPassEvent = RenderPassEvent.AfterRenderingPostProcessing;

            [Tooltip("引用使用 S_Pixelation.shader 的材质（M_Pixelation）")]
            public Material pixelationMaterial;
        }

        public Settings settings = new Settings();

        private PixelationPass _pass;

        /// <inheritdoc />
        public override void Create()
        {
            _pass = new PixelationPass("PixelationPass")
            {
                renderPassEvent = settings.renderPassEvent
            };
        }

        /// <inheritdoc />
        public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
        {
            if (settings.pixelationMaterial == null)
            {
                Debug.LogWarning("[PixelationRendererFeature] pixelationMaterial 未赋值，跳过像素化Pass。");
                return;
            }

            if (settings.pixelSize <= 1)
                return;

            // 把 pixelSize 同步到材质，Shader 通过 _PixelSize 读取
            settings.pixelationMaterial.SetFloat("_PixelSize", settings.pixelSize);

            _pass.Setup(settings.pixelationMaterial);
            renderer.EnqueuePass(_pass);
        }

        // ──────────────────────────────────────────────
        // 内部 ScriptableRenderPass
        // ──────────────────────────────────────────────
        private class PixelationPass : ScriptableRenderPass
        {
            private Material _material;
            private static readonly MaterialPropertyBlock s_PropertyBlock = new MaterialPropertyBlock();

            public PixelationPass(string passName)
            {
                profilingSampler = new ProfilingSampler(passName);
                // 声明此 Pass 需要读当前颜色缓冲区作为输入
                requiresIntermediateTexture = true;
            }

            public void Setup(Material material)
            {
                _material = material;
            }

            /// <inheritdoc />
            public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
            {
                UniversalResourceData resourceData = frameData.Get<UniversalResourceData>();
                UniversalCameraData cameraData = frameData.Get<UniversalCameraData>();

                if (cameraData.cameraType == CameraType.Preview || cameraData.cameraType == CameraType.Reflection)
                    return;

                TextureHandle activeColor = resourceData.activeColorTexture;
                if (!activeColor.IsValid())
                    return;

                // ── 步骤1：将当前相机颜色复制到临时纹理 ──
                var desc = renderGraph.GetTextureDesc(resourceData.cameraColor);
                desc.name         = "_PixelationColorCopy";
                desc.clearBuffer  = false;
                TextureHandle colorCopy = renderGraph.CreateTexture(desc);

                // AddBlitPass：bilinear Blit（缩放参数 Vector2.one, Vector2.zero）
                renderGraph.AddBlitPass(activeColor, colorCopy, Vector2.one, Vector2.zero,
                    passName: "Pixelation_CopyColor");

                // ── 步骤2：用像素化材质将临时纹理渲染回相机颜色 RT ──
                // BlitMaterialParameters：source → destination 通过材质的 Pass 0 处理
                var blitParams = new BlitMaterialParameters(
                    colorCopy,      // source（_BlitTexture 会被自动绑定）
                    activeColor,    // destination
                    _material,
                    0               // pass index
                );
                renderGraph.AddBlitPass(blitParams, passName: "Pixelation_Apply");
            }

            // URP 17 RenderGraph 模式下不会调用 Execute，此处仅作 API 满足
#pragma warning disable CS0672
            public override void Execute(ScriptableRenderContext context, ref RenderingData renderingData) { }
#pragma warning restore CS0672
        }
    }
}
