Shader "Pen/PostProcess/S_Pixelation"
{
    Properties
    {
        // _BlitTexture 由 RenderGraph BlitPass 自动绑定，无需手动赋值
        [HideInInspector] _BlitTexture ("Source", 2D) = "white" {}
        [HideInInspector] _BlitScaleBias ("Scale Bias", Vector) = (1, 1, 0, 0)
        _PixelSize ("Pixel Size", Float) = 4
        // 参考分辨率高度（默认 1080）——像素化强度以此分辨率为基准进行缩放，
        // 使不同分辨率下的视觉"像素格数"保持一致。
        _ReferenceHeight ("Reference Height", Float) = 1080
    }

    SubShader
    {
        Tags
        {
            "RenderType"     = "Opaque"
            "RenderPipeline" = "UniversalPipeline"
        }

        // Pass 0：UV Snapping 像素化
        // RenderGraph 通过 DrawProcedural 调用此 Pass，_BlitTexture 由 MaterialPropertyBlock 绑定
        Pass
        {
            Name "PixelationBlit"

            Cull Off
            ZTest Always
            ZWrite Off
            Blend Off

            HLSLPROGRAM

            #pragma vertex   Vert
            #pragma fragment Frag
            #pragma target   2.0

            // URP Core.hlsl 提供：TEXTURE2D / GetFullScreenTriangle* / half4 / float2 等
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            // RenderGraph BlitPass 通过 MaterialPropertyBlock 设置以下 uniform
            TEXTURE2D(_BlitTexture);
            float4 _BlitScaleBias;    // xy = scale, zw = offset（Blitter 标准 uniform）
            float  _PixelSize;        // 像素块大小（由 Feature 通过 material.SetFloat 传入）
            float  _ReferenceHeight;  // 参考分辨率高度，用于跨分辨率缩放

            // 用 SamplerState naming convention 声明 linear clamp 采样器，无需 SAMPLER() 宏
            SamplerState sampler_linear_clamp;

            struct Attributes
            {
                uint vertexID : SV_VertexID;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 texcoord   : TEXCOORD0;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            Varyings Vert(Attributes input)
            {
                Varyings output;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);

                // 全屏三角形（vertexID 0/1/2 覆盖整个 NDC 空间）
                output.positionCS = GetFullScreenTriangleVertexPosition(input.vertexID);
                float2 uv         = GetFullScreenTriangleTexCoord(input.vertexID);

                // 应用 BlitScaleBias（支持 RenderGraph viewport 变换）
                output.texcoord = uv * _BlitScaleBias.xy + _BlitScaleBias.zw;
                return output;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);

                float2 uv = input.texcoord;

                // UV Snapping：将 UV 对齐到像素块中心，模拟 Point 采样效果
                // 按参考分辨率缩放 PixelSize，使不同屏幕分辨率下的像素格视觉大小一致：
                //   scaledPS = _PixelSize * (当前高度 / 参考高度)
                // 例：_PixelSize=4, 参考1080p → 4K(2160p)时 scaledPS=8，
                //   两种分辨率下画面都显示 1080/4=270 行像素格。
                float refH = max(1.0, _ReferenceHeight);
                float scaledPS = _PixelSize * (_ScreenParams.y / refH);
                float ps = max(1.0, scaledPS);
                // 1.0/_ScreenParams.xy = 单个屏幕像素在 UV 空间的大小
                float2 pixelUVSize = ps / _ScreenParams.xy;
                float2 snappedUV = floor(uv / pixelUVSize) * pixelUVSize + pixelUVSize * 0.5;

                return SAMPLE_TEXTURE2D(_BlitTexture, sampler_linear_clamp, snappedUV);
            }

            ENDHLSL
        }
    }

    FallBack Off
}
