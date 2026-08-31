#ifndef VIVIDRP_GPU_DRIVEN_COMMON_INCLUDED
#define VIVIDRP_GPU_DRIVEN_COMMON_INCLUDED

#include "Packages/com.vivid.render-pipelines/Shaders/Core/Public/Input.hlsl"
#include "Packages/com.vivid.render-pipelines/Runtime/SubSystem/GPUDriven/VividGPUDrivenStructs.cs.hlsl"

#define VIVID_MATERIAL_PROGRAM_LEGACY_FALLBACK 0u
#define VIVID_MATERIAL_PROGRAM_KNOWN 1u
#define VIVID_MATERIAL_PROGRAM_KNOWN_FAILURE 2u
#define VIVID_MAX_MESHLET_INDICES MAX_MESHLET_INDICES
#define VIVID_INVALID_FORCED_MESH_LOD_NODE_DEPTH 0xffffffffu
struct VividDecodedMeshLODNode
{
    float4 Bounds;
    float4 ParentBounds;
    float ParentError;
    float Error;
    uint MeshletStartIndex;
    uint MeshletCount;
    uint LevelIndex;
};

struct VividDecodedMeshlet
{
    uint VertexOffset;
    uint TriangleOffset;
    uint VertexCount;
    uint TriangleCount;
    float4 BoundingSphere;
    float3 ConeAxis;
    float ConeCutoff;
    uint ConeValid;
};

struct VividDecodedMeshletVertex
{
    float4 Position;
    float4 Normal;
    float4 Tangent;
    float4 UV;
};

static const uint VIVID_MESHLET_OCTAHEDRAL_COMPONENT_MASK = 0x7FFFu;
static const uint VIVID_MESHLET_NORMAL_VALID_BIT = 1u << 30;
static const uint VIVID_MESHLET_TANGENT_NEGATIVE_HANDEDNESS_BIT = 1u << 30;
static const uint VIVID_MESHLET_TANGENT_VALID_BIT = 1u << 31;
static const uint VIVID_MESHLET_CONE_OCTAHEDRAL_COMPONENT_MASK = 0x3FFu;
static const uint VIVID_MESHLET_CONE_CUTOFF_MASK = 0x7FFu;
static const uint VIVID_MESHLET_CONE_VALID_BIT = 1u << 31;

float3 DecodeVividMeshletOctahedral15(const uint packedDirection)
{
    float2 octahedral = float2(
        packedDirection & VIVID_MESHLET_OCTAHEDRAL_COMPONENT_MASK,
        (packedDirection >> 15) & VIVID_MESHLET_OCTAHEDRAL_COMPONENT_MASK
    );
    octahedral = octahedral / float(VIVID_MESHLET_OCTAHEDRAL_COMPONENT_MASK) * 2.0f - 1.0f;

    float3 direction = float3(
        octahedral,
        1.0f - abs(octahedral.x) - abs(octahedral.y)
    );
    const float fold = saturate(-direction.z);
    const float2 foldSign = step(0.0f, direction.xy) * 2.0f - 1.0f;
    direction.xy -= foldSign * fold;
    return direction * rsqrt(max(dot(direction, direction), 1e-20f));
}

float3 DecodeVividMeshletOctahedral10(const uint packedDirection)
{
    float2 octahedral = float2(
        packedDirection & VIVID_MESHLET_CONE_OCTAHEDRAL_COMPONENT_MASK,
        (packedDirection >> 10) & VIVID_MESHLET_CONE_OCTAHEDRAL_COMPONENT_MASK
    );
    octahedral = octahedral / float(VIVID_MESHLET_CONE_OCTAHEDRAL_COMPONENT_MASK) * 2.0f - 1.0f;

    float3 direction = float3(
        octahedral,
        1.0f - abs(octahedral.x) - abs(octahedral.y)
    );
    const float fold = saturate(-direction.z);
    const float2 foldSign = step(0.0f, direction.xy) * 2.0f - 1.0f;
    direction.xy -= foldSign * fold;
    return direction * rsqrt(max(dot(direction, direction), 1e-20f));
}

