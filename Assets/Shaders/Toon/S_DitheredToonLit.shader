Shader "Pen/DitheredToonLit"
{
    Properties
    {
        _BaseColor      ("Base Color",      Color)          = (1, 1, 1, 1)
        _BaseMap        ("Base Texture",    2D)             = "white" {}
        _ShadowColor    ("Shadow Color",    Color)          = (0.3, 0.3, 0.35, 1)
        _CelSteps       ("Cel Steps",       Range(1, 4))    = 2
        _DitherStrength ("Dither Strength", Range(0, 1))    = 1
        _RimColor       ("Rim Color",       Color)          = (1, 1, 1, 1)
        _RimThreshold   ("Rim Threshold",   Range(0, 1))    = 0.1
    }

    SubShader
    {
        Tags
        {
            "RenderPipeline" = "UniversalPipeline"
            "RenderType"     = "Opaque"
            "Queue"          = "Geometry"
        }

        // ─────────────────────────────────────────────────────────────
        // Pass 1: UniversalForward — 主光照 + Rim Light + Dithered Cel
        // ─────────────────────────────────────────────────────────────
        Pass
        {
            Name "UniversalForward"
            Tags { "LightMode" = "UniversalForward" }

            Cull Back
            ZWrite On
            ZTest LEqual

            HLSLPROGRAM
            #pragma vertex   vert
            #pragma fragment frag

            // URP 关键字支持
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE
            #pragma multi_compile _ _SHADOWS_SOFT
            #pragma multi_compile_fog

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            #include "Assets/Shaders/Include/DitherUtils.hlsl"

            // ── CBUFFER（SRP Batcher 兼容）──────────────────────────
            CBUFFER_START(UnityPerMaterial)
                float4 _BaseColor;
                float4 _BaseMap_ST;
                float4 _ShadowColor;
                float  _CelSteps;
                float  _DitherStrength;
                float4 _RimColor;
                float  _RimThreshold;
            CBUFFER_END

            TEXTURE2D(_BaseMap);
            SAMPLER(sampler_BaseMap);

            // ── 顶点输入 / 输出 ────────────────────────────────────
            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS   : NORMAL;
                float2 uv         : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionCS  : SV_POSITION;
                float3 positionWS  : TEXCOORD0;
                float3 normalWS    : TEXCOORD1;
                float2 uv          : TEXCOORD2;
                float4 screenPos   : TEXCOORD3;
                float  fogFactor   : TEXCOORD4;
            };

            // ── 顶点着色器 ─────────────────────────────────────────
            Varyings vert(Attributes IN)
            {
                Varyings OUT;

                VertexPositionInputs posInputs = GetVertexPositionInputs(IN.positionOS.xyz);
                VertexNormalInputs   normInputs = GetVertexNormalInputs(IN.normalOS);

                OUT.positionCS = posInputs.positionCS;
                OUT.positionWS = posInputs.positionWS;
                OUT.normalWS   = normInputs.normalWS;
                OUT.uv         = TRANSFORM_TEX(IN.uv, _BaseMap);
                OUT.screenPos  = ComputeScreenPos(posInputs.positionCS);
                OUT.fogFactor  = ComputeFogFactor(posInputs.positionCS.z);

                return OUT;
            }

            // ── 片元着色器 ─────────────────────────────────────────
            half4 frag(Varyings IN) : SV_Target
            {
                // 1. 采样贴图 × 基础颜色
                half4 baseColor = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, IN.uv) * _BaseColor;

                // 2. 法线归一化
                float3 normalWS = normalize(IN.normalWS);

                // 3. 视线方向（世界空间）
                float3 viewDirWS = normalize(GetWorldSpaceViewDir(IN.positionWS));

                // 4. 获取 URP 主光源
                #if defined(_MAIN_LIGHT_SHADOWS) || defined(_MAIN_LIGHT_SHADOWS_CASCADE)
                    float4 shadowCoord = TransformWorldToShadowCoord(IN.positionWS);
                    Light mainLight = GetMainLight(shadowCoord);
                #else
                    Light mainLight = GetMainLight();
                #endif

                float3 lightDir   = normalize(mainLight.direction);
                float3 lightColor = mainLight.color * mainLight.distanceAttenuation * mainLight.shadowAttenuation;

                // 5. NdotL
                float NdotL = dot(normalWS, lightDir);

                // 6. 屏幕像素坐标（用于 Bayer 抖动）
                float2 screenPixel = (IN.screenPos.xy / IN.screenPos.w) * _ScreenParams.xy;

                // 7. Dithered Cel Shading
                float celPure    = CelShade(NdotL, 0.0, 0.02);
                float celDither  = DitheredCelShade(NdotL, screenPixel, _CelSteps);
                float celFactor  = lerp(celPure, celDither, _DitherStrength);

                // 8. 漫反射合色：亮区 = baseColor × lightColor，暗区 = _ShadowColor
                float3 litColor    = baseColor.rgb * lightColor;
                float3 shadowColor = _ShadowColor.rgb;
                float3 diffuse     = lerp(shadowColor, litColor, celFactor);

                // 9. Rim Light（基于 NdotV）
                float NdotV    = saturate(dot(normalWS, viewDirWS));
                float rimMask  = 1.0 - NdotV;
                // 仅在亮面显示 rim
                float rimLight = step(_RimThreshold, rimMask) * step(0.0, NdotL);
                float3 rim     = rimLight * _RimColor.rgb;

                // 10. 合并输出
                float3 finalColor = diffuse + rim;
                finalColor = MixFog(finalColor, IN.fogFactor);

                return half4(finalColor, 1.0);
            }
            ENDHLSL
        }

        // ─────────────────────────────────────────────────────────────
        // Pass 2: ShadowCaster — 投射阴影（手写最简版，兼容 URP 17）
        // ─────────────────────────────────────────────────────────────
        Pass
        {
            Name "ShadowCaster"
            Tags { "LightMode" = "ShadowCaster" }

            ZWrite On
            ZTest LEqual
            ColorMask 0
            Cull Back

            HLSLPROGRAM
            #pragma vertex   ShadowPassVertex
            #pragma fragment ShadowPassFragment

            #pragma multi_compile_vertex _ _CASTING_PUNCTUAL_LIGHT_SHADOW

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float4 _BaseColor;
                float4 _BaseMap_ST;
                float4 _ShadowColor;
                float  _CelSteps;
                float  _DitherStrength;
                float4 _RimColor;
                float  _RimThreshold;
            CBUFFER_END

            // URP ShadowCaster 所需的内置 uniform（不在 CBUFFER 内）
            float3 _LightDirection;
            float3 _LightPosition;

            struct ShadowAttributes
            {
                float4 positionOS : POSITION;
                float3 normalOS   : NORMAL;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct ShadowVaryings
            {
                float4 positionCS : SV_POSITION;
                UNITY_VERTEX_INPUT_INSTANCE_ID
                UNITY_VERTEX_OUTPUT_STEREO
            };

            // 计算阴影 Clip 坐标（含 bias，与 URP 内部逻辑一致）
            float4 GetShadowPositionHClip(ShadowAttributes input)
            {
                float3 positionWS = TransformObjectToWorld(input.positionOS.xyz);
                float3 normalWS   = TransformObjectToWorldNormal(input.normalOS);

            #if _CASTING_PUNCTUAL_LIGHT_SHADOW
                float3 lightDir = normalize(_LightPosition - positionWS);
            #else
                float3 lightDir = _LightDirection;
            #endif

                float4 positionCS = TransformWorldToHClip(ApplyShadowBias(positionWS, normalWS, lightDir));

            #if UNITY_REVERSED_Z
                positionCS.z = min(positionCS.z, UNITY_NEAR_CLIP_VALUE);
            #else
                positionCS.z = max(positionCS.z, UNITY_NEAR_CLIP_VALUE);
            #endif
                return positionCS;
            }

            ShadowVaryings ShadowPassVertex(ShadowAttributes input)
            {
                ShadowVaryings output;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_TRANSFER_INSTANCE_ID(input, output);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);
                output.positionCS = GetShadowPositionHClip(input);
                return output;
            }

            half4 ShadowPassFragment(ShadowVaryings input) : SV_TARGET
            {
                return 0;
            }

            ENDHLSL
        }
    }

    FallBack "Universal Render Pipeline/Lit"
}
