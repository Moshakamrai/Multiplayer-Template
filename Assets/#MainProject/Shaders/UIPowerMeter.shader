// Custom/UIPowerMeter
// Cyberpunk neon power meter for a UGUI Image (Screen Space - Overlay compatible).
//
// Paints the WHITE INTERIOR of a cone/wedge sprite with a vertical neon gradient
// (cyan -> magenta -> purple), fills from the bottom up by _FillAmount, and fakes
// the bloom you can't get on an Overlay canvas with a hot white crest at the fill
// line plus an emissive glow. Scrolling scanlines + cheap procedural noise add
// energy. All the reactive props (_FillAmount, _GlowIntensity, _ColorShift) are
// driven from PowerMeterReactor.cs.
//
// Assign this material to the FILL image whose sprite is the interior mask
// (interior = opaque, everything else transparent). The neon border + tick lines
// live on a SEPARATE plain Image stacked on top.
Shader "Custom/UIPowerMeter"
{
    Properties
    {
        [PerRendererData] _MainTex ("Fill Mask (interior=opaque)", 2D) = "white" {}
        _Color ("Tint", Color) = (1,1,1,1)

        [Header(Fill)]
        _FillAmount ("Fill Amount", Range(0,1)) = 0.5
        _EdgeSoft   ("Fill Edge Softness", Range(0.0001,0.2)) = 0.02

        [Header(Gradient HDR)]
        [HDR] _ColorBottom ("Color Bottom (cyan)",    Color) = (0.0, 1.0, 1.0, 1)
        [HDR] _ColorMid    ("Color Mid (magenta)",    Color) = (1.0, 0.0, 1.0, 1)
        [HDR] _ColorTop    ("Color Top (purple)",     Color) = (0.6, 0.0, 1.0, 1)

        [Header(Crest (faked bloom at fill line))]
        [HDR] _CrestColor ("Crest Color", Color) = (1,1,1,1)
        _CrestWidth ("Crest Width", Range(0.001,0.3)) = 0.06
        _CrestBoost ("Crest Boost", Range(0,8)) = 2.5

        [Header(Reactivity)]
        _GlowIntensity ("Glow Intensity", Range(0,6)) = 1.0
        _ColorShift    ("Color Shift (hue)", Range(0,1)) = 0.0

        [Header(Scanlines)]
        _ScanFreq     ("Scanline Frequency", Float) = 60
        _ScanSpeed    ("Scanline Speed", Float) = 3
        _ScanStrength ("Scanline Strength", Range(0,1)) = 0.25

        [Header(Noise)]
        _NoiseScale    ("Noise Scale", Float) = 40
        _NoiseScroll   ("Noise Scroll Speed", Float) = 0.6
        _NoiseStrength ("Noise Strength", Range(0,1)) = 0.12

        // ---- Standard UI / masking plumbing (do not remove) ----
        _StencilComp ("Stencil Comparison", Float) = 8
        _Stencil ("Stencil ID", Float) = 0
        _StencilOp ("Stencil Operation", Float) = 0
        _StencilWriteMask ("Stencil Write Mask", Float) = 255
        _StencilReadMask ("Stencil Read Mask", Float) = 255
        _ColorMask ("Color Mask", Float) = 15
        [Toggle(UNITY_UI_ALPHACLIP)] _UseUIAlphaClip ("Use Alpha Clip", Float) = 0
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

            float  _FillAmount;
            float  _EdgeSoft;
            fixed4 _ColorBottom;
            fixed4 _ColorMid;
            fixed4 _ColorTop;
            fixed4 _CrestColor;
            float  _CrestWidth;
            float  _CrestBoost;
            float  _GlowIntensity;
            float  _ColorShift;
            float  _ScanFreq;
            float  _ScanSpeed;
            float  _ScanStrength;
            float  _NoiseScale;
            float  _NoiseScroll;
            float  _NoiseStrength;

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

            // RGB <-> HSV for the on-beat hue shift.
            float3 RGBtoHSV(float3 c)
            {
                float4 K = float4(0.0, -1.0 / 3.0, 2.0 / 3.0, -1.0);
                float4 p = lerp(float4(c.bg, K.wz), float4(c.gb, K.xy), step(c.b, c.g));
                float4 q = lerp(float4(p.xyw, c.r), float4(c.r, p.yzx), step(p.x, c.r));
                float d = q.x - min(q.w, q.y);
                float e = 1.0e-10;
                return float3(abs(q.z + (q.w - q.y) / (6.0 * d + e)), d / (q.x + e), q.x);
            }
            float3 HSVtoRGB(float3 c)
            {
                float4 K = float4(1.0, 2.0 / 3.0, 1.0 / 3.0, 3.0);
                float3 p = abs(frac(c.xxx + K.xyz) * 6.0 - K.www);
                return c.z * lerp(K.xxx, saturate(p - K.xxx), c.y);
            }

            float hash21(float2 p)
            {
                return frac(sin(dot(p, float2(12.9898, 78.233))) * 43758.5453);
            }

            fixed4 frag(v2f IN) : SV_Target
            {
                // Mask: the sprite alpha defines the cone interior (and tick gaps).
                half maskAlpha = (tex2D(_MainTex, IN.texcoord) + _TextureSampleAdd).a;

                float y = IN.texcoord.y; // 0 = bottom, 1 = top

                // Vertical neon gradient: cyan -> magenta -> purple.
                float3 grad = (y < 0.5)
                    ? lerp(_ColorBottom.rgb, _ColorMid.rgb, saturate(y / 0.5))
                    : lerp(_ColorMid.rgb,    _ColorTop.rgb, saturate((y - 0.5) / 0.5));

                // Fill from the bottom up with a soft top edge.
                float fillMask = smoothstep(_FillAmount, _FillAmount - _EdgeSoft, y);

                // Hot crest at the fill line — fakes bloom on an Overlay canvas.
                float crest = saturate(1.0 - abs(y - _FillAmount) / _CrestWidth);
                crest = crest * crest;

                // Scanlines scrolling upward.
                float scan = sin(y * _ScanFreq - _Time.y * _ScanSpeed) * 0.5 + 0.5;
                scan = lerp(1.0, scan, _ScanStrength);

                // Cheap animated noise for extra energy.
                float n = hash21(floor(IN.texcoord * _NoiseScale) + floor(_Time.y * _NoiseScroll * 10.0));
                float noise = lerp(1.0, n, _NoiseStrength);

                float3 rgb = grad * scan * noise;
                rgb += _CrestColor.rgb * (crest * _CrestBoost);
                rgb *= _GlowIntensity;

                // On-beat hue shift.
                if (_ColorShift > 0.0001)
                {
                    float3 hsv = RGBtoHSV(rgb);
                    hsv.x = frac(hsv.x + _ColorShift);
                    rgb = HSVtoRGB(hsv);
                }

                // Alpha: filled body + a thin glowing line at the crest, gated by the mask.
                float bodyA = fillMask;
                float crestA = crest * 0.85;
                half alpha = maskAlpha * IN.color.a * saturate(max(bodyA, crestA));

                fixed4 col = fixed4(rgb * IN.color.rgb, alpha);

                #ifdef UNITY_UI_CLIP_RECT
                col.a *= UnityGet2DClipping(IN.worldPosition.xy, _ClipRect);
                #endif

                #ifdef UNITY_UI_ALPHACLIP
                clip(col.a - 0.001);
                #endif

                return col;
            }
        ENDCG
        }
    }
}