VividDecodedMeshlet DecodeVividMeshlet(const VividMeshlet packedMeshlet)
{
    VividDecodedMeshlet meshlet;
    meshlet.VertexOffset = packedMeshlet.VertexOffset;
    meshlet.TriangleOffset = packedMeshlet.TriangleOffset;
    meshlet.VertexCount = packedMeshlet.PackedVertexTriangleCounts & 0xFFFFu;
    meshlet.TriangleCount = packedMeshlet.PackedVertexTriangleCounts >> 16;
    meshlet.BoundingSphere = packedMeshlet.BoundingSphere;
    meshlet.ConeValid = (packedMeshlet.PackedCone & VIVID_MESHLET_CONE_VALID_BIT) != 0u;
    meshlet.ConeAxis = meshlet.ConeValid != 0u
        ? DecodeVividMeshletOctahedral10(packedMeshlet.PackedCone)
        : 0.0f;
    const uint packedCutoff = (packedMeshlet.PackedCone >> 20) & VIVID_MESHLET_CONE_CUTOFF_MASK;
    meshlet.ConeCutoff = packedCutoff / float(VIVID_MESHLET_CONE_CUTOFF_MASK) * 2.0f - 1.0f;
    return meshlet;
}

VividDecodedMeshLODNode DecodeVividMeshLODNode(const VividMeshLODNode packedNode)
{
    VividDecodedMeshLODNode node;
    node.Bounds = packedNode.Bounds;
    node.ParentError = asfloat((packedNode.PackedParentErrorRadius & 0xFFFFu) << 16);
    const float parentRadius = asfloat(packedNode.PackedParentErrorRadius & 0xFFFF0000u);
    node.ParentBounds = node.ParentError < 0.0f
        ? 0.0f
        : float4(packedNode.Bounds.xyz, parentRadius);
    node.Error = packedNode.Error;
    node.MeshletStartIndex = packedNode.MeshletStartIndex;
    node.MeshletCount = packedNode.PackedMeshletCountLevel & 0xFFFFu;
    node.LevelIndex = packedNode.PackedMeshletCountLevel >> 16;
    return node;
}

VividDecodedMeshletVertex DecodeVividMeshletVertex(const VividMeshletVertex packedVertex)
{
    VividDecodedMeshletVertex vertex;
    vertex.Position = float4(
        packedVertex.PositionX,
        packedVertex.PositionY,
        packedVertex.PositionZ,
        1.0f);
    vertex.Normal = (packedVertex.PackedNormal & VIVID_MESHLET_NORMAL_VALID_BIT) != 0u
        ? float4(DecodeVividMeshletOctahedral15(packedVertex.PackedNormal), 0.0f)
        : 0.0f;
    vertex.Tangent = (packedVertex.PackedTangent & VIVID_MESHLET_TANGENT_VALID_BIT) != 0u
        ? float4(
            DecodeVividMeshletOctahedral15(packedVertex.PackedTangent),
            (packedVertex.PackedTangent & VIVID_MESHLET_TANGENT_NEGATIVE_HANDEDNESS_BIT) != 0u
                ? -1.0f
                : 1.0f)
        : 0.0f;
    vertex.UV = float4(packedVertex.UV, 0.0f, 0.0f);
    return vertex;
}

static const uint VIVID_INDIRECT_DRAW_ARGS_STRIDE = 16u;
static const uint VIVID_INDIRECT_DRAW_ARGS_VERTEX_COUNT_OFFSET = 0u;
static const uint VIVID_INDIRECT_DRAW_ARGS_INSTANCE_COUNT_OFFSET = 4u;
static const uint VIVID_INDIRECT_DRAW_ARGS_START_VERTEX_OFFSET = 8u;
static const uint VIVID_INDIRECT_DRAW_ARGS_START_INSTANCE_OFFSET = 12u;

