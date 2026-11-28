#ifndef VIVIDRP_GPU_DRIVEN_COMMON_INCLUDED
#define VIVIDRP_GPU_DRIVEN_COMMON_INCLUDED

#include "Packages/com.vivid.render-pipelines/Shaders/Core/Public/Input.hlsl"
#include "Packages/com.vivid.render-pipelines/Runtime/SubSystem/GPUDriven/VividGPUDrivenStructs.cs.hlsl"
#include "Packages/com.vivid.render-pipelines/Runtime/SubSystem/PrimitiveScene/VividPrimitiveSceneTypes.cs.hlsl"

#define VIVID_MATERIAL_PROGRAM_LEGACY_FALLBACK 0u
#define VIVID_MATERIAL_PROGRAM_KNOWN 1u
#define VIVID_MATERIAL_PROGRAM_KNOWN_FAILURE 2u
#define VIVID_MAX_MESHLET_INDICES MAX_MESHLET_INDICES
#define VIVID_INVALID_FORCED_MESH_LOD_NODE_DEPTH 0xffffffffu
#include "Packages/com.vivid.render-pipelines/Shaders/Core/Public/GPUDriven/VividMeshletDecode.hlsli"
#include "Packages/com.vivid.render-pipelines/Shaders/Core/Public/GPUDriven/VividMeshLODError.hlsli"

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
    uint StreamingLeafMask; // one bit per record in this <=32-record job
};

StructuredBuffer<VividInstanceData> _InstanceData;
uint _InstanceDataCount;

StructuredBuffer<VividPrimitiveData> _VividPrimitiveData;
StructuredBuffer<VividPrimitiveTransformData> _VividPrimitiveTransformData;
StructuredBuffer<VividPrimitiveDrawSectionData> _VividPrimitiveDrawSectionData;
StructuredBuffer<VividPrimitiveGeometryData> _VividPrimitiveGeometryData;
StructuredBuffer<VividPrimitiveMaterialData> _VividPrimitiveMaterialData;
uint _VividPrimitiveCount;
uint _VividPrimitiveDrawSectionCount;
uint _VividPrimitiveGeometryCount;
uint _VividPrimitiveMaterialCount;
uint _VividPrimitiveSceneDirectEnabled;

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
StructuredBuffer<VividMeshLODNodePacked> _MeshLODNodes;
StructuredBuffer<VividMeshLODGroupPacked> _MeshLODGroups;
StructuredBuffer<VividMeshLODGroupOverflow> _MeshLODGroupOverflow;
StructuredBuffer<VividMeshlet> _Meshlets;
uint _MeshLODNodeCount;
uint _MeshletCount;

uint ExtractPrimitiveInstanceFlags(const uint primitiveFlags)
{
    uint instanceFlags = 0u;
    if ((primitiveFlags & VIVIDPRIMITIVEFLAGS_DISABLED) != 0u)
        instanceFlags |= (uint) VIVIDINSTANCEFLAGS_DISABLED;
    if ((primitiveFlags & VIVIDPRIMITIVEFLAGS_FLIP_WINDING_ORDER) != 0u)
        instanceFlags |= (uint) VIVIDINSTANCEFLAGS_FLIP_WINDING_ORDER;
    if ((primitiveFlags & VIVIDPRIMITIVEFLAGS_TWO_SIDED_SHADOWS) != 0u)
        instanceFlags |= (uint) VIVIDINSTANCEFLAGS_TWO_SIDED_SHADOWS;
    return instanceFlags;
}

