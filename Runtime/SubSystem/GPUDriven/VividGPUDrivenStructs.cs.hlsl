//
// This file was automatically generated. Please don't edit by hand. Execute Editor command [ Edit > Rendering > Generate Shader Includes ] instead
//

#ifndef VIVIDGPUDRIVENSTRUCTS_CS_HLSL
#define VIVIDGPUDRIVENSTRUCTS_CS_HLSL
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

//
// VividRP.Runtime.GPUDriven.VividInstancePassMask:  static fields
//
#define VIVIDINSTANCEPASSMASK_NONE (0)
#define VIVIDINSTANCEPASSMASK_MAIN (1)
#define VIVIDINSTANCEPASSMASK_SHADOWS (2)

//
// VividRP.Runtime.GPUDriven.VividMaterialFlags:  static fields
//
#define VIVIDMATERIALFLAGS_NONE (0)
#define VIVIDMATERIALFLAGS_UNLIT (1)
#define VIVIDMATERIALFLAGS_TERRAIN (2)
#define VIVIDMATERIALFLAGS_TERRAIN_RUNTIME_VIRTUAL_TEXTURE (4)

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