uint GetIndirectDrawArgsByteAddress(const uint drawArgsIndex)
{
    return drawArgsIndex * VIVID_INDIRECT_DRAW_ARGS_STRIDE;
}

struct VividMeshletListBuildJob
{
    uint InstanceID;
    uint MeshLODNodeOffset;
    uint MeshLODNodeCount;
    uint Padding0;
};

StructuredBuffer<VividInstanceData> _InstanceData;
uint _InstanceDataCount;

StructuredBuffer<VividMaterialData> _MaterialData;
uint _MaterialDataCount;
StructuredBuffer<VividDualSlabMaterialData> _DualSlabMaterialData;
uint _DualSlabMaterialDataCount;
StructuredBuffer<uint4> _MaterialParameterData;
uint _MaterialParameterDataCount;
StructuredBuffer<VividMaterialResourceData> _MaterialResourceData;
uint _MaterialResourceDataCount;
StructuredBuffer<VividMaterialRuntimeHeader> _MaterialRuntimeHeaders;
uint _MaterialRuntimeHeaderCount;
StructuredBuffer<VividMaterialProgramData> _MaterialPrograms;
uint _MaterialProgramCount;
StructuredBuffer<VividSurfaceBindingData> _SurfaceBindingData;
uint _SurfaceBindingDataCount;
StructuredBuffer<VividTerrainMaterialData> _TerrainMaterialData;
uint _TerrainMaterialDataCount;
StructuredBuffer<VividTerrainLayerGPUData> _TerrainLayerData;
uint _TerrainLayerDataCount;
StructuredBuffer<VividMeshLODNode> _MeshLODNodes;
StructuredBuffer<VividMeshlet> _Meshlets;
uint _MeshLODNodeCount;
uint _MeshletCount;

VividInstanceData PullInstanceData(const uint instanceIndex)
{
    return _InstanceData[instanceIndex];
}

VividMaterialData PullMaterialData(const uint materialIndex)
{
    return _MaterialData[materialIndex];
}

VividDualSlabMaterialData PullDualSlabMaterialData(const uint materialIndex)
{
    return _DualSlabMaterialData[materialIndex];
}

uint VividLoadMaterialParameterWord(
    const uint parameterAddress,
    const uint wordOffset)
{
    const uint4 lane = _MaterialParameterData[
        parameterAddress + (wordOffset >> 2u)];
    return lane[wordOffset & 3u];
}

bool VividLoadMaterialBool(
    const uint parameterAddress,
    const uint wordOffset)
{
    return VividLoadMaterialParameterWord(parameterAddress, wordOffset) != 0u;
}

float VividLoadMaterialFloat(
    const uint parameterAddress,
    const uint wordOffset)
{
    return asfloat(VividLoadMaterialParameterWord(parameterAddress, wordOffset));
}

float2 VividLoadMaterialFloat2(
    const uint parameterAddress,
    const uint wordOffset)
{
    return asfloat(uint2(
        VividLoadMaterialParameterWord(parameterAddress, wordOffset),
        VividLoadMaterialParameterWord(parameterAddress, wordOffset + 1u)));
}

float3 VividLoadMaterialFloat3(
    const uint parameterAddress,
    const uint wordOffset)
{
    return asfloat(uint3(
        VividLoadMaterialParameterWord(parameterAddress, wordOffset),
        VividLoadMaterialParameterWord(parameterAddress, wordOffset + 1u),
        VividLoadMaterialParameterWord(parameterAddress, wordOffset + 2u)));
}

float4 VividLoadMaterialFloat4(
    const uint parameterAddress,
    const uint wordOffset)
{
    return asfloat(uint4(
        VividLoadMaterialParameterWord(parameterAddress, wordOffset),
        VividLoadMaterialParameterWord(parameterAddress, wordOffset + 1u),
        VividLoadMaterialParameterWord(parameterAddress, wordOffset + 2u),
        VividLoadMaterialParameterWord(parameterAddress, wordOffset + 3u)));
}

