Shader "Custom/SwordArc"
{
    // Additive, double-sided, vertex-colored shader for the swept blade-arc mesh.
    // Brightness/fade comes from the mesh's vertex colors (the SwordArcTrail script bakes the
    // age-fade into vertex alpha). Optional _MainTex lets you add a soft gradient along the blade.
    Properties
    {
        [HDR]_TintColor ("Tint", Color) = (0.6,0.9,1,1)
        _MainTex ("Gradient (optional)", 2D) = "white" {}
    }
    SubShader
    {
        Tags { "Queue"="Transparent" "RenderType"="Transparent" "IgnoreProjector"="True" "RenderPipeline"="UniversalPipeline" }
        Blend One One        // additive
        Cull Off
        ZWrite Off
        ZTest Always         // draw on top so the arc is always visible (stylized slash)

        Pass
        {
        HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct Attributes { float4 positionOS:POSITION; float4 color:COLOR; float2 uv:TEXCOORD0; };
            struct Varyings   { float4 positionHCS:SV_POSITION; float4 color:COLOR; float2 uv:TEXCOORD0; };

            TEXTURE2D(_MainTex); SAMPLER(sampler_MainTex);
            CBUFFER_START(UnityPerMaterial)
                float4 _MainTex_ST;
                half4  _TintColor;
            CBUFFER_END

            Varyings vert(Attributes IN)
            {
                Varyings OUT;
                OUT.positionHCS = TransformObjectToHClip(IN.positionOS.xyz);
                OUT.color = IN.color;
                OUT.uv = TRANSFORM_TEX(IN.uv, _MainTex);
                return OUT;
            }

            half4 frag(Varyings IN) : SV_Target
            {
                half4 tex = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, IN.uv);
                // Additive: bake the vertex-alpha fade into rgb (alpha itself is ignored by One/One blend).
                half3 rgb = tex.rgb * _TintColor.rgb * IN.color.rgb * IN.color.a;
                return half4(rgb, 1.0);
            }
        ENDHLSL
        }
    }
    Fallback Off
}
