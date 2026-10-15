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
StructuredBuffer<VividMeshLODGroup> ExpectedGroups : register(t4);
RWStructuredBuffer<Result> Results : register(u0);

bool SameGroup(VividMeshLODGroup a, VividMeshLODGroup b)
{
    return all(asuint(a.Bounds) == asuint(b.Bounds)) && asuint(a.Error) == asuint(b.Error);
}

bool ConservativeBounds(float4 original, float4 decoded, float4x4 objectToWorld, float4x4 vp)
{
    float4x4 shadow = float4x4(.05,0,0,.5, 0,.05,0,.5, 0,0,.05,.5, 0,0,0,1);
    uint2 a, b, c, d;
    bool before = VividVSMProjectLocalBounds(original.xyz, 0, original.w, true, mul(shadow, objectToWorld), 16384, a, b);
    bool after = VividVSMProjectLocalBounds(decoded.xyz, 0, decoded.w, true, mul(shadow, objectToWorld), 16384, c, d);
    bool valid = !before || (after && all(c <= a) && all(d >= b));
    before = VividVSMProjectCasterSphere(TransformSphere(original, objectToWorld), shadow, 16384, a, b);
    after = VividVSMProjectCasterSphere(TransformSphere(decoded, objectToWorld), shadow, 16384, c, d);
    valid = valid && (!before || (after && all(c <= a) && all(d >= b)));
    float4 planes[6] = { vp[3] + vp[0], vp[3] - vp[0], vp[3] + vp[1], vp[3] - vp[1], vp[2], vp[3] - vp[2] };
    before = VividVSMFrustumVsLocalBounds(planes, original.xyz, 0, original.w, true, objectToWorld);
    after = VividVSMFrustumVsLocalBounds(planes, decoded.xyz, 0, decoded.w, true, objectToWorld);
    return valid && (!before || after);
}
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
    // Production 16-byte nodes/groups and 8-byte overflow records. t1/t2/t3
    // contain production C# bytes; t4 holds that encoder's expected FP32 values.
    VividDecodedMeshLODNode parent = PullMeshLODNode(id.x * 3);
    VividDecodedMeshLODNode child = PullMeshLODNode(id.x * 3 + 1);
    VividDecodedMeshLODNode grandparent = PullMeshLODNode(id.x * 3 + 2);
    uint parentId = _MeshLODNodes[id.x * 3].GroupIndex;
    uint childId = _MeshLODNodes[id.x * 3 + 1].GroupIndex;
    uint grandId = _MeshLODNodes[id.x * 3 + 2].GroupIndex;

    float4x4 localToClip = mul(c.ViewProjection, c.ObjectToWorld);
    Result r;
    r.Metrics.x = VividLODProjectedError(parent.Bounds, parent.Error, localToClip, c.Screen.xy);
    r.Metrics.y = VividLODProjectedError(child.Bounds, child.Error, localToClip, c.Screen.xy);
    r.Metrics.z = VividLODProjectedError(grandparent.Bounds, grandparent.Error, localToClip, c.Screen.xy);
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
    VividMeshLODNodePacked middle = _MeshLODNodes[id.x * 3];
    middle.ParentGroupIndex = _MeshLODNodes[id.x * 3 + 2].GroupIndex;
    middle.PackedMeshletCountLevel = 1u | (1u << 16);
    parent = DecodeVividMeshLODNode(middle, PullMeshLODGroup(middle.GroupIndex), PullMeshLODGroup(middle.ParentGroupIndex));
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
    range.ErrorBounds.x = ExpectedGroups[parentId].Error * ((id.x & 1) ? 0.5f : 1.0f);
    range.ErrorBounds.z = ExpectedGroups[grandId].Error * ((id.x & 1) ? 2.0f : 1.0f);
    bool branchKept = VSMLodRangeMaySelect(range, 2, r.Metrics.w, c.Metrics.z * instance.LODErrorScale);
    bool middleSelected = ShouldSelectShadowMeshLODNode(parent, instance, r.Metrics.w,
        VIVID_INVALID_FORCED_MESH_LOD_NODE_DEPTH, c.Metrics.z);
    r.Chain.w = uint(branchKept) | (uint(middleSelected) << 1);
    // A truncated finest node must remain drawable even with a nonzero stored
    // error. A range containing it cannot use the own-error rejection.
    range.Max.w = 2;
    range.ErrorBounds.z = asfloat(0x7f800000u);
    r.Chain.w |= uint(VSMLodRangeMaySelect(range, 2, r.Metrics.w, c.Metrics.z)) << 2;
    bool exactDecode = SameGroup(PullMeshLODGroup(parentId), ExpectedGroups[parentId])
        && SameGroup(PullMeshLODGroup(childId), ExpectedGroups[childId])
        && SameGroup(PullMeshLODGroup(grandId), ExpectedGroups[grandId]);
    r.Chain.w |= uint(exactDecode) << 3;
    bool outer = all(asuint(parent.Bounds.xyz) == asuint(c.ParentBounds.xyz))
        && parent.Bounds.w >= c.ParentBounds.w && parent.Error >= c.Metrics.x
        && child.Bounds.w >= c.ChildBounds.w && child.Error >= c.Metrics.w
        && grandparent.Bounds.w >= c.GrandparentBounds.w && grandparent.Error >= c.Metrics.y;
    r.Chain.w |= uint(outer) << 4;
    r.Chain.w |= uint(ConservativeBounds(c.ParentBounds, parent.Bounds, c.ObjectToWorld, c.ViewProjection)) << 5;
    r.Chain.w |= uint(all(asuint(child.ParentBounds) == asuint(parent.Bounds))
        && asuint(child.ParentError) == asuint(parent.Error)) << 6;
    float upper = VividLODRangeProjectedError(abs(grandparent.Bounds.xyz) + grandparent.Bounds.w,
        grandparent.Error, localToClip, c.Screen.xy);
    r.Chain.w |= uint(upper >= r.Metrics.z * (1.0f - 1e-5f)) << 7;
    // A resident terminal group can be above the globally finest level. Only
    // its own refinement is bypassed; its parent must still refine into it.
    bool parentRefines = r.Metrics.z > budget * instance.LODErrorScale;
    bool residentSelected = ShouldSelectMeshLODNode(context, parent, instance,
        VIVID_INVALID_FORCED_MESH_LOD_NODE_DEPTH, budget, true);
    r.Chain.w |= uint(residentSelected == parentRefines) << 8;
    bool shadowParentRefines = grandparent.Error * r.Metrics.w > c.Metrics.z * instance.LODErrorScale;
    r.Chain.w |= uint(ShouldSelectShadowMeshLODNode(parent, instance, r.Metrics.w,
        VIVID_INVALID_FORCED_MESH_LOD_NODE_DEPTH, c.Metrics.z, true) == shadowParentRefines) << 9;
    r.Chain.w |= uint(ShouldSelectMeshLODNode(context, parent, instance, 2, budget, true)
        && ShouldSelectShadowMeshLODNode(parent, instance, r.Metrics.w, 2, budget, true)
        && !ShouldSelectMeshLODNode(context, parent, instance, 0, budget, true)) << 10;
    Results[id.x] = r;
}