VividMaterialResourceData PullMaterialResourceData(const uint resourceIndex)
{
    return _MaterialResourceData[resourceIndex];
}

VividSlabMaterialData VividCreateSlabMaterialData(
    const VividMaterialResourceData resourceData)
{
    VividSlabMaterialData slabData = (VividSlabMaterialData) 0;
    slabData.TextureTilingOffset = resourceData.TextureTilingOffset;
    slabData.MetallicSmoothnessRemap =
        resourceData.MetallicSmoothnessRemap;
    slabData.AmbientOcclusionRemap = resourceData.AmbientOcclusionRemap;
    slabData.NormalsStrength = resourceData.NormalsStrength;
    slabData.MaskMode = resourceData.MaskMode;
    return slabData;
}

VividSlabMaterialData VividCreateSlabMaterialData(
    const VividMaterialData materialData)
{
    VividSlabMaterialData slabData;
    slabData.AlbedoColor = materialData.AlbedoColor;
    slabData.TextureTilingOffset = materialData.TextureTilingOffset;
    slabData.MetallicSmoothnessRemap = materialData.MetallicSmoothnessRemap;
    slabData.AmbientOcclusionRemap = materialData.AmbientOcclusionRemap;
    slabData.NormalsStrength = materialData.NormalsStrength;
    slabData.Roughness = materialData.Roughness;
    slabData.Metallic = materialData.Metallic;
    slabData.MaskMode = materialData.Padding0;
    return slabData;
}

VividSlabMaterialData VividGetBaseSlabMaterialData(
    const VividDualSlabMaterialData materialData)
{
    VividSlabMaterialData slabData;
    slabData.AlbedoColor = materialData.BaseAlbedoColor;
    slabData.TextureTilingOffset = materialData.BaseTextureTilingOffset;
    slabData.MetallicSmoothnessRemap = materialData.BaseMetallicSmoothnessRemap;
    slabData.AmbientOcclusionRemap = materialData.BaseAmbientOcclusionRemap;
    slabData.NormalsStrength = materialData.BaseNormalsStrength;
    slabData.Roughness = materialData.BaseRoughness;
    slabData.Metallic = materialData.BaseMetallic;
    slabData.MaskMode = materialData.BaseMaskMode;
    return slabData;
}

VividSlabMaterialData VividGetTopSlabMaterialData(
    const VividDualSlabMaterialData materialData)
{
    VividSlabMaterialData slabData;
    slabData.AlbedoColor = materialData.TopAlbedoColor;
    slabData.TextureTilingOffset = materialData.TopTextureTilingOffset;
    slabData.MetallicSmoothnessRemap = materialData.TopMetallicSmoothnessRemap;
    slabData.AmbientOcclusionRemap = materialData.TopAmbientOcclusionRemap;
    slabData.NormalsStrength = materialData.TopNormalsStrength;
    slabData.Roughness = materialData.TopRoughness;
    slabData.Metallic = materialData.TopMetallic;
    slabData.MaskMode = materialData.TopMaskMode;
    return slabData;
}

VividMaterialRuntimeHeader PullMaterialRuntimeHeader(const uint materialIndex)
{
    return _MaterialRuntimeHeaders[materialIndex];
}

VividMaterialProgramData PullMaterialProgramData(const uint programIndex)
{
    return _MaterialPrograms[programIndex];
}

