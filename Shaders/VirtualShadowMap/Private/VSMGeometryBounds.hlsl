#ifndef VIVIDRP_VSM_GEOMETRY_BOUNDS_INCLUDED
#define VIVIDRP_VSM_GEOMETRY_BOUNDS_INCLUDED

struct VividVSMLocalBounds
{
    float3 center;
    float3 extent;
    float radius;
    bool sphere;
};

VividVSMLocalBounds VividVSMSphereBounds(float4 sphere)
{
    VividVSMLocalBounds result;
    result.center = sphere.xyz; result.extent = 0.0;
    result.radius = sphere.w; result.sphere = true;
    return result;
}

#if defined(VIVID_VSM_GEOMETRY_BOUNDS)
// Auxiliary buffers; the serialized meshlet/LOD sphere layouts are unchanged.
struct VividVSMAuxiliaryBounds { float4 Center; float4 Extent; };
StructuredBuffer<VividVSMAuxiliaryBounds> _VSMLodBounds;
StructuredBuffer<VividVSMAuxiliaryBounds> _VSMMeshletBounds;
uint _VSMLodBoundsCount;
uint _VSMMeshletBoundsCount;

VividVSMLocalBounds VividVSMResolveGeometryBounds(VividVSMAuxiliaryBounds box, float4 sphere)
{
    VividVSMLocalBounds result = VividVSMSphereBounds(sphere);
    if (box.Center.w != 0.0)
    {
        result.center = box.Center.xyz; result.extent = box.Extent.xyz;
        result.radius = 0.0; result.sphere = false;
    }
    return result;
}
#endif

VividVSMLocalBounds VividVSMLoadLodBounds(uint index, float4 sphere)
{
#if defined(VIVID_VSM_GEOMETRY_BOUNDS)
    if (index < _VSMLodBoundsCount) return VividVSMResolveGeometryBounds(_VSMLodBounds[index], sphere);
#endif
    return VividVSMSphereBounds(sphere);
}

VividVSMLocalBounds VividVSMLoadMeshletBounds(uint index, float4 sphere)
{
#if defined(VIVID_VSM_GEOMETRY_BOUNDS)
    if (index < _VSMMeshletBoundsCount) return VividVSMResolveGeometryBounds(_VSMMeshletBounds[index], sphere);
#endif
    return VividVSMSphereBounds(sphere);
}
#endif
