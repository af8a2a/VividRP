#ifndef VIVID_MESH_SDF_TRACE_INCLUDED
#define VIVID_MESH_SDF_TRACE_INCLUDED

#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Common.hlsl"

// Structured buffer layouts match VividMeshSdfGpuScene.
struct VividMeshSdfField
{
    float4 originAndVoxelSize;
    uint3 dimensions;
    uint wordOffset;
    uint signMode;
    uint3 padding;
};

struct VividMeshSdfInstance
{
    float4 worldToLocal0, worldToLocal1, worldToLocal2;
    uint fieldIndex;
    uint layerMask;
    uint2 entityId;
};

struct VividMeshSdfNode
{
    float3 boundsMin;
    uint instanceIndex;
    float3 boundsMax;
    uint escapeIndex;
};

struct VividMeshSdfHit
{
    float distance;
    float3 normalWS;
    uint2 entityId;
    uint instanceIndex;
    uint signMode;
};

StructuredBuffer<uint> _MeshSdfVoxels;
StructuredBuffer<VividMeshSdfField> _MeshSdfFields;
StructuredBuffer<VividMeshSdfInstance> _MeshSdfInstances;
StructuredBuffer<VividMeshSdfNode> _MeshSdfNodes;
uint _MeshSdfNodeCount;

bool VividMeshSdfClipRay(float3 origin, float3 direction, float3 boundsMin, float3 boundsMax,
    inout float tMin, inout float tMax)
{
    [unroll]
    for (int axis = 0; axis < 3; axis++)
    {
        if (direction[axis] == 0.0)
        {
            if (origin[axis] < boundsMin[axis] || origin[axis] > boundsMax[axis])
                return false;
            continue;
        }

        float a = (boundsMin[axis] - origin[axis]) / direction[axis];
        float b = (boundsMax[axis] - origin[axis]) / direction[axis];
        tMin = max(tMin, min(a, b));
        tMax = min(tMax, max(a, b));
    }
    return tMin <= tMax;
}

float VividMeshSdfLoad(VividMeshSdfField field, uint3 cell)
{
    uint texel = cell.x + field.dimensions.x * (cell.y + field.dimensions.y * cell.z);
    uint pair = _MeshSdfVoxels[field.wordOffset + (texel >> 1)];
    return f16tof32((pair >> ((texel & 1u) * 16u)) & 0xffffu);
}

float VividMeshSdfSample(VividMeshSdfField field, float3 positionInGrid)
{
    float3 voxel = clamp(positionInGrid / field.originAndVoxelSize.w, 0.0, float3(field.dimensions - 1u));
    uint3 lo = uint3(voxel);
    uint3 hi = min(lo + 1u, field.dimensions - 1u);
    float3 f = voxel - lo;
    float z0 = lerp(
        lerp(VividMeshSdfLoad(field, lo), VividMeshSdfLoad(field, uint3(hi.x, lo.y, lo.z)), f.x),
        lerp(VividMeshSdfLoad(field, uint3(lo.x, hi.y, lo.z)), VividMeshSdfLoad(field, uint3(hi.xy, lo.z)), f.x),
        f.y);
    float z1 = lerp(
        lerp(VividMeshSdfLoad(field, uint3(lo.xy, hi.z)), VividMeshSdfLoad(field, uint3(hi.x, lo.y, hi.z)), f.x),
        lerp(VividMeshSdfLoad(field, uint3(lo.x, hi.yz)), VividMeshSdfLoad(field, hi), f.x),
        f.y);
    return lerp(z0, z1, f.z);
}

float3 VividMeshSdfGradient(VividMeshSdfField field, float3 positionInGrid)
{
    float2 stepSize = float2(field.originAndVoxelSize.w, 0.0);
    return float3(
        VividMeshSdfSample(field, positionInGrid + stepSize.xyy) - VividMeshSdfSample(field, positionInGrid - stepSize.xyy),
        VividMeshSdfSample(field, positionInGrid + stepSize.yxy) - VividMeshSdfSample(field, positionInGrid - stepSize.yxy),
        VividMeshSdfSample(field, positionInGrid + stepSize.yyx) - VividMeshSdfSample(field, positionInGrid - stepSize.yyx));
}

