#ifndef VIVIDRP_GPU_DRIVEN_MATERIAL_SURFACE_INCLUDED
#define VIVIDRP_GPU_DRIVEN_MATERIAL_SURFACE_INCLUDED

bool VividIsStandardSingleSlabSurfaceProgramCompatible(
    const VividMaterialProgramData programData,
    const uint requiredCapabilities)
{
    return programData.Version == VIVID_MATERIAL_PROGRAM_VERSION
        && programData.SurfaceProgramID
            == VIVIDMATERIALSURFACEPROGRAMID_STANDARD_SINGLE_SLAB
        && programData.ParameterLayoutID
            == VIVIDMATERIALPARAMETERLAYOUTID_LEGACY_MATERIAL_DATA
        && programData.ResourceLayoutID
            == VIVIDMATERIALRESOURCELAYOUTID_LEGACY_SURFACE_BINDING
        && (programData.CapabilityFlags & requiredCapabilities)
            == requiredCapabilities
        && programData.ExecutionClass
            == VIVIDMATERIALEXECUTIONCLASS_VISIBILITY_DEFERRED;
}

bool VividTryLoadStandardSingleSlabSurfaceProgram(
    const uint materialIndex,
    const uint requiredCapabilities,
    out VividMaterialData materialData,
    out VividSurfaceBindingData surfaceBindingData)
{
    materialData = (VividMaterialData) 0;
    surfaceBindingData = (VividSurfaceBindingData) 0;
    if (materialIndex >= _MaterialRuntimeHeaderCount)
        return false;

    const VividMaterialRuntimeHeader runtimeHeader =
        PullMaterialRuntimeHeader(materialIndex);
    if (runtimeHeader.ProgramID == VIVIDMATERIALPROGRAMID_INVALID
        || runtimeHeader.ProgramID >= _MaterialProgramCount)
    {
        return false;
    }

    const VividMaterialProgramData programData =
        PullMaterialProgramData(runtimeHeader.ProgramID);
    if (!VividIsStandardSingleSlabSurfaceProgramCompatible(
            programData,
            requiredCapabilities)
        || runtimeHeader.ParameterAddress >= _MaterialDataCount
        || runtimeHeader.ResourceBindingAddress >= _SurfaceBindingDataCount)
    {
        return false;
    }

    materialData = PullMaterialData(runtimeHeader.ParameterAddress);
    surfaceBindingData = PullSurfaceBindingData(
        runtimeHeader.ResourceBindingAddress);
    return true;
}

bool VividIsDualSlabSurfaceProgramCompatible(
    const VividMaterialProgramData programData,
    const uint requiredCapabilities)
{
    return programData.Version == VIVID_MATERIAL_PROGRAM_VERSION
        && programData.SurfaceProgramID
            == VIVIDMATERIALSURFACEPROGRAMID_DUAL_SLAB
        && programData.ParameterLayoutID
            == VIVIDMATERIALPARAMETERLAYOUTID_DUAL_SLAB_MATERIAL_DATA
        && programData.ResourceLayoutID
            == VIVIDMATERIALRESOURCELAYOUTID_DUAL_SURFACE_BINDING
        && (programData.CapabilityFlags & requiredCapabilities)
            == requiredCapabilities
        && programData.ExecutionClass
            == VIVIDMATERIALEXECUTIONCLASS_VISIBILITY_DEFERRED;
}

bool VividTryLoadDualSlabSurfaceProgram(
    const uint materialIndex,
    const uint requiredCapabilities,
    out VividDualSlabMaterialData materialData,
    out VividSurfaceBindingData baseSurfaceBindingData,
    out VividSurfaceBindingData topSurfaceBindingData)
{
    materialData = (VividDualSlabMaterialData) 0;
    baseSurfaceBindingData = (VividSurfaceBindingData) 0;
    topSurfaceBindingData = (VividSurfaceBindingData) 0;
    if (materialIndex >= _MaterialRuntimeHeaderCount)
        return false;

    const VividMaterialRuntimeHeader runtimeHeader =
        PullMaterialRuntimeHeader(materialIndex);
    if (runtimeHeader.ProgramID == VIVIDMATERIALPROGRAMID_INVALID
        || runtimeHeader.ProgramID >= _MaterialProgramCount)
    {
        return false;
    }

    const VividMaterialProgramData programData =
        PullMaterialProgramData(runtimeHeader.ProgramID);
    const uint bindingAddress = runtimeHeader.ResourceBindingAddress;
    if (!VividIsDualSlabSurfaceProgramCompatible(
            programData,
            requiredCapabilities)
        || runtimeHeader.ParameterAddress >= _DualSlabMaterialDataCount
        || bindingAddress >= _SurfaceBindingDataCount
        || _SurfaceBindingDataCount - bindingAddress < 2u)
    {
        return false;
    }

    materialData = PullDualSlabMaterialData(runtimeHeader.ParameterAddress);
    baseSurfaceBindingData = PullSurfaceBindingData(bindingAddress);
    topSurfaceBindingData = PullSurfaceBindingData(bindingAddress + 1u);
    return true;
}

struct VividEvaluatedSlabSurface
{
    float3 BaseColor;
    float3 NormalTS;
    float PerceptualRoughness;
    float Metallic;
    float AmbientOcclusion;
    uint HasNormal;
};

float VividRemapSlabPBRChannel(const float sampleValue, const float2 remap)
{
    return saturate(lerp(remap.x, remap.y, saturate(sampleValue)));
}