VividInstanceData PullPrimitiveSceneInstanceData(const uint drawSectionIndex)
{
    VividInstanceData result = (VividInstanceData) 0;
    result.Flags = VIVIDINSTANCEFLAGS_DISABLED;
    if (drawSectionIndex >= _VividPrimitiveDrawSectionCount)
        return result;

    const VividPrimitiveDrawSectionData section = _VividPrimitiveDrawSectionData[drawSectionIndex];
    if ((section.Flags & VIVIDPRIMITIVEDRAWSECTIONFLAGS_VALID) == 0u
        || section.PrimitiveIndex >= _VividPrimitiveCount
        || section.GeometryIndex >= _VividPrimitiveGeometryCount
        || section.MaterialIndex >= _VividPrimitiveMaterialCount)
    {
        return result;
    }

    const VividPrimitiveData primitive = _VividPrimitiveData[section.PrimitiveIndex];
    const VividPrimitiveGeometryData geometry = _VividPrimitiveGeometryData[section.GeometryIndex];
    const VividPrimitiveMaterialData material = _VividPrimitiveMaterialData[section.MaterialIndex];
    if (primitive.Generation != section.PrimitiveGeneration
        || geometry.Generation != section.GeometryGeneration
        || material.Generation != section.MaterialGeneration
        || (primitive.Flags & VIVIDPRIMITIVEFLAGS_VALID) == 0u
        || material.LegacyMaterialIndex == 0xffffffffu
        || geometry.LegacyTopMeshLODStartIndex == 0xffffffffu)
    {
        return result;
    }

    const VividPrimitiveTransformData transformData = _VividPrimitiveTransformData[primitive.TransformIndex];
    result.ObjectToWorldMatrix = transformData.ObjectToWorldMatrix;
    result.WorldToObjectMatrix = transformData.WorldToObjectMatrix;
    result.AABBMin = float4(section.LocalBoundsMin.xyz, 0.0f);
    result.AABBMax = section.LocalBoundsMax;
    result.TopMeshLODStartIndex = geometry.LegacyTopMeshLODStartIndex;
    result.TotalMeshLODCount = geometry.LegacyTotalMeshLODCount;
    result.MaterialIndex = material.LegacyMaterialIndex;
    result.MeshLODLevelCount = geometry.LegacyMeshLODLevelCount;
    result.LODErrorScale = section.LocalBoundsMin.w;
    result.PassMask = (int) primitive.PassMask;
    result.Flags = (int) ExtractPrimitiveInstanceFlags(primitive.Flags);
    result.ShaderUserValue = primitive.ShaderUserValue;
    return result;
}

VividInstanceData PullInstanceData(const uint instanceIndex)
{
    if (_VividPrimitiveSceneDirectEnabled != 0u)
        return PullPrimitiveSceneInstanceData(instanceIndex);

    return _InstanceData[instanceIndex];
}

// Phase 3 SoA hot-path pull helpers: read only the tables and fields required by each GPU stage
// while preserving full fallback compatibility with legacy _InstanceData.

uint PullInstanceMaterialIndex(const uint instanceIndex)
{
    if (_VividPrimitiveSceneDirectEnabled != 0u)
    {
        if (instanceIndex >= _VividPrimitiveDrawSectionCount)
            return 0xffffffffu;

        const VividPrimitiveDrawSectionData section = _VividPrimitiveDrawSectionData[instanceIndex];
        if ((section.Flags & VIVIDPRIMITIVEDRAWSECTIONFLAGS_VALID) == 0u
            || section.MaterialIndex >= _VividPrimitiveMaterialCount)
        {
            return 0xffffffffu;
        }

        const VividPrimitiveMaterialData material = _VividPrimitiveMaterialData[section.MaterialIndex];
        return material.Generation == section.MaterialGeneration
            ? material.LegacyMaterialIndex
            : 0xffffffffu;
    }

    return _InstanceData[instanceIndex].MaterialIndex;
}

struct VividInstanceCullHeader
{
    float4x4 ObjectToWorldMatrix;
    float3 AABBMinOS;
    float LODErrorScale;
    float3 AABBMaxOS;
    float WpoPaddingWS;
    uint GeometryIndex;
    uint GeometryGeneration;
    uint MaterialIndex;
    uint MaterialGeneration;
};

struct VividInstanceLODTraversalData
{
    float4x4 ObjectToWorldMatrix;
    float LODErrorScale;
    float WpoPaddingWS;
    uint TopMeshLODStartIndex;
    uint TotalMeshLODCount;
    uint MeshLODLevelCount;
};