bool VividMeshSdfTraceInstance(VividMeshSdfField field, VividMeshSdfInstance instance, float3 originWS,
    float3 directionWS, float minDistance, float maxDistance, out float distance, out float3 positionInGrid)
{
    float4 origin = float4(originWS, 1.0);
    float3 localOrigin = float3(dot(instance.worldToLocal0, origin), dot(instance.worldToLocal1, origin),
        dot(instance.worldToLocal2, origin)) - field.originAndVoxelSize.xyz;
    float3 localDirection = float3(dot(instance.worldToLocal0.xyz, directionWS),
        dot(instance.worldToLocal1.xyz, directionWS), dot(instance.worldToLocal2.xyz, directionWS));
    // March in local units and convert back to the original ray parameter, including under non-uniform scale.
    float localUnitsPerWorldUnit = length(localDirection);
    localDirection /= localUnitsPerWorldUnit;
    float t = minDistance * localUnitsPerWorldUnit;
    float tExit = maxDistance * localUnitsPerWorldUnit;
    float voxelSize = field.originAndVoxelSize.w;
    float3 extent = float3(field.dimensions - 1u) * voxelSize;
    distance = maxDistance;
    positionInGrid = 0.0;
    if (!VividMeshSdfClipRay(localOrigin, localDirection, 0.0, extent, t, tExit))
        return false;

    [loop]
    for (int step = 0; step < 192 && t <= tExit; step++)
    {
        positionInGrid = localOrigin + t * localDirection;
        float localDistance = abs(VividMeshSdfSample(field, positionInGrid));
        if (localDistance <= 0.075 * voxelSize)
        {
            distance = t / localUnitsPerWorldUnit;
            return true;
        }
        t += localDistance * 0.7;
    }
    return false;
}

// directionWS is normalized. The returned distance is measured from originWS in world units.
bool VividTraceMeshSdfScene(float3 originWS, float3 directionWS, float minDistance, float maxDistance,
    uint layerMask, out VividMeshSdfHit hit)
{
    hit = (VividMeshSdfHit)0;
    hit.distance = maxDistance;
    hit.instanceIndex = 0xffffffffu;
    VividMeshSdfInstance hitInstance = (VividMeshSdfInstance)0;
    VividMeshSdfField hitField = (VividMeshSdfField)0;
    float3 hitPosition = 0.0;

    // Nodes are in preorder. A miss skips the entire subtree without a per-ray stack.
    uint nodeIndex = 0u;
    [loop]
    while (nodeIndex < _MeshSdfNodeCount)
    {
        VividMeshSdfNode node = _MeshSdfNodes[nodeIndex];
        float entry = minDistance;
        float exit = hit.distance;
        if (!VividMeshSdfClipRay(originWS, directionWS, node.boundsMin, node.boundsMax, entry, exit))
        {
            nodeIndex = node.escapeIndex;
            continue;
        }
        nodeIndex++;
        if (node.instanceIndex == 0xffffffffu)
            continue;

        VividMeshSdfInstance instance = _MeshSdfInstances[node.instanceIndex];
        if ((instance.layerMask & layerMask) == 0u)
            continue;

        VividMeshSdfField field = _MeshSdfFields[instance.fieldIndex];
        float distance;
        float3 positionInGrid;
        if (VividMeshSdfTraceInstance(field, instance, originWS, directionWS, minDistance, hit.distance,
            distance, positionInGrid))
        {
            hit.distance = distance;
            hit.instanceIndex = node.instanceIndex;
            hitInstance = instance;
            hitField = field;
            hitPosition = positionInGrid;
        }
    }

    if (hit.instanceIndex == 0xffffffffu)
        return false;

    float3 gradient = VividMeshSdfGradient(hitField, hitPosition);
    hit.normalWS = SafeNormalize(gradient.x * hitInstance.worldToLocal0.xyz
        + gradient.y * hitInstance.worldToLocal1.xyz + gradient.z * hitInstance.worldToLocal2.xyz);
    hit.entityId = hitInstance.entityId;
    hit.signMode = hitField.signMode;
    return true;
}

#endif
