Shader "Pen/PostProcess/S_UnifiedOutline"
{
    Properties
    {
        // _BlitTexture / _BlitScaleBias 由 URP Full Screen Pass Renderer Feature 通过 Blitter 自动绑定
        [HideInInspector] _BlitTexture    ("Source",      2D)      = "white" {}
        [HideInInspector] _BlitScaleBias  ("Scale Bias",  Vector)  = (1, 1, 0, 0)

        // 深度边缘检测阈值——深度差超过此值判定为轮廓线
        _DepthThreshold  ("Depth Threshold",  Float)  = 0.5
        // 法线边缘检测阈值——法线夹角差超过此值判定为结构线
        _NormalThreshold ("Normal Threshold", Float)  = 0.4
        // 深度遮罩缩放系数——控制法线描边在外轮廓处的消隐强度
        _MaskScale       ("Mask Scale",       Float)  = 5.0
        // 描边颜色
        _OutlineColor    ("Outline Color",    Color)  = (0, 0, 0, 1)
    }

    SubShader
    {
        Tags
        {
            "RenderType"     = "Opaque"
            "RenderPipeline" = "UniversalPipeline"
        }

        // ─────────────────── Pass：统一描边 Blit ───────────────────
        // 三层叠加策略：
        //   D-1 深度描边    —— 捕捉物体外轮廓
        //   D-2 法线描边    —— 捕捉物体内部结构线
        //   D-3 深度遮罩    —— 消除外轮廓处的法线双重线
        Pass
        {
            Name "UnifiedOutlineBlit"

            Cull Off
            ZTest Always
            ZWrite Off
            Blend Off

            HLSLPROGRAM

            #pragma vertex   Vert
            #pragma fragment Frag
            // SV_VertexID + DrawProcedural（Full Screen Pass 无 Mesh 模式）要求 SM3.5+
            // 若使用 2.0 则 SV_VertexID 语义未定义，全屏三角形不生成，画面全黑
            #pragma target   3.5

            // URP 核心库：引入 LinearEyeDepth / _ZBufferParams / CBUFFER 等基础定义
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            // TextureXR.hlsl：必须在 Blit.hlsl 之前显式引入。
            // 该文件定义了 TEXTURE2D_X / SAMPLE_TEXTURE2D_X_LOD 等跨平台纹理宏。
            // Blit.hlsl 在第 14 行直接使用 TEXTURE2D_X(_BlitTexture)，
            // 但其自身的 include 链（Common.hlsl → Color.hlsl 等）均不经过 TextureXR.hlsl，
            // 因此必须在此处手动补充，否则编译器报 "unrecognized identifier 'TEXTURE2D_X'"。
            #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/TextureXR.hlsl"

            // ── Blit.hlsl：URP Full Screen Pass Renderer Feature 官方约定 ──
            // 提供标准 Vert 函数、Varyings / Attributes 结构体，
            // 以及 _BlitTexture、_BlitScaleBias、sampler_LinearClamp 的标准声明。
            #include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl"
            // 提供 SampleSceneDepth(uv)
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"
            // 提供 SampleSceneNormals(uv)
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareNormalsTexture.hlsl"

            // ─────────────────── 材质属性（SRP Batcher 兼容）───────────────────
            // 注意：_BlitTexture / _BlitScaleBias 已由 Blit.hlsl 声明，不可重复放入 CBUFFER
            CBUFFER_START(UnityPerMaterial)
                float  _DepthThreshold;   // 深度边缘检测阈值
                float  _NormalThreshold;  // 法线边缘检测阈值
                float  _MaskScale;        // 深度遮罩缩放系数
                half4  _OutlineColor;     // 描边颜色（RGBA）
            CBUFFER_END

            // ─────────────────── 片元着色器 ───────────────────
            // Vert 由 Blit.hlsl 提供；Varyings.texcoord 已包含 BlitScaleBias 应用后的 UV

            half4 Frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);

                float2 uv = input.texcoord;

                // ── 1. 计算邻域 texel 偏移 UV ──────────────────────────────
                // _BlitTexture_TexelSize.xy = (1/width, 1/height)，即一个像素在 UV 空间的大小
                // Blit.hlsl 已将 _BlitTexture 绑定为全局纹理，_TexelSize 由 Unity 自动填充
                float2 texelSize = _BlitTexture_TexelSize.xy;
                float2 uvU = uv + float2(0,            texelSize.y);   // 上
                float2 uvD = uv - float2(0,            texelSize.y);   // 下
                float2 uvL = uv - float2(texelSize.x,  0           );  // 左
                float2 uvR = uv + float2(texelSize.x,  0           );  // 右

                // ── 2. D-1 深度描边（外轮廓）──────────────────────────────
                // 使用 LinearEyeDepth 将非线性深度缓冲值转换为线性视空间深度，
                // 使阈值判断在所有距离下保持一致的物理意义。
                // _ZBufferParams 由 Unity Core.hlsl 自动提供，无需手动声明。
                float depthC = LinearEyeDepth(SampleSceneDepth(uv),  _ZBufferParams);
                float depthU = LinearEyeDepth(SampleSceneDepth(uvU), _ZBufferParams);
                float depthD = LinearEyeDepth(SampleSceneDepth(uvD), _ZBufferParams);
                float depthL = LinearEyeDepth(SampleSceneDepth(uvL), _ZBufferParams);
                float depthR = LinearEyeDepth(SampleSceneDepth(uvR), _ZBufferParams);

                // 取 4 邻域与中心深度差的最大值，反映边缘处的深度跳变程度
                float depthDiff = max(
                    max(abs(depthU - depthC), abs(depthD - depthC)),
                    max(abs(depthL - depthC), abs(depthR - depthC))
                );
                // 超过阈值即视为外轮廓边缘（0 或 1）
                float depthEdge = step(_DepthThreshold, depthDiff);

                // ── 3. D-2 法线描边（内结构线）──────────────────────────────
                // SampleSceneNormals 返回世界空间法线（需在 URP Renderer Feature 中开启 DepthNormals）
                float3 normalC = SampleSceneNormals(uv);
                float3 normalU = SampleSceneNormals(uvU);
                float3 normalD = SampleSceneNormals(uvD);
                float3 normalL = SampleSceneNormals(uvL);
                float3 normalR = SampleSceneNormals(uvR);

                // 1 - dot(n1, n2) 表示法线夹角差异，值越大表示法线变化越剧烈
                float normalDiff = max(
                    max(1.0 - dot(normalC, normalU), 1.0 - dot(normalC, normalD)),
                    max(1.0 - dot(normalC, normalL), 1.0 - dot(normalC, normalR))
                );
                // 超过阈值即视为内部结构边缘（0 或 1）
                float normalEdge = step(_NormalThreshold, normalDiff);

                // ── 4. D-3 反向深度遮罩（消除双重线）──────────────────────────────
                // 深度差越大说明越靠近外轮廓，遮罩值趋近 0，
                // 使法线描边在外轮廓处被抑制，避免外轮廓出现双重线叠加。
                float depthMask       = 1.0 - saturate(depthDiff * _MaskScale);
                float maskedNormalEdge = normalEdge * depthMask;

                // 深度边缘与遮罩后法线边缘叠加，saturate 防止超出 [0,1]
                float finalEdge = saturate(depthEdge + maskedNormalEdge);

                // ── 5. 最终颜色混合输出 ──────────────────────────────
                // 采样原始场景颜色，与描边颜色按 finalEdge 线性插值
                // Blit.hlsl 中 _BlitTexture 为 TEXTURE2D_X，使用 SAMPLE_TEXTURE2D_X_LOD 以支持 XR
                half3 sceneColor = SAMPLE_TEXTURE2D_X_LOD(_BlitTexture, sampler_LinearClamp, uv, 0).rgb;
                half3 result     = lerp(sceneColor, _OutlineColor.rgb, finalEdge);

                return half4(result, 1.0);
            }

            ENDHLSL
        }
    }

    FallBack Off
}