void VividApplySlabMaskSample(
    const VividSlabMaterialData slabData,
    const float4 maskSample,
    inout float perceptualRoughness,
    inout float metallic,
    inout float ambientOcclusion)
{
    if (slabData.MaskMode == 1u)
    {
        metallic = VividRemapSlabPBRChannel(
            maskSample.r,
            slabData.MetallicSmoothnessRemap.xy);
        perceptualRoughness = 1.0f - VividRemapSlabPBRChannel(
            maskSample.a,
            slabData.MetallicSmoothnessRemap.zw);
    }
    else if (slabData.MaskMode == 2u)
    {
        perceptualRoughness = 1.0f - VividRemapSlabPBRChannel(
            1.0f - maskSample.r,
            slabData.MetallicSmoothnessRemap.zw);
    }
    else if (slabData.MaskMode == 3u)
    {
        metallic = VividRemapSlabPBRChannel(
            maskSample.r,
            slabData.MetallicSmoothnessRemap.xy);
        ambientOcclusion = VividRemapSlabPBRChannel(
            maskSample.g,
            slabData.AmbientOcclusionRemap.xy);
        perceptualRoughness = 1.0f - VividRemapSlabPBRChannel(
            maskSample.a,
            slabData.MetallicSmoothnessRemap.zw);
    }
    else if (slabData.MaskMode == 4u)
    {
        perceptualRoughness = 1.0f - VividRemapSlabPBRChannel(
            1.0f - maskSample.r,
            slabData.MetallicSmoothnessRemap.zw);
        metallic = VividRemapSlabPBRChannel(
            maskSample.g,
            slabData.MetallicSmoothnessRemap.xy);
        ambientOcclusion = VividRemapSlabPBRChannel(
            maskSample.b,
            slabData.AmbientOcclusionRemap.xy);
    }
}

float3 VividUnpackSlabNormalScale(const float4 packedNormal, const float scale)
{
    float3 normalTS;
    normalTS.xy = packedNormal.wy * 2.0f - 1.0f;
    normalTS.xy *= scale;
    normalTS.z = sqrt(saturate(1.0f - dot(normalTS.xy, normalTS.xy)));
    return normalTS;
}

VividEvaluatedSlabSurface VividEvaluateAOTSlabSurfaceDetail(
    const VividSlabMaterialData slabData,
    const VividSurfaceBindingData surfaceBindingData,
    const VividSurfaceSampleContext context,
    const bool evaluateNormal,
    const bool evaluateMask,
    const float3 baseColor,
    const float perceptualRoughness,
    const float metallic)
{
    VividEvaluatedSlabSurface surface;
    surface.BaseColor = baseColor;
    surface.NormalTS = float3(0.0f, 0.0f, 1.0f);
    surface.PerceptualRoughness = perceptualRoughness;
    surface.Metallic = metallic;
    surface.AmbientOcclusion = 1.0f;
    surface.HasNormal = evaluateNormal
        && VividSurfaceHasNormal(surfaceBindingData)
            ? 1u
            : 0u;
    if (surface.HasNormal != 0u)
    {
        surface.NormalTS = VividUnpackSlabNormalScale(
            VividSampleNormalGrad(surfaceBindingData, context),
            slabData.NormalsStrength);
    }
    if (evaluateMask && VividSurfaceHasMask(surfaceBindingData))
    {
        VividApplySlabMaskSample(
            slabData,
            VividSampleMaskGrad(surfaceBindingData, context),
            surface.PerceptualRoughness,
            surface.Metallic,
            surface.AmbientOcclusion);
    }
    return surface;
}

VividEvaluatedSlabSurface VividEvaluateSlabSurfaceDetail(
    const VividSlabMaterialData slabData,
    const VividSurfaceBindingData surfaceBindingData,
    const VividSurfaceSampleContext context,
    const float3 baseColor,
    const float perceptualRoughness,
    const float metallic)
{
    return VividEvaluateAOTSlabSurfaceDetail(
        slabData,
        surfaceBindingData,
        context,
        true,
        true,
        baseColor,
        perceptualRoughness,
        metallic);
}

VividEvaluatedSlabSurface VividEvaluateSlabSurfaceGrad(
    const VividSlabMaterialData slabData,
    const VividSurfaceBindingData surfaceBindingData,
    const float2 uv0,
    const float2 uvDdx,
    const float2 uvDdy,
    const float4 positionCS)
{
    const float2 uv = uv0 * slabData.TextureTilingOffset.xy
        + slabData.TextureTilingOffset.zw;
    const float2 tiledUVDdx = uvDdx * slabData.TextureTilingOffset.xy;
    const float2 tiledUVDdy = uvDdy * slabData.TextureTilingOffset.xy;
    const VividSurfaceSampleContext context = VividCreateSurfaceSampleContextGrad(
        surfaceBindingData,
        uv,
        tiledUVDdx,
        tiledUVDdy,
        positionCS);

    const float3 baseColor = (
        VividSampleBaseColorGrad(surfaceBindingData, context)
        * slabData.AlbedoColor).rgb;
    return VividEvaluateSlabSurfaceDetail(
        slabData,
        surfaceBindingData,
        context,
        baseColor,
        slabData.Roughness,
        slabData.Metallic);
}

#include "Packages/com.vivid.render-pipelines/Shaders/Core/Public/GPUDriven/VividMaterialSurfaceAOT.generated.hlsl"

#endif // VIVIDRP_GPU_DRIVEN_MATERIAL_SURFACE_INCLUDED
