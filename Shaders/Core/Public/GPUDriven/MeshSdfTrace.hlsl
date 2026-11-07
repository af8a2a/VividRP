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
    uint pageOffset;
    uint2 padding;
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
    uint firstLeaf;
    float3 boundsMax;
    uint escapeIndex;
    uint leafCount;
    uint layerMask;
    uint2 padding;
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
StructuredBuffer<uint> _MeshSdfLeafInstances;
uint _MeshSdfNodeCount;

// The page directory lives at the start of _MeshSdfVoxels. Entries address words relative to the field payload.
static const uint VividMeshSdfBrickCells = 12u;
static const uint VividMeshSdfDense = 0xffffffffu;
static const uint VividMeshSdfEmptyOutside = 0xffffffffu;
static const uint VividMeshSdfEmptyInside = 0xfffffffeu;
static const float VividMeshSdfBandVoxels = 2.0;

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

uint3 VividMeshSdfBrickCount(uint3 dimensions)
{
    return (dimensions + VividMeshSdfBrickCells - 2u) / VividMeshSdfBrickCells;
}

uint VividMeshSdfPage(uint pageOffset, uint3 brick, uint3 counts)
{
    return _MeshSdfVoxels[pageOffset + brick.x + counts.x * (brick.y + counts.y * brick.z)];
}

float VividMeshSdfLoad(uint3 dimensions, uint wordOffset, uint3 cell)
{
    uint texel = cell.x + dimensions.x * (cell.y + dimensions.y * cell.z);
    uint pair = _MeshSdfVoxels[wordOffset + (texel >> 1)];
    return f16tof32((pair >> ((texel & 1u) * 16u)) & 0xffffu);
}

float VividMeshSdfSampleBlock(uint3 dimensions, uint wordOffset, float3 voxel)
{
    voxel = clamp(voxel, 0.0, float3(dimensions - 1u));
    uint3 lo = uint3(voxel);
    uint3 hi = min(lo + 1u, dimensions - 1u);
    float3 f = voxel - lo;
    float z0 = lerp(
        lerp(VividMeshSdfLoad(dimensions, wordOffset, lo), VividMeshSdfLoad(dimensions, wordOffset, uint3(hi.x, lo.y, lo.z)), f.x),
        lerp(VividMeshSdfLoad(dimensions, wordOffset, uint3(lo.x, hi.y, lo.z)), VividMeshSdfLoad(dimensions, wordOffset, uint3(hi.xy, lo.z)), f.x),
        f.y);
    float z1 = lerp(
        lerp(VividMeshSdfLoad(dimensions, wordOffset, uint3(lo.xy, hi.z)), VividMeshSdfLoad(dimensions, wordOffset, uint3(hi.x, lo.y, hi.z)), f.x),
        lerp(VividMeshSdfLoad(dimensions, wordOffset, uint3(lo.x, hi.yz)), VividMeshSdfLoad(dimensions, wordOffset, hi), f.x),
        f.y);
    return lerp(z0, z1, f.z);
}

float VividMeshSdfSample(VividMeshSdfField field, float3 positionInGrid)
{
    float3 voxel = positionInGrid / field.originAndVoxelSize.w;
    if (field.pageOffset == VividMeshSdfDense)
        return VividMeshSdfSampleBlock(field.dimensions, field.wordOffset, voxel);

    voxel = clamp(voxel, 0.0, float3(field.dimensions - 1u));
    uint3 counts = VividMeshSdfBrickCount(field.dimensions);
    uint3 brick = min(uint3(voxel) / VividMeshSdfBrickCells, counts - 1u);
    uint page = VividMeshSdfPage(field.pageOffset, brick, counts);
    if (page == VividMeshSdfEmptyInside || page == VividMeshSdfEmptyOutside)
        return (page == VividMeshSdfEmptyInside ? -1.0 : 1.0) * VividMeshSdfBandVoxels * field.originAndVoxelSize.w;

    uint3 start = brick * VividMeshSdfBrickCells;
    uint3 size = min(field.dimensions - start, VividMeshSdfBrickCells + 1u);
    return VividMeshSdfSampleBlock(size, field.wordOffset + page, voxel - start);
}

float3 VividMeshSdfGradient(VividMeshSdfField field, float3 positionInGrid)
{
    float2 stepSize = float2(field.originAndVoxelSize.w, 0.0);
    return float3(
        VividMeshSdfSample(field, positionInGrid + stepSize.xyy) - VividMeshSdfSample(field, positionInGrid - stepSize.xyy),
        VividMeshSdfSample(field, positionInGrid + stepSize.yxy) - VividMeshSdfSample(field, positionInGrid - stepSize.yxy),
        VividMeshSdfSample(field, positionInGrid + stepSize.yyx) - VividMeshSdfSample(field, positionInGrid - stepSize.yyx));
}

