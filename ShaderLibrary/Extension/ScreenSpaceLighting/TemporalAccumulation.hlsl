#ifndef URP_TEMPORAL_ACCUMULATION_HLSL
#define URP_TEMPORAL_ACCUMULATION_HLSL

// From URP's "TemporalAA.hlsl"
// Per-pixel camera backwards velocity
half2 GetVelocityWithOffset(float2 uv,float2 texelUnit, half2 depthOffsetUv)
{
	// Unity motion vectors are forward motion vectors in screen UV space
	half2 offsetUv =_MotionVectorTexture.SampleLevel(sampler_LinearClamp,uv + texelUnit * depthOffsetUv,0).xy;

	return -offsetUv;
}

void AdjustBestDepthOffset(inout half bestDepth, inout half bestX, inout half bestY, float2 uv,float2 texelUnit, half currX, half currY)
{
	// Half precision should be fine, as we are only concerned about choosing the better value along sharp edges, so it's
	// acceptable to have banding on continuous surfaces
	half depth = SAMPLE_TEXTURE2D_X(_CameraDepthTexture, my_point_clamp_sampler, uv.xy + texelUnit * half2(currX, currY)).r;

#if UNITY_REVERSED_Z
	depth = 1.0 - depth;
#endif

	bool isBest = depth < bestDepth;
	bestDepth = isBest ? depth : bestDepth;
	bestX = isBest ? currX : bestX;
	bestY = isBest ? currY : bestY;
}

half3 SampleColorPoint(Texture2D colorTexture, float2 uv, float2 texelUnit, float2 texelOffset)
{
	return SAMPLE_TEXTURE2D_X_LOD(colorTexture, my_point_clamp_sampler, uv + texelUnit * texelOffset, 0).xyz;
}

void AdjustColorBox(Texture2D colorTexture, float2 texelUnit, inout half3 boxMin, inout half3 boxMax, inout half3 moment1, inout half3 moment2, float2 uv,
                    half currX, half currY)
{
	half3 color = SampleColorPoint(colorTexture, uv, texelUnit, float2(currX, currY));
	boxMin = min(color, boxMin);
	boxMax = max(color, boxMax);
	moment1 += color;
	moment2 += color * color;
}

// From Playdead's TAA
// (half version of HDRP impl)
half3 ClipToAABBCenter(half3 history, half3 minimum, half3 maximum)
{
	// note: only clips towards aabb center (but fast!)
	half3 center = 0.5 * (maximum + minimum);
	half3 extents = 0.5 * (maximum - minimum);

	// This is actually `distance`, however the keyword is reserved
	half3 offset = history - center;
	half3 v_unit = offset.xyz / extents.xyz;
	half3 absUnit = abs(v_unit);
	half maxUnit = Max3(absUnit.x, absUnit.y, absUnit.z);
	if (maxUnit > 1.0)
		return center + (offset / maxUnit);
	else
		return history;
}

#endif