bool TryPullInstanceCullHeader(
    const uint instanceIndex,
    const uint requiredPassMask,
    out VividInstanceCullHeader header)
{
    header = (VividInstanceCullHeader) 0;
    if (_VividPrimitiveSceneDirectEnabled != 0u)
    {
        if (instanceIndex >= _VividPrimitiveDrawSectionCount)
            return false;

        const VividPrimitiveDrawSectionData section = _VividPrimitiveDrawSectionData[instanceIndex];
        if ((section.Flags & VIVIDPRIMITIVEDRAWSECTIONFLAGS_VALID) == 0u
            || section.PrimitiveIndex >= _VividPrimitiveCount
            || section.GeometryIndex >= _VividPrimitiveGeometryCount
            || section.MaterialIndex >= _VividPrimitiveMaterialCount)
        {
            return false;
        }

        const VividPrimitiveData primitive = _VividPrimitiveData[section.PrimitiveIndex];
        if (primitive.Generation != section.PrimitiveGeneration
            || (primitive.Flags & VIVIDPRIMITIVEFLAGS_VALID) == 0u
            || (primitive.Flags & VIVIDPRIMITIVEFLAGS_DISABLED) != 0u
            || (primitive.PassMask & requiredPassMask) == 0u)
        {
            return false;
        }

        header.ObjectToWorldMatrix = _VividPrimitiveTransformData[primitive.TransformIndex].ObjectToWorldMatrix;
        header.AABBMinOS = section.LocalBoundsMin.xyz;
        header.LODErrorScale = section.LocalBoundsMin.w;
        header.AABBMaxOS = section.LocalBoundsMax.xyz;
        header.WpoPaddingWS = max(section.LocalBoundsMax.w, 0.0f);
        header.GeometryIndex = section.GeometryIndex;
        header.GeometryGeneration = section.GeometryGeneration;
        header.MaterialIndex = section.MaterialIndex;
        header.MaterialGeneration = section.MaterialGeneration;
        return true;
    }

    const VividInstanceData instanceData = _InstanceData[instanceIndex];
    if ((instanceData.Flags & VIVIDINSTANCEFLAGS_DISABLED) != 0u
        || ((uint) instanceData.PassMask & requiredPassMask) == 0u)
    {
        return false;
    }

    header.ObjectToWorldMatrix = instanceData.ObjectToWorldMatrix;
    header.AABBMinOS = instanceData.AABBMin.xyz;
    header.LODErrorScale = instanceData.LODErrorScale;
    header.AABBMaxOS = instanceData.AABBMax.xyz;
    header.WpoPaddingWS = max(instanceData.AABBMax.w, 0.0f);
    header.GeometryIndex = instanceData.TopMeshLODStartIndex;
    header.GeometryGeneration = instanceData.TotalMeshLODCount;
    header.MaterialIndex = instanceData.MeshLODLevelCount;
    header.MaterialGeneration = 0u;
    return true;
}

bool TryPullInstanceLODTraversalFromCullHeader(
    const VividInstanceCullHeader header,
    out VividInstanceLODTraversalData lodData)
{
    lodData = (VividInstanceLODTraversalData) 0;
    lodData.ObjectToWorldMatrix = header.ObjectToWorldMatrix;
    lodData.LODErrorScale = header.LODErrorScale;
    lodData.WpoPaddingWS = header.WpoPaddingWS;

    if (_VividPrimitiveSceneDirectEnabled != 0u)
    {
        const VividPrimitiveGeometryData geometry = _VividPrimitiveGeometryData[header.GeometryIndex];
        const VividPrimitiveMaterialData material = _VividPrimitiveMaterialData[header.MaterialIndex];
        if (geometry.Generation != header.GeometryGeneration
            || material.Generation != header.MaterialGeneration
            || material.LegacyMaterialIndex == 0xffffffffu
            || geometry.LegacyTopMeshLODStartIndex == 0xffffffffu
            || geometry.LegacyTotalMeshLODCount == 0u)
        {
            return false;
        }

        lodData.TopMeshLODStartIndex = geometry.LegacyTopMeshLODStartIndex;
        lodData.TotalMeshLODCount = geometry.LegacyTotalMeshLODCount;
        lodData.MeshLODLevelCount = geometry.LegacyMeshLODLevelCount;
        return true;
    }

    if (header.GeometryGeneration == 0u)
        return false;

    lodData.TopMeshLODStartIndex = header.GeometryIndex;
    lodData.TotalMeshLODCount = header.GeometryGeneration;
    lodData.MeshLODLevelCount = header.MaterialIndex;
    return true;
}

bool TryPullInstanceLODTraversalData(
    const uint instanceIndex,
    out VividInstanceLODTraversalData lodData)
{
    lodData = (VividInstanceLODTraversalData) 0;
    if (_VividPrimitiveSceneDirectEnabled != 0u)
    {
        if (instanceIndex >= _VividPrimitiveDrawSectionCount)
            return false;

        const VividPrimitiveDrawSectionData section = _VividPrimitiveDrawSectionData[instanceIndex];
        if ((section.Flags & VIVIDPRIMITIVEDRAWSECTIONFLAGS_VALID) == 0u
            || section.PrimitiveIndex >= _VividPrimitiveCount
            || section.GeometryIndex >= _VividPrimitiveGeometryCount)
        {
            return false;
        }

        const VividPrimitiveData primitive = _VividPrimitiveData[section.PrimitiveIndex];
        const VividPrimitiveGeometryData geometry = _VividPrimitiveGeometryData[section.GeometryIndex];
        if (primitive.Generation != section.PrimitiveGeneration
            || geometry.Generation != section.GeometryGeneration
            || (primitive.Flags & VIVIDPRIMITIVEFLAGS_VALID) == 0u
            || geometry.LegacyTopMeshLODStartIndex == 0xffffffffu
            || geometry.LegacyTotalMeshLODCount == 0u)
        {
            return false;
        }

        lodData.ObjectToWorldMatrix = _VividPrimitiveTransformData[primitive.TransformIndex].ObjectToWorldMatrix;
        lodData.LODErrorScale = section.LocalBoundsMin.w;
        lodData.WpoPaddingWS = max(section.LocalBoundsMax.w, 0.0f);
        lodData.TopMeshLODStartIndex = geometry.LegacyTopMeshLODStartIndex;
        lodData.TotalMeshLODCount = geometry.LegacyTotalMeshLODCount;
        lodData.MeshLODLevelCount = geometry.LegacyMeshLODLevelCount;
        return true;
    }

    const VividInstanceData instanceData = _InstanceData[instanceIndex];
    if (instanceData.TotalMeshLODCount == 0u)
        return false;

    lodData.ObjectToWorldMatrix = instanceData.ObjectToWorldMatrix;
    lodData.LODErrorScale = instanceData.LODErrorScale;
    lodData.WpoPaddingWS = max(instanceData.AABBMax.w, 0.0f);
    lodData.TopMeshLODStartIndex = instanceData.TopMeshLODStartIndex;
    lodData.TotalMeshLODCount = instanceData.TotalMeshLODCount;
    lodData.MeshLODLevelCount = instanceData.MeshLODLevelCount;
    return true;
}

