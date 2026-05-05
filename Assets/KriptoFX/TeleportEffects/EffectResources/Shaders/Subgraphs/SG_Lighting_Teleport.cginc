

inline void GetExposureTeleport_float(out float exposure)
{
	exposure = 1;
	#ifndef SHADERGRAPH_PREVIEW
		#ifdef SHADEROPTIONS_PRE_EXPOSITION
			exposure = GetCurrentExposureMultiplier();
		#endif
	#endif
}

inline void GetPlatformSpecificEmissionMultiplierTeleport_float(out float multiplier)
{
	multiplier = 1;
	#ifndef SHADERGRAPH_PREVIEW
		#ifdef SHADEROPTIONS_PRE_EXPOSITION
			multiplier = 10;
		#endif
	#endif
}

inline void GetDissolveCutout_float(float alpha, float2 noise, float2 noiseScale, float3 color1, float3 color2, float3 color3, float3 dissolveThreshold,  out float3 dissolveColor, out float dissolveAlpha)
{
	dissolveColor = 0;
	dissolveAlpha = 1;
	alpha += 1;

	noise = noise - 0.5f;
	alpha += noise.x * noiseScale.x + noise.y * noiseScale.y;
	alpha = saturate(alpha);
	
	if(alpha < 0.1) alpha = 0.1;
	if(alpha > 0.99) alpha = 0;

	//float threshold1 = saturate(alphaRemap * (1.0 / dissolveThreshold.x));
	dissolveAlpha = smoothstep(0, dissolveThreshold.x, alpha);
	dissolveColor = dissolveAlpha * color3;
	dissolveColor = lerp(dissolveColor, color2, smoothstep(dissolveThreshold.y, 1, alpha));
	dissolveColor = lerp(dissolveColor, color1, smoothstep(dissolveThreshold.z, 1, alpha));

}

inline void GetVertexAttractor_float(float3 localPos, float3 noise, float noiseStrength, float effectStrentgh, out float3 newPosition)
{
	float3 remapedNoise = noise * 2 - 1;
	float distanceToCenter = 1-saturate(length(localPos));
	effectStrentgh = saturate(effectStrentgh);
	
	newPosition = localPos + remapedNoise * noiseStrength * effectStrentgh;
	//newPosition = lerp(newPosition, float3(0,0,0), pow(distanceToCenter, 1.5 - effectStrentgh ) *  effectStrentgh);
	//newPosition *= (1.0 - effectStrentgh);
	float lerpVal = noise.z + (effectStrentgh * 2 - 1);
	lerpVal = saturate(lerpVal / effectStrentgh);

	newPosition = lerp(newPosition, float3(0, 0, 0),  lerpVal * lerpVal * lerpVal * lerpVal);
}

inline void GetVertexTeleportation_float(float3 vertex, float VertexMaxHeight, float VertexCutout, float3 worldPos, out float3 newWorldPosition)
{
	float _Smoothness = 0.5;
	VertexCutout = saturate(VertexCutout);
	float initialVertexCount = VertexCutout;
	VertexCutout = 1-VertexCutout;
	VertexCutout = VertexCutout*3;
	VertexCutout -= 2;


	float smoothFactor = smoothstep(VertexCutout, VertexCutout + _Smoothness, vertex.y);
	if(vertex.y > VertexCutout && initialVertexCount > 0.01)  
	{
	  worldPos.y += smoothFactor * VertexMaxHeight;
	}
	
	newWorldPosition = worldPos;

}

inline void GetVertexTeleportationDiscard_float(float3 pos, float TeleportMaxHeight, out float3 newPos)
{
	newPos = pos;
	if(pos.y > TeleportMaxHeight * 0.5) newPos = asfloat(0x7fc00000);
}