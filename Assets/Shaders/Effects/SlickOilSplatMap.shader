Shader "Pen/Slick Oil Splat Map"
{
    Properties
    {
        _SplatMap ("Splat Map", 2D) = "black" {}
        _SplatColor ("Splat Color", Color) = (0.006, 0.045, 0.035, 0.98)
        _EdgeColor ("Edge Color", Color) = (0.06, 0.28, 0.22, 0.92)
        _FoamColor ("Foam Color", Color) = (0.4, 0.78, 0.62, 0.18)
        _FoamTexture ("Foam Texture", 2D) = "white" {}
        _FoamScale ("Foam Scale", Float) = 3.4
        _NormalTexture ("Normal Texture", 2D) = "bump" {}
        _SecondNormal ("Second Normal", 2D) = "bump" {}
        _MainNormalScale ("Main Normal Scale", Vector) = (0.95, 0.95, 0, 0)
        _SecondNormalScale ("Second Normal Scale", Vector) = (1.65, 1.65, 0, 0)
        _WaveDir ("Wave Dir", Vector) = (1, 0.55, 0, 0)
        _WaveSpeed ("Wave Speed", Float) = 0.22
        _Smoothness ("Smoothness", Range(0, 1)) = 0.95
        _SplatEdgeBump ("Splat Edge Bump", Range(0, 1)) = 0.62
        _SplatTileBump ("Splat Tile Bump", Range(0, 1)) = 0.48
        _SpecularIntensity ("Specular Intensity", Range(0, 2)) = 1.45
        _ThinFilmStrength ("Thin Film Strength", Range(0, 1)) = 0.025
        _HighlightStrength ("Highlight Strength", Range(0, 1)) = 0.48
        _Alpha ("Alpha", Range(0, 1)) = 0.86
        _PixelGrid ("Pixel Grid", Vector) = (144, 91, 0, 0)
        _DitherStrength ("Dither Strength", Range(0, 1)) = 0.45
        _HighlightFps ("Highlight FPS", Range(1, 16)) = 6
        _SideFlowOpacity ("Side Flow Opacity", Range(0, 1)) = 0.32
    }

    SubShader
    {
        Tags
        {
            "RenderPipeline" = "UniversalPipeline"
            "Queue" = "Transparent+60"
            "RenderType" = "Transparent"
        }

        Pass
        {
            Name "SlickOilSplatMap"
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            ZTest LEqual
            Cull Off

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            TEXTURE2D(_SplatMap);
            SAMPLER(sampler_SplatMap);
            TEXTURE2D(_FoamTexture);
            SAMPLER(sampler_FoamTexture);
            TEXTURE2D(_NormalTexture);
            SAMPLER(sampler_NormalTexture);
            TEXTURE2D(_SecondNormal);
            SAMPLER(sampler_SecondNormal);

            CBUFFER_START(UnityPerMaterial)
                float4 _SplatMap_TexelSize;
                half4 _SplatColor;
                half4 _EdgeColor;
                half4 _FoamColor;
                float4 _FoamTexture_ST;
                half _FoamScale;
                float4 _NormalTexture_ST;
                float4 _SecondNormal_ST;
                float4 _MainNormalScale;
                float4 _SecondNormalScale;
                float4 _WaveDir;
                half _WaveSpeed;
                half _Smoothness;
                half _SplatEdgeBump;
                half _SplatTileBump;
                half _SpecularIntensity;
                half _ThinFilmStrength;
                half _HighlightStrength;
                half _Alpha;
                float4 _PixelGrid;
                half _DitherStrength;
                half _HighlightFps;
                half _SideFlowOpacity;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv : TEXCOORD0;
                half4 color : COLOR;
            };

            struct Varyings
            {
                float4 positionHCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float2 uv : TEXCOORD1;
                half4 color : COLOR;
            };

            Varyings vert(Attributes input)
            {
                Varyings output;
                VertexPositionInputs positionInputs = GetVertexPositionInputs(input.positionOS.xyz);
                output.positionHCS = positionInputs.positionCS;
                output.positionWS = positionInputs.positionWS;
                output.uv = input.uv;
                output.color = input.color;
                return output;
            }

            half SampleMask(float2 uv)
            {
                return SAMPLE_TEXTURE2D(_SplatMap, sampler_SplatMap, saturate(uv)).a;
            }

            half Bayer4(float2 screenPosition)
            {
                uint x = (uint)screenPosition.x & 3u;
                uint y = (uint)screenPosition.y & 3u;
                uint index = y * 4u + x;
                if (index == 0u) return 1.0h / 17.0h;
                if (index == 1u) return 9.0h / 17.0h;
                if (index == 2u) return 3.0h / 17.0h;
                if (index == 3u) return 11.0h / 17.0h;
                if (index == 4u) return 13.0h / 17.0h;
                if (index == 5u) return 5.0h / 17.0h;
                if (index == 6u) return 15.0h / 17.0h;
                if (index == 7u) return 7.0h / 17.0h;
                if (index == 8u) return 4.0h / 17.0h;
                if (index == 9u) return 12.0h / 17.0h;
                if (index == 10u) return 2.0h / 17.0h;
                if (index == 11u) return 10.0h / 17.0h;
                if (index == 12u) return 16.0h / 17.0h;
                if (index == 13u) return 8.0h / 17.0h;
                if (index == 14u) return 14.0h / 17.0h;
                return 6.0h / 17.0h;
            }

            half HashPixel(float2 cell, half frame)
            {
                return frac(sin(dot(cell + frame, float2(12.9898, 78.233))) * 43758.5453);
            }

            half4 frag(Varyings input) : SV_Target
            {
                float2 uv = input.uv;
                float2 pixelGrid = max(_PixelGrid.xy, float2(1.0, 1.0));
                float2 cell = floor(uv * pixelGrid);
                float2 pixelUv = (cell + 0.5) / pixelGrid;
                half4 splat = SAMPLE_TEXTURE2D(_SplatMap, sampler_SplatMap, pixelUv);
                half mask = splat.a;
                clip(mask - 0.006h);

                float2 cellTexel = 1.0 / pixelGrid;
                half left = SampleMask(pixelUv - float2(cellTexel.x, 0));
                half right = SampleMask(pixelUv + float2(cellTexel.x, 0));
                half down = SampleMask(pixelUv - float2(0, cellTexel.y));
                half up = SampleMask(pixelUv + float2(0, cellTexel.y));
                half2 gradient = half2(right - left, up - down);
                half edge = step(0.04h, length(gradient));
                half body = step(0.18h, mask);
                half core = step(0.62h, mask);

                float time = _Time.y;
                half steppedFrame = floor(time * _HighlightFps) / _HighlightFps;
                float2 waveDir = float2(_WaveDir.x, _WaveDir.y);
                waveDir = dot(waveDir, waveDir) < 0.0001 ? float2(1.0, 0.35) : normalize(waveDir);
                float2 crossDir = normalize(float2(-waveDir.y, waveDir.x) + float2(0.17, -0.09));
                float2 normalUvA = pixelUv * max(float2(0.01, 0.01), float2(_MainNormalScale.x, _MainNormalScale.y))
                    + waveDir * steppedFrame * _WaveSpeed * 0.045;
                float2 normalUvB = pixelUv * max(float2(0.01, 0.01), float2(_SecondNormalScale.x, _SecondNormalScale.y))
                    - crossDir * steppedFrame * _WaveSpeed * 0.033;
                normalUvA = normalUvA * _NormalTexture_ST.xy + _NormalTexture_ST.zw;
                normalUvB = normalUvB * _SecondNormal_ST.xy + _SecondNormal_ST.zw;
                half3 nA = SAMPLE_TEXTURE2D(_NormalTexture, sampler_NormalTexture, normalUvA).rgb * 2.0h - 1.0h;
                half3 nB = SAMPLE_TEXTURE2D(_SecondNormal, sampler_SecondNormal, normalUvB).rgb * 2.0h - 1.0h;
                half2 tileNormal = (nA.xy * 0.35h + nB.yx * 0.22h) * _SplatTileBump * core;
                half2 edgeNormal = normalize(gradient + half2(0.0001h, -0.0001h)) * edge * _SplatEdgeBump;
                half3 normalWS = normalize(half3((tileNormal.x + edgeNormal.x) * 0.92h, 1.0h, (tileNormal.y + edgeNormal.y) * 0.92h));

                float2 foamUv = (pixelUv + tileNormal * 0.028h) * _FoamScale + waveDir * steppedFrame * _WaveSpeed * 0.018;
                foamUv = foamUv * _FoamTexture_ST.xy + _FoamTexture_ST.zw;
                half foamNoise = SAMPLE_TEXTURE2D(_FoamTexture, sampler_FoamTexture, foamUv).r;
                half foam = edge * step(0.52h, foamNoise) * _FoamColor.a;
                half edgeHighlight = edge * step(0.34h, foamNoise);
                half wetGlint = step(0.94h, HashPixel(cell, steppedFrame * 31.0h)) * core * (1.0h - edge * 0.35h);
                half highlightStrength = saturate(_HighlightStrength);

                Light mainLight = GetMainLight();
                half3 lightDir = normalize(mainLight.direction + half3(0.0001h, 0.0001h, 0.0001h));
                half3 viewDir = normalize(GetWorldSpaceViewDir(input.positionWS));
                half3 halfDir = normalize(lightDir + viewDir);
                half ndl = step(0.45h, saturate(dot(normalWS, lightDir))) * 0.18h + 0.82h;
                half specular = step(0.72h, pow(saturate(dot(normalWS, halfDir)), 32.0h)) * _SpecularIntensity;
                half fresnel = step(0.62h, pow(saturate(1.0h - abs(dot(normalWS, viewDir))), 3.0h));
                half wetness = max(splat.b, mask);
                half3 thinFilm = 0.5h + 0.5h * cos(6.2831853h * (wetness + pixelUv.xyx * half3(0.17h, 0.31h, 0.47h) + steppedFrame * 0.018h));

                half3 midColor = lerp(_SplatColor.rgb, _EdgeColor.rgb, 0.45h);
                half3 color = lerp(midColor, _SplatColor.rgb, core);
                color = lerp(color, _EdgeColor.rgb, edge * 0.82h);
                color *= ndl;
                color += _EdgeColor.rgb * edgeHighlight * 0.12h * highlightStrength;
                color += _FoamColor.rgb * foam * 0.68h;
                color += thinFilm * _ThinFilmStrength * body * (0.35h + highlightStrength * 0.65h);
                color += (wetGlint * 0.18h + specular * 0.24h + fresnel * 0.04h)
                    * highlightStrength * half3(0.72h, 1.0h, 0.86h);

                half sideMask = 1.0h - saturate(input.color.r);
                half sideFade = lerp(1.0h, input.color.a * _SideFlowOpacity, sideMask);
                color *= lerp(1.0h, 0.86h, sideMask);

                half alpha = saturate(mask * _Alpha * lerp(_SplatColor.a, _EdgeColor.a, edge * 0.35h)
                    + edgeHighlight * 0.025h * highlightStrength
                    + foam * 0.24h
                    + wetGlint * 0.015h * highlightStrength
                    + specular * 0.012h * highlightStrength);
                alpha *= sideFade;
                half dither = Bayer4(input.positionHCS.xy);
                half ditheredCoverage = smoothstep(dither - 0.18h, dither + 0.18h, alpha);
                clip(lerp(alpha, ditheredCoverage, _DitherStrength) - 0.025h);
                alpha = lerp(alpha, alpha * (0.64h + ditheredCoverage * 0.36h), _DitherStrength);
                return half4(color, alpha);
            }
            ENDHLSL
        }
    }

    FallBack Off
}