struct VividInstanceLODBuildData
{
    float4x4 ObjectToWorldMatrix;
    float LODErrorScale;
    float WpoPaddingWS;
    uint TopMeshLODStartIndex;
    uint MeshLODLevelCount;
    uint BaseRendererListID;
    uint InstanceFlags;
};

VividInstanceLODBuildData PullInstanceLODBuildData(const uint instanceIndex)
{
    VividInstanceLODBuildData result = (VividInstanceLODBuildData) 0;
    if (_VividPrimitiveSceneDirectEnabled != 0u)
    {
        if (instanceIndex >= _VividPrimitiveDrawSectionCount)
            return result;

        const VividPrimitiveDrawSectionData section = _VividPrimitiveDrawSectionData[instanceIndex];
        if ((section.Flags & VIVIDPRIMITIVEDRAWSECTIONFLAGS_VALID) == 0u
            || section.PrimitiveIndex >= _VividPrimitiveCount
            || section.GeometryIndex >= _VividPrimitiveGeometryCount
            || section.MaterialIndex >= _VividPrimitiveMaterialCount)
        {
            return result;
        }

        const VividPrimitiveData primitive = _VividPrimitiveData[section.PrimitiveIndex];
        const VividPrimitiveGeometryData geometry = _VividPrimitiveGeometryData[section.GeometryIndex];
        const VividPrimitiveMaterialData material = _VividPrimitiveMaterialData[section.MaterialIndex];
        if (primitive.Generation != section.PrimitiveGeneration
            || geometry.Generation != section.GeometryGeneration
            || material.Generation != section.MaterialGeneration
            || (primitive.Flags & VIVIDPRIMITIVEFLAGS_VALID) == 0u
            || material.LegacyMaterialIndex == 0xffffffffu
            || geometry.LegacyTopMeshLODStartIndex == 0xffffffffu)
        {
            return result;
        }

        result.ObjectToWorldMatrix = _VividPrimitiveTransformData[primitive.TransformIndex].ObjectToWorldMatrix;
        result.LODErrorScale = section.LocalBoundsMin.w;
        result.WpoPaddingWS = max(section.LocalBoundsMax.w, 0.0f);
        result.TopMeshLODStartIndex = geometry.LegacyTopMeshLODStartIndex;
        result.MeshLODLevelCount = geometry.LegacyMeshLODLevelCount;
        result.BaseRendererListID = (uint) material.RendererListID;
        result.InstanceFlags = ExtractPrimitiveInstanceFlags(primitive.Flags);
        return result;
    }

    const VividInstanceData instanceData = _InstanceData[instanceIndex];
    const VividMaterialData materialData = _MaterialData[instanceData.MaterialIndex];
    result.ObjectToWorldMatrix = instanceData.ObjectToWorldMatrix;
    result.LODErrorScale = instanceData.LODErrorScale;
    result.WpoPaddingWS = max(instanceData.AABBMax.w, 0.0f);
    result.TopMeshLODStartIndex = instanceData.TopMeshLODStartIndex;
    result.MeshLODLevelCount = instanceData.MeshLODLevelCount;
    result.BaseRendererListID = (uint) materialData.RendererListID;
    result.InstanceFlags = (uint) instanceData.Flags;
    return result;
}

struct VividInstanceMeshletCullData
{
    float4x4 ObjectToWorldMatrix;
    float WpoPaddingWS;
    uint BaseRendererListID;
    uint InstanceFlags;
    uint TransformIndex;
};

