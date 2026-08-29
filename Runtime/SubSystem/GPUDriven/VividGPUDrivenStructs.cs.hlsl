//
// This file was automatically generated. Please don't edit by hand. Execute Editor command [ Edit > Rendering > Generate Shader Includes ] instead
//

#ifndef VIVIDGPUDRIVENSTRUCTS_CS_HLSL
#define VIVIDGPUDRIVENSTRUCTS_CS_HLSL
//
// VividRP.Runtime.GPUDriven.VividDualSlabOperator:  static fields
//
#define VIVIDDUALSLABOPERATOR_HORIZONTAL_MIX (0)
#define VIVIDDUALSLABOPERATOR_VERTICAL_LAYER (1)

//
// VividRP.Runtime.GPUDriven.VividGeometryFlags:  static fields
//
#define VIVIDGEOMETRYFLAGS_NONE (0)
#define VIVIDGEOMETRYFLAGS_SPECULAR_AA (1)

//
// VividRP.Runtime.GPUDriven.VividInstanceFlags:  static fields
//
#define VIVIDINSTANCEFLAGS_NONE (0)
#define VIVIDINSTANCEFLAGS_DISABLED (1)
#define VIVIDINSTANCEFLAGS_FLIP_WINDING_ORDER (2)
#define VIVIDINSTANCEFLAGS_TWO_SIDED_SHADOWS (4)

//
// VividRP.Runtime.GPUDriven.VividInstancePassMask:  static fields
//
#define VIVIDINSTANCEPASSMASK_NONE (0)
#define VIVIDINSTANCEPASSMASK_MAIN (1)
#define VIVIDINSTANCEPASSMASK_SHADOWS (2)

//
// VividRP.Runtime.GPUDriven.VividMaterialCoverageProgramID:  static fields
//
#define VIVIDMATERIALCOVERAGEPROGRAMID_BASE_COLOR_ALPHA (0)

//
// VividRP.Runtime.GPUDriven.VividMaterialExecutionClass:  static fields
//
#define VIVIDMATERIALEXECUTIONCLASS_VISIBILITY_DEFERRED (0)

//
// VividRP.Runtime.GPUDriven.VividMaterialFlags:  static fields
//
#define VIVIDMATERIALFLAGS_NONE (0)
#define VIVIDMATERIALFLAGS_UNLIT (1)
#define VIVIDMATERIALFLAGS_TERRAIN (2)
#define VIVIDMATERIALFLAGS_TERRAIN_RUNTIME_VIRTUAL_TEXTURE (4)

//
// VividRP.Runtime.GPUDriven.VividMaterialParameterLayoutID:  static fields
//
#define VIVIDMATERIALPARAMETERLAYOUTID_LEGACY_MATERIAL_DATA (0)
#define VIVIDMATERIALPARAMETERLAYOUTID_DUAL_SLAB_MATERIAL_DATA (1)

//
// VividRP.Runtime.GPUDriven.VividMaterialProgramCapabilities:  static fields
//
#define VIVIDMATERIALPROGRAMCAPABILITIES_NONE (0)
#define VIVIDMATERIALPROGRAMCAPABILITIES_LEGACY_GBUFFER_EXPORT (1)
#define VIVIDMATERIALPROGRAMCAPABILITIES_ALPHA_CLIP (2)
#define VIVIDMATERIALPROGRAMCAPABILITIES_UNLIT (4)

//
// VividRP.Runtime.GPUDriven.VividMaterialProgramID:  static fields
//
#define VIVIDMATERIALPROGRAMID_STANDARD_SINGLE_SLAB (0)
#define VIVIDMATERIALPROGRAMID_DUAL_SLAB_HORIZONTAL_MIX (1)
#define VIVIDMATERIALPROGRAMID_DUAL_SLAB_VERTICAL_LAYER (2)
#define VIVIDMATERIALPROGRAMID_INVALID (4294967295)

//
// VividRP.Runtime.GPUDriven.VividMaterialResourceLayoutID:  static fields
//
#define VIVIDMATERIALRESOURCELAYOUTID_LEGACY_SURFACE_BINDING (0)
#define VIVIDMATERIALRESOURCELAYOUTID_DUAL_SURFACE_BINDING (1)

//
// VividRP.Runtime.GPUDriven.VividMaterialRuntimeFlags:  static fields
//
#define VIVIDMATERIALRUNTIMEFLAGS_NONE (0)
#define VIVIDMATERIALRUNTIMEFLAGS_ALPHA_CLIP (1)
#define VIVIDMATERIALRUNTIMEFLAGS_UNLIT (2)