bool VividMeshSdfTraceBricks(VividMeshSdfField field, float3 entry, float3 direction, float tExit, out float hitT)
{
    uint3 counts = VividMeshSdfBrickCount(field.dimensions);
    int3 brick = int3(min(uint3(entry) / VividMeshSdfBrickCells, counts - 1u));
    int3 step = int3(sign(direction));
    float3 face = float3(brick + max(step, 0)) * VividMeshSdfBrickCells;
    float3 inverseDirection = rcp(direction);
    float3 nextFaceT = (face - entry) * inverseDirection;
    float3 deltaT = abs(VividMeshSdfBrickCells * inverseDirection);
    // Parallel axes never advance, even when the entry lies exactly on a face.
    nextFaceT.x = step.x == 0 ? FLT_MAX : max(0.0, nextFaceT.x);
    nextFaceT.y = step.y == 0 ? FLT_MAX : max(0.0, nextFaceT.y);
    nextFaceT.z = step.z == 0 ? FLT_MAX : max(0.0, nextFaceT.z);
    float sampleT = 0.0;
    int samples = 0;
    float voxelSize = field.originAndVoxelSize.w;
    hitT = 0.0;

    // DDA intervals meet at exact brick faces. Empty bricks consume no SDF samples.
    [loop]
    while (all(brick >= 0) && all(brick < int3(counts)))
    {
        float boundary = min(nextFaceT.x, min(nextFaceT.y, nextFaceT.z));
        float brickExit = min(tExit, boundary);
        uint page = VividMeshSdfPage(field.pageOffset, uint3(brick), counts);
        if (page != VividMeshSdfEmptyInside && page != VividMeshSdfEmptyOutside)
        {
            uint3 start = uint3(brick) * VividMeshSdfBrickCells;
            uint3 size = min(field.dimensions - start, VividMeshSdfBrickCells + 1u);
            [loop]
            while (sampleT <= brickExit)
            {
                if (samples++ >= 192)
                    return false;
                float3 voxel = entry + sampleT * direction - start;
                float distanceInVoxels = abs(VividMeshSdfSampleBlock(size, field.wordOffset + page, voxel)) / voxelSize;
                if (distanceInVoxels <= 0.075)
                {
                    hitT = sampleT;
                    return true;
                }
                sampleT += distanceInVoxels * 0.7;
            }
        }
        if (boundary >= tExit)
            break;

        if (nextFaceT.x == boundary) { brick.x += step.x; nextFaceT.x += deltaT.x; }
        if (nextFaceT.y == boundary) { brick.y += step.y; nextFaceT.y += deltaT.y; }
        if (nextFaceT.z == boundary) { brick.z += step.z; nextFaceT.z += deltaT.z; }
        // Keep a completed sphere step across retained bricks. An empty brick can advance it to the next face.
        sampleT = max(sampleT, boundary);
    }
    return false;
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

    if (field.pageOffset != VividMeshSdfDense)
    {
        // Rebase at the grid entry so a distant ray still takes steps in small voxel-space coordinates.
        float3 entry = clamp((localOrigin + t * localDirection) / voxelSize, 0.0, float3(field.dimensions - 1u));
        float hitT;
        if (!VividMeshSdfTraceBricks(field, entry, localDirection, (tExit - t) / voxelSize, hitT))
            return false;
        distance = (t + hitT * voxelSize) / localUnitsPerWorldUnit;
        positionInGrid = (entry + hitT * localDirection) * voxelSize;
        return true;
    }

    [loop]
    for (int step = 0; step < 192 && t <= tExit; step++)
    {
        positionInGrid = localOrigin + t * localDirection;
        float localDistance = abs(VividMeshSdfSampleBlock(field.dimensions, field.wordOffset, positionInGrid / voxelSize));
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
        if ((node.layerMask & layerMask) == 0u
            || !VividMeshSdfClipRay(originWS, directionWS, node.boundsMin, node.boundsMax, entry, exit))
        {
            nodeIndex = node.escapeIndex;
            continue;
        }
        nodeIndex++;
        [loop]
        for (uint i = 0u; i < node.leafCount; i++)
        {
            uint instanceIndex = _MeshSdfLeafInstances[node.firstLeaf + i];
            VividMeshSdfInstance instance = _MeshSdfInstances[instanceIndex];
            if ((instance.layerMask & layerMask) == 0u)
                continue;

            VividMeshSdfField field = _MeshSdfFields[instance.fieldIndex];
            float distance;
            float3 positionInGrid;
            if (VividMeshSdfTraceInstance(field, instance, originWS, directionWS, minDistance, hit.distance,
                distance, positionInGrid)
                && (distance < hit.distance || (distance == hit.distance && instanceIndex < hit.instanceIndex)))
            {
                hit.distance = distance;
                hit.instanceIndex = instanceIndex;
                hitInstance = instance;
                hitField = field;
                hitPosition = positionInGrid;
            }
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
