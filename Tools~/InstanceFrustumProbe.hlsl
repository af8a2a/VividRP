// The compute predicate and half arithmetic are shared verbatim with production.
#if defined(PROBE_FP32)
#define float16_t float
#define float16_t3 float3
#endif
#include "../Shaders/Core/Public/GPUDriven/VividInstanceFrustumCull.hlsli"

struct Case
{
    float4 Min;
    float4 Max;
    row_major float4x4 ObjectToWorld;
    float4 Planes[6];
};
struct Result
{
    uint4 Visible;
    float4 Sphere;
};
StructuredBuffer<Case> Cases : register(t0);
RWStructuredBuffer<Result> Results : register(u0);
[numthreads(64, 1, 1)]
void Main(uint3 id : SV_DispatchThreadID)
{
    Case test = Cases[id.x];
    VividInstanceCullBounds bounds = VividBuildInstanceCullBounds(test.Min.xyz, test.Max.xyz, test.ObjectToWorld);
    Result result;
    result.Visible = uint4(VividInstanceFrustumVisible(bounds, test.Planes), bounds.Valid, 0, 0);
    result.Sphere = VividInstanceConservativeSphereWS(bounds);
    Results[id.x] = result;
}
