// Storybook/Cel — a stylized 3D-bust shader built to sit believably in front of the
// painterly gothic-mystery backdrops (Gnomes & Gaslight interview rooms). Forked from
// CyberPixel/RimLit's structure (posterized NdotL lighting + inverted-hull outline pass)
// but with the neon HDR rim glow removed entirely and replaced with a quiet ink-line edge,
// because a 3D head lit like a cyberpunk arena fighter reads as a pasted-in render next to
// hand-painted watercolor rooms — flat PBR specular and neon glow are exactly what breaks
// the illusion. This shader intentionally has NO specular highlight and NO rim glow: only
// stepped (posterized) diffuse lighting bands, a soft warm shadow tint (never pure black —
// the paintings never use true black either), and a thin painterly outline.
Shader "Storybook/Cel"
{
    Properties
    {
        _BaseMap ("Base Map", 2D) = "white" {}
        _BaseColor ("Base Color", Color) = (1,1,1,1)
        _LightSteps ("Light Posterize Steps (2-3 = storybook, higher = smoother)", Range(1, 6)) = 3
        _ShadowTint ("Shadow Tint (multiplied into the dark bands — keep warm, never black)", Color) = (0.55, 0.48, 0.5, 1)
        _ShadowSoftness ("Band Edge Softness (0 = hard comic steps, higher = gentler)", Range(0.0, 0.35)) = 0.08
        [HDR] _OutlineColor ("Outline Color (ink line, not a glow — keep LDR-ish)", Color) = (0.08, 0.06, 0.05, 1)
        _OutlineWidth ("Outline Width (world units)", Range(0, 0.02)) = 0.006
        [Toggle(_ALPHATEST_ON)] _AlphaTest ("Alpha Clip", Float) = 0
        _Cutoff ("Alpha Cutoff", Range(0,1)) = 0.5
    }

    SubShader
    {
        Tags { "RenderType"="Opaque" "RenderPipeline"="UniversalPipeline" "Queue"="Geometry" }
        LOD 100

        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

        CBUFFER_START(UnityPerMaterial)
            float4 _BaseMap_ST;
            half4 _BaseColor;
            half _LightSteps;
            half4 _ShadowTint;
            half _ShadowSoftness;
            half4 _OutlineColor;
            half _OutlineWidth;
            half _AlphaTest;
            half _Cutoff;
        CBUFFER_END

        TEXTURE2D(_BaseMap);
        SAMPLER(sampler_BaseMap);
        ENDHLSL

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode"="UniversalForward" }

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma shader_feature_local_fragment _ALPHATEST_ON
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE
            #pragma multi_compile_fragment _ _SHADOWS_SOFT
            #pragma multi_compile _ _ADDITIONAL_LIGHTS_VERTEX _ADDITIONAL_LIGHTS

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS   : NORMAL;
                float2 uv         : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv         : TEXCOORD0;
                float3 normalWS   : TEXCOORD1;
                float3 positionWS : TEXCOORD2;
            };

            Varyings Vert(Attributes input)
            {
                Varyings o;
                o.positionWS = TransformObjectToWorld(input.positionOS.xyz);
                o.positionCS = TransformWorldToHClip(o.positionWS);
                o.normalWS = TransformObjectToWorldNormal(input.normalOS);
                o.uv = TRANSFORM_TEX(input.uv, _BaseMap);
                return o;
            }

            // Posterizes toward comic-book lighting bands, but with a soft transition
            // (smoothstep between adjacent steps) so it reads as painterly shading rather
            // than a harsh toon cutoff — matches the backdrops' soft brush-blended shadows.
            half PosterizeLight(half x)
            {
                half steps = max(_LightSteps, 1.0h);
                half scaled = x * steps;
                half stepped = floor(scaled);
                half frac = scaled - stepped;
                half soft = smoothstep(0.5h - _ShadowSoftness, 0.5h + _ShadowSoftness, frac);
                return (stepped + soft) / steps;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                half4 albedo = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, input.uv) * _BaseColor;
                #ifdef _ALPHATEST_ON
                clip(albedo.a - _Cutoff);
                #endif

                float3 normalWS = normalize(input.normalWS);

                float4 shadowCoord = TransformWorldToShadowCoord(input.positionWS);
                Light mainLight = GetMainLight(shadowCoord);
                half ndl = saturate(dot(normalWS, mainLight.direction)) * mainLight.shadowAttenuation;
                half lit = PosterizeLight(ndl);

                // Shadowed bands are tinted (warm grey-rose), never pure black — the
                // paintings' own shadow areas are always a muted color, not black ink fill.
                half3 shadowColor = albedo.rgb * _ShadowTint.rgb;
                half3 litColor = albedo.rgb * mainLight.color;
                half3 color = lerp(shadowColor, litColor, lit);

                #ifdef _ADDITIONAL_LIGHTS
                uint lightCount = GetAdditionalLightsCount();
                for (uint li = 0u; li < lightCount; li++)
                {
                    Light light = GetAdditionalLight(li, input.positionWS);
                    half andl = saturate(dot(normalWS, light.direction)) * light.distanceAttenuation * light.shadowAttenuation;
                    color += albedo.rgb * light.color * PosterizeLight(andl);
                }
                #endif

                half3 ambient = SampleSH(normalWS) * albedo.rgb * 0.5h; // soft fill only, no rim/glow
                color += ambient;

                return half4(color, albedo.a);
            }
            ENDHLSL
        }

        Pass
        {
            // Inverted-hull ink outline — same technique as CyberPixel/RimLit's outline
            // pass, but the default color/width here are tuned as a quiet line-art edge
            // (dark warm brown, thin) rather than a bold cyberpunk silhouette.
            Name "Outline"
            Tags { "LightMode"="SRPDefaultUnlit" }
            Cull Front

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS   : NORMAL;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
            };

            Varyings Vert(Attributes input)
            {
                Varyings o;
                float3 positionWS = TransformObjectToWorld(input.positionOS.xyz);
                float3 normalWS = normalize(TransformObjectToWorldNormal(input.normalOS));
                positionWS += normalWS * _OutlineWidth;
                o.positionCS = TransformWorldToHClip(positionWS);
                return o;
            }

            half4 Frag() : SV_Target
            {
                return half4(_OutlineColor.rgb, 1);
            }
            ENDHLSL
        }

        Pass
        {
            Name "ShadowCaster"
            Tags { "LightMode"="ShadowCaster" }
            ZWrite On
            ZTest LEqual
            ColorMask 0

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma shader_feature_local_fragment _ALPHATEST_ON
            #pragma multi_compile_vertex _ _CASTING_PUNCTUAL_LIGHT_SHADOW

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Shadows.hlsl"

            float3 _LightDirection;
            float3 _LightPosition;

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS   : NORMAL;
                float2 uv         : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv         : TEXCOORD0;
            };

            Varyings Vert(Attributes input)
            {
                Varyings o;
                float3 positionWS = TransformObjectToWorld(input.positionOS.xyz);
                float3 normalWS = TransformObjectToWorldNormal(input.normalOS);
                #if _CASTING_PUNCTUAL_LIGHT_SHADOW
                float3 lightDirectionWS = normalize(_LightPosition - positionWS);
                #else
                float3 lightDirectionWS = _LightDirection;
                #endif
                float4 positionCS = TransformWorldToHClip(ApplyShadowBias(positionWS, normalWS, lightDirectionWS));
                #if UNITY_REVERSED_Z
                positionCS.z = min(positionCS.z, UNITY_NEAR_CLIP_VALUE);
                #else
                positionCS.z = max(positionCS.z, UNITY_NEAR_CLIP_VALUE);
                #endif
                o.positionCS = positionCS;
                o.uv = TRANSFORM_TEX(input.uv, _BaseMap);
                return o;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                #ifdef _ALPHATEST_ON
                half alpha = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, input.uv).a * _BaseColor.a;
                clip(alpha - _Cutoff);
                #endif
                return 0;
            }
            ENDHLSL
        }

        Pass
        {
            Name "DepthOnly"
            Tags { "LightMode"="DepthOnly" }
            ZWrite On
            ColorMask R

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma shader_feature_local_fragment _ALPHATEST_ON

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv         : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv         : TEXCOORD0;
            };

            Varyings Vert(Attributes input)
            {
                Varyings o;
                o.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                o.uv = TRANSFORM_TEX(input.uv, _BaseMap);
                return o;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                #ifdef _ALPHATEST_ON
                half alpha = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, input.uv).a * _BaseColor.a;
                clip(alpha - _Cutoff);
                #endif
                return 0;
            }
            ENDHLSL
        }
    }

    FallBack "Universal Render Pipeline/Lit"
}