VividInstanceMeshletCullData PullInstanceMeshletCullData(const uint instanceIndex)
{
    VividInstanceMeshletCullData result = (VividInstanceMeshletCullData) 0;
    if (_VividPrimitiveSceneDirectEnabled != 0u)
    {
        if (instanceIndex >= _VividPrimitiveDrawSectionCount)
            return result;

        const VividPrimitiveDrawSectionData section = _VividPrimitiveDrawSectionData[instanceIndex];
        if ((section.Flags & VIVIDPRIMITIVEDRAWSECTIONFLAGS_VALID) == 0u
            || section.PrimitiveIndex >= _VividPrimitiveCount
            || section.MaterialIndex >= _VividPrimitiveMaterialCount)
        {
            return result;
        }

        const VividPrimitiveData primitive = _VividPrimitiveData[section.PrimitiveIndex];
        const VividPrimitiveMaterialData material = _VividPrimitiveMaterialData[section.MaterialIndex];
        if (primitive.Generation != section.PrimitiveGeneration
            || material.Generation != section.MaterialGeneration
            || (primitive.Flags & VIVIDPRIMITIVEFLAGS_VALID) == 0u
            || material.LegacyMaterialIndex == 0xffffffffu)
        {
            return result;
        }

        result.ObjectToWorldMatrix = _VividPrimitiveTransformData[primitive.TransformIndex].ObjectToWorldMatrix;
        result.WpoPaddingWS = max(section.LocalBoundsMax.w, 0.0f);
        result.BaseRendererListID = (uint) material.RendererListID;
        result.InstanceFlags = ExtractPrimitiveInstanceFlags(primitive.Flags);
        result.TransformIndex = primitive.TransformIndex;
        return result;
    }

    const VividInstanceData instanceData = _InstanceData[instanceIndex];
    const VividMaterialData materialData = _MaterialData[instanceData.MaterialIndex];
    result.ObjectToWorldMatrix = instanceData.ObjectToWorldMatrix;
    result.WpoPaddingWS = max(instanceData.AABBMax.w, 0.0f);
    result.BaseRendererListID = (uint) materialData.RendererListID;
    result.InstanceFlags = (uint) instanceData.Flags;
    result.TransformIndex = instanceIndex;
    return result;
}

float4x4 PullInstanceWorldToObjectMatrix(const uint instanceIndex, const uint transformIndex)
{
    if (_VividPrimitiveSceneDirectEnabled != 0u)
        return _VividPrimitiveTransformData[transformIndex].WorldToObjectMatrix;

    return _InstanceData[instanceIndex].WorldToObjectMatrix;
}

bool TryPullInstanceBoundsTransform(
    const uint instanceIndex,
    out float4x4 objectToWorldMatrix,
    out float wpoPaddingWS)
{
    objectToWorldMatrix = (float4x4) 0;
    wpoPaddingWS = 0.0f;
    if (_VividPrimitiveSceneDirectEnabled != 0u)
    {
        if (instanceIndex >= _VividPrimitiveDrawSectionCount)
            return false;

        const VividPrimitiveDrawSectionData section = _VividPrimitiveDrawSectionData[instanceIndex];
        if ((section.Flags & VIVIDPRIMITIVEDRAWSECTIONFLAGS_VALID) == 0u
            || section.PrimitiveIndex >= _VividPrimitiveCount)
        {
            return false;
        }

        const VividPrimitiveData primitive = _VividPrimitiveData[section.PrimitiveIndex];
        if (primitive.Generation != section.PrimitiveGeneration
            || (primitive.Flags & VIVIDPRIMITIVEFLAGS_VALID) == 0u)
        {
            return false;
        }

        objectToWorldMatrix = _VividPrimitiveTransformData[primitive.TransformIndex].ObjectToWorldMatrix;
        wpoPaddingWS = max(section.LocalBoundsMax.w, 0.0f);
        return true;
    }

    const VividInstanceData instanceData = _InstanceData[instanceIndex];
    objectToWorldMatrix = instanceData.ObjectToWorldMatrix;
    wpoPaddingWS = max(instanceData.AABBMax.w, 0.0f);
    return true;
}

