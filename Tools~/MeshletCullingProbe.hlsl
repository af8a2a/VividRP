#if defined(PROBE_FP32)
#define float16_t float
#define float16_t3 float3
#endif
#include "../Shaders/Core/Public/GPUDriven/VividMeshletCull.hlsli"

struct Case
{
    float4 Sphere;
    row_major float4x4 ObjectToWorld;
    float4 Planes[6];
    row_major float4x4 ViewProjection;
    row_major float4x4 WorldToObject;
    float4 Cone;
    float4 Camera;
    float4 Forward;
    float4 Settings; // viewport XY, UV starts at top, reserved
    float4 Depths;
    float4 Test; // comparison depth, bias, cone valid, explicit footprint
    float4 Pixels;
    float4 Texture; // physical size XY, mip count, reserved
};
struct Result
{
    uint4 State;
    uint4 Pixels;
    float4 Values;
    uint4 Mip;
};
StructuredBuffer<Case> Cases : register(t0);
RWStructuredBuffer<Result> Results : register(u0);
[numthreads(64, 1, 1)]
void Main(uint3 id : SV_DispatchThreadID)
{
    Case c = Cases[id.x];
    VividInstanceCullBounds bounds = VividBuildMeshletCullBounds(c.Sphere, c.ObjectToWorld);
    Result r;
    r.State.x = VividInstanceFrustumVisible(bounds, c.Planes);
    r.State.y = VividMeshletConeVisible(c.Sphere, c.Cone.xyz, c.Cone.w, c.Test.z != 0,
        c.Forward.w != 0, c.Camera.w != 0, c.Camera.xyz, c.Forward.xyz, c.WorldToObject);
    uint2 lo, hi;
    float depth;
    r.State.z = VividBuildMeshletOcclusionQuery(bounds, c.ViewProjection,
        c.Settings.xy, c.Settings.z != 0, lo, hi, depth);
    r.Pixels = uint4(lo, hi);
    uint mip;
    uint2 mipLo, mipHi;
    if (c.Test.w != 0) { lo = uint2(c.Pixels.xy); hi = uint2(c.Pixels.zw); }
    r.State.w = VividMeshletHZBFootprint(lo, hi, uint2(c.Texture.xy), uint(c.Texture.z), mip, mipLo, mipHi);
    r.Values = float4(depth, VividMeshletVisibleAgainstHZB(c.Depths, c.Test.x, c.Test.y),
        !r.State.z || !r.State.w || VividMeshletVisibleAgainstHZB(c.Depths, depth, c.Test.y), 0);
    r.Mip = uint4(mip, mipLo, 0);
    Results[id.x] = r;
}
