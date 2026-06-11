Shader "Custom/EnergyGlove"
{
    // Cool energy gloves. Keeps the glove's own colour (_BaseMap / _BaseColor — same property names
    // as URP Lit, so swapping to this shader preserves the existing look), and layers on:
    //   • a Fresnel RIM glow around the silhouette (pulses)
    //   • a scrolling ENERGY FLOW across the surface
    //   • EMISSION driven by GloveBeatGlow (beat-charge + strike/hurt flashes)
    // Unlit/self-lit so the gloves pop in the dark neon arena.
    Properties
    {
        _BaseMap ("Base Map", 2D) = "white" {}
        _BaseColor ("Base Color", Color) = (0.55,0.08,0.08,1)

        [HDR]_RimColor ("Rim Color", Color) = (1,0.35,0.3,1)
        _RimPower ("Rim Tightness", Range(0.5,8)) = 4.0
        _RimIntensity ("Rim Intensity", Range(0,6)) = 0.8
        _PulseSpeed ("Rim Pulse Speed", Float) = 2.5

        [HDR]_FlowColor ("Energy Flow Color", Color) = (1,0.5,0.45,1)
        _FlowTiling ("Flow Lines", Float) = 7
        _FlowSpeed ("Flow Speed", Float) = 1.5
        _FlowStrength ("Flow Strength", Range(0,2)) = 0.12

        [Header(Lighting   gives the glove 3D form in VR)]
        _ShadeStrength ("Diffuse Shading", Range(0,1)) = 0.7
        _AmbientFloor ("Ambient Floor", Range(0,1)) = 0.35

        [HDR]_EmissionColor ("Emission (driven by GloveBeatGlow)", Color) = (0,0,0,1)

        // Alias so code that uses material.color (e.g. the hurt-flash) doesn't error; tints the base.
        _Color ("Color (tint)", Color) = (1,1,1,1)
    }

    SubShader
    {
        Tags { "RenderType"="Opaque" "RenderPipeline"="UniversalPipeline" "Queue"="Geometry" }

        Pass
        {
            Name "ForwardUnlit"
            Tags { "LightMode"="UniversalForward" }

        HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE
            #pragma multi_compile_fragment _ _ADDITIONAL_LIGHTS
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS   : NORMAL;
                float2 uv         : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionHCS : SV_POSITION;
                float2 uv          : TEXCOORD0;
                float3 normalWS    : TEXCOORD1;
                float3 viewWS      : TEXCOORD2;
                float3 positionWS  : TEXCOORD3;
            };

            TEXTURE2D(_BaseMap);
            SAMPLER(sampler_BaseMap);

            CBUFFER_START(UnityPerMaterial)
                float4 _BaseMap_ST;
                half4  _BaseColor, _RimColor, _FlowColor, _EmissionColor, _Color;
                float  _RimPower, _RimIntensity, _PulseSpeed;
                float  _FlowTiling, _FlowSpeed, _FlowStrength;
                float  _ShadeStrength, _AmbientFloor;
            CBUFFER_END

            Varyings vert(Attributes IN)
            {
                Varyings OUT;
                VertexPositionInputs pos = GetVertexPositionInputs(IN.positionOS.xyz);
                OUT.positionHCS = pos.positionCS;
                OUT.uv          = TRANSFORM_TEX(IN.uv, _BaseMap);
                OUT.normalWS    = TransformObjectToWorldNormal(IN.normalOS);
                OUT.viewWS      = GetWorldSpaceViewDir(pos.positionWS);
                OUT.positionWS  = pos.positionWS;
                return OUT;
            }

            half4 frag(Varyings IN) : SV_Target
            {
                half3 baseCol = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, IN.uv).rgb * _BaseColor.rgb * _Color.rgb;

                float3 N = normalize(IN.normalWS);
                float3 V = normalize(IN.viewWS);

                // Directional DIFFUSE shading — gives the glove a real light/shadow gradient so it reads
                // as a 3D shape in stereo VR (was fully unlit = flat blob). half-Lambert keeps it soft.
                Light mainLight = GetMainLight();
                float  ndl      = saturate(dot(N, mainLight.direction));
                float  halfLam  = ndl * 0.5 + 0.5;                          // soft wrap
                float  shade    = lerp(1.0, halfLam, _ShadeStrength);       // 0 = flat, 1 = full shading
                float3 ambient  = SampleSH(N) + _AmbientFloor;             // sky/ambient fill so shadows aren't black
                baseCol *= (mainLight.color * shade + ambient);

                // Fresnel rim — bright energy edge around the silhouette, gently pulsing.
                float fres  = pow(saturate(1.0 - saturate(dot(N, V))), _RimPower);
                float pulse = 0.6 + 0.4 * sin(_Time.y * _PulseSpeed);
                half3 rim   = _RimColor.rgb * (fres * _RimIntensity * pulse);

                // Scrolling energy flow, strongest toward the edges.
                float flow   = sin((IN.uv.y * _FlowTiling - _Time.y * _FlowSpeed) * 6.28318) * 0.5 + 0.5;
                half3 flowCol = _FlowColor.rgb * (flow * _FlowStrength * (0.3 + fres));

                half3 col = baseCol + rim + flowCol + _EmissionColor.rgb;
                return half4(col, 1.0);
            }
        ENDHLSL
        }
    }

    Fallback "Universal Render Pipeline/Unlit"
}
