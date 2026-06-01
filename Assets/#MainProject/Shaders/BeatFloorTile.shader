Shader "Custom/BeatFloorTile"
{
    // Drop-in floor tile. Reads _EmissionColor (driven by FloorBeatColorizer) as the base glow,
    // and layers on grid edge-glow + scrolling scanlines + a sweep bar that crosses the tile as
    // the beat approaches (_BeatProgress 0→1) + a white blow-out on the beat (_BeatFlash).
    //
    // Assign this shader to the 6 floor materials. The script keeps driving _EmissionColor, so the
    // base behavior is unchanged — this just makes each tile look more alive and "incoming".
    //
    // NOTE: this is UNLIT/emissive. If your floor previously contributed to realtime GI via a
    // Standard emissive material, that GI contribution goes away (the tiles still glow themselves).
    // If the arena gets darker, revert these mats to Standard — the script works on both.
    Properties
    {
        _EmissionColor ("Emission (driven by script)", Color) = (0,0,0,1)
        _BaseColor     ("Base Tint", Color) = (0.02,0.02,0.04,1)
        _ScanColor     ("Scan / Sweep Color", Color) = (1,1,1,1)

        _BeatProgress  ("Beat Progress", Range(0,1)) = 0
        _BeatFlash     ("Beat Flash", Range(0,1)) = 0

        _EdgeGlow      ("Edge Glow", Range(0,4)) = 1.4
        _EdgeWidth     ("Edge Width", Range(0.001,0.3)) = 0.07
        _GridLines     ("Scanline Count", Float) = 5
        _ScanStrength  ("Scanline Strength", Range(0,1)) = 0.25
        _SweepStrength ("Sweep Bar Strength", Range(0,3)) = 1.8
    }

    SubShader
    {
        Tags { "RenderType"="Opaque" "Queue"="Geometry" }
        Cull Back
        ZWrite On

        Pass
        {
        CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            struct appdata { float4 vertex : POSITION; float2 uv : TEXCOORD0; };
            struct v2f     { float4 pos : SV_POSITION; float2 uv : TEXCOORD0; };

            fixed4 _EmissionColor, _BaseColor, _ScanColor;
            float  _BeatProgress, _BeatFlash;
            float  _EdgeGlow, _EdgeWidth, _GridLines, _ScanStrength, _SweepStrength;

            v2f vert(appdata v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.uv  = v.uv;
                return o;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                float3 emis = _EmissionColor.rgb;
                float3 col  = _BaseColor.rgb + emis;

                // Edge glow — brighter near tile borders to define the grid
                float2 e    = min(i.uv, 1.0 - i.uv);
                float  edge = 1.0 - smoothstep(0.0, _EdgeWidth, min(e.x, e.y));
                col += emis * edge * _EdgeGlow;

                // Scrolling scanlines that speed up as the beat approaches
                float speed = lerp(1.0, 9.0, _BeatProgress);
                float scan  = sin((i.uv.y * _GridLines - _Time.y * speed) * 6.28318) * 0.5 + 0.5;
                col += emis * scan * _ScanStrength * _BeatProgress;

                // Sweep bar that crosses the tile toward the player as progress fills
                float sweep = 1.0 - smoothstep(0.0, 0.14, abs(i.uv.y - _BeatProgress));
                col += _ScanColor.rgb * sweep * _SweepStrength * _BeatProgress;

                // Beat flash — blow out to white
                col = lerp(col, _ScanColor.rgb * 4.0, _BeatFlash);

                return fixed4(col, 1.0);
            }
        ENDCG
        }
    }
    Fallback "Unlit/Color"
}
