#ifndef VIVIDRP_GPU_DRIVEN_MATERIAL_COVERAGE_INCLUDED
#define VIVIDRP_GPU_DRIVEN_MATERIAL_COVERAGE_INCLUDED

struct VividMaterialCoverageEvaluation
{
    float Coverage;
    float AlphaClipThreshold;
};

VividMaterialCoverageEvaluation VividEvaluateBaseColorAlphaCoverage(
    const VividMaterialData materialData,
    const VividSurfaceBindingData surfaceBindingData,
    const float2 uv0,
    const float2 uv0Ddx,
    const float2 uv0Ddy)
{
    const float2 tiling = materialData.TextureTilingOffset.xy;
    const float2 uv = uv0 * tiling
        + materialData.TextureTilingOffset.zw;
    const float2 uvDdx = uv0Ddx * tiling;
    const float2 uvDdy = uv0Ddy * tiling;
    const float4 albedo = materialData.AlbedoColor
        * VividSampleBaseColorGrad(surfaceBindingData, uv, uvDdx, uvDdy);

    VividMaterialCoverageEvaluation evaluation;
    evaluation.Coverage = albedo.a;
    evaluation.AlphaClipThreshold = materialData.AlphaClipThreshold;
    return evaluation;
}

#include "Packages/com.vivid.render-pipelines/Shaders/Core/Public/GPUDriven/VividMaterialCoverageAOT.generated.hlsl"

bool VividIsCoverageProgramCompatible(
    const VividMaterialRuntimeHeader runtimeHeader,
    const VividMaterialProgramData programData)
{
    return programData.Version == VIVID_MATERIAL_PROGRAM_VERSION
        && programData.CoverageProgramID
            == VIVIDMATERIALCOVERAGEPROGRAMID_BASE_COLOR_ALPHA
        && (programData.CapabilityFlags
            & VIVIDMATERIALPROGRAMCAPABILITIES_ALPHA_CLIP) != 0u
        && (runtimeHeader.Flags & VIVIDMATERIALRUNTIMEFLAGS_ALPHA_CLIP) != 0u
        && programData.ExecutionClass
            == VIVIDMATERIALEXECUTIONCLASS_VISIBILITY_DEFERRED;
}

#define VIVID_MATERIAL_COVERAGE_LEGACY_FALLBACK 0u
#define VIVID_MATERIAL_COVERAGE_EVALUATED 1u
#define VIVID_MATERIAL_COVERAGE_KNOWN_FAILURE 2u

uint VividEvaluateCoverageProgram(
    const uint materialIndex,
    const float2 uv0,
    const float2 uv0Ddx,
    const float2 uv0Ddy,
    out VividMaterialCoverageEvaluation evaluation)
{
    evaluation = (VividMaterialCoverageEvaluation) 0;
    if (materialIndex >= _MaterialRuntimeHeaderCount)
        return VIVID_MATERIAL_COVERAGE_KNOWN_FAILURE;

    const VividMaterialRuntimeHeader runtimeHeader =
        PullMaterialRuntimeHeader(materialIndex);
    if (runtimeHeader.ProgramID == VIVIDMATERIALPROGRAMID_INVALID)
        return VIVID_MATERIAL_COVERAGE_LEGACY_FALLBACK;
    if (runtimeHeader.ProgramID >= _MaterialProgramCount)
        return VIVID_MATERIAL_COVERAGE_KNOWN_FAILURE;

    const VividMaterialProgramData programData =
        PullMaterialProgramData(runtimeHeader.ProgramID);
    if (!VividIsCoverageProgramCompatible(runtimeHeader, programData))
        return VIVID_MATERIAL_COVERAGE_KNOWN_FAILURE;

    VividAOTCoverageContext context;
    context.UV0 = uv0;
    context.UV0Ddx = uv0Ddx;
    context.UV0Ddy = uv0Ddy;
    return VividTryEvaluateAOTCoverageProgram(
            runtimeHeader,
            programData,
            context,
            evaluation)
        ? VIVID_MATERIAL_COVERAGE_EVALUATED
        : VIVID_MATERIAL_COVERAGE_KNOWN_FAILURE;
}

#endif // VIVIDRP_GPU_DRIVEN_MATERIAL_COVERAGE_INCLUDED