uint VividGetMaterialProgramStatus(
    const uint materialIndex,
    out VividMaterialRuntimeHeader runtimeHeader,
    out VividMaterialProgramData programData)
{
    runtimeHeader = (VividMaterialRuntimeHeader) 0;
    runtimeHeader.ProgramID = VIVIDMATERIALPROGRAMID_INVALID;
    programData = (VividMaterialProgramData) 0;
    if (materialIndex >= _MaterialRuntimeHeaderCount)
        return VIVID_MATERIAL_PROGRAM_KNOWN_FAILURE;

    runtimeHeader = PullMaterialRuntimeHeader(materialIndex);
    if (runtimeHeader.ProgramID == VIVIDMATERIALPROGRAMID_INVALID)
        return VIVID_MATERIAL_PROGRAM_LEGACY_FALLBACK;
    if (runtimeHeader.ProgramID >= _MaterialProgramCount)
        return VIVID_MATERIAL_PROGRAM_KNOWN_FAILURE;

    programData = PullMaterialProgramData(runtimeHeader.ProgramID);
    return VIVID_MATERIAL_PROGRAM_KNOWN;
}

VividSurfaceBindingData PullSurfaceBindingData(const uint surfaceBindingIndex)
{
    return _SurfaceBindingData[surfaceBindingIndex];
}

VividTerrainMaterialData PullTerrainMaterialData(const uint terrainMaterialIndex)
{
    return _TerrainMaterialData[terrainMaterialIndex];
}

VividTerrainLayerGPUData PullTerrainLayerData(const uint terrainLayerIndex)
{
    return _TerrainLayerData[terrainLayerIndex];
}

VividDecodedMeshLODNode PullMeshLODNode(const uint nodeIndex)
{
    return DecodeVividMeshLODNode(_MeshLODNodes[nodeIndex]);
}

VividDecodedMeshlet PullMeshletData(const uint meshletIndex)
{
    return DecodeVividMeshlet(_Meshlets[meshletIndex]);
}

float LengthSq(const float3 value)
{
    return dot(value, value);
}

float3 TransformPosition(const float4x4 matrixValue, const float3 positionOS)
{
    return mul(matrixValue, float4(positionOS, 1.0f)).xyz;
}

float4 TransformSphere(const float4 sphereOS, const float4x4 objectToWorldMatrix)
{
    const float3 centerWS = TransformPosition(objectToWorldMatrix, sphereOS.xyz);
    const float radiusOS = sphereOS.w;

    const float3 xWS = TransformPosition(objectToWorldMatrix, sphereOS.xyz + float3(radiusOS, 0.0f, 0.0f));
    const float3 yWS = TransformPosition(objectToWorldMatrix, sphereOS.xyz + float3(0.0f, radiusOS, 0.0f));
    const float3 zWS = TransformPosition(objectToWorldMatrix, sphereOS.xyz + float3(0.0f, 0.0f, radiusOS));

    const float radiusWS = max(length(xWS - centerWS), max(length(yWS - centerWS), length(zWS - centerWS)));
    return float4(centerWS, radiusWS);
}

float4 ComputeInstanceBoundingSphereWS(const VividInstanceData instanceData)
{
    const float3 aabbMinOS = instanceData.AABBMin.xyz;
    const float3 aabbMaxOS = instanceData.AABBMax.xyz;
    const float3 centerOS = (aabbMinOS + aabbMaxOS) * 0.5f;
    const float radiusOS = length(aabbMaxOS - centerOS);
    return TransformSphere(float4(centerOS, radiusOS), instanceData.ObjectToWorldMatrix);
}

bool FrustumVsSphere(const VividGPUCullingContext cullingContext, const float4 sphereWS)
{
    [unroll]
    for (uint planeIndex = 0; planeIndex < 6; ++planeIndex)
    {
        const float4 plane = cullingContext.FrustumPlanes[planeIndex];
        if (dot(plane.xyz, sphereWS.xyz) + plane.w < -sphereWS.w)
        {
            return false;
        }
    }

    return true;
}