//
// VividRP.Runtime.GPUDriven.VividMaterialSurfaceProgramID:  static fields
//
#define VIVIDMATERIALSURFACEPROGRAMID_STANDARD_SINGLE_SLAB (0)
#define VIVIDMATERIALSURFACEPROGRAMID_DUAL_SLAB (1)

//
// VividRP.Runtime.GPUDriven.VividMaterialTransportProgramID:  static fields
//
#define VIVIDMATERIALTRANSPORTPROGRAMID_NONE (0)

//
// VividRP.Runtime.GPUDriven.VividRendererListID:  static fields
//
#define VIVIDRENDERERLISTID_DEFAULT (0)
#define VIVIDRENDERERLISTID_CULL_FRONT (1)
#define VIVIDRENDERERLISTID_CULL_OFF (2)
#define VIVIDRENDERERLISTID_ALPHA_TEST (4)
#define VIVIDRENDERERLISTID_COUNT (8)

//
// VividRP.Runtime.GPUDriven.VividSurfaceBindingFlags:  static fields
//
#define VIVIDSURFACEBINDINGFLAGS_NONE (0)
#define VIVIDSURFACEBINDINGFLAGS_BASE_COLOR (1)
#define VIVIDSURFACEBINDINGFLAGS_NORMAL (2)
#define VIVIDSURFACEBINDINGFLAGS_MASK (4)

//
// VividRP.Runtime.GPUDriven.VividMeshletConfiguration:  static fields
//
#define MAX_MESHLET_VERTICES (128)
#define MAX_MESHLET_TRIANGLES (128)
#define MAX_MESHLET_INDICES (384)
#define MESHLET_CONE_WEIGHT (0.25)

//
// VividRP.Runtime.GPUDriven.VividSurfaceBindingData:  static fields
//
#define INVALID_RESOURCE (4294967295)

// Generated from VividRP.Runtime.GPUDriven.IndirectDispatchArgs
// PackingRules = Exact
struct IndirectDispatchArgs
{
    uint ThreadGroupsX;
    uint ThreadGroupsY;
    uint ThreadGroupsZ;
};

// Generated from VividRP.Runtime.GPUDriven.VividDualSlabMaterialData
// PackingRules = Exact
struct VividDualSlabMaterialData
{
    float4 BaseAlbedoColor;
    float4 BaseTextureTilingOffset;
    float4 BaseMetallicSmoothnessRemap;
    float4 BaseAmbientOcclusionRemap;
    float BaseNormalsStrength;
    float BaseRoughness;
    float BaseMetallic;
    uint BaseMaskMode;
    float4 TopAlbedoColor;
    float4 TopTextureTilingOffset;
    float4 TopMetallicSmoothnessRemap;
    float4 TopAmbientOcclusionRemap;
    float TopNormalsStrength;
    float TopRoughness;
    float TopMetallic;
    uint TopMaskMode;
    float4 Emission;
    uint LayerOperator;
    float LayerWeight;
    float AlphaClipThreshold;
    uint Padding0;
};

// Generated from VividRP.Runtime.GPUDriven.VividGPUCullingContext
// PackingRules = Exact
struct VividGPUCullingContext
{
    float4x4 ViewProjectionMatrix;
    float4x4 ViewMatrix;
    float4 CameraPosition;
    float4 FrustumPlanes[6];
    float4 CullingSphereLS;
    int PassMask;
    int CameraIsPerspective;
    uint BaseStartInstance;
    uint MeshletListBuildJobsOffset;
    uint MeshletRenderRequestsOffset;
    uint Padding0;
    uint Padding1;
    uint Padding2;
};

// Generated from VividRP.Runtime.GPUDriven.VividGPULODSelectionContext
// PackingRules = Exact
struct VividGPULODSelectionContext
{
    float4x4 ViewProjectionMatrix;
    float4 CameraPosition;
    float4 CameraUp;
    float4 CameraRight;
    float2 ScreenSizePixels;
    uint Padding0;
    uint Padding1;
};

// Generated from VividRP.Runtime.GPUDriven.VividIndirectDrawArgs
// PackingRules = Exact
struct VividIndirectDrawArgs
{
    uint VertexCountPerInstance;
    uint InstanceCount;
    uint StartVertex;
    uint StartInstance;
};