bool TryPullInstanceShadowRasterData(
    const uint instanceIndex,
    out float4x4 objectToWorldMatrix,
    out uint materialIndex)
{
    objectToWorldMatrix = (float4x4) 0;
    materialIndex = 0xffffffffu;
    if (_VividPrimitiveSceneDirectEnabled != 0u)
    {
        if (instanceIndex >= _VividPrimitiveDrawSectionCount)
            return false;

        const VividPrimitiveDrawSectionData section = _VividPrimitiveDrawSectionData[instanceIndex];
        if ((section.Flags & VIVIDPRIMITIVEDRAWSECTIONFLAGS_VALID) == 0u
            || section.PrimitiveIndex >= _VividPrimitiveCount
            || section.MaterialIndex >= _VividPrimitiveMaterialCount)
        {
            return false;
        }

        const VividPrimitiveData primitive = _VividPrimitiveData[section.PrimitiveIndex];
        const VividPrimitiveMaterialData material = _VividPrimitiveMaterialData[section.MaterialIndex];
        if (primitive.Generation != section.PrimitiveGeneration
            || material.Generation != section.MaterialGeneration
            || (primitive.Flags & VIVIDPRIMITIVEFLAGS_VALID) == 0u
            || material.LegacyMaterialIndex == 0xffffffffu)
        {
            return false;
        }

        objectToWorldMatrix = _VividPrimitiveTransformData[primitive.TransformIndex].ObjectToWorldMatrix;
        materialIndex = material.LegacyMaterialIndex;
        return true;
    }

    const VividInstanceData instanceData = _InstanceData[instanceIndex];
    objectToWorldMatrix = instanceData.ObjectToWorldMatrix;
    materialIndex = instanceData.MaterialIndex;
    return true;
}

bool TryPullInstanceMainRasterData(
    const uint instanceIndex,
    out float4x4 objectToWorldMatrix,
    out float4x4 worldToObjectMatrix,
    out uint materialIndex)
{
    objectToWorldMatrix = (float4x4) 0;
    worldToObjectMatrix = (float4x4) 0;
    materialIndex = 0xffffffffu;
    if (_VividPrimitiveSceneDirectEnabled != 0u)
    {
        if (instanceIndex >= _VividPrimitiveDrawSectionCount)
            return false;

        const VividPrimitiveDrawSectionData section = _VividPrimitiveDrawSectionData[instanceIndex];
        if ((section.Flags & VIVIDPRIMITIVEDRAWSECTIONFLAGS_VALID) == 0u
            || section.PrimitiveIndex >= _VividPrimitiveCount
            || section.MaterialIndex >= _VividPrimitiveMaterialCount)
        {
            return false;
        }

        const VividPrimitiveData primitive = _VividPrimitiveData[section.PrimitiveIndex];
        const VividPrimitiveMaterialData material = _VividPrimitiveMaterialData[section.MaterialIndex];
        if (primitive.Generation != section.PrimitiveGeneration
            || material.Generation != section.MaterialGeneration
            || (primitive.Flags & VIVIDPRIMITIVEFLAGS_VALID) == 0u
            || material.LegacyMaterialIndex == 0xffffffffu)
        {
            return false;
        }

        const VividPrimitiveTransformData transformData = _VividPrimitiveTransformData[primitive.TransformIndex];
        objectToWorldMatrix = transformData.ObjectToWorldMatrix;
        worldToObjectMatrix = transformData.WorldToObjectMatrix;
        materialIndex = material.LegacyMaterialIndex;
        return true;
    }

    const VividInstanceData instanceData = _InstanceData[instanceIndex];
    objectToWorldMatrix = instanceData.ObjectToWorldMatrix;
    worldToObjectMatrix = instanceData.WorldToObjectMatrix;
    materialIndex = instanceData.MaterialIndex;
    return true;
}

struct VividInstanceResolveData
{
    float4x4 ObjectToWorldMatrix;
    float4x4 WorldToObjectMatrix;
    uint BaseRendererListID;
    uint InstanceFlags;
};