bool DoLightSphereCulling(
    const float4 receiverBoundingSphereLS,
    const float4 casterBoundingSphereWS,
    const float3x3 worldToLightSpaceRotation
)
{
    const float3 casterCenterLS = mul(worldToLightSpaceRotation, casterBoundingSphereWS.xyz);
    const float casterRadius = casterBoundingSphereWS.w;

    const float3 receiverCenterLS = receiverBoundingSphereLS.xyz;
    const float receiverRadius = receiverBoundingSphereLS.w;
    const float3 receiverToCasterLS = casterCenterLS - receiverCenterLS;

    const float intersectionMaxDistance = casterRadius + receiverRadius;
    const float zSqAtSphereIntersection =
        intersectionMaxDistance * intersectionMaxDistance - dot(receiverToCasterLS.xy, receiverToCasterLS.xy);

    UNITY_FLATTEN
    if (zSqAtSphereIntersection < 0.0f)
    {
        return false;
    }

    UNITY_FLATTEN
    if (receiverToCasterLS.z < 0.0f && receiverToCasterLS.z * receiverToCasterLS.z > zSqAtSphereIntersection)
    {
        return false;
    }

    return true;
}

bool LightSphereCulling(const VividGPUCullingContext cullingContext, const float4 casterBoundingSphereWS)
{
    UNITY_BRANCH
    if (cullingContext.CullingSphereLS.w <= 0.0f)
    {
        return true;
    }

    const float3x3 worldToLightSpaceRotation = (float3x3) cullingContext.ViewMatrix;
    return DoLightSphereCulling(cullingContext.CullingSphereLS, casterBoundingSphereWS, worldToLightSpaceRotation);
}

float2 GetNormalizedScreenCoordinates(const float4x4 viewProjectionMatrix, const float3 positionWS)
{
    float4 clipPosition = mul(viewProjectionMatrix, float4(positionWS, 1.0f));
    clipPosition.xy /= max(clipPosition.w, 1e-5f);
    clipPosition.xy = clipPosition.xy * 0.5f + 0.5f;
    return clipPosition.xy;
}

float GetScreenBoundRadiusSq(const VividGPULODSelectionContext lodSelectionContext, const float4 boundsWS)
{
    const float2 center = GetNormalizedScreenCoordinates(lodSelectionContext.ViewProjectionMatrix, boundsWS.xyz);
    const float2 up = GetNormalizedScreenCoordinates(
        lodSelectionContext.ViewProjectionMatrix,
        boundsWS.xyz + lodSelectionContext.CameraUp.xyz * boundsWS.w
    );
    const float2 right = GetNormalizedScreenCoordinates(
        lodSelectionContext.ViewProjectionMatrix,
        boundsWS.xyz + lodSelectionContext.CameraRight.xyz * boundsWS.w
    );

    const float2 screenUp = (up - center) * lodSelectionContext.ScreenSizePixels;
    const float2 screenRight = (right - center) * lodSelectionContext.ScreenSizePixels;
    return max(dot(screenUp, screenUp), dot(screenRight, screenRight));
}

uint GetRendererListID(const VividInstanceData instanceData, const VividMaterialData materialData)
{
    uint rendererListID = materialData.RendererListID;

    if ((instanceData.Flags & VIVIDINSTANCEFLAGS_FLIP_WINDING_ORDER) != 0u &&
        (rendererListID & VIVIDRENDERERLISTID_CULL_OFF) == 0u)
    {
        if ((rendererListID & VIVIDRENDERERLISTID_CULL_FRONT) != 0u)
        {
            rendererListID &= ~VIVIDRENDERERLISTID_CULL_FRONT;
        }
        else
        {
            rendererListID |= VIVIDRENDERERLISTID_CULL_FRONT;
        }
    }

    return rendererListID;
}

float3 GetViewForwardDir(const float4x4 viewMatrix)
{
    // View forward in view space is (0, 0, -1). Rotating it by the inverse rotation of the view
    // matrix (i.e. transpose of the upper 3x3, since rotations are orthogonal) yields the world-
    // space forward. In HLSL `mul(v, M3x3)` is equivalent to `mul(transpose(M3x3), v)`, so
    // multiplying (0,0,-1) on the left by the upper 3x3 gives -row2.xyz.
    return normalize(-float3(viewMatrix._m20, viewMatrix._m21, viewMatrix._m22));
}

