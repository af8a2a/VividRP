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
    const float2 uv0)
{
    const float2 uv = uv0 * materialData.TextureTilingOffset.xy
        + materialData.TextureTilingOffset.zw;
    const float4 albedo = materialData.AlbedoColor
        * VividSampleBaseColor(surfaceBindingData, uv);

    VividMaterialCoverageEvaluation evaluation;
    evaluation.Coverage = albedo.a;
    evaluation.AlphaClipThreshold = materialData.AlphaClipThreshold;
    return evaluation;
}

bool VividIsCoverageProgramCompatible(
    const VividMaterialRuntimeHeader runtimeHeader,
    const VividMaterialProgramData programData)
{
    return programData.Version == VIVID_MATERIAL_PROGRAM_VERSION
        && programData.ParameterLayoutID
            == VIVIDMATERIALPARAMETERLAYOUTID_LEGACY_MATERIAL_DATA
        && programData.ResourceLayoutID
            == VIVIDMATERIALRESOURCELAYOUTID_LEGACY_SURFACE_BINDING
        && (programData.CapabilityFlags
            & VIVIDMATERIALPROGRAMCAPABILITIES_ALPHA_CLIP) != 0u
        && (runtimeHeader.Flags & VIVIDMATERIALRUNTIMEFLAGS_ALPHA_CLIP) != 0u;
}

bool VividTryEvaluateCoverageProgram(
    const uint materialIndex,
    const float2 uv0,
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

    UNITY_BRANCH
    if (programData.CoverageProgramID
        == VIVIDMATERIALCOVERAGEPROGRAMID_BASE_COLOR_ALPHA)
    {
        if (runtimeHeader.ParameterAddress >= _MaterialDataCount
            || runtimeHeader.ResourceBindingAddress >= _SurfaceBindingDataCount)
        {
            return false;
        }

        const VividMaterialData materialData =
            PullMaterialData(runtimeHeader.ParameterAddress);
        const VividSurfaceBindingData surfaceBindingData =
            PullSurfaceBindingData(runtimeHeader.ResourceBindingAddress);
        evaluation = VividEvaluateBaseColorAlphaCoverage(
            materialData,
            surfaceBindingData,
            uv0);
        return true;
    }

    return false;
}

#endif // VIVIDRP_GPU_DRIVEN_MATERIAL_COVERAGE_INCLUDED