bool TryPullInstanceResolveData(
    const uint instanceIndex,
    out VividInstanceResolveData data)
{
    data = (VividInstanceResolveData) 0;
    if (_VividPrimitiveSceneDirectEnabled != 0u)
    {
        if (instanceIndex >= _VividPrimitiveDrawSectionCount)
            return false;

        const VividPrimitiveDrawSectionData section = _VividPrimitiveDrawSectionData[instanceIndex];
        if ((section.Flags & VIVIDPRIMITIVEDRAWSECTIONFLAGS_VALID) == 0u
            || section.PrimitiveIndex >= _VividPrimitiveCount
            || section.MaterialIndex >= _VividPrimitiveMaterialCount)
        {
            return false;
        }

        const VividPrimitiveData primitive = _VividPrimitiveData[section.PrimitiveIndex];
        const VividPrimitiveMaterialData material = _VividPrimitiveMaterialData[section.MaterialIndex];
        if (primitive.Generation != section.PrimitiveGeneration
            || material.Generation != section.MaterialGeneration
            || (primitive.Flags & VIVIDPRIMITIVEFLAGS_VALID) == 0u
            || material.LegacyMaterialIndex == 0xffffffffu)
        {
            return false;
        }

        const VividPrimitiveTransformData transformData = _VividPrimitiveTransformData[primitive.TransformIndex];
        data.ObjectToWorldMatrix = transformData.ObjectToWorldMatrix;
        data.WorldToObjectMatrix = transformData.WorldToObjectMatrix;
        data.BaseRendererListID = (uint) material.RendererListID;
        data.InstanceFlags = ExtractPrimitiveInstanceFlags(primitive.Flags);
        return true;
    }

    const VividInstanceData instanceData = _InstanceData[instanceIndex];
    const VividMaterialData materialData = _MaterialData[instanceData.MaterialIndex];
    data.ObjectToWorldMatrix = instanceData.ObjectToWorldMatrix;
    data.WorldToObjectMatrix = instanceData.WorldToObjectMatrix;
    data.BaseRendererListID = (uint) materialData.RendererListID;
    data.InstanceFlags = (uint) instanceData.Flags;
    return true;
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

VividMeshLODGroup PullMeshLODGroup(const uint groupIndex)
{
    const VividMeshLODGroupPacked packed = _MeshLODGroups[groupIndex];
    float2 values;
    if ((packed.RadiusError & 0x80000000u) != 0u)
        values = _MeshLODGroupOverflow[packed.RadiusError & 0x7fffffffu].RadiusError;
    else
        values = float2(f16tof32(packed.RadiusError & 0xffffu), f16tof32(packed.RadiusError >> 16u));
    VividMeshLODGroup group;
    group.Bounds = float4(packed.CenterX, packed.CenterY, packed.CenterZ, values.x);
    group.Error = values.y;
    return group;
}

VividDecodedMeshLODNode PullMeshLODNode(const uint nodeIndex)
{
    const VividMeshLODNodePacked node = _MeshLODNodes[nodeIndex];
    const VividMeshLODGroup own = PullMeshLODGroup(node.GroupIndex);
    VividMeshLODGroup parent = (VividMeshLODGroup)0;
    if (node.ParentGroupIndex != 0xffffffffu)
        parent = PullMeshLODGroup(node.ParentGroupIndex);
    return DecodeVividMeshLODNode(node, own, parent);
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

float VividGetInstanceWpoPaddingWS(const VividInstanceData instanceData)
{
    return max(instanceData.AABBMax.w, 0.0f);
}

float4 ComputeInstanceBoundingSphereWS(const VividInstanceData instanceData)
{
    const float3 aabbMinOS = instanceData.AABBMin.xyz;
    const float3 aabbMaxOS = instanceData.AABBMax.xyz;
    const float3 centerOS = (aabbMinOS + aabbMaxOS) * 0.5f;
    const float radiusOS = length(aabbMaxOS - centerOS);
    float4 sphereWS = TransformSphere(float4(centerOS, radiusOS), instanceData.ObjectToWorldMatrix);
    sphereWS.w += VividGetInstanceWpoPaddingWS(instanceData);
    return sphereWS;
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

uint ResolveRendererListID(
    const uint baseRendererListID,
    const uint instanceFlags,
    const uint passMask)
{
    uint rendererListID = baseRendererListID;

    if ((passMask & VIVIDINSTANCEPASSMASK_SHADOWS) != 0u &&
        (instanceFlags & VIVIDINSTANCEFLAGS_TWO_SIDED_SHADOWS) != 0u)
    {
        rendererListID &= ~VIVIDRENDERERLISTID_CULL_FRONT;
        rendererListID |= VIVIDRENDERERLISTID_CULL_OFF;
    }
    else if ((instanceFlags & VIVIDINSTANCEFLAGS_FLIP_WINDING_ORDER) != 0u &&
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

uint GetRendererListID(
    const VividInstanceData instanceData,
    const VividMaterialData materialData,
    const uint passMask)
{
    return ResolveRendererListID(
        (uint) materialData.RendererListID,
        (uint) instanceData.Flags,
        passMask);
}

uint GetRendererListID(const VividInstanceData instanceData, const VividMaterialData materialData)
{
    return GetRendererListID(instanceData, materialData, VIVIDINSTANCEPASSMASK_MAIN);
}

float3 GetViewForwardDir(const float4x4 viewMatrix)
{
    // View forward in view space is (0, 0, -1). Rotating it by the inverse rotation of the view
    // matrix (i.e. transpose of the upper 3x3, since rotations are orthogonal) yields the world-
    // space forward. In HLSL `mul(v, M3x3)` is equivalent to `mul(transpose(M3x3), v)`, so
    // multiplying (0,0,-1) on the left by the upper 3x3 gives -row2.xyz.
    return normalize(-float3(viewMatrix._m20, viewMatrix._m21, viewMatrix._m22));
}

bool ShouldSelectMeshLODNode(
    const VividGPULODSelectionContext lodSelectionContext,
    const VividDecodedMeshLODNode meshLODNode,
    const float4x4 objectToWorldMatrix,
    const uint meshLODLevelCount,
    const float lodErrorScale,
    const uint forcedMeshLODNodeDepth,
    const float meshLODErrorThreshold,
    const bool streamingLeaf
)
{
    if (forcedMeshLODNodeDepth != VIVID_INVALID_FORCED_MESH_LOD_NODE_DEPTH)
    {
        const bool isLeaf = streamingLeaf || meshLODNode.LevelIndex == meshLODLevelCount - 1;
        return meshLODNode.LevelIndex == forcedMeshLODNodeDepth ||
               (meshLODNode.LevelIndex < forcedMeshLODNodeDepth && isLeaf);
    }

    const float4x4 localToClip = mul(lodSelectionContext.ViewProjectionMatrix, objectToWorldMatrix);
    const float budget = meshLODErrorThreshold * lodErrorScale;
    // Parent and own tests use bit-identical shared group data and complementary
    // comparisons. Equality selects the coarse side of the cut exactly once.
    const bool parentRefines = meshLODNode.ParentError < 0.0f || VividLODProjectedError(
        meshLODNode.ParentBounds, meshLODNode.ParentError, localToClip, lodSelectionContext.ScreenSizePixels) > budget;
    const bool isLeaf = streamingLeaf || meshLODNode.LevelIndex == meshLODLevelCount - 1;
    const bool ownRefines = !isLeaf && VividLODProjectedError(
        meshLODNode.Bounds, meshLODNode.Error, localToClip, lodSelectionContext.ScreenSizePixels) > budget;
    return parentRefines && !ownRefines;
}

bool ShouldSelectMeshLODNode(
    const VividGPULODSelectionContext lodSelectionContext,
    const VividDecodedMeshLODNode meshLODNode,
    const VividInstanceData instanceData,
    const uint forcedMeshLODNodeDepth,
    const float meshLODErrorThreshold,
    const bool streamingLeaf
)
{
    return ShouldSelectMeshLODNode(
        lodSelectionContext,
        meshLODNode,
        instanceData.ObjectToWorldMatrix,
        instanceData.MeshLODLevelCount,
        instanceData.LODErrorScale,
        forcedMeshLODNodeDepth,
        meshLODErrorThreshold,
        streamingLeaf);
}

bool ShouldSelectMeshLODNode(const VividGPULODSelectionContext context,
    const VividDecodedMeshLODNode node, const VividInstanceData instance, uint forcedDepth, float budget)
{
    return ShouldSelectMeshLODNode(context, node, instance, forcedDepth, budget, false);
}

bool ShouldSelectShadowMeshLODNode(
    const VividDecodedMeshLODNode node,
    const uint meshLODLevelCount,
    const float lodErrorScale,
    float pixelsPerObjectUnit,
    uint forcedDepth,
    float pixelErrorBudget,
    bool streamingLeaf)
{
    if (forcedDepth != VIVID_INVALID_FORCED_MESH_LOD_NODE_DEPTH)
    {
        bool leaf = streamingLeaf || node.LevelIndex == meshLODLevelCount - 1;
        return node.LevelIndex == forcedDepth || (node.LevelIndex < forcedDepth && leaf);
    }
    float budget = pixelErrorBudget * lodErrorScale;
    bool parentRefines = node.ParentError < 0.0f || node.ParentError * pixelsPerObjectUnit > budget;
    bool ownRefines = !streamingLeaf && node.LevelIndex != meshLODLevelCount - 1
        && node.Error * pixelsPerObjectUnit > budget;
    return parentRefines && !ownRefines;
}

bool ShouldSelectShadowMeshLODNode(
    const VividDecodedMeshLODNode node, const VividInstanceData instance,
    float pixelsPerObjectUnit, uint forcedDepth, float pixelErrorBudget, bool streamingLeaf)
{
    return ShouldSelectShadowMeshLODNode(
        node,
        instance.MeshLODLevelCount,
        instance.LODErrorScale,
        pixelsPerObjectUnit,
        forcedDepth,
        pixelErrorBudget,
        streamingLeaf);
}

bool ShouldSelectShadowMeshLODNode(const VividDecodedMeshLODNode node, const VividInstanceData instance,
    float pixelsPerObjectUnit, uint forcedDepth, float budget)
{
    return ShouldSelectShadowMeshLODNode(node, instance, pixelsPerObjectUnit, forcedDepth, budget, false);
}

#endif // VIVIDRP_GPU_DRIVEN_COMMON_INCLUDED