// Generated from VividRP.Runtime.GPUDriven.VividInstanceData
// PackingRules = Exact
struct VividInstanceData
{
    float4x4 ObjectToWorldMatrix;
    float4x4 WorldToObjectMatrix;
    float4 AABBMin;
    float4 AABBMax;
    uint TopMeshLODStartIndex;
    uint TotalMeshLODCount;
    uint MaterialIndex;
    uint MeshLODLevelCount;
    float LODErrorScale;
    int PassMask;
    int Flags;
    uint Padding0;
};

// Generated from VividRP.Runtime.GPUDriven.VividMaterialData
// PackingRules = Exact
struct VividMaterialData
{
    float4 AlbedoColor;
    float4 TextureTilingOffset;
    float4 Emission;
    float4 MetallicSmoothnessRemap;
    float4 AmbientOcclusionRemap;
    uint SurfaceBindingIndex;
    float NormalsStrength;
    float Roughness;
    float Metallic;
    float SpecularAAScreenSpaceVariance;
    float SpecularAAThreshold;
    int GeometryFlags;
    int MaterialFlags;
    int RendererListID;
    float AlphaClipThreshold;
    uint Padding0;
    uint Padding1;
};

// Generated from VividRP.Runtime.GPUDriven.VividMaterialProgramData
// PackingRules = Exact
struct VividMaterialProgramData
{
    uint Version;
    uint CoverageProgramID;
    uint SurfaceProgramID;
    uint TransportProgramID;
    uint ParameterLayoutID;
    uint ResourceLayoutID;
    uint CapabilityFlags;
    uint ExecutionClass;
};

// Generated from VividRP.Runtime.GPUDriven.VividMaterialRuntimeHeader
// PackingRules = Exact
struct VividMaterialRuntimeHeader
{
    uint ProgramID;
    uint ParameterAddress;
    uint ResourceBindingAddress;
    uint Flags;
};

// Generated from VividRP.Runtime.GPUDriven.VividMeshlet
// PackingRules = Exact
struct VividMeshlet
{
    uint VertexOffset;
    uint TriangleOffset;
    uint PackedVertexTriangleCounts;
    uint PackedCone;
    float4 BoundingSphere;
};

// Generated from VividRP.Runtime.GPUDriven.VividMeshletRenderRequestPacked
// PackingRules = Exact
struct VividMeshletRenderRequestPacked
{
    uint InstanceID_LOD;
    uint MeshletID;
};

// Generated from VividRP.Runtime.GPUDriven.VividMeshletVertex
// PackingRules = Exact
struct VividMeshletVertex
{
    float PositionX;
    float PositionY;
    float PositionZ;
    uint PackedNormal;
    uint PackedTangent;
    float2 UV;
    uint Reserved;
};

// Generated from VividRP.Runtime.GPUDriven.VividMeshLODNode
// PackingRules = Exact
struct VividMeshLODNode
{
    float4 Bounds;
    float Error;
    uint PackedParentErrorRadius;
    uint MeshletStartIndex;
    uint PackedMeshletCountLevel;
};

// Generated from VividRP.Runtime.GPUDriven.VividSlabMaterialData
// PackingRules = Exact
struct VividSlabMaterialData
{
    float4 AlbedoColor;
    float4 TextureTilingOffset;
    float4 MetallicSmoothnessRemap;
    float4 AmbientOcclusionRemap;
    float NormalsStrength;
    float Roughness;
    float Metallic;
    uint MaskMode;
};

// Generated from VividRP.Runtime.GPUDriven.VividSurfaceBindingData
// PackingRules = Exact
struct VividSurfaceBindingData
{
    uint BaseColorResource;
    uint NormalResource;
    uint MaskResource;
    uint Flags;
    float4 UVScaleBias;
};

// Generated from VividRP.Runtime.GPUDriven.VividTerrainLayerGPUData
// PackingRules = Exact
struct VividTerrainLayerGPUData
{
    float4 TextureTilingOffset;
    uint SurfaceBindingIndex;
    float NormalsStrength;
    float Roughness;
    float Metallic;
    uint MaskMode;
    uint Padding0;
    uint Padding1;
    uint Padding2;
};

// Generated from VividRP.Runtime.GPUDriven.VividTerrainMaterialData
// PackingRules = Exact
struct VividTerrainMaterialData
{
    uint LayerStartIndex;
    uint LayerCount;
    uint ControlBindingIndex0;
    uint ControlBindingIndex1;
};


#endif
