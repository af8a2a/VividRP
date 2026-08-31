//
// This file was automatically generated. Please don't edit by hand. Execute Editor command [ Edit > Rendering > Generate Shader Includes ] instead
//

#ifndef VIVIDPRIMITIVESCENETYPES_CS_HLSL
#define VIVIDPRIMITIVESCENETYPES_CS_HLSL
//
// VividRP.Runtime.PrimitiveScene.VividPrimitiveDrawSectionFlags:  static fields
//
#define VIVIDPRIMITIVEDRAWSECTIONFLAGS_NONE (0)
#define VIVIDPRIMITIVEDRAWSECTIONFLAGS_VALID (1)
#define VIVIDPRIMITIVEDRAWSECTIONFLAGS_TERRAIN (2)

//
// VividRP.Runtime.PrimitiveScene.VividPrimitiveFlags:  static fields
//
#define VIVIDPRIMITIVEFLAGS_NONE (0)
#define VIVIDPRIMITIVEFLAGS_VALID (1)
#define VIVIDPRIMITIVEFLAGS_DISABLED (2)
#define VIVIDPRIMITIVEFLAGS_FLIP_WINDING_ORDER (4)
#define VIVIDPRIMITIVEFLAGS_STATIC (8)
#define VIVIDPRIMITIVEFLAGS_SKINNED (16)
#define VIVIDPRIMITIVEFLAGS_TERRAIN (32)
#define VIVIDPRIMITIVEFLAGS_RECEIVE_SHADOWS (64)
#define VIVIDPRIMITIVEFLAGS_TWO_SIDED_SHADOWS (128)

// Generated from VividRP.Runtime.PrimitiveScene.VividLegacyInstanceMappingData
// PackingRules = Exact
struct VividLegacyInstanceMappingData
{
    uint PrimitiveIndex;
    uint PrimitiveGeneration;
    uint DrawSectionIndex;
    uint Flags;
};

// Generated from VividRP.Runtime.PrimitiveScene.VividPrimitiveData
// PackingRules = Exact
struct VividPrimitiveData
{
    float4 WorldBoundsMin;
    float4 WorldBoundsMax;
    uint TransformIndex;
    uint DrawSectionOffset;
    uint DrawSectionCount;
    uint RenderingLayerMask;
    uint PassMask;
    uint Flags;
    uint Generation;
    uint CustomDataAddress;
};

// Generated from VividRP.Runtime.PrimitiveScene.VividPrimitiveDrawSectionData
// PackingRules = Exact
struct VividPrimitiveDrawSectionData
{
    uint GeometryIndex;
    uint GeometryGeneration;
    uint MaterialIndex;
    uint MaterialGeneration;
    uint SourceSectionIndex;
    uint Flags;
    uint Padding0;
    uint Padding1;
};

// Generated from VividRP.Runtime.PrimitiveScene.VividPrimitiveGeometryData
// PackingRules = Exact
struct VividPrimitiveGeometryData
{
    uint Generation;
    uint LegacyTopMeshLODStartIndex;
    uint LegacyTotalMeshLODCount;
    uint LegacyMeshLODLevelCount;
};

// Generated from VividRP.Runtime.PrimitiveScene.VividPrimitiveMaterialData
// PackingRules = Exact
struct VividPrimitiveMaterialData
{
    uint Generation;
    uint LegacyMaterialIndex;
    int RendererListID;
    int MaterialFlags;
};

// Generated from VividRP.Runtime.PrimitiveScene.VividPrimitivePreviousTransformData
// PackingRules = Exact
struct VividPrimitivePreviousTransformData
{
    float4x4 PreviousObjectToWorldMatrix;
};

// Generated from VividRP.Runtime.PrimitiveScene.VividPrimitiveTransformData
// PackingRules = Exact
struct VividPrimitiveTransformData
{
    float4x4 ObjectToWorldMatrix;
    float4x4 WorldToObjectMatrix;
};


#endif
