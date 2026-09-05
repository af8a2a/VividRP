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
#endif
