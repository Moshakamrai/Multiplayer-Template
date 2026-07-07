// Fullscreen pixelation blit with a per-layer fine/coarse split:
// pixels covered by the character mask use the FINE grid, everything else the COARSE grid.
Shader "Hidden/CyberPixel/PixelateBlit"
{
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" }
        ZWrite Off Cull Off ZTest Always

        Pass
        {
            Name "PixelateBlit"

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Common.hlsl"
            #include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl"

            TEXTURE2D(_CyberPixelMask);
            // xy = coarse grid (env) pixel counts, zw = fine grid (characters) pixel counts
            float4 _CyberPixelGrids;

            half4 Frag(Varyings input) : SV_Target
            {
                float2 uv = input.texcoord;
                float2 coarseUV = (floor(uv * _CyberPixelGrids.xy) + 0.5) / _CyberPixelGrids.xy;
                float2 fineUV   = (floor(uv * _CyberPixelGrids.zw) + 0.5) / _CyberPixelGrids.zw;
                half mask = SAMPLE_TEXTURE2D(_CyberPixelMask, sampler_PointClamp, fineUV).r;
                float2 finalUV = mask > 0.5h ? fineUV : coarseUV;
                return SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_PointClamp, finalUV);
            }
            ENDHLSL
        }
    }
}
