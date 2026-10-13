#include "../Shaders/Core/Public/GPUDriven/VividGPUDrivenCommon.hlsl"

struct Case
{
    float4 ParentBounds;
    float4 ChildBounds;
    float4 PackedParentBounds;
    row_major float4x4 ObjectToWorld;
    row_major float4x4 ViewProjection;
    float4 Metrics; // parent error, packed parent error, threshold, reserved
    float4 Screen;
};
struct Result
{
    float4 Metrics;
    uint4 Selected;
};
StructuredBuffer<Case> Cases : register(t0);
RWStructuredBuffer<Result> Results : register(u0);
[numthreads(64, 1, 1)]
void Main(uint3 id : SV_DispatchThreadID)
{
    Case c = Cases[id.x];
    VividGPULODSelectionContext context = (VividGPULODSelectionContext)0;
    context.ViewProjectionMatrix = c.ViewProjection;
    context.ScreenSizePixels = c.Screen.xy;
    VividInstanceData instance = (VividInstanceData)0;
    instance.ObjectToWorldMatrix = c.ObjectToWorld;
    instance.MeshLODLevelCount = 2;
    instance.LODErrorScale = 1;
    VividDecodedMeshLODNode parent = (VividDecodedMeshLODNode)0;
    parent.Bounds = c.ParentBounds;
    parent.Error = c.Metrics.x;
    parent.ParentError = -1;
    VividDecodedMeshLODNode child = (VividDecodedMeshLODNode)0;
    child.Bounds = c.ChildBounds;
    child.ParentBounds = c.PackedParentBounds;
    child.ParentError = c.Metrics.y;
    child.LevelIndex = 1;

    float4x4 localToClip = mul(c.ViewProjection, c.ObjectToWorld);
    Result r;
    r.Metrics.xy = float2(VividLODProjectedSphereRadiusSq(c.ParentBounds, localToClip, c.Screen.xy),
        VividLODProjectedSphereRadiusSq(c.PackedParentBounds, localToClip, c.Screen.xy));
    r.Metrics.zw = r.Metrics.xy * c.Metrics.xy;
    r.Selected.x = ShouldSelectMeshLODNode(context, parent, instance, 1,
        VIVID_INVALID_FORCED_MESH_LOD_NODE_DEPTH, c.Metrics.z);
    r.Selected.y = ShouldSelectMeshLODNode(context, child, instance, 1,
        VIVID_INVALID_FORCED_MESH_LOD_NODE_DEPTH, c.Metrics.z);
    r.Selected.z = ShouldSelectMeshLODNode(context, child, instance, 1, 1, c.Metrics.z);
    r.Selected.w = ShouldSelectMeshLODNode(context, parent, instance, 1, 1, c.Metrics.z);
    Results[id.x] = r;
}
