Shader "Custom/Pixelation"
{
    Properties
    {
        // Full Screen Pass Renderer Feature 会自动将屏幕颜色绑定到 _BlitTexture
        // 此处仅保留用户可调参数
        _PixelWidth  ("Pixel Width",  Float) = 480
        _PixelHeight ("Pixel Height", Float) = 270
    }

    SubShader
    {
        Tags
        {
            "RenderType"     = "Opaque"
            "RenderPipeline" = "UniversalPipeline"
        }

        ZWrite Off
        ZTest Always
        Cull Off

        Pass
        {
            Name "PixelationPass"

            HLSLPROGRAM
            #pragma vertex   Vert
            #pragma fragment Frag

            // URP Core（包含 TransformObjectToHClip、UNITY_UV_STARTS_AT_TOP 等）
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            // -------------------------------------------------------------------
            // 手动声明屏幕纹理（避免依赖 Blit.hlsl / TEXTURE2D_X）
            // URP Full Screen Pass RF 会将源纹理写入 _BlitTexture
            // -------------------------------------------------------------------
            TEXTURE2D(_BlitTexture);
            SAMPLER(sampler_BlitTexture_Point_Clamp);   // Point + Clamp 内联采样器

            CBUFFER_START(UnityPerMaterial)
                float _PixelWidth;
                float _PixelHeight;
            CBUFFER_END

            // -------------------------------------------------------------------
            // 自定义 Varyings（无 XR 依赖）
            // -------------------------------------------------------------------
            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 texcoord   : TEXCOORD0;
            };

            // -------------------------------------------------------------------
            // 全屏三角形顶点着色器（无需 Mesh，3 个 SV_VertexID 生成全屏覆盖）
            // -------------------------------------------------------------------
            Varyings Vert(uint vertexID : SV_VertexID)
            {
                Varyings o;

                // 生成覆盖 [-1,1] NDC 的大三角形
                o.positionCS = float4(
                    (vertexID == 2) ?  3.0 : -1.0,
                    (vertexID == 1) ? -3.0 :  1.0,
                    0.0, 1.0);

                // 对应 UV（0~1 范围）
                o.texcoord = float2(
                    (vertexID == 2) ? 2.0 : 0.0,
                    (vertexID == 1) ? -1.0 : 1.0);

                // 处理 DX / OpenGL UV 翻转
                #if UNITY_UV_STARTS_AT_TOP
                    o.texcoord.y = 1.0 - o.texcoord.y;
                #endif

                return o;
            }

            // -------------------------------------------------------------------
            // 像素化片段着色器：将 UV 量化到像素网格
            // -------------------------------------------------------------------
            half4 Frag(Varyings input) : SV_Target
            {
                float2 uv = input.texcoord;

                // 量化 UV → 像素块效果
                uv = floor(uv * float2(_PixelWidth, _PixelHeight))
                   / float2(_PixelWidth, _PixelHeight);

                return SAMPLE_TEXTURE2D(_BlitTexture, sampler_BlitTexture_Point_Clamp, uv);
            }
            ENDHLSL
        }
    }
}
