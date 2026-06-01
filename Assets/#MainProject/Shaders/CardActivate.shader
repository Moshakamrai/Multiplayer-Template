Shader "UI/CardActivate"
{
    Properties
    {
        [PerRendererData] _MainTex ("Sprite Texture", 2D) = "white" {}
        _Color ("Tint", Color) = (1,1,1,1)

        // Standard UI stencil/clip plumbing (so it behaves inside a Canvas/Mask)
        _StencilComp ("Stencil Comparison", Float) = 8
        _Stencil ("Stencil ID", Float) = 0
        _StencilOp ("Stencil Operation", Float) = 0
        _StencilWriteMask ("Stencil Write Mask", Float) = 255
        _StencilReadMask ("Stencil Read Mask", Float) = 255
        _ColorMask ("Color Mask", Float) = 15
        [Toggle(UNITY_UI_ALPHACLIP)] _UseUIAlphaClip ("Use Alpha Clip", Float) = 0

        // ── Activation effect ──
        _ShineColor ("Shine Color", Color) = (1,1,1,1)
        _Shine ("Shine Progress", Range(0,1)) = 0          // driven 0->1 by script
        _ShineWidth ("Shine Width", Range(0.01,1)) = 0.18
        _Glow ("Glow Amount", Range(0,3)) = 0              // driven by script (activation flash)
        _GlowColor ("Glow Color", Color) = (0.3,0.9,1,1)

        // ── Idle holographic shimmer ──
        _ShimmerColor ("Shimmer Color", Color) = (0.4,0.8,1,1)
        _ShimmerStrength ("Shimmer Strength", Range(0,1)) = 0.06
        _ShimmerSpeed ("Shimmer Speed", Float) = 3.0

        // ── Digital dissolve (exit/enter) ──
        _Dissolve ("Dissolve", Range(0,1)) = 0               // 0 = fully visible, 1 = fully gone
        _DissolveEdge ("Dissolve Edge Width", Range(0.001,0.5)) = 0.08
        _DissolveScale ("Dissolve Noise Scale", Float) = 14
        _DissolveColor ("Dissolve Edge Color", Color) = (1,0.55,0.1,1)
    }

    SubShader
    {
        Tags
        {
            "Queue"="Transparent"
            "IgnoreProjector"="True"
            "RenderType"="Transparent"
            "PreviewType"="Plane"
            "CanUseSpriteAtlas"="True"
        }

        Stencil
        {
            Ref [_Stencil]
            Comp [_StencilComp]
            Pass [_StencilOp]
            ReadMask [_StencilReadMask]
            WriteMask [_StencilWriteMask]
        }

        Cull Off
        Lighting Off
        ZWrite Off
        ZTest [unity_GUIZTestMode]
        Blend SrcAlpha OneMinusSrcAlpha
        ColorMask [_ColorMask]

        Pass
        {
            Name "Default"
        CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 2.0

            #include "UnityCG.cginc"
            #include "UnityUI.cginc"

            #pragma multi_compile_local _ UNITY_UI_CLIP_RECT
            #pragma multi_compile_local _ UNITY_UI_ALPHACLIP

            struct appdata_t
            {
                float4 vertex   : POSITION;
                float4 color    : COLOR;
                float2 texcoord : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct v2f
            {
                float4 vertex        : SV_POSITION;
                fixed4 color         : COLOR;
                float2 texcoord      : TEXCOORD0;
                float4 worldPosition : TEXCOORD1;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            sampler2D _MainTex;
            float4 _MainTex_ST;
            fixed4 _Color;
            fixed4 _TextureSampleAdd;
            float4 _ClipRect;

            fixed4 _ShineColor;
            float  _Shine;
            float  _ShineWidth;
            float  _Glow;
            fixed4 _GlowColor;
            fixed4 _ShimmerColor;
            float  _ShimmerStrength;
            float  _ShimmerSpeed;
            float  _Dissolve;
            float  _DissolveEdge;
            float  _DissolveScale;
            fixed4 _DissolveColor;

            // Cheap procedural value noise for the dissolve (no texture needed).
            float cardHash(float2 p)
            {
                p = frac(p * float2(123.34, 456.21));
                p += dot(p, p + 45.32);
                return frac(p.x * p.y);
            }
            float cardValueNoise(float2 uv)
            {
                float2 i = floor(uv);
                float2 f = frac(uv);
                float a = cardHash(i);
                float b = cardHash(i + float2(1.0, 0.0));
                float c = cardHash(i + float2(0.0, 1.0));
                float d = cardHash(i + float2(1.0, 1.0));
                float2 u = f * f * (3.0 - 2.0 * f);
                return lerp(lerp(a, b, u.x), lerp(c, d, u.x), u.y);
            }

            v2f vert(appdata_t v)
            {
                v2f OUT;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(OUT);
                OUT.worldPosition = v.vertex;
                OUT.vertex = UnityObjectToClipPos(OUT.worldPosition);
                OUT.texcoord = TRANSFORM_TEX(v.texcoord, _MainTex);
                OUT.color = v.color * _Color;
                return OUT;
            }

            fixed4 frag(v2f IN) : SV_Target
            {
                half4 color = (tex2D(_MainTex, IN.texcoord) + _TextureSampleAdd) * IN.color;
                half srcA = color.a;

                // Diagonal shine band sweeping across the card as _Shine goes 0->1
                float diag   = (IN.texcoord.x + IN.texcoord.y) * 0.5;
                float center = _Shine * (1.0 + _ShineWidth * 2.0) - _ShineWidth;
                float band   = saturate(1.0 - abs(diag - center) / _ShineWidth);
                band = band * band;                 // sharpen the streak
                band *= step(0.0001, _Shine);        // off when not animating
                color.rgb += _ShineColor.rgb * (band * _ShineColor.a * srcA);

                // Subtle scrolling holographic shimmer (always on)
                float shim = sin(IN.texcoord.y * 8.0 - _Time.y * _ShimmerSpeed) * 0.5 + 0.5;
                color.rgb += _ShimmerColor.rgb * (shim * _ShimmerStrength * srcA);

                // Activation glow / emissive pulse
                color.rgb += _GlowColor.rgb * (_Glow * srcA);

                // Digital dissolve with a hot glowing edge. _Dissolve 0->1 burns the card away;
                // animate it 1->0 to materialize a card back in.
                if (_Dissolve > 0.0001)
                {
                    float n = cardValueNoise(IN.texcoord * _DissolveScale);
                    if (n < _Dissolve)
                    {
                        color.a = 0.0; // already dissolved
                    }
                    else
                    {
                        float band = 1.0 - saturate((n - _Dissolve) / max(_DissolveEdge, 1e-4));
                        color.rgb += _DissolveColor.rgb * (band * _DissolveColor.a * 2.0); // burning edge
                    }
                }

                #ifdef UNITY_UI_CLIP_RECT
                color.a *= UnityGet2DClipping(IN.worldPosition.xy, _ClipRect);
                #endif

                #ifdef UNITY_UI_ALPHACLIP
                clip (color.a - 0.001);
                #endif

                return color;
            }
        ENDCG
        }
    }
}
