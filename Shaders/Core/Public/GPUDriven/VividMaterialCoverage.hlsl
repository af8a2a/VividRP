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

VividMaterialCoverageEvaluation VividEvaluateBaseColorAlphaCoverage(
    const VividSlabMaterialData slabData,
    const float alphaClipThreshold,
    const VividSurfaceBindingData surfaceBindingData,
    const float2 uv0,
    const float2 uv0Ddx,
    const float2 uv0Ddy)
{
    const float2 tiling = slabData.TextureTilingOffset.xy;
    const float2 uv = uv0 * tiling
        + slabData.TextureTilingOffset.zw;
    const float2 uvDdx = uv0Ddx * tiling;
    const float2 uvDdy = uv0Ddy * tiling;
    const float4 albedo = slabData.AlbedoColor
        * VividSampleBaseColorGrad(surfaceBindingData, uv, uvDdx, uvDdy);

    VividMaterialCoverageEvaluation evaluation;
    evaluation.Coverage = albedo.a;
    evaluation.AlphaClipThreshold = alphaClipThreshold;
    return evaluation;
}

bool VividIsCoverageProgramCompatible(
    const VividMaterialRuntimeHeader runtimeHeader,
    const VividMaterialProgramData programData)
{
    return programData.Version == VIVID_MATERIAL_PROGRAM_VERSION
        && (programData.CapabilityFlags
            & VIVIDMATERIALPROGRAMCAPABILITIES_ALPHA_CLIP) != 0u
        && (runtimeHeader.Flags & VIVIDMATERIALRUNTIMEFLAGS_ALPHA_CLIP) != 0u;
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

    UNITY_BRANCH
    if (programData.CoverageProgramID
        == VIVIDMATERIALCOVERAGEPROGRAMID_BASE_COLOR_ALPHA)
    {
        if (programData.ParameterLayoutID
                == VIVIDMATERIALPARAMETERLAYOUTID_LEGACY_MATERIAL_DATA
            && programData.ResourceLayoutID
                == VIVIDMATERIALRESOURCELAYOUTID_LEGACY_SURFACE_BINDING)
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
                uv0,
                uv0Ddx,
                uv0Ddy);
            return true;
        }
        if (programData.ParameterLayoutID
                == VIVIDMATERIALPARAMETERLAYOUTID_DUAL_SLAB_MATERIAL_DATA
            && programData.ResourceLayoutID
                == VIVIDMATERIALRESOURCELAYOUTID_DUAL_SURFACE_BINDING)
        {
            const uint bindingAddress = runtimeHeader.ResourceBindingAddress;
            if (runtimeHeader.ParameterAddress >= _DualSlabMaterialDataCount
                || bindingAddress >= _SurfaceBindingDataCount
                || _SurfaceBindingDataCount - bindingAddress < 2u)
            {
                return false;
            }

            const VividDualSlabMaterialData materialData =
                PullDualSlabMaterialData(runtimeHeader.ParameterAddress);
            const VividSurfaceBindingData surfaceBindingData =
                PullSurfaceBindingData(bindingAddress);
            evaluation = VividEvaluateBaseColorAlphaCoverage(
                VividGetBaseSlabMaterialData(materialData),
                materialData.AlphaClipThreshold,
                surfaceBindingData,
                uv0,
                uv0Ddx,
                uv0Ddy);
            return true;
        }
    }

    return false;
}

#endif // VIVIDRP_GPU_DRIVEN_MATERIAL_COVERAGE_INCLUDED
