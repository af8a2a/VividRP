#ifndef VIVIDRP_VIRTUAL_SHADOW_MAP_PROJECTION_INCLUDED
#define VIVIDRP_VIRTUAL_SHADOW_MAP_PROJECTION_INCLUDED

struct VividVSMProjection
{
    float4x4 worldToClip;
    float4x4 worldToShadow;
    float4 selectionSphere; // clipmaps: unsnapped camera xyz, negative level radius
    float4 parameters; // world texel size, normal bias, border, max distance
};
StructuredBuffer<VividVSMProjection> _VSMProjections;
int _VSMProjectionCount;

// Preserve full virtual-map pixel density while rasterizing into a bounded target.
float4 VividVSMToRasterClip(float4 positionCS, uint2 origin, uint resolution, uint tileSize)
{
    float2 offset = ((float)resolution - 2.0 * (float2)origin - (float)tileSize)
        / (float)tileSize;
#if UNITY_UV_STARTS_AT_TOP
    offset.y = -offset.y;
#endif
    positionCS.xy = positionCS.xy * ((float)resolution / (float)tileSize)
        + offset * positionCS.w;
    return positionCS;
}

// Evaluate the same original triangle at the same virtual texel center,
// independent of page origin, viewport extent and clip-generated vertices.
// Depth stays floating point: do not quantize or merge nearby surfaces.
float3 VividVSMClipToVirtualRaster(float4 clip, uint resolution)
{
    precise float3 ndc = clip.xyz / clip.w;
#if UNITY_UV_STARTS_AT_TOP
    precise float2 uv = ndc.xy * float2(0.5, -0.5) + 0.5;
#else
    precise float2 uv = ndc.xy * 0.5 + 0.5;
#endif
    return float3(uv * (float)resolution, ndc.z);
}

bool VividVSMBuildDepthPlane(float4 clip0, float4 clip1, float4 clip2,
    uint resolution, out float4 gradientOrigin, out float depthBase)
{
    float3 p0 = VividVSMClipToVirtualRaster(clip0, resolution);
    float3 p1 = VividVSMClipToVirtualRaster(clip1, resolution);
    float3 p2 = VividVSMClipToVirtualRaster(clip2, resolution);
    precise float3 e0 = p1 - p0;
    precise float3 e1 = p2 - p0;
    precise float determinant = e0.x * e1.y - e0.y * e1.x;
    gradientOrigin = 0.0;
    depthBase = 0.0;
    if (determinant == 0.0 || !all(isfinite(float3(p0.z, p1.z, p2.z)))) return false;
    precise float2 gradient = float2(e0.z * e1.y - e0.y * e1.z,
        e0.x * e1.z - e0.z * e1.x) / determinant;
    if (!all(isfinite(gradient)) || !all(isfinite(p0.xy))) return false;
    gradientOrigin = float4(gradient, p0.xy);
    depthBase = p0.z;
    return true;
}

float VividVSMEvaluateDepthPlane(float4 gradientOrigin, float depthBase, uint2 virtualTexel)
{
    precise float2 center = (float2)virtualTexel + 0.5;
    precise float2 relative = center - gradientOrigin.zw;
    precise float depth = depthBase + relative.x * gradientOrigin.x + relative.y * gradientOrigin.y;
    return depth;
}

// Snap geometry once in virtual-map space, before subtracting an integer page
// origin. One 1/256-texel grid removes viewport-dependent edge rounding. This
// affects only raster coverage, never the floating point depth representation.
float4 VividVSMToStableRasterClip(float4 clip, uint2 origin, uint resolution, uint tileSize)
{
    float3 screen = VividVSMClipToVirtualRaster(clip, resolution);
    precise float2 snapped = floor(screen.xy * 256.0 + 0.5) * (1.0 / 256.0);
    precise float2 local = snapped - (float2)origin;
#if UNITY_UV_STARTS_AT_TOP
    precise float2 ndc = local * (2.0 / (float)tileSize) * float2(1.0, -1.0) + float2(-1.0, 1.0);
#else
    precise float2 ndc = local * (2.0 / (float)tileSize) - 1.0;
#endif
    clip.xy = ndc * clip.w;
    return clip;
}

#endif
