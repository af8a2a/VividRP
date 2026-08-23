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

bool VividTryEvaluateCoverageProgram(
    const uint materialIndex,
    const float2 uv0,
    const float2 uv0Ddx,
    const float2 uv0Ddy,
    out VividMaterialCoverageEvaluation evaluation)
{
    evaluation = (VividMaterialCoverageEvaluation) 0;
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
    if (!VividIsCoverageProgramCompatible(runtimeHeader, programData))
        return false;

    VividAOTCoverageContext context;
    context.UV0 = uv0;
    context.UV0Ddx = uv0Ddx;
    context.UV0Ddy = uv0Ddy;
    return VividTryEvaluateAOTCoverageProgram(
        runtimeHeader,
        programData,
        context,
        evaluation);
}

#endif // VIVIDRP_GPU_DRIVEN_MATERIAL_COVERAGE_INCLUDED
