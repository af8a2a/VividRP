#define VIVID_VSM_EARLY_CULL
#define VIVID_VSM_LOD_HIERARCHY
#define VIVID_VSM_AFFINE_BOUNDS
#include "../Shaders/Core/Private/GPUDriven/GPUInstanceCulling.compute"

struct Case
{
    float4 ParentBounds;
    float4 ChildBounds;
    float4 GrandparentBounds;
    row_major float4x4 ObjectToWorld;
    row_major float4x4 ViewProjection;
    float4 Metrics; // parent error, grandparent error, pixel budget, child error
    float4 Screen; // width, height, instance budget scale, boundary test mode
};
struct Result
{
    float4 Metrics;
    uint4 Selected;
    uint4 Shadow;
    uint4 Chain;
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
    instance.LODErrorScale = c.Screen.z;
    VividMeshLODNode packedParent = (VividMeshLODNode)0;
    packedParent.Bounds = c.ParentBounds;
    packedParent.Error = c.Metrics.x;
    packedParent.ParentError = -1;
    VividMeshLODNode packedChild = (VividMeshLODNode)0;
    packedChild.Bounds = c.ChildBounds;
    packedChild.ParentBounds = c.ParentBounds;
    packedChild.ParentError = c.Metrics.x;
    packedChild.PackedMeshletCountLevel = 1u << 16;
    VividDecodedMeshLODNode parent = DecodeVividMeshLODNode(packedParent);
    VividDecodedMeshLODNode child = DecodeVividMeshLODNode(packedChild);

    float4x4 localToClip = mul(c.ViewProjection, c.ObjectToWorld);
    Result r;
    r.Metrics.x = VividLODProjectedError(c.ParentBounds, c.Metrics.x, localToClip, c.Screen.xy);
    r.Metrics.y = VividLODProjectedError(c.ChildBounds, c.Metrics.w, localToClip, c.Screen.xy);
    r.Metrics.z = VividLODProjectedError(c.GrandparentBounds, c.Metrics.y, localToClip, c.Screen.xy);
    r.Metrics.w = VividLODOrthographicPixelScale(localToClip, c.Screen.xy);
    float budget = c.Metrics.z;
    if (c.Screen.w == 1) budget = r.Metrics.x; // exact GPU equality
    if (c.Screen.w == 2) budget = asfloat(asuint(r.Metrics.x) - 1u);
    if (c.Screen.w == 3) budget = asfloat(asuint(r.Metrics.x) + 1u);
    r.Selected.x = ShouldSelectMeshLODNode(context, parent, instance,
        VIVID_INVALID_FORCED_MESH_LOD_NODE_DEPTH, budget);
    r.Selected.y = ShouldSelectMeshLODNode(context, child, instance,
        VIVID_INVALID_FORCED_MESH_LOD_NODE_DEPTH, budget);
    r.Selected.z = ShouldSelectMeshLODNode(context, child, instance, 1, budget);
    r.Selected.w = ShouldSelectMeshLODNode(context, parent, instance, 1, budget);
    r.Shadow.x = ShouldSelectShadowMeshLODNode(parent, instance, r.Metrics.w,
        VIVID_INVALID_FORCED_MESH_LOD_NODE_DEPTH, c.Metrics.z);
    r.Shadow.y = ShouldSelectShadowMeshLODNode(child, instance, r.Metrics.w,
        VIVID_INVALID_FORCED_MESH_LOD_NODE_DEPTH, c.Metrics.z);
    r.Shadow.z = ShouldSelectShadowMeshLODNode(child, instance, r.Metrics.w, 1, c.Metrics.z);
    r.Shadow.w = ShouldSelectShadowMeshLODNode(parent, instance, r.Metrics.w, 1, c.Metrics.z);

    // Three nested transitions catch non-monotone projection/error metrics,
    // which a complementary two-node test alone cannot detect.
    VividDecodedMeshLODNode grandparent = parent;
    grandparent.Bounds = c.GrandparentBounds;
    grandparent.Error = c.Metrics.y;
    parent.ParentBounds = c.GrandparentBounds;
    parent.ParentError = c.Metrics.y;
    parent.LevelIndex = 1;
    child.LevelIndex = 2;
    instance.MeshLODLevelCount = 3;
    r.Chain.x = ShouldSelectMeshLODNode(context, grandparent, instance,
        VIVID_INVALID_FORCED_MESH_LOD_NODE_DEPTH, budget);
    r.Chain.y = ShouldSelectMeshLODNode(context, parent, instance,
        VIVID_INVALID_FORCED_MESH_LOD_NODE_DEPTH, budget);
    r.Chain.z = ShouldSelectMeshLODNode(context, child, instance,
        VIVID_INVALID_FORCED_MESH_LOD_NODE_DEPTH, budget);
    // Exercise the actual VSM hierarchy rejection against a contained middle
    // node's leaf decision. Loose intervals model a multi-record branch.
    VSMLodHierarchyNode range = (VSMLodHierarchyNode)0;
    range.Max.w = 1;
    range.ErrorBounds.x = c.Metrics.x * 0.5f;
    range.ErrorBounds.z = c.Metrics.y * 2.0f;
    bool branchKept = VSMLodRangeMaySelect(range, 2, r.Metrics.w, c.Metrics.z * instance.LODErrorScale);
    bool middleSelected = ShouldSelectShadowMeshLODNode(parent, instance, r.Metrics.w,
        VIVID_INVALID_FORCED_MESH_LOD_NODE_DEPTH, c.Metrics.z);
    r.Chain.w = uint(branchKept) | (uint(middleSelected) << 1);
    // A truncated finest node must remain drawable even with a nonzero stored
    // error. A range containing it cannot use the own-error rejection.
    range.Max.w = 2;
    range.ErrorBounds.z = asfloat(0x7f800000u);
    r.Chain.w |= uint(VSMLodRangeMaySelect(range, 2, r.Metrics.w, c.Metrics.z)) << 2;
    Results[id.x] = r;
}