bool ConeCulling(
    const VividGPUCullingContext cullingContext,
    const VividInstanceData instanceData,
    const VividDecodedMeshlet meshlet
)
{
    if (meshlet.ConeValid == 0u)
    {
        return true;
    }

    float3 coneAxisWS = mul(meshlet.ConeAxis, (float3x3) instanceData.WorldToObjectMatrix);
    const float axisLengthSq = LengthSq(coneAxisWS);

    if (axisLengthSq <= 1e-8f)
    {
        return true;
    }

    coneAxisWS *= rsqrt(axisLengthSq);

    float3 viewDirWS;
    float coneCutoff = meshlet.ConeCutoff;
    if (cullingContext.CameraIsPerspective != 0)
    {
        const float4 boundingSphereWS = TransformSphere(meshlet.BoundingSphere, instanceData.ObjectToWorldMatrix);
        const float3 viewVectorWS = boundingSphereWS.xyz - cullingContext.CameraPosition.xyz;
        const float viewDistanceSq = LengthSq(viewVectorWS);
        const float radiusSq = boundingSphereWS.w * boundingSphereWS.w;
        if (viewDistanceSq <= max(radiusSq, 1e-8f))
        {
            return true;
        }

        const float inverseViewDistance = rsqrt(viewDistanceSq);
        viewDirWS = viewVectorWS * inverseViewDistance;
        const float sinViewCone = saturate(boundingSphereWS.w * inverseViewDistance);
        const float cosViewCone = sqrt(max(0.0f, 1.0f - sinViewCone * sinViewCone));
        const float sinNormalCone = sqrt(max(0.0f, 1.0f - coneCutoff * coneCutoff));
        if (coneCutoff >= 0.0f && sinViewCone >= sinNormalCone)
        {
            return true;
        }

        coneCutoff = min(
            1.0f,
            coneCutoff * cosViewCone + sinNormalCone * sinViewCone);
    }
    else
    {
        viewDirWS = GetViewForwardDir(cullingContext.ViewMatrix);
    }

    const float dotResult = dot(viewDirWS, coneAxisWS);
    return !(dotResult >= coneCutoff);
}

bool ShouldSelectMeshLODNode(
    const VividGPULODSelectionContext lodSelectionContext,
    const VividDecodedMeshLODNode meshLODNode,
    const VividInstanceData instanceData,
    const float distanceToViewSq,
    const uint forcedMeshLODNodeDepth,
    const float meshLODErrorThreshold
)
{
    if (forcedMeshLODNodeDepth != VIVID_INVALID_FORCED_MESH_LOD_NODE_DEPTH)
    {
        const bool isLeaf = meshLODNode.LevelIndex == instanceData.MeshLODLevelCount - 1;
        return meshLODNode.LevelIndex == forcedMeshLODNodeDepth ||
               (meshLODNode.LevelIndex < forcedMeshLODNodeDepth && isLeaf);
    }

    const float4 boundsWS = TransformSphere(meshLODNode.Bounds, instanceData.ObjectToWorldMatrix);
    const float4 parentBoundsWS = TransformSphere(meshLODNode.ParentBounds, instanceData.ObjectToWorldMatrix);
    const float error = meshLODNode.Error * GetScreenBoundRadiusSq(lodSelectionContext, boundsWS);
    const float parentError = meshLODNode.ParentError >= 0.0f
        ? meshLODNode.ParentError * GetScreenBoundRadiusSq(lodSelectionContext, parentBoundsWS)
        : 3.402823466e+38f;
    const float threshold = meshLODErrorThreshold * distanceToViewSq * instanceData.LODErrorScale;
    return parentError > threshold && error <= threshold;
}

#endif // VIVIDRP_GPU_DRIVEN_COMMON_INCLUDED
